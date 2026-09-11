using System.Threading.Channels;
using Gateway.Domain.Entities;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Gateway.Infrastructure.Services;

public sealed class ProxyAttemptWriter : BackgroundService, IProxyAttemptSink
{
    private readonly Channel<ProxyRequestEvent> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProxyAttemptWriter> _logger;
    private readonly Func<Task>? _afterCommitForTesting;
    private readonly DateTime _since = DateTime.UtcNow;
    private long _accepted, _persisted, _droppedQueueFull, _droppedWriteFailure, _persistenceUnknown, _inFlight;
    private DateTime? _lastSuccessfulWriteAt, _lastWriteFailureAt;

    public ProxyAttemptWriter(IServiceScopeFactory scopeFactory, ILogger<ProxyAttemptWriter> logger, int capacity = 10_000, Func<Task>? afterCommitForTesting = null)
    {
        _queue = Channel.CreateBounded<ProxyRequestEvent>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        _scopeFactory = scopeFactory;
        _logger = logger;
        _afterCommitForTesting = afterCommitForTesting;
    }

    public bool TryEnqueue(ProxyRequestEvent requestEvent)
    {
        if (_queue.Writer.TryWrite(requestEvent)) { Interlocked.Increment(ref _accepted); return true; }
        Interlocked.Increment(ref _droppedQueueFull);
        return false;
    }

    public object GetState() => new
    {
        since = _since, accepted = Interlocked.Read(ref _accepted), persisted = Interlocked.Read(ref _persisted),
        droppedQueueFull = Interlocked.Read(ref _droppedQueueFull), droppedWriteFailure = Interlocked.Read(ref _droppedWriteFailure),
        persistenceUnknown = Interlocked.Read(ref _persistenceUnknown), pending = _queue.Reader.Count + Interlocked.Read(ref _inFlight),
        lastSuccessfulWriteAt = _lastSuccessfulWriteAt, lastWriteFailureAt = _lastWriteFailureAt,
        lastWriteFailureKind = _lastWriteFailureAt is null ? null : "database", status = _lastWriteFailureAt is not null || Interlocked.Read(ref _persistenceUnknown) > 0 ? "degraded" : _queue.Reader.Count + Interlocked.Read(ref _inFlight) > 0 ? "writing" : "idle",
        crashWindow = "Accepted in-memory events are lost if the process stops before database commit; persistenceUnknown means a commit result could not be confirmed."
    };

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await base.StopAsync(linked.Token);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ProxyRequestEvent>(250);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();
                while (batch.Count < 250 && _queue.Reader.TryRead(out var item)) batch.Add(item);
                Interlocked.Add(ref _inFlight, batch.Count);
                try { await WriteBatchAsync(batch, stoppingToken); }
                finally { Interlocked.Add(ref _inFlight, -batch.Count); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task WriteBatchAsync(List<ProxyRequestEvent> batch, CancellationToken ct)
    {
        var commitWasUncertain = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await InsertBatchAsync(db, batch, ct);
                try
                {
                    await transaction.CommitAsync(ct);
                    if (_afterCommitForTesting is not null) await _afterCommitForTesting();
                }
                catch
                {
                    commitWasUncertain = true;
                    throw;
                }
                Interlocked.Add(ref _persisted, batch.Count);
                _lastSuccessfulWriteAt = DateTime.UtcNow;
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                if (attempt < 2) { await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), ct); continue; }
                _lastWriteFailureAt = DateTime.UtcNow;
                if (commitWasUncertain)
                {
                    Interlocked.Add(ref _persistenceUnknown, batch.Count);
                    _logger.LogWarning("Proxy telemetry batch persistence could not be confirmed after database failures");
                }
                else
                {
                    Interlocked.Add(ref _droppedWriteFailure, batch.Count);
                    _logger.LogWarning("Proxy telemetry batch dropped after three database write failures");
                }
            }
        }
    }

    private static Task InsertBatchAsync(GatewayDbContext db, List<ProxyRequestEvent> batch, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "ProxyRequestEvents" ("Id", "OccurredAt", "CompletedAt", "DurationMs", "Method", "RequestPath", "EndpointId", "GroupId", "EndpointName", "GroupName", "ConfiguredDestination", "Outcome", "ResponseStatus")
        SELECT * FROM unnest(@ids, @occurredAt, @completedAt, @durationMs, @methods, @requestPaths, @endpointIds, @groupIds, @endpointNames, @groupNames, @destinations, @outcomes, @responseStatuses)
        ON CONFLICT ("Id") DO NOTHING
        """, new object[]
        {
            ArrayParameter("ids", NpgsqlDbType.Uuid, batch.Select(x => x.Id).ToArray()), ArrayParameter("occurredAt", NpgsqlDbType.TimestampTz, batch.Select(x => x.OccurredAt).ToArray()),
            ArrayParameter("completedAt", NpgsqlDbType.TimestampTz, batch.Select(x => x.CompletedAt).ToArray()), ArrayParameter("durationMs", NpgsqlDbType.Double, batch.Select(x => x.DurationMs).ToArray()),
            ArrayParameter("methods", NpgsqlDbType.Varchar, batch.Select(x => x.Method).ToArray()), ArrayParameter("requestPaths", NpgsqlDbType.Varchar, batch.Select(x => x.RequestPath).ToArray()),
            ArrayParameter("endpointIds", NpgsqlDbType.Uuid, batch.Select(x => x.EndpointId).ToArray()), ArrayParameter("groupIds", NpgsqlDbType.Uuid, batch.Select(x => x.GroupId).ToArray()),
            ArrayParameter("endpointNames", NpgsqlDbType.Varchar, batch.Select(x => x.EndpointName).ToArray()), ArrayParameter("groupNames", NpgsqlDbType.Varchar, batch.Select(x => x.GroupName).ToArray()),
            ArrayParameter("destinations", NpgsqlDbType.Varchar, batch.Select(x => x.ConfiguredDestination).ToArray()), ArrayParameter("outcomes", NpgsqlDbType.Varchar, batch.Select(x => x.Outcome).ToArray()),
            ArrayParameter("responseStatuses", NpgsqlDbType.Integer, batch.Select(x => x.ResponseStatus).ToArray())
        }, ct);

    private static NpgsqlParameter ArrayParameter(string name, NpgsqlDbType type, Array values) => new(name, type | NpgsqlDbType.Array) { Value = values };
}
