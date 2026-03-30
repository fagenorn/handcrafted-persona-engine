using PersonaEngine.Lib.UI.Common;
using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.UI.GUI;

public class ConfigSectionRegistrationTask : IStartupTask
{
    private readonly ChatEditor _chatEditor;

    private readonly IConfigSectionRegistry _registry;

    private readonly RouletteWheelEditor _rouletteWheelEditor;

    private readonly TtsConfigEditor _ttsEditor;

    private readonly MicrophoneConfigEditor _microphoneConfigEditor;

    public ConfigSectionRegistrationTask(
        IConfigSectionRegistry registry,
        TtsConfigEditor ttsEditor,
        RouletteWheelEditor rouletteWheelEditor,
        ChatEditor chatEditor,
        MicrophoneConfigEditor microphoneConfigEditor
    )
    {
        _registry = registry;
        _ttsEditor = ttsEditor;
        _rouletteWheelEditor = rouletteWheelEditor;
        _chatEditor = chatEditor;
        _microphoneConfigEditor = microphoneConfigEditor;
    }

    public void Execute(GL _)
    {
        _registry.RegisterSection(_ttsEditor);
        _registry.RegisterSection(_rouletteWheelEditor);
        _registry.RegisterSection(_chatEditor);
        _registry.RegisterSection(_microphoneConfigEditor);
    }
}
