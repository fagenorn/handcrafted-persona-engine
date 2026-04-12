namespace PersonaEngine.Lib.UI.Rendering.RouletteWheel;

public partial class RouletteWheel
{
    /// <summary>
    ///     Defines the animation state for the wheel visibility transitions.
    /// </summary>
    public enum AnimationState
    {
        /// <summary>
        ///     Wheel is idle and not animating.
        /// </summary>
        Idle,

        /// <summary>
        ///     Wheel is animating to an enabled state.
        /// </summary>
        AnimatingIn,

        /// <summary>
        ///     Wheel is animating to a disabled state.
        /// </summary>
        AnimatingOut,
    }

    private float _animationCurrentScale = 1.0f;

    private float _animationDuration = 0.3f; // Animation duration in seconds

    private float _animationStartScale = 1.0f;

    private float _animationStartTime = 0f;

    private float _animationTargetScale = 1.0f;

    /// <summary>
    ///     Gets whether the wheel is currently enabled.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    ///     Gets the current animation state of the wheel.
    /// </summary>
    public AnimationState CurrentAnimationState { get; private set; } = AnimationState.Idle;

    /// <summary>
    ///     Gets whether the wheel is currently animating.
    /// </summary>
    public bool IsAnimating => CurrentAnimationState != AnimationState.Idle;

    /// <summary>
    ///     Enables the wheel with an optional animation.
    /// </summary>
    public void Enable()
    {
        if (IsEnabled && CurrentAnimationState == AnimationState.Idle)
        {
            return;
        }

        IsEnabled = true;

        if (!_config.CurrentValue.AnimateToggle)
        {
            CurrentAnimationState = AnimationState.Idle;
            _animationCurrentScale = 1.0f;

            return;
        }

        CurrentAnimationState = AnimationState.AnimatingIn;
        _animationStartTime = _time;
        _animationDuration = _config.CurrentValue.AnimationDuration;
        _animationStartScale = _animationCurrentScale;
        _animationTargetScale = 1.0f;
    }

    /// <summary>
    ///     Disables the wheel with an optional animation.
    /// </summary>
    public void Disable()
    {
        if (!IsEnabled && CurrentAnimationState == AnimationState.Idle)
        {
            return;
        }

        IsEnabled = false;

        if (!_config.CurrentValue.AnimateToggle)
        {
            CurrentAnimationState = AnimationState.Idle;
            _animationCurrentScale = 0.0f;

            return;
        }

        CurrentAnimationState = AnimationState.AnimatingOut;
        _animationStartTime = _time;
        _animationDuration = _config.CurrentValue.AnimationDuration;
        _animationStartScale = _animationCurrentScale;
        _animationTargetScale = 0.0f;
    }

    /// <summary>
    ///     Toggles the wheel's enabled state with an optional animation.
    /// </summary>
    /// <param name="animate">Whether to animate the transition.</param>
    /// <param name="duration">Duration of the animation in seconds.</param>
    public void Toggle()
    {
        if (IsEnabled)
        {
            Disable();
        }
        else
        {
            Enable();
        }
    }

    private void UpdateVisibilityAnimation()
    {
        if (CurrentAnimationState == AnimationState.Idle)
        {
            return;
        }

        var elapsed = _time - _animationStartTime;
        var progress = Math.Min(elapsed / _animationDuration, 1.0f);

        // Apply easing function to the progress
        var easedProgress = EaseOutBack(progress);

        // Calculate current scale based on progress
        _animationCurrentScale =
            _animationStartScale + (_animationTargetScale - _animationStartScale) * easedProgress;

        // Check if animation is complete
        if (progress >= 1.0f)
        {
            CurrentAnimationState = AnimationState.Idle;
            _animationCurrentScale = _animationTargetScale;
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;

        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }
}
