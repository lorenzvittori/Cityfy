namespace ServiceDefault.Messaging;

public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a persistent message on 'cityfy.events' and waits for the broker confirm.
    /// Throws if the broker does not accept it.
    /// </summary>
    Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default) where T : class;
}
