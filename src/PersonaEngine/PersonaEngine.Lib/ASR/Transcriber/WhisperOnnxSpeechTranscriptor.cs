using System.Runtime.CompilerServices;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.ASR.Transcriber;

public class WhisperOnnxSpeechTranscriptor : ISpeechTranscriptor
{
    private static readonly int[] MinLengthMemoryCache = [0];

    private static readonly int[] MaxLengthMemoryCache = [448];

    private static readonly int[] NumBeamsMemoryCache = [2];

    private static readonly int[] NumReturnSequencesMemoryCache = [1];

    private static readonly float[] LengthPenaltyMemoryCache = [1.0f];

    private static readonly float[] RepetitionPenaltyMemoryCache = [1.0f];

    private static readonly Dictionary<string, int> LangToId = new() {
                                                                         { "af", 50327 },
                                                                         { "am", 50334 },
                                                                         { "ar", 50272 },
                                                                         { "as", 50350 },
                                                                         { "az", 50304 },
                                                                         { "ba", 50355 },
                                                                         { "be", 50330 },
                                                                         { "bg", 50292 },
                                                                         { "bn", 50302 },
                                                                         { "bo", 50347 },
                                                                         { "br", 50309 },
                                                                         { "bs", 50315 },
                                                                         { "ca", 50270 },
                                                                         { "cs", 50283 },
                                                                         { "cy", 50297 },
                                                                         { "da", 50285 },
                                                                         { "de", 50261 },
                                                                         { "el", 50281 },
                                                                         { "en", 50259 },
                                                                         { "es", 50262 },
                                                                         { "et", 50307 },
                                                                         { "eu", 50310 },
                                                                         { "fa", 50300 },
                                                                         { "fi", 50277 },
                                                                         { "fo", 50338 },
                                                                         { "fr", 50265 },
                                                                         { "gl", 50319 },
                                                                         { "gu", 50333 },
                                                                         { "haw", 50352 },
                                                                         { "ha", 50354 },
                                                                         { "he", 50279 },
                                                                         { "hi", 50276 },
                                                                         { "hr", 50291 },
                                                                         { "ht", 50339 },
                                                                         { "hu", 50286 },
                                                                         { "hy", 50312 },
                                                                         { "id", 50275 },
                                                                         { "is", 50311 },
                                                                         { "it", 50274 },
                                                                         { "ja", 50266 },
                                                                         { "jw", 50356 },
                                                                         { "ka", 50329 },
                                                                         { "kk", 50316 },
                                                                         { "km", 50323 },
                                                                         { "kn", 50306 },
                                                                         { "ko", 50264 },
                                                                         { "la", 50294 },
                                                                         { "lb", 50345 },
                                                                         { "ln", 50353 },
                                                                         { "lo", 50336 },
                                                                         { "lt", 50293 },
                                                                         { "lv", 50301 },
                                                                         { "mg", 50349 },
                                                                         { "mi", 50295 },
                                                                         { "mk", 50308 },
                                                                         { "ml", 50296 },
                                                                         { "mn", 50314 },
                                                                         { "mr", 50320 },
                                                                         { "ms", 50282 },
                                                                         { "mt", 50343 },
                                                                         { "my", 50346 },
                                                                         { "ne", 50313 },
                                                                         { "nl", 50271 },
                                                                         { "nn", 50342 },
                                                                         { "no", 50288 },
                                                                         { "oc", 50328 },
                                                                         { "pa", 50321 },
                                                                         { "pl", 50269 },
                                                                         { "ps", 50340 },
                                                                         { "pt", 50267 },
                                                                         { "ro", 50284 },
                                                                         { "ru", 50263 },
                                                                         { "sa", 50344 },
                                                                         { "sd", 50332 },
                                                                         { "si", 50322 },
                                                                         { "sk", 50298 },
                                                                         { "sl", 50305 },
                                                                         { "sn", 50324 },
                                                                         { "so", 50326 },
                                                                         { "sq", 50317 },
                                                                         { "sr", 50303 },
                                                                         { "su", 50357 },
                                                                         { "sv", 50273 },
                                                                         { "sw", 50318 },
                                                                         { "ta", 50287 },
                                                                         { "te", 50299 },
                                                                         { "tg", 50331 },
                                                                         { "th", 50289 },
                                                                         { "tk", 50341 },
                                                                         { "tl", 50325 },
                                                                         { "tr", 50268 },
                                                                         { "tt", 50335 },
                                                                         { "ug", 50348 },
                                                                         { "uk", 50260 },
                                                                         { "ur", 50337 },
                                                                         { "uz", 50351 },
                                                                         { "vi", 50278 },
                                                                         { "xh", 50322 },
                                                                         { "yi", 50305 },
                                                                         { "yo", 50324 },
                                                                         { "zh", 50258 },
                                                                         { "zu", 50321 }
                                                                     };

    private readonly InferenceSession _inferenceSession;

    private readonly SpeechTranscriptorOptions _options;

    public WhisperOnnxSpeechTranscriptor(string modelPath, SpeechTranscriptorOptions options)
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_CPU();
        sessionOptions.RegisterOrtExtensions();
        _inferenceSession = new InferenceSession(modelPath, sessionOptions);

        _options = options;
    }

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(IAudioSource source, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if ( source.ChannelCount != 1 )
        {
            throw new NotSupportedException("Only mono-channel audio is supported. Consider one channel aggregation on the audio source.");
        }

        if ( source.SampleRate != 16000 )
        {
            throw new NotSupportedException("Only 16 kHz audio is supported. Consider resampling before calling this transcriptor.");
        }

        var samples                = await source.GetSamplesAsync(0, cancellationToken: cancellationToken);
        var audioTensor            = new DenseTensor<float>(samples, [1, samples.Length]);
        var timestampsEnableTensor = new DenseTensor<int>(new[] { _options.RetrieveTokenDetails ? 1 : 0 }, [1]);

        var langCode = _options.LanguageAutoDetect ? 54021 : GetLangId(_options.Language.TwoLetterISOLanguageName);
        // 50359 = Transcribe, 50358 = Translate
        var decoderInputIds   = new[] { 50258, langCode, 50359 };
        var langAndModeTensor = new DenseTensor<int>(decoderInputIds, [1, 3]);

        var inputs = new List<NamedOnnxValue> {
                                                  NamedOnnxValue.CreateFromTensor("audio_pcm", audioTensor),
                                                  NamedOnnxValue.CreateFromTensor("min_length", new DenseTensor<int>(MinLengthMemoryCache, [1])),
                                                  NamedOnnxValue.CreateFromTensor("max_length", new DenseTensor<int>(MaxLengthMemoryCache, [1])),
                                                  NamedOnnxValue.CreateFromTensor("num_beams", new DenseTensor<int>(NumBeamsMemoryCache, [1])),
                                                  NamedOnnxValue.CreateFromTensor("num_return_sequences", new DenseTensor<int>(NumReturnSequencesMemoryCache, [1])),
                                                  NamedOnnxValue.CreateFromTensor("length_penalty", new DenseTensor<float>(LengthPenaltyMemoryCache, [1])),
                                                  NamedOnnxValue.CreateFromTensor("repetition_penalty", new DenseTensor<float>(RepetitionPenaltyMemoryCache, [1])),
                                                  NamedOnnxValue.CreateFromTensor("logits_processor", timestampsEnableTensor)
                                                  // NamedOnnxValue.CreateFromTensor("decoder_input_ids", langAndModeTensor)
                                              };

        foreach ( var result in await Task.Run(
                                               () =>
                                               {
                                                   using var results = _inferenceSession.Run(inputs);

                                                   var text = results[0].AsTensor<string>().GetValue(0);
                                                   var aaa  = new List<TranscriptSegment> { new() { Text = text } };

                                                   return aaa;
                                               },
                                               cancellationToken) )
        {
            yield return result;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if ( _inferenceSession is IAsyncDisposable inferenceSessionAsyncDisposable )
        {
            await inferenceSessionAsyncDisposable.DisposeAsync();
        }
        else
        {
            _inferenceSession.Dispose();
        }
    }

    public void Dispose() { _inferenceSession.Dispose(); }

    public static int GetLangId(string languageString) { return LangToId.GetValueOrDefault(languageString, 50259); }
}