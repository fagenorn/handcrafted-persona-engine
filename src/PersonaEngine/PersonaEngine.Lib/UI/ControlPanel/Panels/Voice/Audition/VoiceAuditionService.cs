using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Engine;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

public sealed class VoiceAuditionService(
    ITtsEngineProvider engineProvider,
    IRvcAuditionProcessor rvc,
    IOneShotPlayer player,
    IPhonemizer phonemizer,
    IOptionsMonitor<TtsConfiguration> ttsOptions,
    ILogger<VoiceAuditionService> logger
) : IVoiceAuditionService
{
    private readonly object _gate = new();
    private CancellationTokenSource? _activeCts;
    private Task? _activeTask;

    public bool IsPreviewing { get; private set; }
    public string? ActivePreviewId { get; private set; }

    public event EventHandler<VoiceAuditionRequest>? PreviewStarted;
    public event EventHandler? PreviewFinished;

    public async Task PreviewAsync(VoiceAuditionRequest request, CancellationToken ct = default)
    {
        await CancelPreviousAsync().ConfigureAwait(false);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = Task.Run(() => ExecuteAsync(request, linkedCts.Token), linkedCts.Token);

        lock (_gate)
        {
            _activeCts = linkedCts;
            _activeTask = task;
            IsPreviewing = true;
            ActivePreviewId = request.Id;
        }

        PreviewStarted?.Invoke(this, request);

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { /* expected */
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Voice audition failed for {Engine}/{Voice}",
                request.Engine,
                request.Voice
            );
        }
        finally
        {
            linkedCts.Dispose();
            lock (_gate)
            {
                if (ReferenceEquals(_activeCts, linkedCts))
                {
                    _activeCts = null;
                    _activeTask = null;
                    IsPreviewing = false;
                    ActivePreviewId = null;
                }
            }

            PreviewFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await CancelPreviousAsync().ConfigureAwait(false);
    }

    private async Task CancelPreviousAsync()
    {
        CancellationTokenSource? previousCts;
        Task? previousTask;
        lock (_gate)
        {
            previousCts = _activeCts;
            previousTask = _activeTask;
        }

        previousCts?.Cancel();
        if (previousTask is not null)
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { /* expected */
            }
        }
    }

    private async Task ExecuteAsync(VoiceAuditionRequest request, CancellationToken ct)
    {
        var sampleText = SampleFor(request.Engine);
        var engine = engineProvider.Get(request.Engine);
        // Use the voice-override overload so the synthesizer uses the requested voice
        // immediately, without waiting for the config write → file reload → options cycle.
        await using var session = engine.CreateSession(request.Voice);

        var buffer = await SynthesizeToBufferAsync(session, sampleText, ct).ConfigureAwait(false);

        if (request.RvcEnabled)
        {
            rvc.Apply(
                buffer.Segment,
                new RvcOverride { Voice = request.RvcVoice, F0UpKey = request.RvcPitchShift }
            );
            await player
                .PlayAsync(buffer.Segment.AudioData, buffer.Segment.SampleRate, ct)
                .ConfigureAwait(false);
        }
        else
        {
            await player.PlayAsync(buffer.Pcm, buffer.SampleRate, ct).ConfigureAwait(false);
        }
    }

    private string SampleFor(string engineId) =>
        engineId.ToLowerInvariant() switch
        {
            "qwen3" => ttsOptions.CurrentValue.AuditionSample.Qwen3,
            _ => ttsOptions.CurrentValue.AuditionSample.Kokoro,
        };

    private async Task<SynthesizedBuffer> SynthesizeToBufferAsync(
        ISynthesisSession session,
        string text,
        CancellationToken ct
    )
    {
        var phonemes = await phonemizer.ToPhonemesAsync(text, ct).ConfigureAwait(false);

        var segments = new List<AudioSegment>();
        await foreach (
            var segment in session.SynthesizeAsync(text, phonemes, isLastSegment: true, ct)
        )
        {
            segments.Add(segment);
        }

        if (segments.Count == 0)
            throw new InvalidOperationException("Synthesis produced no audio.");

        var sampleRate = segments[0].SampleRate;
        var total = segments.Sum(s => s.AudioData.Length);
        var pcm = new float[total];
        var offset = 0;
        foreach (var s in segments)
        {
            s.AudioData.CopyTo(pcm.AsMemory(offset));
            offset += s.AudioData.Length;
        }

        var combined = new AudioSegment(pcm, sampleRate, Array.Empty<Token>());
        return new SynthesizedBuffer(pcm.AsMemory(), sampleRate, combined);
    }

    private sealed record SynthesizedBuffer(
        ReadOnlyMemory<float> Pcm,
        int SampleRate,
        AudioSegment Segment
    );
}
