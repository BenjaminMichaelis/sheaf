using System.Threading.Channels;

namespace Sheaf.Email;

/// <summary>A plain-text email ready to send.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Accepts email for delivery after the request finishes. Endpoints never wait for the mail server, so a request for
/// an unknown address takes as long as one for a real account, and a slow or failing server can't fail sign-up.
/// </summary>
public interface IEmailOutbox
{
    void Enqueue(EmailMessage message);
}

/// <summary>Sends one message through the configured mail server.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>An in-memory queue drained by <see cref="EmailDeliveryService"/>. Messages still queued at shutdown are lost.</summary>
internal sealed partial class ChannelEmailOutbox(ILogger<ChannelEmailOutbox> logger) : IEmailOutbox
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    internal ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
        {
            LogQueueFull(logger, message.Subject);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Email queue is full; dropped a message with subject '{Subject}'.")]
    private static partial void LogQueueFull(ILogger logger, string subject);
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
