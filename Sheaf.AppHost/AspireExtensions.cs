
using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sheaf.AppHost;

internal static partial class AspireExtensions
{
    extension(ExecuteCommandContext context)
    {
        public ILogger GetResourceLogger<T>(IResourceBuilder<T> resourceBuilder)
        where T : IResource
        {
            return GetResourceLogger(context.Services, resourceBuilder.Resource);
        }
    }

    extension(IServiceProvider serviceProvider)
    {
        public ILogger GetResourceLogger(IResource resource)
        {
            var loggerService = serviceProvider.GetRequiredService<ResourceLoggerService>();
            return loggerService.GetLogger(resource);
        }
    }

    extension(IResource resource)
    {
        public async Task<bool> ExecuteProcessAsync(IServiceProvider serviceProvider, ProcessStartInfo processInfo, CancellationToken cancellationToken = default)
        {
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;

            var logger = serviceProvider.GetResourceLogger(resource);
            if (logger.IsEnabled(LogLevel.Information))
            {
                RunningProcessLog(logger, processInfo.ToDisplayString(), null);
            }
            try
            {
                if (Process.Start(processInfo) is { } process)
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            LogProcessOutput(logger, e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            LogProcessErrorOutput(logger, e.Data);
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);
                    if (process.ExitCode != 0)
                    {
                        LogProcessExitCode(logger, Path.GetFileName(processInfo.FileName), process.ExitCode);
                        return false;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                LogProcessFailure(logger, e, processInfo.FileName);
                return false;
            }
        }
    }

    extension<T>(IResourceBuilder<T> resourceBuilder) where T : IResource
    {
        public Task<bool> ExecuteProcessAsync(ExecuteCommandContext context, ProcessStartInfo processInfo)
        {
            return resourceBuilder.Resource.ExecuteProcessAsync(context.Services, processInfo, context.CancellationToken);
        }

    }

    extension<T>(IResourceBuilder<T> resourceBuilder) where T : IResource, IResourceWithWaitSupport, IResourceWithEnvironment
    {
        public IResourceBuilder<T> WithDependency(IResourceBuilder<IResourceWithConnectionString> dependency,
            string? connectionName = null)
        {
            resourceBuilder.WaitFor(dependency);
            return resourceBuilder.WithReference(dependency, connectionName);
        }
        public IResourceBuilder<T> WithDependency(IResourceBuilder<IResourceWithServiceDiscovery> dependency)
        {
            resourceBuilder.WaitFor(dependency);
            return resourceBuilder.WithReference(dependency);
        }
    }

    extension(ProcessStartInfo startInfo)
    {
        public string ToDisplayString()
        {
            StringBuilder rv = new();

            rv.Append(startInfo.FileName);
            if (!string.IsNullOrWhiteSpace(startInfo.Arguments))
            {
                rv.Append(' ');
                rv.Append(startInfo.Arguments);
            }
            if (startInfo.ArgumentList?.Count > 0)
            {
                foreach (var arg in startInfo.ArgumentList)
                {
                    rv.Append(' ');
                    rv.Append(arg);
                }
            }

            if (!string.IsNullOrWhiteSpace(startInfo.WorkingDirectory))
            {
                rv.Append(" (");
                rv.Append(Path.GetFullPath(startInfo.WorkingDirectory));
                rv.Append(')');
            }

            return rv.ToString();
        }
    }

    private static readonly Action<ILogger, string, Exception?> RunningProcessLog =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(0), "Running: {ProcessInfo}");

    [LoggerMessage(Level = LogLevel.Information, Message = "{Output}")]
    private static partial void LogProcessOutput(ILogger logger, string? output);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Output}")]
    private static partial void LogProcessErrorOutput(ILogger logger, string? output);

    [LoggerMessage(Level = LogLevel.Error, Message = "{ProcessName} process exited with code {ExitCode}")]
    private static partial void LogProcessExitCode(ILogger logger, string? processName, int exitCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to run '{ProcessName}' process")]
    private static partial void LogProcessFailure(ILogger logger, Exception exception, string processName);
}
