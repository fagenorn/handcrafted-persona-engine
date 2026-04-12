using System.Runtime.CompilerServices;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// Smoothly interpolates toward a target using exponential decay.
/// </summary>
public struct AnimatedFloat
{
    public float Current;
    public float Target;
    public float Speed;

    public AnimatedFloat(float initial, float speed = 10f)
    {
        Current = initial;
        Target = initial;
        Speed = speed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(float dt)
    {
        Current = Lerp(Current, Target, 1f - MathF.Exp(-Speed * dt));
    }

    public readonly bool IsSettled => MathF.Abs(Current - Target) < 0.001f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>
/// Common easing functions. All take <paramref name="t"/> in [0, 1] and return a value in [0, 1].
/// </summary>
public static class Easing
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float t) => t * t * (3f - 2f * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(t - 1f, 3) + c1 * MathF.Pow(t - 1f, 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInOutQuad(float t) =>
        t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2) / 2f;

    public static float EaseOutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1f / d1)
        {
            return n1 * t * t;
        }
        else if (t < 2f / d1)
        {
            t -= 1.5f / d1;
            return n1 * t * t + 0.75f;
        }
        else if (t < 2.5f / d1)
        {
            t -= 2.25f / d1;
            return n1 * t * t + 0.9375f;
        }
        else
        {
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}

/// <summary>
/// Tracks a one-shot 0→1 animation.
/// </summary>
public struct OneShotAnimation
{
    private float _elapsed;
    private float _duration;
    private bool _active;

    public float Progress { get; private set; }

    public readonly bool IsActive => _active;

    public readonly bool IsComplete => _elapsed >= _duration;

    public void Start(float duration)
    {
        _elapsed = 0f;
        _duration = duration;
        _active = true;
        Progress = 0f;
    }

    public void Update(float dt)
    {
        if (!_active)
            return;

        _elapsed += dt;
        Progress = Math.Clamp(_duration > 0f ? _elapsed / _duration : 1f, 0f, 1f);

        if (_elapsed >= _duration)
        {
            _active = false;
        }
    }

    public void Reset()
    {
        _elapsed = 0f;
        _duration = 0f;
        _active = false;
        Progress = 0f;
    }
}

/// <summary>
/// Generates a continuous sine wave oscillation around a center value.
/// </summary>
public readonly struct SineOscillator
{
    public readonly float Center;
    public readonly float Amplitude;
    public readonly float FrequencyHz;

    public SineOscillator(float center, float amplitude, float frequencyHz)
    {
        Center = center;
        Amplitude = amplitude;
        FrequencyHz = frequencyHz;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Sample(float elapsed) =>
        Center + Amplitude * MathF.Sin(elapsed * FrequencyHz * MathF.Tau);
}
