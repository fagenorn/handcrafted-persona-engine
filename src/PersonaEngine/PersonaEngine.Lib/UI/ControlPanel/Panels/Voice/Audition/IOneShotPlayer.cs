namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

public interface IOneShotPlayer
{
    Task PlayAsync(ReadOnlyMemory<float> pcm, int sampleRate, CancellationToken ct);
}
