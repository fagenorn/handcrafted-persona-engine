using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Aggregation;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.Core.Conversation.Detection;
using PersonaEngine.Lib.UI.Common;

using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.Core.Conversation;

public class InternalConversationServicesManager : IStartupTask
{
    private readonly IUtteranceAggregator _aggregator;

    private readonly IBargeInDetector _detector;

    private readonly ILogger<InternalConversationServicesManager> _logger;

    private readonly IConversationOrchestrator _orchestrator;

    public InternalConversationServicesManager(IConversationOrchestrator orchestrator, IUtteranceAggregator aggregator, IBargeInDetector detector, ILogger<InternalConversationServicesManager> logger)
    {
        _orchestrator = orchestrator;
        _aggregator   = aggregator;
        _detector     = detector;
        _logger       = logger;
    }

    public void Execute(GL gl) { _ = StartAsync(CancellationToken.None); }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting internal conversation services...");
        // Assuming internal StartAsync methods exist and return Task
        await _aggregator.StartAsync(cancellationToken);
        await _detector.StartAsync(cancellationToken);
        await _orchestrator.StartAsync(cancellationToken); // Orchestrator needs Start/Stop if it runs ProcessEventsAsync
        _logger.LogInformation("Internal conversation services started.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping internal conversation services...");
        // Stop in reverse order?
        await _orchestrator.StopAsync();
        await _detector.StopAsync();
        await _aggregator.StopAsync();
        _logger.LogInformation("Internal conversation services stopped.");
    }

    public void Dispose() { _ = StopAsync(CancellationToken.None); }
}