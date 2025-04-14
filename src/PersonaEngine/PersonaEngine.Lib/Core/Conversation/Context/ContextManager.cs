using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Vision;

namespace PersonaEngine.Lib.Core.Conversation.Context;

public class ContextManager : IContextManager
{
    private readonly Lock _historyLock = new();

    private readonly List<Interaction> _interactionHistory = new();

    private readonly ILogger<ContextManager> _logger;

    private readonly ContextManagerOptions _options;

    private readonly IVisualQAService _visualQaService;

    public ContextManager(
        IVisualQAService                visualQaService,
        IOptions<ContextManagerOptions> options,
        ILogger<ContextManager>         logger)
    {
        _visualQaService = visualQaService ?? throw new ArgumentNullException(nameof(visualQaService));
        _options         = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger          = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task AddInteractionAsync(Interaction? interaction)
    {
        if ( interaction == null )
        {
            _logger.LogWarning("Attempted to add a null interaction to history.");

            return Task.CompletedTask;
        }

        lock (_historyLock)
        {
            _interactionHistory.Add(interaction);
            _logger.LogTrace("Added interaction to history: Role={Role}, SourceId={SourceId}, Content='{ContentSummary}'",
                             interaction.Role,
                             interaction.SourceId,
                             interaction.Content.Length > 50 ? interaction.Content[..50] + "..." : interaction.Content);

            // Trim history if MaxHistoryTurns is set
            if ( _options.MaxHistoryTurns > 0 && _interactionHistory.Count > _options.MaxHistoryTurns )
            {
                var removeCount = _interactionHistory.Count - _options.MaxHistoryTurns;
                _interactionHistory.RemoveRange(0, removeCount);
                _logger.LogDebug("Trimmed {Count} oldest interactions from history.", removeCount);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<Interaction>> GetConversationHistoryAsync(string? sourceId = null, int? maxTurns = null)
    {
        lock (_historyLock)
        {
            IEnumerable<Interaction> history = _interactionHistory;

            // Apply filtering (filtering by sourceId might not be common for LLM context, but available)
            if ( !string.IsNullOrEmpty(sourceId) )
            {
                // This interpretation might need refinement - does it mean only turns *from* sourceId,
                // or turns relevant *to* a conversation involving sourceId? Assuming the former for now.
                history = history.Where(i => i.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
            }

            // Apply turn limit (take most recent)
            var turnsToTake = maxTurns ?? _options.MaxHistoryTurns;
            if ( turnsToTake > 0 )
            {
                history = history.TakeLast(turnsToTake);
            }

            // Return a copy to prevent external modification
            return Task.FromResult(history.ToList().AsEnumerable());
        }
    }

    public async Task<LlmContextSnapshot> GetContextSnapshotAsync(string? sourceId = null)
    {
        // Get history (use internal limit by default)
        // ReSharper disable once InconsistentlySynchronizedField
        var history = await GetConversationHistoryAsync(sourceId, _options.MaxHistoryTurns);

        // Get latest screen caption directly from the service
        var screenCaption = _visualQaService.ScreenCaption; // Assuming this property provides the latest caption

        if ( !string.IsNullOrEmpty(screenCaption) )
        {
            _logger.LogDebug("Retrieved screen caption (Length: {Length}) for context snapshot.", screenCaption.Length);
        }

        var snapshot = new LlmContextSnapshot(
                                              history,
                                              screenCaption,
                                              _options.Topics.AsReadOnly(), // Use configured topics
                                              _options.SystemContext        // Use configured system context
                                             );

        _logger.LogDebug("Generated LlmContextSnapshot. HistoryTurns={Count}, HasCaption={HasCaption}",
                         snapshot.History.Count(), snapshot.ScreenCaption != null);

        return snapshot;
    }
}