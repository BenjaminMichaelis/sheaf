using System.Threading.Channels;

namespace Sheaf.Email;

/// <summary>A plain-text email ready to send.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Accepts email for delivery after the request finishes. Endpoints never wait for the mail server, so its speed can't
/// tell a real account from an unknown address, and a slow or failing server can't fail sign-up.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>Queues <paramref name="message"/>. Waits only if the queue is full.</summary>
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Sends one message through the configured mail server.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// An in-memory queue drained by <see cref="EmailDeliveryService"/>, following the queued background task pattern in
/// "Background tasks with hosted services in ASP.NET Core": a bounded channel whose writers wait when it is full, so
/// no message is dropped. Messages still queued at shutdown are lost.
/// </summary>
internal sealed class ChannelEmailOutbox : IEmailOutbox
{
    private const int Capacity = 1000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    internal ChannelReader<EmailMessage> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(message, cancellationToken);
}

internal sealed partial class EmailDeliveryService(
    ChannelEmailOutbox outbox, IEmailTransport transport, ILogger<EmailDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (EmailMessage message in outbox.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await transport.SendAsync(message, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The body holds a verification code and the address is personal data, so log neither.
                LogSendFailed(logger, exception, message.Subject);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send email with subject '{Subject}'.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string subject);
}
