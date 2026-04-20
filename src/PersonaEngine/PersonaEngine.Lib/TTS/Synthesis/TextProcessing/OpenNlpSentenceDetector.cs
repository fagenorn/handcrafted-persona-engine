using Microsoft.Extensions.Logging;
using OpenNLP.Tools.SentenceDetect;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Implementation of OpenNLP-based sentence detection
/// </summary>
public class OpenNlpSentenceDetector : IMlSentenceDetector
{
    private readonly EnglishMaximumEntropySentenceDetector _detector;

    private readonly ILogger<OpenNlpSentenceDetector> _logger;

    private bool _disposed;

    public OpenNlpSentenceDetector(string modelPath, ILogger<OpenNlpSentenceDetector> logger)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            throw new ArgumentException("Model path cannot be null or empty", nameof(modelPath));
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            _detector = new EnglishMaximumEntropySentenceDetector(modelPath);
            _logger.LogInformation(
                "Initialized OpenNLP sentence detector from {ModelPath}",
                modelPath
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize OpenNLP sentence detector");

            throw;
        }
    }

    /// <summary>
    ///     Detects sentences in text using OpenNLP
    /// </summary>
    public IReadOnlyList<string> Detect(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        try
        {
            var sentences = _detector.SentenceDetect(text);

            return sentences;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting sentences");

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
