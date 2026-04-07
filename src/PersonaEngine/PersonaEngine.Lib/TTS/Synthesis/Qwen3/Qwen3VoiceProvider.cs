using PersonaEngine.Lib.IO;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

internal sealed class Qwen3VoiceProvider(IModelProvider modelProvider) : IQwen3VoiceProvider
{
    public IReadOnlyList<string> GetAvailableSpeakers()
    {
        var speakersDir = modelProvider.GetModelPath(IO.ModelType.Qwen3.Speakers);

        if (!Directory.Exists(speakersDir))
        {
            return [];
        }

        var speakers = Directory
            .GetFiles(speakersDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && name != "index")
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return speakers;
    }
}
