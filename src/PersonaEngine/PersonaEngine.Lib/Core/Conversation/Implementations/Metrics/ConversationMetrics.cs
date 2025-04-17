using System.Diagnostics;
using System.Diagnostics.Metrics;

using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;

public sealed class ConversationMetrics : IDisposable
{
    public const string MeterName = "PersonaEngine.Conversation";

    private readonly Histogram<double> _audioPlaybackDurationMs;

    private readonly Histogram<double> _endToEndTurnDurationMs;

    private readonly Counter<long> _errorsCounter;

    private readonly Histogram<double> _firstAudioLatencyMs;

    private readonly Histogram<double> _llmResponseDurationMs;

    private readonly Meter _meter;

    private readonly Histogram<double> _sttSegmentDurationMs;

    private readonly Histogram<double> _ttsSynthesisDurationMs;

    private readonly Counter<long> _turnsInterruptedCounter;

    private readonly Counter<long> _turnsStartedCounter;

    public ConversationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _endToEndTurnDurationMs = _meter.CreateHistogram<double>(
                                                                 "personaengine.conversation.turn.duration",
                                                                 "ms",
                                                                 "End-to-end duration of a conversation turn (Input Finalized to Audio End).");

        _llmResponseDurationMs = _meter.CreateHistogram<double>(
                                                                "personaengine.conversation.llm.duration",
                                                                "ms",
                                                                "Duration of LLM response generation (Request Sent to Stream End).");

        _ttsSynthesisDurationMs = _meter.CreateHistogram<double>(
                                                                 "personaengine.conversation.tts.duration",
                                                                 "ms",
                                                                 "Duration of TTS synthesis (First Input Chunk to Stream End).");

        _firstAudioLatencyMs = _meter.CreateHistogram<double>(
                                                              "personaengine.conversation.turn.first_audio_latency",
                                                              "ms",
                                                              "Latency from user input finalization to the start of the assistant's audio playback.");

        _audioPlaybackDurationMs = _meter.CreateHistogram<double>(
                                                                  "personaengine.conversation.audio.playback.duration",
                                                                  "ms",
                                                                  "Duration of audio playback for the assistant's response.");

        _sttSegmentDurationMs = _meter.CreateHistogram<double>(
                                                               "personaengine.conversation.stt.segment.duration",
                                                               "ms",
                                                               "Duration of STT processing for a single recognized segment.");

        _turnsStartedCounter = _meter.CreateCounter<long>(
                                                          "personaengine.conversation.turns.started.count",
                                                          "{turn}",
                                                          "Number of conversation turns started.");

        _turnsInterruptedCounter = _meter.CreateCounter<long>(
                                                              "personaengine.conversation.turns.interrupted.count",
                                                              "{turn}",
                                                              "Number of conversation turns interrupted by barge-in.");

        _errorsCounter = _meter.CreateCounter<long>(
                                                    "personaengine.conversation.errors.count",
                                                    "{error}",
                                                    "Number of errors encountered during conversation processing.");
    }

    public void Dispose() { _meter.Dispose(); }

    public void RecordTurnDuration(double durationMs, Guid sessionId, Guid turnId, CompletionReason reason)
    {
        if ( !_endToEndTurnDurationMs.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "TurnId", turnId.ToString() }, { "FinishReason", reason.ToString() } };

        _endToEndTurnDurationMs.Record(durationMs, tags);
    }

    public void RecordLlmDuration(double durationMs, Guid sessionId, Guid turnId, CompletionReason reason)
    {
        if ( !_llmResponseDurationMs.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "TurnId", turnId.ToString() }, { "FinishReason", reason.ToString() } };

        _llmResponseDurationMs.Record(durationMs, tags);
    }

    public void RecordTtsDuration(double durationMs, Guid sessionId, Guid turnId, CompletionReason reason)
    {
        if ( !_ttsSynthesisDurationMs.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "TurnId", turnId.ToString() }, { "FinishReason", reason.ToString() } };

        _ttsSynthesisDurationMs.Record(durationMs, tags);
    }

    public void RecordAudioPlaybackDuration(double durationMs, Guid sessionId, Guid turnId, CompletionReason reason)
    {
        if ( !_audioPlaybackDurationMs.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "TurnId", turnId.ToString() }, { "FinishReason", reason.ToString() } };

        _audioPlaybackDurationMs.Record(durationMs, tags);
    }

    public void RecordSttSegmentDuration(double durationMs, Guid sessionId, string participantId)
    {
        if ( !_sttSegmentDurationMs.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "ParticipantId", participantId } };

        _sttSegmentDurationMs.Record(durationMs, tags);
    }

    public void IncrementTurnsStarted(Guid sessionId)
    {
        if ( !_turnsStartedCounter.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() } };
        _turnsStartedCounter.Add(1, tags);
    }

    public void IncrementTurnsInterrupted(Guid sessionId, Guid turnId)
    {
        if ( !_turnsInterruptedCounter.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "TurnId", turnId.ToString() } };
        _turnsInterruptedCounter.Add(1, tags);
    }

    public void IncrementErrors(Guid sessionId, Guid? turnId, Exception exception)
    {
        if ( !_errorsCounter.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "ErrorType", exception.GetType().Name } };

        if ( turnId.HasValue )
        {
            tags.Add(new KeyValuePair<string, object?>("TurnId", turnId.Value.ToString()));
        }

        _errorsCounter.Add(1, tags);
    }

    public void RecordFirstAudioLatency(double durationMs, Guid sessionId, Guid turnId)
    {
        if ( !_firstAudioLatencyMs.Enabled )
        {
            return;
        }

        var tags = new TagList { { "SessionId", sessionId.ToString() }, { "TurnId", turnId.ToString() } };

        _firstAudioLatencyMs.Record(durationMs, tags);
    }
}