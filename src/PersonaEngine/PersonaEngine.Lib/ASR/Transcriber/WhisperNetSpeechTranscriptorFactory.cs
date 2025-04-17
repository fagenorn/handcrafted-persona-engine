using System.Globalization;

using PersonaEngine.Lib.Configuration;

using Whisper.net;

namespace PersonaEngine.Lib.ASR.Transcriber;

public sealed class WhisperSpeechTranscriptorFactory : ISpeechTranscriptorFactory
{
    private readonly WhisperProcessorBuilder builder;

    private readonly WhisperFactory? whisperFactory;

    public WhisperSpeechTranscriptorFactory(WhisperFactory factory, bool dispose = true)
    {
        builder = factory.CreateBuilder();
        if ( dispose )
        {
            whisperFactory = factory;
        }
    }

    public WhisperSpeechTranscriptorFactory(WhisperProcessorBuilder builder) { this.builder = builder; }

    public WhisperSpeechTranscriptorFactory(string modelFileName)
    {
        whisperFactory = WhisperFactory.FromPath(modelFileName);
        builder        = whisperFactory.CreateBuilder();
    }

    public ISpeechTranscriptor Create(SpeechTranscriptorOptions options)
    {
        var currentBuilder = builder;
        if ( options.Prompt != null )
        {
            currentBuilder = currentBuilder.WithPrompt(options.Prompt);
        }

        if ( options.LanguageAutoDetect )
        {
            currentBuilder = currentBuilder.WithLanguage("auto");
        }
        else
        {
            currentBuilder = currentBuilder.WithLanguage(ToWhisperLanguage(options.Language));
        }

        if ( options.RetrieveTokenDetails )
        {
            currentBuilder = currentBuilder.WithTokenTimestamps();
        }
        
        if ( options.Template != null )
        {
            currentBuilder = currentBuilder.ApplyTemplate(options.Template.Value);
        }

        var processor = currentBuilder.Build();

        return new WhisperNetSpeechTranscriptor(processor);
    }

    public void Dispose() { whisperFactory?.Dispose(); }

    private static string ToWhisperLanguage(CultureInfo languageCode)
    {
        if ( !WhisperNetSupportedLanguage.IsSupported(languageCode) )
        {
            throw new NotSupportedException($"The language provided as: {languageCode.ThreeLetterISOLanguageName} is not supported by Whisper.net.");
        }

        return languageCode.TwoLetterISOLanguageName;
    }
}