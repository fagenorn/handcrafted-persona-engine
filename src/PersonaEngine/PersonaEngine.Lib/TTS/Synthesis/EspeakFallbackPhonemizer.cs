using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Espeak-based fallback phonemizer matching the Python reference implementation
/// </summary>
public class EspeakFallbackPhonemizer : IFallbackPhonemizer
{
    // Dictionary of espeak to model phoneme mappings, sorted by key length descending (matching Python E2M)
    private static readonly IReadOnlyDictionary<string, string> E2M = new Dictionary<string, string> {
                                                                                                         { "ʔˌn\u0329", "tn" },
                                                                                                         { "ʔn\u0329", "tn" },
                                                                                                         { "ʔn", "tn" },
                                                                                                         { "ʔ", "t" },
                                                                                                         { "a^ɪ", "I" },
                                                                                                         { "a^ʊ", "W" },
                                                                                                         { "d^ʒ", "ʤ" },
                                                                                                         { "e^ɪ", "A" },
                                                                                                         { "e", "A" },
                                                                                                         { "t^ʃ", "ʧ" },
                                                                                                         { "ɔ^ɪ", "Y" },
                                                                                                         { "ə^l", "ᵊl" },
                                                                                                         { "ʲo", "jo" },
                                                                                                         { "ʲə", "jə" },
                                                                                                         { "ʲ", "" },
                                                                                                         { "ɚ", "əɹ" },
                                                                                                         { "r", "ɹ" },
                                                                                                         { "x", "k" },
                                                                                                         { "ç", "k" },
                                                                                                         { "ɐ", "ə" },
                                                                                                         { "ɬ", "l" },
                                                                                                         { "\u0303", "" }
                                                                                                     }.OrderByDescending(kv => kv.Key.Length).ToDictionary(kv => kv.Key, kv => kv.Value);

    // Add a cache to avoid repeated processing
    private readonly ConcurrentDictionary<string, (string? Phonemes, int? Rating)> _cache = new();

    private readonly ILogger<EspeakFallbackPhonemizer> _logger;

    private readonly SemaphoreSlim _processLock = new(1, 1);

    private readonly int _processReadTimeoutMs = 2000;

    private readonly int _processStartTimeoutMs = 5000;

    private readonly IOptionsMonitor<TtsConfiguration> _ttsConfig;

    private readonly IDisposable? _ttsConfigChangeToken;

    private readonly IOptionsMonitor<KokoroVoiceOptions> _voiceOptions;

    private readonly IDisposable? _voiceOptionsChangeToken;

    private bool _disposed;

    private volatile string _espeakPath;

    // Process state
    private Process? _espeakProcess;

    private volatile bool _needProcessReset;

    private volatile bool _useBritishEnglish;

    public EspeakFallbackPhonemizer(
        IOptionsMonitor<TtsConfiguration>   ttsConfig,
        IOptionsMonitor<KokoroVoiceOptions> voiceOptions,
        ILogger<EspeakFallbackPhonemizer>   logger)
    {
        _ttsConfig    = ttsConfig ?? throw new ArgumentNullException(nameof(ttsConfig));
        _voiceOptions = voiceOptions ?? throw new ArgumentNullException(nameof(voiceOptions));
        _logger       = logger ?? throw new ArgumentNullException(nameof(logger));

        _espeakPath        = _ttsConfig.CurrentValue.EspeakPath ?? throw new ArgumentNullException(nameof(ttsConfig.CurrentValue.EspeakPath));
        _useBritishEnglish = _voiceOptions.CurrentValue.UseBritishEnglish;

        _voiceOptionsChangeToken = _voiceOptions.OnChange(options =>
                                                          {
                                                              _useBritishEnglish = options.UseBritishEnglish;
                                                              _logger.LogDebug("Voice options updated: UseBritishEnglish={UseBritishEnglish}", _useBritishEnglish);
                                                              _needProcessReset = true;

                                                              // Clear cache when voice options change
                                                              _cache.Clear();
                                                          });

        _ttsConfigChangeToken = _ttsConfig.OnChange(config =>
                                                    {
                                                        if ( string.IsNullOrEmpty(config.EspeakPath) || _espeakPath == config.EspeakPath )
                                                        {
                                                            return;
                                                        }

                                                        _espeakPath = config.EspeakPath;
                                                        _logger.LogDebug("TTS configuration updated: EspeakPath={EspeakPath}", _espeakPath);
                                                        _needProcessReset = true;

                                                        // Clear cache when espeak path changes
                                                        _cache.Clear();
                                                    });
    }

    /// <summary>
    ///     Gets phonemes for a word using espeak-ng command-line tool
    /// </summary>
    public async Task<(string? Phonemes, int? Rating)> GetPhonemesAsync(
        string            word,
        CancellationToken cancellationToken = default)
    {
        if ( string.IsNullOrEmpty(word) )
        {
            return (null, null);
        }

        // Check cache first
        if ( _cache.TryGetValue(word, out var cachedResult) )
        {
            return cachedResult;
        }

        var cancelled = false;

        try
        {
            await _processLock.WaitAsync(cancellationToken);

            // Check cache again in case another thread added the result while we were waiting
            if ( _cache.TryGetValue(word, out cachedResult) )
            {
                return cachedResult;
            }

            var cleanWord = SanitizeInput(word);

            // Ensure the process is running
            var process = await EnsureProcessIsInitializedAsync(cancellationToken);

            // Write the word to the process stdin
            await process.StandardInput.WriteLineAsync(cleanWord);
            await process.StandardInput.FlushAsync();

            // Read the output with a timeout
            var readTask = process.StandardOutput.ReadLineAsync();

            if ( await Task.WhenAny(readTask, Task.Delay(_processReadTimeoutMs, cancellationToken)) != readTask )
            {
                _logger.LogWarning("Timeout reading output from espeak-ng for word {Word}", word);
                _needProcessReset = true;

                return (null, null);
            }

            var output = await readTask;

            if ( string.IsNullOrEmpty(output) )
            {
                _logger.LogWarning("Empty output from espeak-ng for word {Word}", word);
                _needProcessReset = true;

                return (null, null);
            }

            var phonemes = NormalizeEspeakOutput(output.Trim(), _useBritishEnglish);

            var result = (phonemes, 2);

            // Add to cache
            _cache[word] = result;

            return result;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;

            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting phonemes from espeak-ng for word {Word}", word);
            _needProcessReset = true;

            return (null, null);
        }
        finally
        {
            if ( !cancelled )
            {
                _processLock.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if ( _disposed )
        {
            return;
        }

        _voiceOptionsChangeToken?.Dispose();
        _ttsConfigChangeToken?.Dispose();

        if ( _espeakProcess != null )
        {
            try
            {
                if ( !_espeakProcess.HasExited )
                {
                    _espeakProcess.Kill();
                }

                _espeakProcess.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing espeak-ng process");
            }
        }

        _processLock.Dispose();
        _disposed = true;

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Ensures the espeak process is initialized and running
    /// </summary>
    private async Task<Process> EnsureProcessIsInitializedAsync(CancellationToken cancellationToken)
    {
        // If process needs to be reset or doesn't exist or has exited, create a new one
        if ( _needProcessReset || _espeakProcess == null || _espeakProcess.HasExited )
        {
            // Dispose of the old process if it exists
            if ( _espeakProcess != null )
            {
                try
                {
                    if ( !_espeakProcess.HasExited )
                    {
                        _espeakProcess.Kill();
                    }

                    _espeakProcess.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing espeak-ng process");
                }

                _espeakProcess = null;
            }

            // Create a new process
            var language = _useBritishEnglish ? "en-gb" : "en-us";
            var startInfo = new ProcessStartInfo {
                                                     FileName               = _espeakPath,
                                                     Arguments              = $"--ipa=1 -v {language} -q --tie=^",
                                                     RedirectStandardOutput = true,
                                                     RedirectStandardInput  = true,
                                                     StandardOutputEncoding = Encoding.UTF8,
                                                     StandardInputEncoding  = Encoding.UTF8,
                                                     UseShellExecute        = false,
                                                     CreateNoWindow         = true
                                                 };

            var process = new Process { StartInfo = startInfo };

            // Start the process with a timeout
            var startTask = Task.Run(() =>
                                     {
                                         process.Start();

                                         return true;
                                     });

            if ( await Task.WhenAny(startTask, Task.Delay(_processStartTimeoutMs, cancellationToken)) != startTask )
            {
                try
                {
                    if ( !process.HasExited )
                    {
                        process.Kill();
                    }

                    process.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing espeak-ng process after start timeout");
                }

                throw new TimeoutException("Timeout starting espeak-ng process");
            }

            if ( !startTask.Result )
            {
                throw new InvalidOperationException("Failed to start espeak-ng process");
            }

            _espeakProcess    = process;
            _needProcessReset = false;
            _logger.LogDebug("Started new espeak-ng process");
        }

        return _espeakProcess;
    }

    /// <summary>
    ///     Sanitizes input for espeak command line
    /// </summary>
    private string SanitizeInput(string input)
    {
        // Remove characters that could cause command line issues
        return input.Replace("\"", "")
                    .Replace("\\", "")
                    .Replace(";", "")
                    .Replace("&", "")
                    .Replace("|", "")
                    .Replace(">", "")
                    .Replace("<", "")
                    .Replace("`", "");
    }

    /// <summary>
    ///     Normalizes espeak output to match the Python implementation
    /// </summary>
    private string NormalizeEspeakOutput(string output, bool useBritishEnglish)
    {
        // Apply all replacements from E2M dictionary
        var result = output;

        // Apply all the E2M replacements in order of key length (longest first)
        foreach ( var kvp in E2M )
        {
            result = result.Replace(kvp.Key, kvp.Value);
        }

        // Apply the regex substitution similar to the Python version
        // This converts characters with combining subscript (U+0329) to have a schwa prefix
        result = Regex.Replace(result, @"(\S)\u0329", "ᵊ$1");
        result = result.Replace("\u0329", "");

        // Apply language-specific replacements as in the Python implementation
        if ( useBritishEnglish )
        {
            result = result.Replace("e^ə", "ɛː");
            result = result.Replace("iə", "ɪə");
            result = result.Replace("ə^ʊ", "Q");
        }
        else // American English
        {
            result = result.Replace("o^ʊ", "O");
            result = result.Replace("ɜːɹ", "ɜɹ");
            result = result.Replace("ɜː", "ɜɹ");
            result = result.Replace("ɪə", "iə");
            result = result.Replace("ː", "");
        }

        // Common replacement for both language variants
        result = result.Replace("o", "ɔ"); // for espeak < 1.52

        // Final clean-up - remove tie character and spaces
        return result.Replace("^", "").Replace(" ", "");
    }
}