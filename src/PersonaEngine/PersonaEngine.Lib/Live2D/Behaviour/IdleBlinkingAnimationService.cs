using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Live2D.App;
using PersonaEngine.Lib.Live2D.Framework.Motion;

namespace PersonaEngine.Lib.Live2D.Behaviour;

/// <summary>
///     Manages Live2D model idle animations and custom automatic blinking.
///     Ensures a random idle animation from the 'Idle' group is playing if available
///     and handles periodic blinking by directly manipulating eye parameters.
///     Blinking operates independently of the main animation system.
/// </summary>
public sealed class IdleBlinkingAnimationService : ILive2DAnimationService
{
    private const string IDLE_MOTION_GROUP = "Idle";

    private const MotionPriority IDLE_MOTION_PRIORITY = MotionPriority.PriorityIdle;

    private const string PARAM_EYE_L_OPEN = "ParamEyeLOpen";

    private const string PARAM_EYE_R_OPEN = "ParamEyeROpen";

    private const float BLINK_INTERVAL_MIN_SECONDS = 1.5f;

    private const float BLINK_INTERVAL_MAX_SECONDS = 6.0f;

    private const float BLINK_CLOSING_DURATION = 0.06f;

    private const float BLINK_CLOSED_DURATION = 0.05f;

    private const float BLINK_OPENING_DURATION = 0.10f;

    private const float EYE_OPEN_VALUE = 1.0f;

    private const float EYE_CLOSED_VALUE = 0.0f;

    private readonly ILogger<IdleBlinkingAnimationService> _logger;

    private readonly Random _random = new();

    private float _blinkPhaseTimer = 0.0f;

    private BlinkState _currentBlinkState = BlinkState.Idle;

    private CubismMotionQueueEntry? _currentIdleMotionEntry;

    private bool _disposed = false;

    private bool _eyeParamsValid = false;

    private bool _isBlinking = false;

    private bool _isIdleAnimationAvailable = false;

    private bool _isStarted = false;

    private LAppModel? _model;

    private float _timeUntilNextBlink = 0.0f;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdleBlinkingAnimationService" /> class.
    /// </summary>
    /// <param name="logger">The logger instance for logging messages.</param>
    public IdleBlinkingAnimationService(ILogger<IdleBlinkingAnimationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SetNextBlinkInterval();
    }

    #region Asset Validation

    /// <summary>
    ///     Checks if the required assets (idle motion group, eye parameters) are available in the model.
    ///     Sets the internal flags `_isIdleAnimationAvailable` and `_eyeParamsValid`.
    /// </summary>
    private void ValidateModelAssets()
    {
        if ( _model == null )
        {
            return;
        }

        _logger.LogDebug("Validating configured idle/blinking mappings against model assets...");

        _isIdleAnimationAvailable = false;
        foreach ( var motionKey in _model.Motions )
        {
            var groupName = LAppModel.GetMotionGroupName(motionKey);
            if ( groupName == IDLE_MOTION_GROUP )
            {
                _isIdleAnimationAvailable = true;
            }
        }

        if ( _isIdleAnimationAvailable )
        {
            _logger.LogDebug("Idle motion group '{IdleGroup}' is configured and available in the model.", IDLE_MOTION_GROUP);
        }
        else
        {
            _logger.LogWarning("Configured IDLE_MOTION_GROUP ('{IdleGroup}') not found in model! Idle animations disabled.", IDLE_MOTION_GROUP);
        }

        _eyeParamsValid = false;

        try
        {
            // An invalid index (usually -1) means the parameter doesn't exist.
            var leftEyeIndex  = _model.Model.GetParameterIndex(PARAM_EYE_L_OPEN);
            var rightEyeIndex = _model.Model.GetParameterIndex(PARAM_EYE_R_OPEN);

            if ( leftEyeIndex >= 0 && rightEyeIndex >= 0 )
            {
                _eyeParamsValid = true;
                _logger.LogDebug("Required eye parameters found: '{ParamL}' (Index: {IndexL}), '{ParamR}' (Index: {IndexR}).",
                                 PARAM_EYE_L_OPEN, leftEyeIndex, PARAM_EYE_R_OPEN, rightEyeIndex);
            }
            else
            {
                if ( leftEyeIndex < 0 )
                {
                    _logger.LogWarning("Eye parameter '{ParamL}' not found in the model.", PARAM_EYE_L_OPEN);
                }

                if ( rightEyeIndex < 0 )
                {
                    _logger.LogWarning("Eye parameter '{ParamR}' not found in the model.", PARAM_EYE_R_OPEN);
                }

                _logger.LogWarning("Automatic blinking disabled because one or both eye parameters are missing.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while validating eye parameters. Blinking disabled.");
            _eyeParamsValid = false;
        }
    }

    #endregion

    private enum BlinkState
    {
        Idle,

        Closing,

        Closed,

        Opening
    }

    #region ILive2DAnimationService Implementation

    /// <summary>
    ///     Starts the service with the specified Live2D model.
    ///     Validates required assets (idle motion group, eye parameters) and initializes state.
    /// </summary>
    /// <param name="model">The Live2D model instance to animate.</param>
    /// <exception cref="ArgumentNullException">Thrown if model is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the service has been disposed.</exception>
    public void Start(LAppModel model)
    {
        ObjectDisposedException.ThrowIf(_disposed, typeof(IdleBlinkingAnimationService));

        if ( _isStarted )
        {
            _logger.LogWarning("Service already started. Call Stop() first if reconfiguration is needed.");

            return;
        }

        _model = model ?? throw new ArgumentNullException(nameof(model));

        ValidateModelAssets();

        SetNextBlinkInterval();
        _currentBlinkState = BlinkState.Idle;
        _isBlinking        = false;
        _blinkPhaseTimer   = 0f;
        ResetEyesToOpenState();

        _currentIdleMotionEntry = null;

        _isStarted = true;
        _logger.LogInformation("IdleBlinkingAnimationService started. Idle animations: {IdleStatus}, Blinking: {BlinkStatus}.",
                               _isIdleAnimationAvailable ? "Enabled" : "Disabled",
                               _eyeParamsValid ? "Enabled" : "Disabled");

        if ( _isIdleAnimationAvailable && _currentIdleMotionEntry == null )
        {
            TryStartIdleMotion();
        }
    }

    /// <summary>
    ///     Stops the service, resetting blinking and clearing the tracked idle motion.
    ///     Attempts to leave the eyes in an open state.
    /// </summary>
    public void Stop()
    {
        if ( !_isStarted || _disposed )
        {
            return;
        }

        _isStarted = false;

        ResetEyesToOpenState();

        _isBlinking        = false;
        _currentBlinkState = BlinkState.Idle;
        _blinkPhaseTimer   = 0f;

        _currentIdleMotionEntry = null;
        // Note: The motion itself isn't forcibly stopped here; Instead we
        // rely on the model's lifecycle management to handle it.

        _logger.LogInformation("IdleBlinkingAnimationService stopped.");
    }

    /// <summary>
    ///     Updates the idle animation and blinking state based on elapsed time.
    ///     Should be called once per frame.
    /// </summary>
    /// <param name="deltaTime">The time elapsed since the last update call, in seconds.</param>
    public void Update(float deltaTime)
    {
        if ( !_isStarted || _model?.Model == null || _disposed || deltaTime <= 0.0f )
        {
            return;
        }

        UpdateIdleMotion();

        if ( _eyeParamsValid )
        {
            UpdateBlinking(deltaTime);
        }
    }

    #endregion

    #region Core Logic: Idle

    private void UpdateIdleMotion()
    {
        if ( _currentIdleMotionEntry is { Finished: true } )
        {
            _logger.LogTrace("Tracked idle motion finished.");
            _currentIdleMotionEntry = null;
        }

        if ( _isIdleAnimationAvailable && _currentIdleMotionEntry == null )
        {
            TryStartIdleMotion();
        }
    }

    private void TryStartIdleMotion()
    {
        if ( _model == null )
        {
            return;
        }

        _logger.LogTrace("Attempting to start a new idle motion for group '{IdleGroup}'.", IDLE_MOTION_GROUP);

        try
        {
            var newEntry = _model.StartRandomMotion(IDLE_MOTION_GROUP, IDLE_MOTION_PRIORITY);

            _currentIdleMotionEntry = newEntry;

            if ( _currentIdleMotionEntry != null )
            {
                _logger.LogDebug("Successfully started idle motion.");
            }
            // _logger.LogWarning("Failed to start idle motion for group '{IdleGroup}'. The group might be empty or invalid.", IDLE_MOTION_GROUP);
            // Optionally disable idle animations if it fails consistently?
            // We don't because sometimes this occurs when another animation with the same or higher priority is also playing.
            // _isIdleAnimationAvailable = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while trying to start idle motion for group '{IdleGroup}'. Disabling idle animations.", IDLE_MOTION_GROUP);
            _isIdleAnimationAvailable = false;
            _currentIdleMotionEntry   = null;
        }
    }

    #endregion

    #region Core Logic: Blinking

    private void UpdateBlinking(float deltaTime)
    {
        try
        {
            if ( _isBlinking )
            {
                _blinkPhaseTimer += deltaTime;
                float eyeValue;

                switch ( _currentBlinkState )
                {
                    case BlinkState.Closing:
                        // Calculate progress towards closed state (1.0 -> 0.0)
                        eyeValue = Math.Max(EYE_CLOSED_VALUE, EYE_OPEN_VALUE - _blinkPhaseTimer / BLINK_CLOSING_DURATION);
                        if ( _blinkPhaseTimer >= BLINK_CLOSING_DURATION )
                        {
                            _currentBlinkState = BlinkState.Closed;
                            _blinkPhaseTimer   = 0f;
                            eyeValue           = EYE_CLOSED_VALUE;
                            _logger.LogTrace("Blink phase: Closed");
                        }

                        break;

                    case BlinkState.Closed:
                        eyeValue = EYE_CLOSED_VALUE;
                        if ( _blinkPhaseTimer >= BLINK_CLOSED_DURATION )
                        {
                            _currentBlinkState = BlinkState.Opening;
                            _blinkPhaseTimer   = 0f;
                            _logger.LogTrace("Blink phase: Opening");
                        }

                        break;

                    case BlinkState.Opening:
                        // Calculate progress towards open state (0.0 -> 1.0)
                        eyeValue = Math.Min(EYE_OPEN_VALUE, EYE_CLOSED_VALUE + _blinkPhaseTimer / BLINK_OPENING_DURATION);
                        if ( _blinkPhaseTimer >= BLINK_OPENING_DURATION )
                        {
                            _isBlinking        = false;
                            _currentBlinkState = BlinkState.Idle;
                            SetNextBlinkInterval();
                            eyeValue = EYE_OPEN_VALUE;
                            _logger.LogTrace("Blink finished. Next blink in {Interval:F2}s", _timeUntilNextBlink);
                            SetEyeParameters(eyeValue);

                            return; // Exit early as state is reset
                        }

                        break;

                    case BlinkState.Idle:
                    default:
                        _logger.LogWarning("Invalid blink state detected while _isBlinking was true. Resetting blink state.");
                        _isBlinking        = false;
                        _currentBlinkState = BlinkState.Idle;
                        SetNextBlinkInterval();
                        ResetEyesToOpenState();

                        return; // Exit early
                }

                SetEyeParameters(eyeValue);
            }
            else
            {
                _timeUntilNextBlink -= deltaTime;
                if ( _timeUntilNextBlink <= 0f )
                {
                    // Start a new blink cycle
                    _isBlinking        = true;
                    _currentBlinkState = BlinkState.Closing;
                    _blinkPhaseTimer   = 0f;
                    _logger.LogTrace("Starting blink.");
                    // Set initial closing value immediately? Or wait for next frame?
                    // Current logic waits for the next frame's update. This is usually fine.
                }
                // IMPORTANT: Do not call SetEyeParameters here when idle.
                // Other animations (like facial expressions) might be controlling the eyes.
                // We only take control during the blink itself.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during blinking update. Disabling blinking for safety.");
            _eyeParamsValid    = false; // Prevent further blinking attempts
            _isBlinking        = false;
            _currentBlinkState = BlinkState.Idle;
            ResetEyesToOpenState();
        }
    }

    /// <summary>
    ///     Sets the values for the left and right eye openness parameters.
    ///     Includes error handling to disable blinking if parameters become invalid.
    /// </summary>
    /// <param name="value">The value to set (typically between 0.0 and 1.0).</param>
    private void SetEyeParameters(float value)
    {
        // Redundant checks, but safe:
        if ( _model?.Model == null || !_eyeParamsValid )
        {
            return;
        }

        try
        {
            _model.Model.SetParameterValue(PARAM_EYE_L_OPEN, value);
            _model.Model.SetParameterValue(PARAM_EYE_R_OPEN, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set eye parameters (L:'{ParamL}', R:'{ParamR}') to value {Value}. Disabling blinking.",
                             PARAM_EYE_L_OPEN, PARAM_EYE_R_OPEN, value);

            _eyeParamsValid    = false;
            _isBlinking        = false;
            _currentBlinkState = BlinkState.Idle;
            // Don't try to reset eyes here, as the SetParameterValue call itself failed.
        }
    }

    private void ResetEyesToOpenState()
    {
        if ( _model?.Model != null && _eyeParamsValid )
        {
            _logger.LogTrace("Attempting to reset eyes to open state.");
            SetEyeParameters(EYE_OPEN_VALUE);
        }
        else
        {
            _logger.LogTrace("Skipping reset eyes to open state (Model null or eye params invalid).");
        }
    }

    /// <summary>
    ///     Calculates and sets the random time interval until the next blink should start.
    /// </summary>
    private void SetNextBlinkInterval()
    {
        _timeUntilNextBlink = (float)(_random.NextDouble() *
                                      (BLINK_INTERVAL_MAX_SECONDS - BLINK_INTERVAL_MIN_SECONDS) +
                                      BLINK_INTERVAL_MIN_SECONDS);
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose() { Dispose(true); }

    private void Dispose(bool disposing)
    {
        if ( !_disposed )
        {
            if ( disposing )
            {
                _logger.LogDebug("Disposing IdleBlinkingAnimationService...");
                Stop();
                _model = null;
                _logger.LogInformation("IdleBlinkingAnimationService disposed.");
            }

            _disposed = true;
        }
    }

    #endregion
}