using System.Text.Json;
using System.Text.Json.Nodes;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI;

public class ConfigWriterTests : IDisposable
{
    private const int DebounceMs = 50;

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            if (File.Exists(f))
                File.Delete(f);
        }
    }

    private string CreateTempConfig(object? extraContent = null)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);

        var root = new JsonObject
        {
            ["Config"] = new JsonObject
            {
                ["Window"] = JsonSerializer.SerializeToNode(new WindowConfiguration()),
                ["Tts"] = new JsonObject
                {
                    ["Kokoro"] = JsonSerializer.SerializeToNode(new KokoroVoiceOptions()),
                },
            },
        };

        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        );
        return path;
    }

    private ConfigWriter CreateWriter(string path) => new(path, DebounceMs);

    [Fact]
    public async Task Write_TypedOptions_UpdatesJsonSection()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        var updated = new KokoroVoiceOptions { DefaultVoice = "af_nova", DefaultSpeed = 1.5f };
        writer.Write(updated);

        await Task.Delay(DebounceMs * 4);

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(
            "af_nova",
            json["Config"]!["Tts"]!["Kokoro"]!["DefaultVoice"]!.GetValue<string>()
        );
        Assert.Equal(1.5f, json["Config"]!["Tts"]!["Kokoro"]!["DefaultSpeed"]!.GetValue<float>());
    }

    [Fact]
    public async Task Write_PreservesOtherSections()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        // Write only Kokoro — Window section must survive
        writer.Write(new KokoroVoiceOptions { DefaultVoice = "af_nova" });

        await Task.Delay(DebounceMs * 4);

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        // Window section should still be present and intact
        Assert.NotNull(json["Config"]!["Window"]);
        var width = json["Config"]!["Window"]!["Width"]!.GetValue<int>();
        Assert.Equal(new WindowConfiguration().Width, width);
    }

    [Fact]
    public async Task Write_MultipleTypes_AppliesAll()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        var kokoro = new KokoroVoiceOptions { DefaultVoice = "af_sky" };
        var window = new WindowConfiguration { Width = 1280, Height = 720 };

        writer.Write(kokoro);
        writer.Write(window);

        await Task.Delay(DebounceMs * 4);

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(
            "af_sky",
            json["Config"]!["Tts"]!["Kokoro"]!["DefaultVoice"]!.GetValue<string>()
        );
        Assert.Equal(1280, json["Config"]!["Window"]!["Width"]!.GetValue<int>());
        Assert.Equal(720, json["Config"]!["Window"]!["Height"]!.GetValue<int>());
    }

    [Fact]
    public async Task Write_DebouncesBatchesRapidCalls()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        var originalJson = File.ReadAllText(path);

        // Fire 3 rapid writes — file should not change yet
        writer.Write(new KokoroVoiceOptions { DefaultVoice = "af_1" });
        writer.Write(new KokoroVoiceOptions { DefaultVoice = "af_2" });
        writer.Write(new KokoroVoiceOptions { DefaultVoice = "af_3" });

        // File should be unchanged immediately (debounce not fired yet)
        Assert.Equal(originalJson, File.ReadAllText(path));

        // Wait for debounce to fire
        await Task.Delay(DebounceMs * 4);

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        // Only the final value should be written
        Assert.Equal(
            "af_3",
            json["Config"]!["Tts"]!["Kokoro"]!["DefaultVoice"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task Write_SetsLastSaveTime()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        Assert.Null(writer.LastSaveTime);

        writer.Write(new KokoroVoiceOptions { DefaultVoice = "af_nova" });

        await Task.Delay(DebounceMs * 4);

        Assert.NotNull(writer.LastSaveTime);
        Assert.True(writer.LastSaveTime <= DateTime.UtcNow);
    }

    [Fact]
    public void Write_UnknownType_ThrowsInvalidOperation()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        Assert.Throws<InvalidOperationException>(() => writer.Write("some string"));
    }

    [Fact]
    public async Task AutoDiscovery_FindsAllNestedOptionsTypes()
    {
        var path = CreateTempConfig();
        using var writer = CreateWriter(path);

        // All these should resolve without throwing InvalidOperationException
        writer.Write(new KokoroVoiceOptions());
        writer.Write(new Qwen3TtsOptions());
        writer.Write(new RVCFilterOptions());
        writer.Write(new WindowConfiguration());
        writer.Write(new SubtitleOptions());
        writer.Write(new LlmOptions());

        // Wait for flush
        await Task.Delay(DebounceMs * 4);

        // If we got here, all types were successfully discovered and written
        Assert.NotNull(writer.LastSaveTime);
    }
}
