using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.IO;

namespace PersonaEngine.Lib.TTS.RVC;

public class RVCVoiceProvider : IRVCVoiceProvider
{
    private const int DefaultOutputSampleRate = 40000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<RVCVoiceProvider> _logger;

    private readonly IModelProvider _modelProvider;

    private readonly ConcurrentDictionary<string, RvcVoiceInfo> _voiceCache = new();

    private bool _disposed;

    public RVCVoiceProvider(IModelProvider ittsModelProvider, ILogger<RVCVoiceProvider> logger)
    {
        _modelProvider =
            ittsModelProvider ?? throw new ArgumentNullException(nameof(ittsModelProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public RvcVoiceInfo GetVoice(string voiceId)
    {
        if (string.IsNullOrEmpty(voiceId))
        {
            throw new ArgumentException("Voice ID cannot be null or empty", nameof(voiceId));
        }

        return GetVoiceInfo(voiceId);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAvailableVoices()
    {
        try
        {
            // Get voice directory
            var voicesDir = _modelProvider.GetModelPath(IO.ModelType.Rvc.Voices);

            if (!Directory.Exists(voicesDir))
            {
                _logger.LogWarning("Voices directory not found: {Path}", voicesDir);

                return [];
            }

            // Get all .onnx files
            var voiceFiles = Directory.GetFiles(voicesDir, "*.onnx");

            // Extract voice IDs from filenames
            var voiceIds = new List<string>(voiceFiles.Length);

            foreach (var file in voiceFiles)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                voiceIds.Add(id);

                // Pre-populate cache
                if (!_voiceCache.ContainsKey(id))
                {
                    var sampleRate = ReadOutputSampleRate(file);
                    _voiceCache[id] = new RvcVoiceInfo(file, sampleRate);
                }
            }

            _logger.LogInformation("Found {Count} available RVC voices", voiceIds.Count);

            return voiceIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available voices");

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _voiceCache.Clear();
        _disposed = true;

        await Task.CompletedTask;
    }

    private RvcVoiceInfo GetVoiceInfo(string voiceId)
    {
        if (_voiceCache.TryGetValue(voiceId, out var cached))
        {
            return cached;
        }

        var voicesDir = _modelProvider.GetModelPath(IO.ModelType.Rvc.Voices);

        var voicePath = Path.Combine(voicesDir, $"{voiceId}.onnx");

        if (!File.Exists(voicePath))
        {
            _logger.LogWarning("Voice file not found: {Path}", voicePath);

            throw new FileNotFoundException($"Voice file not found for {voiceId}", voicePath);
        }

        var outputSampleRate = ReadOutputSampleRate(voicePath);
        var info = new RvcVoiceInfo(voicePath, outputSampleRate);

        _voiceCache[voiceId] = info;

        _logger.LogInformation(
            "Loaded RVC voice {VoiceId} (output sample rate: {SampleRate})",
            voiceId,
            outputSampleRate
        );

        return info;
    }

    /// <summary>
    ///     Reads the output sample rate from a sidecar JSON file next to the ONNX model.
    ///     Expected file: {voiceName}.json with at least { "outputSampleRate": 40000 }.
    ///     Falls back to <see cref="DefaultOutputSampleRate" /> if the file is missing or invalid.
    /// </summary>
    private int ReadOutputSampleRate(string onnxPath)
    {
        var jsonPath = Path.ChangeExtension(onnxPath, ".json");

        if (!File.Exists(jsonPath))
        {
            _logger.LogDebug(
                "No sidecar metadata at {Path}, using default output sample rate {Rate}",
                jsonPath,
                DefaultOutputSampleRate
            );

            return DefaultOutputSampleRate;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var metadata = JsonSerializer.Deserialize<RvcVoiceMetadata>(json, JsonOptions);

            if (metadata?.OutputSampleRate is > 0)
            {
                return metadata.OutputSampleRate;
            }

            _logger.LogWarning(
                "Invalid outputSampleRate in {Path}, using default {Rate}",
                jsonPath,
                DefaultOutputSampleRate
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read sidecar metadata at {Path}, using default output sample rate {Rate}",
                jsonPath,
                DefaultOutputSampleRate
            );
        }

        return DefaultOutputSampleRate;
    }

    private sealed class RvcVoiceMetadata
    {
        public int OutputSampleRate { get; set; }
    }
}
