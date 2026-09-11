using Gateway.Domain.Entities;

namespace Gateway.Domain.Interfaces;

public interface IProxyAttemptSink
{
    bool TryEnqueue(ProxyRequestEvent requestEvent);
    object GetState();
}
