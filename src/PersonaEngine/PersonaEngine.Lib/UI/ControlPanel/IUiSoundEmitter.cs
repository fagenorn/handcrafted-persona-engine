namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// Emits UI interaction events for optional audio feedback.
/// Default no-op implementation is silent.
/// </summary>
public interface IUiSoundEmitter
{
    void NavChanged() { }

    void ToggleFlipped() { }

    void SettingSaved() { }

    void PanelEntered() { }

    void PersonaStateChanged(PersonaUiState newState) { }
}

/// <summary>
/// Silent default implementation. Replace with an audio-playing
/// implementation to add UI sounds.
/// </summary>
public sealed class NoOpUiSoundEmitter : IUiSoundEmitter { }
