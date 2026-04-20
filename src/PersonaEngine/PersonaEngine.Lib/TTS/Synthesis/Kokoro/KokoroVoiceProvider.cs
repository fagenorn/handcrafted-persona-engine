using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.IO;

namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

/// <summary>
///     Voice provider implementation to manage voice embeddings
/// </summary>
public class KokoroVoiceProvider : IKokoroVoiceProvider
{
    private readonly ITtsCache _cache;

    private readonly ILogger<KokoroVoiceProvider> _logger;

    private readonly IModelProvider _modelProvider;

    private readonly ConcurrentDictionary<string, string> _voicePaths = new();

    private bool _disposed;

    public KokoroVoiceProvider(
        IModelProvider ittsModelProvider,
        ITtsCache cache,
        ILogger<KokoroVoiceProvider> logger
    )
    {
        _modelProvider =
            ittsModelProvider ?? throw new ArgumentNullException(nameof(ittsModelProvider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<KokoroVoiceEmbedding> GetVoiceAsync(
        string voiceId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(voiceId))
        {
            throw new ArgumentException("Voice ID cannot be null or empty", nameof(voiceId));
        }

        try
        {
            // Use cache to avoid repeated loading
            return await _cache.GetOrAddAsync(
                $"voice_{voiceId}",
                async ct2 =>
                {
                    _logger.LogDebug("Loading voice data for {VoiceId}", voiceId);

                    // Get voice directory
                    var voiceDirPath = GetVoicePath(voiceId);

                    // Load binary data
                    var bytes = await File.ReadAllBytesAsync(voiceDirPath, ct2);

                    // Convert to float array
                    var embedding = new float[bytes.Length / sizeof(float)];
                    Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);

                    _logger.LogInformation(
                        "Loaded voice {VoiceId} with {EmbeddingSize} embedding dimensions",
                        voiceId,
                        embedding.Length
                    );

                    return new KokoroVoiceEmbedding(voiceId, embedding);
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading voice {VoiceId}", voiceId);

            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAvailableVoices()
    {
        try
        {
            // Get voice directory
            var voicesDir = _modelProvider.GetModelPath(IO.ModelType.Kokoro.Voices);

            if (!Directory.Exists(voicesDir))
            {
                _logger.LogWarning("Voices directory not found: {Path}", voicesDir);

                return [];
            }

            // Get all .bin files
            var voiceFiles = Directory.GetFiles(voicesDir, "*.bin");

            // Extract voice IDs from filenames
            var voiceIds = new List<string>(voiceFiles.Length);

            foreach (var file in voiceFiles)
            {
                var voiceId = Path.GetFileNameWithoutExtension(file);
                voiceIds.Add(voiceId);

                // Cache the path for faster lookup
                _voicePaths[voiceId] = file;
            }

            _logger.LogInformation("Found {Count} available Kokoro voices", voiceIds.Count);

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

        _voicePaths.Clear();
        _disposed = true;

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Gets the file path for a voice
    /// </summary>
    private string GetVoicePath(string voiceId)
    {
        // Check if path is cached
        if (_voicePaths.TryGetValue(voiceId, out var cachedPath))
        {
            return cachedPath;
        }

        // Get base directory
        var voicesDir = _modelProvider.GetModelPath(IO.ModelType.Kokoro.Voices);

        // Build path
        var voicePath = Path.Combine(voicesDir, $"{voiceId}.bin");

        // Check if file exists
        if (!File.Exists(voicePath))
        {
            _logger.LogWarning("Voice file not found: {Path}", voicePath);

            throw new FileNotFoundException($"Voice file not found for {voiceId}", voicePath);
        }

        // Cache the path
        _voicePaths[voiceId] = voicePath;

        return voicePath;
    }
}
