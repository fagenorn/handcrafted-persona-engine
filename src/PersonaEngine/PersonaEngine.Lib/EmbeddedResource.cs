using System.Collections.Concurrent;
using System.ComponentModel;
using PersonaEngine.Lib.Utils.IO;

namespace PersonaEngine.Lib;

internal static class ModelUtils
{
    public static string GetModelPath(ModelType modelType)
    {
        var enumDescription = modelType.GetDescription();
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Resources", $"{enumDescription}");

        // Check file or dir exists
        if (!Path.Exists(fullPath))
        {
            throw new ApplicationException($"For {modelType} path {fullPath} doesn't exist");
        }

        return fullPath;
    }
}

internal static class PromptUtils
{
    private static readonly ConcurrentDictionary<string, string> PromptCache = new();

    private static string GetModelPath(string filename)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Prompts", filename);

        if (!File.Exists(fullPath))
        {
            throw new ApplicationException(
                $"Prompt file '{filename}' not found at path: {fullPath}"
            );
        }

        return fullPath;
    }

    public static bool TryGetPrompt(string filename, out string? prompt)
    {
        if (PromptCache.TryGetValue(filename, out prompt))
        {
            return true;
        }

        try
        {
            var fullPath = GetModelPath(filename);
            var promptContent = File.ReadAllText(fullPath);
            PromptCache.AddOrUpdate(filename, promptContent, (_, _) => promptContent);
            prompt = promptContent;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearCache()
    {
        PromptCache.Clear();
    }
}

public enum ModelType
{
    [Description("silero-vad/silero_vad_v5.onnx")]
    Silero,

    [Description("whisper/ggml-large-v3-turbo.bin")]
    WhisperGgmlTurbov3,

    [Description("whisper/ggml-tiny.en.bin")]
    WhisperGgmlTiny,

    [Description("profanity/badwords.txt")]
    BadWords,

    [Description("profanity/tiny_toxic_detector.onnx")]
    TinyToxic,

    [Description("profanity/tiny_toxic_detector_vocab.txt")]
    TinyToxicVocab,
}
