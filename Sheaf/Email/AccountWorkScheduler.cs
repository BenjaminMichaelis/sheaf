using System.Threading.Channels;

namespace Sheaf.Email;

/// <summary>
/// Runs account work after the response is sent, each item in its own DI scope. Anonymous endpoints hand it everything
/// that depends on whether an address has an account (the lookup, storing a code, creating the account), so the response
/// takes the same time and does the same database work either way.
/// </summary>
public interface IAccountWorkScheduler
{
    /// <summary>Accepts <paramref name="work"/> to run later with a scoped service provider.</summary>
    ValueTask EnqueueAsync(Func<IServiceProvider, Task> work);
}

/// <summary>An in-memory queue drained by <see cref="AccountWorkService"/>. Work still queued at shutdown is lost.</summary>
internal sealed partial class ChannelAccountWorkScheduler(ILogger<ChannelAccountWorkScheduler> logger) : IAccountWorkScheduler
{
    private readonly Channel<Func<IServiceProvider, Task>> _channel = Channel.CreateBounded<Func<IServiceProvider, Task>>(
        new BoundedChannelOptions(1000) { SingleReader = true });

    internal ChannelReader<Func<IServiceProvider, Task>> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Func<IServiceProvider, Task> work)
    {
        // Never wait for room: a full queue would make some responses slower than others.
        if (!_channel.Writer.TryWrite(work))
        {
            LogQueueFull(logger);
        }
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Account work queue is full; dropped a request.")]
    private static partial void LogQueueFull(ILogger logger);
}

internal sealed partial class AccountWorkService(
    ChannelAccountWorkScheduler queue, IServiceScopeFactory scopes, ILogger<AccountWorkService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Func<IServiceProvider, Task> work in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            try
            {
                await work(scope.ServiceProvider);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogWorkFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Queued account work failed.")]
    private static partial void LogWorkFailed(ILogger logger, Exception exception);
}
