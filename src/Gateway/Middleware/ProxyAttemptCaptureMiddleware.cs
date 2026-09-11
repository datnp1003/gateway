using System.Diagnostics;
using Gateway.Domain.Entities;
using Gateway.Domain.Interfaces;
using Yarp.ReverseProxy.Forwarder;

namespace Gateway.Middleware;

public sealed class ProxyAttemptCaptureMiddleware
{
    private const int MaxRequestPathLength = 512;
    private readonly RequestDelegate _next;
    public ProxyAttemptCaptureMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IProxyAttemptSink sink)
    {
        IReadOnlyDictionary<string, string>? metadata;
        try { metadata = context.GetEndpoint()?.Metadata.GetMetadata<Yarp.ReverseProxy.Model.RouteModel>()?.Config.Metadata; }
        catch { await _next(context); return; }
        if (metadata is null || !metadata.TryGetValue("gateway.endpoint_id", out var endpointText) || !Guid.TryParse(endpointText, out var endpointId) ||
            !metadata.TryGetValue("gateway.group_id", out var groupText) || !Guid.TryParse(groupText, out var groupId))
        {
            await _next(context);
            return;
        }

        context.Items[ProxyAttemptProvenance.MetadataKey] = metadata;
        // Capture before YARP applies route transforms; Path never includes the query string.
        var requestPath = SafeRequestPath(context.Request.Path.Value);

        var started = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();
        var gatewayFailed = false;
        try { await _next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch { gatewayFailed = true; throw; }
        finally
        {
            var forwarder = context.GetForwarderErrorFeature();
            var rejected = context.Items.ContainsKey(ProxyAttemptProvenance.GatewayRejectedKey);
            var forwarded = context.Features.Get<Yarp.ReverseProxy.Model.IReverseProxyFeature>() is not null;
            int? responseStatus = context.Response.HasStarted || forwarded || rejected ? context.Response.StatusCode : null;
            var outcome = context.RequestAborted.IsCancellationRequested ? "client_disconnected"
                : gatewayFailed ? "gateway_failure"
                : forwarder?.Error is { } error && error != ForwarderError.None ? "network_failure"
                : rejected ? "gateway_rejected"
                : forwarded ? "upstream_response" : "gateway_failure";
            try { sink.TryEnqueue(new ProxyRequestEvent
                {
                    Id = Guid.NewGuid(), OccurredAt = started, CompletedAt = DateTime.UtcNow,
                    DurationMs = outcome == "client_disconnected" ? null : timer.Elapsed.TotalMilliseconds,
                    Method = context.Request.Method, RequestPath = requestPath, EndpointId = endpointId, GroupId = groupId,
                    EndpointName = metadata.GetValueOrDefault("gateway.endpoint_name") ?? "", GroupName = metadata.GetValueOrDefault("gateway.group_name") ?? "",
                    ConfiguredDestination = metadata.GetValueOrDefault("gateway.configured_destination") ?? "", Outcome = outcome,
                    ResponseStatus = outcome is "client_disconnected" or "network_failure" or "gateway_failure" ? null : responseStatus
                }); }
            catch { }
        }
    }

    private static string SafeRequestPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "/";

        var segments = path.Split('/', StringSplitOptions.None);
        var redactNext = false;
        for (var index = 0; index < segments.Length; index++)
        {
            if (redactNext && segments[index].Length > 0)
            {
                segments[index] = "[redacted]";
                redactNext = false;
                continue;
            }

            var segment = segments[index];
            var separator = segment.IndexOf('=');
            var name = separator < 0 ? segment : segment[..separator];
            if (IsSensitiveName(name))
            {
                if (separator >= 0) segments[index] = name + "=[redacted]";
                else redactNext = true;
            }
            else if (LooksLikeSecret(segment)) segments[index] = "[redacted]";
        }

        var safe = string.Join('/', segments);
        return safe.Length <= MaxRequestPathLength ? safe : safe[..(MaxRequestPathLength - 1)] + "…";
    }

    private static bool IsSensitiveName(string value) => value.Equals("token", StringComparison.OrdinalIgnoreCase)
        || value.Equals("access_token", StringComparison.OrdinalIgnoreCase)
        || value.Equals("api_key", StringComparison.OrdinalIgnoreCase)
        || value.Equals("apikey", StringComparison.OrdinalIgnoreCase)
        || value.Equals("secret", StringComparison.OrdinalIgnoreCase)
        || value.Equals("password", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecret(string value) => value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)
        || (value.Length > 20 && value.Count(character => character == '.') == 2);
}

public static class ProxyAttemptProvenance
{
    public const string MetadataKey = "gateway.proxy_attempt.metadata";
    public const string GatewayRejectedKey = "gateway.proxy_attempt.gateway_rejected";
}
