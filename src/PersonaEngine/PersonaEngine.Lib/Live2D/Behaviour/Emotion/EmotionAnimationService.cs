using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.Live2D.App;
using PersonaEngine.Lib.Live2D.Framework.Motion;

namespace PersonaEngine.Lib.Live2D.Behaviour.Emotion;

/// <summary>
///     Manages Live2D model animations and expressions synchronized with detected speech emotions from audio playback.
/// </summary>
/// <remarks>
///     Responsibilities include:
///     - Mapping detected emotion events (e.g., emojis from <see cref="IEmotionService" />) to Live2D expressions and
///     motions.
///     - Applying expressions for a configurable duration (<see cref="EXPRESSION_HOLD_DURATION_SECONDS" />) before
///     potentially reverting to neutral.
///     - Playing specific emotion-driven motions or a neutral "talking" motion during audio playback.
///     - Handling emotion timing based on the audio player's current position.
///     - Persisting the last triggered expression/motion until the hold time expires or it's replaced.
///     This service assumes LipSync, Blinking, and Idle animations are managed by other services with appropriate
///     priorities.
///     It requires an <see cref="IEmotionService" /> for timed emotion data and an
///     <see cref="IAudioProgressNotifier" /> for playback state and time.
/// </remarks>
public class EmotionAnimationService : ILive2DAnimationService
{
    public EmotionAnimationService(ILogger<EmotionAnimationService> logger, IAudioProgressNotifier audioProgressNotifier, IEmotionService emotionService)
    {
        _logger                = logger;
        _audioProgressNotifier = audioProgressNotifier;
        _emotionService        = emotionService;

        SubscribeToAudioProgressNotifier();
    }

    #region Nested Types

    private record EmotionMapping(string? ExpressionId, string? MotionGroup);

    #endregion

    #region Configuration

    // How long to hold an expression after it's triggered before reverting to neutral (if playback stops or no new emotion overrides it).
    private const float EXPRESSION_HOLD_DURATION_SECONDS = 3.0f;

    // Expression ID for the neutral state.
    private const string NEUTRAL_EXPRESSION_ID = "neutral";

    // Motion group used for the default talking animation when no specific emotion motion is active.
    private const string NEUTRAL_TALKING_MOTION_GROUP = "Talking";

    // Priority for motions triggered by specific emotions. Should typically be high to override idle/neutral talking.
    private const MotionPriority EMOTION_MOTION_PRIORITY = MotionPriority.PriorityForce;

    // Priority for the neutral talking motion. Should be lower than emotion motions but higher than idle.
    private const MotionPriority NEUTRAL_TALKING_MOTION_PRIORITY = MotionPriority.PriorityNormal;

    // Defines the mapping between detected emotion identifiers (e.g., emojis) and Live2D assets.
    // TODO: Consider loading this from an external configuration file for customization.
    private static readonly Dictionary<string, EmotionMapping> EmotionMap = new() {
                                                                                      // Positive Emotions
                                                                                      { "😊", new EmotionMapping("happy", "Happy") },
                                                                                      { "🤩", new EmotionMapping("excited_star", "Excited") },
                                                                                      { "😎", new EmotionMapping("cool", "Confident") },
                                                                                      { "😏", new EmotionMapping("smug", "Confident") },
                                                                                      { "💪", new EmotionMapping("determined", "Confident") },
                                                                                      // Reactive Emotions
                                                                                      { "😳", new EmotionMapping("embarrassed", "Nervous") },
                                                                                      { "😲", new EmotionMapping("shocked", "Surprised") },
                                                                                      { "🤔", new EmotionMapping("thinking", "Thinking") },
                                                                                      { "👀", new EmotionMapping("suspicious", "Thinking") },
                                                                                      // Negative Emotions
                                                                                      { "😤", new EmotionMapping("frustrated", "Angry") },
                                                                                      { "😢", new EmotionMapping("sad", "Sad") },
                                                                                      { "😅", new EmotionMapping("awkward", "Nervous") },
                                                                                      { "🙄", new EmotionMapping("dismissive", "Annoyed") },
                                                                                      // Expressive Reactions
                                                                                      { "💕", new EmotionMapping("adoring", "Happy") },
                                                                                      { "😂", new EmotionMapping("laughing", "Happy") },
                                                                                      { "🔥", new EmotionMapping("passionate", "Excited") },
                                                                                      { "✨", new EmotionMapping("sparkle", "Happy") }
                                                                                      // Neutral (can map if specific neutral actions are needed, otherwise handled implicitly)
                                                                                      // { "😐", new EmotionMapping(NEUTRAL_EXPRESSION_ID, null) } // Example if needed
                                                                                  };

    #endregion

    #region Dependencies

    private readonly ILogger<EmotionAnimationService> _logger;

    private readonly IAudioProgressNotifier _audioProgressNotifier;

    private readonly IEmotionService _emotionService;

    #endregion

    #region State

    private LAppModel? _model;

    private readonly List<EmotionTiming> _activeEmotions = new();

    private readonly HashSet<string> _availableExpressions = new();

    private readonly HashSet<string> _availableMotionGroups = new();

    private int _currentEmotionIndex = -1;

    private string? _triggeredEmotionEmoji = null;

    private string? _activeExpressionId = null;

    private float _timeSinceExpressionSet = 0.0f;

    private CubismMotionQueueEntry? _currentEmotionMotionEntry = null;

    private CubismMotionQueueEntry? _neutralTalkingMotionEntry = null;

    private bool _isStarted = false;

    private bool _isPlaying = false;

    private bool _disposed = false;

    #endregion

    #region ILive2DAnimationService Implementation

    private void SubscribeToAudioProgressNotifier()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UnsubscribeFromCurrentNotifier();

        _audioProgressNotifier.ChunkPlaybackStarted += HandleChunkStarted;
        _audioProgressNotifier.ChunkPlaybackEnded   += HandleChunkEnded;
        _audioProgressNotifier.PlaybackProgress     += HandleProgress;
    }

    private void UnsubscribeFromCurrentNotifier()
    {
        _audioProgressNotifier.ChunkPlaybackStarted -= HandleChunkStarted;
        _audioProgressNotifier.ChunkPlaybackEnded   -= HandleChunkEnded;
        _audioProgressNotifier.PlaybackProgress     -= HandleProgress;
    }

    public void Start(LAppModel model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _model = model;

        ValidateModelAssets();

        _isStarted = true;
        ResetEmotionState(true);
        _logger.LogInformation("EmotionAnimationService started for model.");
    }

    public void Stop()
    {
        _isStarted = false;
        ResetEmotionState(true);
        _logger.LogInformation("EmotionAnimationService stopped.");
    }

    #endregion

    #region Update Logic

    public void Update(float deltaTime)
    {
        if ( deltaTime <= 0.0f || _disposed || !_isStarted || _model == null )
        {
            return;
        }

        _timeSinceExpressionSet += deltaTime;
        CheckExpressionTimeout();

        if ( _isPlaying )
        {
            ManageNeutralTalkingMotion();
        }
        // Optionally ensure neutral talking motion is stopped/faded out if needed when not playing,
        // though priority system might handle this.
        // If _neutralTalkingMotionEntry exists and isn't finished, it might need explicit stopping
        // depending on desired behavior when audio stops mid-talk.
        // For now, we assume idle animations (handled elsewhere) will override it.
    }

    private void CheckExpressionTimeout()
    {
        if ( _activeExpressionId == null || _activeExpressionId == NEUTRAL_EXPRESSION_ID ||
             !(_timeSinceExpressionSet >= EXPRESSION_HOLD_DURATION_SECONDS) )
        {
            return;
        }

        _logger.LogTrace("Expression '{ExpressionId}' hold time expired ({HoldDuration}s). Reverting to neutral.",
                         _activeExpressionId, EXPRESSION_HOLD_DURATION_SECONDS);

        ApplyNeutralExpression();
    }

    private void ManageNeutralTalkingMotion()
    {
        if ( _model == null )
        {
            return;
        }

        var isEmotionMotionActive = _currentEmotionMotionEntry is { Finished: false };

        if ( isEmotionMotionActive )
        {
            if ( _currentEmotionMotionEntry is { Finished: true } )
            {
                _currentEmotionMotionEntry = null;
            }

            return;
        }

        if ( _currentEmotionMotionEntry?.Finished ?? false )
        {
            _currentEmotionMotionEntry = null;
        }

        var shouldStartNeutralTalk = _availableMotionGroups.Contains(NEUTRAL_TALKING_MOTION_GROUP) &&
                                     (_neutralTalkingMotionEntry == null || _neutralTalkingMotionEntry.Finished);

        if ( !shouldStartNeutralTalk )
        {
            return;
        }

        _logger.LogTrace("Starting neutral talking motion (group: {MotionGroup}).", NEUTRAL_TALKING_MOTION_GROUP);
        _neutralTalkingMotionEntry = _model.StartRandomMotion(NEUTRAL_TALKING_MOTION_GROUP, NEUTRAL_TALKING_MOTION_PRIORITY);
        if ( _neutralTalkingMotionEntry == null )
        {
            _logger.LogDebug("Could not start neutral talking motion for group '{MotionGroup}'.", NEUTRAL_TALKING_MOTION_GROUP);
        }
    }

    private void UpdateEmotionBasedOnTime(float currentTime)
    {
        if ( _activeEmotions.Count == 0 || !_isPlaying )
        {
            return;
        }

        var targetEmotionIndex = -1;
        for ( var i = 0; i < _activeEmotions.Count; i++ )
        {
            if ( _activeEmotions[i].Timestamp <= currentTime )
            {
                targetEmotionIndex = i;
            }
            else
            {
                break;
            }
        }

        if ( targetEmotionIndex != -1 && targetEmotionIndex != _currentEmotionIndex )
        {
            _logger.LogTrace("Emotion change detected at T={CurrentTime:F3}. New index: {NewIndex}, Old index: {OldIndex}",
                             currentTime, targetEmotionIndex, _currentEmotionIndex);

            _currentEmotionIndex = targetEmotionIndex;
            var newEmotionEmoji = _activeEmotions[_currentEmotionIndex].Emotion;
            ApplyEmotion(newEmotionEmoji);
        }
        // If targetEmotionIndex is -1 (currentTime is before the first emotion),
        // or if the index hasn't changed, do nothing. The state persists.
    }

    private void ApplyEmotion(string? emotionEmoji)
    {
        if ( !_isStarted || _model == null )
        {
            return;
        }

        if ( emotionEmoji == _triggeredEmotionEmoji )
        {
            return;
        }

        _logger.LogDebug("Applying triggered emotion: {Emotion}", emotionEmoji ?? "Neutral (explicit)");
        _triggeredEmotionEmoji = emotionEmoji;

        EmotionMapping? mapping      = null;
        var             foundMapping = emotionEmoji != null && EmotionMap.TryGetValue(emotionEmoji, out mapping);

        var targetExpressionId = foundMapping ? mapping?.ExpressionId : NEUTRAL_EXPRESSION_ID;
        SetExpression(targetExpressionId);

        if ( _currentEmotionMotionEntry?.Finished ?? false )
        {
            _currentEmotionMotionEntry = null;
        }

        var targetMotionGroup = foundMapping ? mapping?.MotionGroup : null;

        if ( !string.IsNullOrEmpty(targetMotionGroup) )
        {
            if ( _availableMotionGroups.Contains(targetMotionGroup) )
            {
                _logger.LogTrace("Attempting to start emotion motion from group: {MotionGroup} with priority {Priority}.",
                                 targetMotionGroup, EMOTION_MOTION_PRIORITY);

                var newMotionEntry = _model.StartRandomMotion(targetMotionGroup, EMOTION_MOTION_PRIORITY);
                if ( newMotionEntry != null )
                {
                    _currentEmotionMotionEntry = newMotionEntry;
                    _neutralTalkingMotionEntry = null;
                    _logger.LogTrace("Emotion motion started successfully.");
                }
                else
                {
                    _logger.LogWarning("Could not start motion for group '{MotionGroup}'. Is the group empty or definition invalid?", targetMotionGroup);
                    _currentEmotionMotionEntry = null;
                }
            }
            else
            {
                _logger.LogWarning("Motion group '{MotionGroup}' for emotion '{Emotion}' not found in the current model.", targetMotionGroup, emotionEmoji);
                _currentEmotionMotionEntry = null;
            }
        }
        else
        {
            _currentEmotionMotionEntry = null;
            _logger.LogTrace("No specific motion group mapped for emotion '{Emotion}'.", emotionEmoji ?? "Neutral");
        }
    }

    private void SetExpression(string? expressionId)
    {
        if ( !_isStarted || _model == null )
        {
            return;
        }

        var actualExpressionId = string.IsNullOrEmpty(expressionId) ? NEUTRAL_EXPRESSION_ID : expressionId;

        if ( string.IsNullOrEmpty(NEUTRAL_EXPRESSION_ID) && string.IsNullOrEmpty(expressionId) )
        {
            actualExpressionId = null;
        }

        if ( actualExpressionId == _activeExpressionId )
        {
            return;
        }

        var isTargetNeutral = string.IsNullOrEmpty(actualExpressionId) || actualExpressionId == NEUTRAL_EXPRESSION_ID;

        if ( isTargetNeutral )
        {
            ApplyNeutralExpression();
        }
        else if ( _availableExpressions.Contains(actualExpressionId!) )
        {
            _logger.LogTrace("Setting expression: {ExpressionId}", actualExpressionId);
            _model.SetExpression(actualExpressionId!);
            _activeExpressionId     = actualExpressionId;
            _timeSinceExpressionSet = 0.0f;
        }
        else
        {
            _logger.LogWarning("Expression '{ExpressionId}' not found in model. Applying neutral instead.", actualExpressionId);
            ApplyNeutralExpression(); // Fallback to neutral
        }
    }

    private void ApplyNeutralExpression()
    {
        if ( !_isStarted || _model == null )
        {
            return;
        }

        var neutralToApply = string.IsNullOrEmpty(NEUTRAL_EXPRESSION_ID) ? null : NEUTRAL_EXPRESSION_ID;

        if ( _activeExpressionId != neutralToApply )
        {
            if ( neutralToApply != null || _activeExpressionId != null ) // Avoid logging null -> null
            {
                _logger.LogTrace("Setting neutral expression (was: {PreviousExpression}).", _activeExpressionId ?? "model default");
            }

            if ( neutralToApply != null )
            {
                if ( _availableExpressions.Contains(neutralToApply) )
                {
                    _model.SetExpression(neutralToApply);
                }
                else
                {
                    _logger.LogError("Configured NEUTRAL_EXPRESSION_ID '{NeutralId}' is not available in the model, cannot apply.", NEUTRAL_EXPRESSION_ID);
                    neutralToApply = null;
                }
            }
            else
            {
                _logger.LogTrace("No neutral expression configured. Model may revert to default.");
            }

            _activeExpressionId = neutralToApply;
        }

        _timeSinceExpressionSet = 0.0f;
    }

    #endregion

    #region Event Handlers

    private void HandleChunkStarted(object? sender, AudioChunkPlaybackStartedEvent e)
    {
        if ( !_isStarted || _model == null )
        {
            return;
        }

        _logger.LogTrace("Audio Chunk Playback Started for segment {SegmentId}.", e.Chunk.Id);
        _isPlaying = true;
        ResetEmotionState(false);

        try
        {
            var emotions = e.Chunk.GetEmotions(_emotionService);

            if ( emotions.Any() )
            {
                _activeEmotions.AddRange(emotions.OrderBy(em => em.Timestamp));
                _logger.LogDebug("Loaded {Count} timed emotions for the segment.", _activeEmotions.Count);

                UpdateEmotionBasedOnTime(0.0f);
            }
            else
            {
                _logger.LogDebug("No timed emotions found for segment {SegmentId}.", e.Chunk.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving or processing emotions for audio segment {SegmentId}.", e.Chunk.Id);
        }
    }

    private void HandleChunkEnded(object? sender, AudioChunkPlaybackEndedEvent e)
    {
        if ( !_isStarted )
        {
            return;
        }

        _logger.LogTrace("Audio Chunk Playback Ended.");
        _isPlaying = false;
        _activeEmotions.Clear();
        _currentEmotionIndex = -1;

        // Expression timeout is handled by Update loop (CheckExpressionTimeout).
        // Motions will finish naturally or be overridden by idle animations (assumed).
        // Resetting _triggeredEmotionEmoji ensures the next chunk starts fresh.
        // Keep _activeExpressionId as is until timeout or next emotion.
        // _triggeredEmotionEmoji = null; // Reset last triggered emoji immediately? Or let timeout handle expression? Let timeout handle.
    }

    private void HandleProgress(object? sender, AudioPlaybackProgressEvent e)
    {
        if ( !_isStarted || !_isPlaying || _model == null )
        {
            return;
        }

        UpdateEmotionBasedOnTime((float)e.CurrentPlaybackTime.TotalSeconds);
    }

    #endregion

    #region State Management & Validation

    private void ResetEmotionState(bool forceNeutral)
    {
        _activeEmotions.Clear();
        _currentEmotionIndex = -1;

        if ( forceNeutral )
        {
            _currentEmotionMotionEntry = null;
        }

        if ( forceNeutral )
        {
            _neutralTalkingMotionEntry = null;
        }

        if ( forceNeutral )
        {
            _logger.LogTrace("Forcing neutral state on reset.");
            ApplyNeutralExpression();
            _triggeredEmotionEmoji = null;
        }

        _logger.LogTrace("Emotion state reset (Active emotions cleared. Forced neutral: {ForceNeutral}).", forceNeutral);
    }

    private void ValidateModelAssets()
    {
        if ( _model == null )
        {
            return;
        }

        _logger.LogDebug("Validating configured emotion mappings against model assets...");

        _availableExpressions.Clear();
        _availableMotionGroups.Clear();

        var modelExpressions = new HashSet<string>(_model.Expressions ?? Enumerable.Empty<string>());
        _logger.LogTrace("Model contains {Count} expressions.", modelExpressions.Count);

        var modelMotionGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ( var motionKey in _model.Motions ?? Enumerable.Empty<string>() )
        {
            var groupName = LAppModel.GetMotionGroupName(motionKey); // Use static helper
            if ( !string.IsNullOrEmpty(groupName) )
            {
                modelMotionGroups.Add(groupName);
            }
        }

        _logger.LogTrace("Model contains {Count} unique motion groups (e.g., {Examples}).",
                         modelMotionGroups.Count, string.Join(", ", modelMotionGroups.Take(5)) + (modelMotionGroups.Count > 5 ? "..." : ""));

        foreach ( var (emotion, mapping) in EmotionMap )
        {
            if ( !string.IsNullOrEmpty(mapping.ExpressionId) )
            {
                if ( modelExpressions.Contains(mapping.ExpressionId) )
                {
                    _availableExpressions.Add(mapping.ExpressionId);
                }
                else
                {
                    _logger.LogWarning("Validation: Expression '{ExpressionId}' (for emotion '{Emotion}') not found.", mapping.ExpressionId, emotion);
                }
            }

            if ( string.IsNullOrEmpty(mapping.MotionGroup) )
            {
                continue;
            }

            if ( modelMotionGroups.Contains(mapping.MotionGroup) )
            {
                _availableMotionGroups.Add(mapping.MotionGroup);
            }
            else
            {
                _logger.LogWarning("Validation: Motion group '{MotionGroup}' (for emotion '{Emotion}') not found.", mapping.MotionGroup, emotion);
            }
        }

        if ( !string.IsNullOrEmpty(NEUTRAL_EXPRESSION_ID) )
        {
            if ( modelExpressions.Contains(NEUTRAL_EXPRESSION_ID) )
            {
                _availableExpressions.Add(NEUTRAL_EXPRESSION_ID);
            }
            else
            {
                _logger.LogWarning("Configured NEUTRAL_EXPRESSION_ID ('{NeutralId}') not found!", NEUTRAL_EXPRESSION_ID);
            }
        }

        if ( !string.IsNullOrEmpty(NEUTRAL_TALKING_MOTION_GROUP) )
        {
            if ( modelMotionGroups.Contains(NEUTRAL_TALKING_MOTION_GROUP) )
            {
                _availableMotionGroups.Add(NEUTRAL_TALKING_MOTION_GROUP);
            }
            else
            {
                _logger.LogWarning("Configured NEUTRAL_TALKING_MOTION_GROUP ('{Group}') not found.", NEUTRAL_TALKING_MOTION_GROUP);
            }
        }

        _logger.LogDebug("Validation complete. Usable Expressions: {ExprCount}, Usable Motion Groups: {MotionCount}",
                         _availableExpressions.Count, _availableMotionGroups.Count);
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if ( !_disposed )
        {
            if ( disposing )
            {
                _logger.LogDebug("Disposing EmotionAnimationService...");
                Stop();
                UnsubscribeFromCurrentNotifier();
                _activeEmotions.Clear();
                _availableExpressions.Clear();
                _availableMotionGroups.Clear();
                _model = null;
                _logger.LogInformation("EmotionAnimationService disposed.");
            }

            _disposed = true;
        }
    }

    ~EmotionAnimationService() { Dispose(false); }

    #endregion
}