using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.UI.Common;

using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.Core.Conversation.Adapters;

/// <summary>
///     An output adapter that writes received text content to the console/logger.
/// </summary>
public sealed class ConsoleOutputAdapter(
    IChannelRegistry              channelRegistry,
    ILogger<ConsoleOutputAdapter> logger)
    : IOutputAdapter, IStartupTask
{
    private CancellationTokenSource? _cts;

    private Task? _executionTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting ConsoleOutputAdapter...");
        _cts           = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = ProcessOutputLoop(channelRegistry.OutputRequests.Reader, _cts.Token);
        logger.LogInformation("ConsoleOutputAdapter started.");

        return Task.CompletedTask;
    }

    Task IOutputAdapter.StopAsync() { return StopAsync(CancellationToken.None); }

    public async ValueTask DisposeAsync()
    {
        logger.LogInformation("Disposing ConsoleOutputAdapter...");
        await StopAsync(CancellationToken.None);
        logger.LogInformation("ConsoleOutputAdapter disposed.");
    }

    public void Execute(GL gl) { StartAsync(CancellationToken.None); }

    private async Task ProcessOutputLoop(ChannelReader<ProcessOutputRequest> reader, CancellationToken cancellationToken)
    {
        logger.LogDebug("ConsoleOutputAdapter listening for output requests...");
        try
        {
            await foreach ( var request in reader.ReadAllAsync(cancellationToken) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Handle all requests by default, or filter based on hints/targets if needed
                if ( request.OutputTypeHint.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                     request.OutputTypeHint.Equals("Default", StringComparison.OrdinalIgnoreCase) )
                {
                    logger.LogInformation("Console Output --> {Content}", request.Content);
                }
                else
                {
                    logger.LogTrace("ConsoleOutputAdapter skipping request with OutputTypeHint: {Hint}", request.OutputTypeHint);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("ConsoleOutputAdapter processing loop cancelled.");
        }
        catch (ChannelClosedException)
        {
            logger.LogInformation("ConsoleOutputAdapter processing loop ended because the channel was closed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ConsoleOutputAdapter encountered an error in the processing loop.");
        }
        finally
        {
            logger.LogInformation("ConsoleOutputAdapter processing loop stopped.");
        }
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping ConsoleOutputAdapter...");
        if ( _cts == null || _executionTask == null )
        {
            return;
        }

        if ( !_cts.IsCancellationRequested )
        {
            await _cts.CancelAsync();
        }

        try
        {
            await _executionTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            logger.LogInformation("ConsoleOutputAdapter processing loop task completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("ConsoleOutputAdapter stop was cancelled by external token.");
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for ConsoleOutputAdapter processing loop to stop.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during ConsoleOutputAdapter stop.");
        }
        finally
        {
            _cts?.Dispose();
            _cts           = null;
            _executionTask = null;
            logger.LogInformation("ConsoleOutputAdapter stopped.");
        }
    }
}