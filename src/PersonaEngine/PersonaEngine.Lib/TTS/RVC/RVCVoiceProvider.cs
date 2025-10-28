using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.TTS.RVC;

public class RVCVoiceProvider : IRVCVoiceProvider
{
    private readonly ILogger<RVCVoiceProvider> _logger;

    private readonly IModelProvider _modelProvider;

    private readonly ConcurrentDictionary<string, string> _voicePaths = new();

    private bool _disposed;

    public RVCVoiceProvider(IModelProvider ittsModelProvider, ILogger<RVCVoiceProvider> logger)
    {
        _modelProvider =
            ittsModelProvider ?? throw new ArgumentNullException(nameof(ittsModelProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GetVoiceAsync(
        string voiceId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(voiceId))
        {
            throw new ArgumentException("Voice ID cannot be null or empty", nameof(voiceId));
        }

        return await GetVoicePathAsync(voiceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAvailableVoicesAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Get voice directory
            var model = await _modelProvider.GetModelAsync(
                IO.ModelType.RVCVoices,
                cancellationToken
            );
            var voicesDir = model.Path;

            if (!Directory.Exists(voicesDir))
            {
                _logger.LogWarning("Voices directory not found: {Path}", voicesDir);

                return Array.Empty<string>();
            }

            // Get all .bin files
            var voiceFiles = Directory.GetFiles(voicesDir, "*.onnx");

            // Extract voice IDs from filenames
            var voiceIds = new List<string>(voiceFiles.Length);

            foreach (var file in voiceFiles)
            {
                var voiceId = Path.GetFileNameWithoutExtension(file);
                voiceIds.Add(voiceId);

                // Cache the path for faster lookup
                _voicePaths[voiceId] = file;
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

        _voicePaths.Clear();
        _disposed = true;

        await Task.CompletedTask;
    }

    private async Task<string> GetVoicePathAsync(
        string voiceId,
        CancellationToken cancellationToken
    )
    {
        if (_voicePaths.TryGetValue(voiceId, out var cachedPath))
        {
            return cachedPath;
        }

        var model = await _modelProvider.GetModelAsync(IO.ModelType.RVCVoices, cancellationToken);
        var voicesDir = model.Path;

        var voicePath = Path.Combine(voicesDir, $"{voiceId}.onnx");

        if (!File.Exists(voicePath))
        {
            _logger.LogWarning("Voice file not found: {Path}", voicePath);

            throw new FileNotFoundException($"Voice file not found for {voiceId}", voicePath);
        }

        _voicePaths[voiceId] = voicePath;

        return voicePath;
    }
}
