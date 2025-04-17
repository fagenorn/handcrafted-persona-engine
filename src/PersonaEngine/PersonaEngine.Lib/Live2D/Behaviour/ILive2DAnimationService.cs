using PersonaEngine.Lib.Audio.Player;
using PersonaEngine.Lib.Live2D.App;

namespace PersonaEngine.Lib.Live2D.Behaviour;

/// <summary>
///     Generic interface for services that animate Live2D model parameters
///     based on various input sources (audio, text content, emotions, etc.).
/// </summary>
public interface ILive2DAnimationService : IDisposable
{
    /// <summary>
    ///     Starts the animation processing. This activates the service to
    ///     begin listening for events and modifying parameters.
    /// </summary>
    void Start(LAppModel model);

    /// <summary>
    ///     Stops the animation processing. Parameter values might be reset to neutral
    ///     or held, depending on the implementation.
    /// </summary>
    void Stop();

    /// <summary>
    ///     Updates the animation state, typically called once per frame.
    ///     This method calculates and applies parameter values to the Live2D model
    ///     based on the current input data and timing.
    /// </summary>
    /// <param name="deltaTime">The time elapsed since the last frame/update, in seconds.</param>
    void Update(float deltaTime);
}