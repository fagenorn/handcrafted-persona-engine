using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;
using A2FModels = PersonaEngine.Lib.IO.ModelType.Audio2Face;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Audio2Face lip-sync processor.  Each <see cref="Process" /> call handles
///     one audio chunk: resample, sliding-window inference (with left padding on
///     the first call for GRU warmup, right padding always for tail coverage),
///     AnimatorSkin compose, vertex EMA, BVLS solve, ARKit-to-Live2D mapping,
///     and per-parameter EMA smoothing.
///
///     GRU, solver, vertex-EMA, and parameter-smoother state persist across calls
///     within a turn for temporal coherence.  <see cref="Reset" /> clears everything.
/// </summary>
public sealed class Audio2FaceLipSyncProcessor : ILipSyncProcessor, IDisposable
{
    private const int TargetSampleRate = 16000;
    private const int WindowSize = TargetSampleRate;
    private const int Stride = TargetSampleRate / 2;
    private const int FramesPerWindow = 30;
    private const int OutputFps = 60;
    private const double SecondsPerFrame = 1.0 / OutputFps;
    private const int PaddingSize = 16000;
    private const double PaddingDuration = (double)PaddingSize / TargetSampleRate;
    private const double StrideDuration = (double)Stride / TargetSampleRate;

    /// <summary>
    ///     Timestamp offset applied to every frame.
    ///     Lag formula: <c>lag = 2 * PaddingDuration − 0.25 − offset</c>.
    ///     At offset 1.55 the residual lag is ~0.20 s with 3 frames of headroom
    ///     at the tightest scheduling point (every 5th chunk).
    /// </summary>
    private const double FrameTimestampOffset =
        PaddingDuration + StrideDuration + 3 * SecondsPerFrame;

    private const int SkinSize = 72006;
    private const int TotalBlendshapes = 52;

    private readonly AnimatorSkinConfig _animatorConfig;
    private readonly BlendshapeConfig _config;
    private readonly BlendshapeData _data;
    private readonly int _identityIndex;
    private readonly Audio2FaceInference _inference;
    private readonly ILogger<Audio2FaceLipSyncProcessor> _logger;
    private readonly IBlendshapeSolver _solver;
    private readonly float[] _vertexEmaAlpha;
    private readonly ParamSmoother _smoother = new();
    private int _saccadeFrameCounter;
    private float[]? _prevDeltas;
    private float[]? _audioCarryover;

    /// <summary>
    ///     Window offsets that were inferred with right-padding and need a
    ///     full-audio re-run to properly advance GRU / solver / EMA state.
    /// </summary>
    private readonly Queue<int> _deferredWindowOffsets = new();

    /// <summary>Sentence-level audio buffer (resampled to 16 kHz).
    /// May be pre-filled with silence for GRU warmup on cold start.</summary>
    private readonly List<float> _sentenceAudio = new();

    /// <summary>Offset in _sentenceAudio where the next sliding window starts.</summary>
    private int _nextWindowOffset;

    /// <summary>All lip-sync frames produced for the current sentence.</summary>
    private readonly List<LipSyncFrame> _sentenceFrames = new();

    /// <summary>Cumulative playback time of segments already emitted.</summary>
    private double _emittedDuration;

    public Audio2FaceLipSyncProcessor(
        IModelProvider modelProvider,
        IOptions<Audio2FaceOptions> options,
        ILogger<Audio2FaceLipSyncProcessor> logger
    )
    {
        _logger = logger;
        var opts = options.Value;

        _identityIndex = opts.Identity switch
        {
            "Claire" => 0,
            "James" => 1,
            "Mark" => 2,
            _ => throw new ArgumentException(
                $"Unknown Audio2Face identity '{opts.Identity}'.",
                nameof(options)
            ),
        };

        var (skinId, skinConfigId, modelDataId, modelConfigId) = opts.Identity switch
        {
            "Claire" => (
                A2FModels.SkinClaire,
                A2FModels.SkinConfigClaire,
                A2FModels.ModelDataClaire,
                A2FModels.ModelConfigClaire
            ),
            "James" => (
                A2FModels.SkinJames,
                A2FModels.SkinConfigJames,
                A2FModels.ModelDataJames,
                A2FModels.ModelConfigJames
            ),
            "Mark" => (
                A2FModels.SkinMark,
                A2FModels.SkinConfigMark,
                A2FModels.ModelDataMark,
                A2FModels.ModelConfigMark
            ),
            _ => throw new ArgumentException($"Unknown identity '{opts.Identity}'."),
        };

        _config = BlendshapeConfig.FromJson(
            File.ReadAllText(modelProvider.GetModelPath(skinConfigId))
        );
        _animatorConfig = AnimatorSkinConfig.FromJson(
            File.ReadAllText(modelProvider.GetModelPath(modelConfigId))
        );
        _data = BlendshapeData.Load(
            modelProvider.GetModelPath(skinId),
            modelProvider.GetModelPath(modelDataId),
            _config
        );
        _vertexEmaAlpha = BuildVertexEmaAlpha(_animatorConfig, _data.NeutralSkinFlat);
        _solver = CreateSolver(opts.SolverType);
        _inference = new Audio2FaceInference(modelProvider, opts.UseGpu, logger);

        _logger.LogInformation(
            "Audio2FaceLipSyncProcessor initialized: {Identity} (index {Index}), solver {SolverType}.",
            opts.Identity,
            _identityIndex,
            opts.SolverType
        );
    }

    public string EngineId => "Audio2Face";

    public LipSyncTimeline Process(AudioSegment segment)
    {
        try
        {
            if (segment.AudioData.Length == 0)
            {
                return LipSyncTimeline.Empty;
            }

            var audio16K = Resample(segment.AudioData.Span, segment.SampleRate, TargetSampleRate);
            _sentenceAudio.AddRange(audio16K);

            // ── Deferred re-runs: advance state with full audio ──
            // Right-padded windows save/restore ALL state so frames can be
            // produced early without contaminating temporal context.  When the
            // full audio for a deferred window becomes available we re-run
            // inference + the full processing pipeline (without appending
            // frames) to properly advance GRU, solver, vertex-EMA and
            // param-smoother state.  This gives both early frames AND correct
            // temporal continuity across window boundaries.
            var buf = CollectionsMarshal.AsSpan(_sentenceAudio);
            while (
                _deferredWindowOffsets.Count > 0
                && _deferredWindowOffsets.Peek() + WindowSize <= _sentenceAudio.Count
            )
            {
                var deferredOffset = _deferredWindowOffsets.Dequeue();
                RunStateAdvancement(buf.Slice(deferredOffset, WindowSize));
            }

            // ── Sliding-window inference ──
            while (_nextWindowOffset + Stride <= _sentenceAudio.Count)
            {
                var available = _sentenceAudio.Count - _nextWindowOffset;
                if (available >= WindowSize)
                {
                    // Full window — best quality, state advances naturally.
                    InferAndAppend(buf.Slice(_nextWindowOffset, WindowSize));
                }
                else
                {
                    // Right-padded window — save ALL state, produce frames,
                    // then restore so the silence doesn't contaminate context.
                    // Queue offset for full-audio re-run later.
                    var savedGru = _inference.SaveGruState();
                    var savedSolver = _solver.SaveTemporal();
                    var savedDeltas = _prevDeltas?.ToArray();
                    var savedSmoother = _smoother.Save();

                    var padded = new float[WindowSize];
                    buf.Slice(_nextWindowOffset, available).CopyTo(padded);
                    InferAndAppend(padded);

                    _inference.RestoreGruState(savedGru);
                    _solver.RestoreTemporal(savedSolver);
                    _prevDeltas = savedDeltas;
                    _smoother.Restore(savedSmoother);

                    _deferredWindowOffsets.Enqueue(_nextWindowOffset);
                }
                _nextWindowOffset += Stride;
            }

            // ── Extract frames for this segment's playback window ──
            var segStart = _emittedDuration;
            var segEnd = _emittedDuration + segment.DurationInSeconds;
            _emittedDuration = segEnd;

            // Find frame range by global timestamp (frames sorted ascending).
            var startIdx = FindFrameIndex(segStart);
            var endIdx = FindFrameIndex(segEnd);

            if (startIdx >= _sentenceFrames.Count)
            {
                // Safety net — with 2 s padding this should not trigger in
                // steady state.  Hold the last available frame so the consumer
                // keeps the current mouth pose rather than snapping to neutral.
                if (_sentenceFrames.Count > 0)
                {
                    var hold = _sentenceFrames[^1];
                    hold.Timestamp = 0;
                    return new LipSyncTimeline([hold], segment.DurationInSeconds);
                }

                return LipSyncTimeline.Empty;
            }

            endIdx = Math.Min(endIdx + 1, _sentenceFrames.Count);
            var count = endIdx - startIdx;
            var frames = new LipSyncFrame[count];
            for (var i = 0; i < count; i++)
            {
                frames[i] = _sentenceFrames[startIdx + i];
                frames[i].Timestamp -= segStart; // chunk-relative
            }

            return new LipSyncTimeline(frames, segment.DurationInSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio2Face processing failed; returning empty timeline.");
            return LipSyncTimeline.Empty;
        }
    }

    public void BeginSentence()
    {
        // Save tail audio from the current sentence for the next sentence's
        // warmup.  Real audio produces natural transition frames instead of
        // the neutral poses that silence padding yields.
        if (_sentenceAudio.Count >= PaddingSize)
        {
            _audioCarryover ??= new float[PaddingSize];
            CollectionsMarshal
                .AsSpan(_sentenceAudio)
                .Slice(_sentenceAudio.Count - PaddingSize, PaddingSize)
                .CopyTo(_audioCarryover);
        }

        _sentenceAudio.Clear();
        _sentenceFrames.Clear();
        _deferredWindowOffsets.Clear();
        _nextWindowOffset = 0;

        // Pre-fill with the previous sentence's tail audio (or silence on the
        // very first sentence) so the first sliding window can run as soon as
        // real audio arrives.  Without pre-fill the model needs a full 1-second
        // window before producing frames, but by then the consumer has already
        // played 1 second and frames are permanently behind.
        //
        // Frame timestamps are offset by FrameTimestampOffset (PaddingDuration
        // + StrideDuration) so pre-fill frames get negative timestamps (consumed
        // during warmup) and real-audio frames start near t=0, giving ~0.25 s
        // residual lag from the model's left-truncation.
        if (_audioCarryover != null)
        {
            _sentenceAudio.AddRange(_audioCarryover);
        }
        else
        {
            _sentenceAudio.AddRange(new float[PaddingSize]);
        }

        _saccadeFrameCounter = 0;
        _emittedDuration = -PaddingDuration;
    }

    public void Reset()
    {
        _inference.ResetState();
        _solver.ResetTemporal();
        _prevDeltas = null;
        _smoother.Reset();
        _saccadeFrameCounter = 0;
        _audioCarryover = null;
        _sentenceAudio.Clear();
        _sentenceFrames.Clear();
        _deferredWindowOffsets.Clear();
        _nextWindowOffset = 0;
        _emittedDuration = 0;
    }

    public void Dispose()
    {
        _inference.Dispose();
    }

    private IBlendshapeSolver CreateSolver(string solverType)
    {
        return solverType switch
        {
            "PGD" => new PgdBlendshapeSolver(
                _data.DeltaMatrix,
                _data.MaskedPositionCount,
                _data.ActiveCount,
                _data.NeutralFlat,
                _config.TemplateBBSize,
                _config.StrengthL2,
                _config.StrengthL1,
                _config.StrengthTemporal
            ),
            _ => new BvlsBlendshapeSolver(
                _data.DeltaMatrix,
                _data.MaskedPositionCount,
                _data.ActiveCount,
                _data.NeutralFlat,
                _config.TemplateBBSize,
                _config.StrengthL2,
                _config.StrengthL1,
                _config.StrengthTemporal
            ),
        };
    }

    /// <summary>
    ///     Runs one inference window and appends the processed center frames
    ///     to <see cref="_sentenceFrames" /> with offset 60 fps timestamps.
    /// </summary>
    private void InferAndAppend(ReadOnlySpan<float> windowAudio)
    {
        ProcessWindow(windowAudio, appendFrames: true);
    }

    /// <summary>
    ///     Re-runs a previously right-padded window with full audio to advance
    ///     GRU, solver, vertex-EMA and param-smoother state without producing
    ///     duplicate frames.
    /// </summary>
    private void RunStateAdvancement(ReadOnlySpan<float> windowAudio)
    {
        ProcessWindow(windowAudio, appendFrames: false);
    }

    private void ProcessWindow(ReadOnlySpan<float> windowAudio, bool appendFrames)
    {
        var (skin, eyeRot) = _inference.Infer(windowAudio, _identityIndex);
        for (var f = 0; f < FramesPerWindow; f++)
        {
            var skinData = new float[SkinSize];
            for (var j = 0; j < SkinSize; j++)
            {
                skinData[j] = skin[f, j];
            }

            var frame = ProcessSkinFrame(skinData);

            var eyesSize = eyeRot.GetLength(1);
            var eyeData = new float[eyesSize];
            for (var j = 0; j < eyesSize; j++)
            {
                eyeData[j] = eyeRot[f, j];
            }
            ApplyEyeRotation(ref frame, eyeData);

            frame = _smoother.Smooth(in frame);

            if (appendFrames)
            {
                frame.Timestamp = _sentenceFrames.Count * SecondsPerFrame - FrameTimestampOffset;
                _sentenceFrames.Add(frame);
            }
        }
    }

    /// <summary>
    ///     Binary search for the first frame whose timestamp ≥ <paramref name="time" />.
    /// </summary>
    private int FindFrameIndex(double time)
    {
        var lo = 0;
        var hi = _sentenceFrames.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_sentenceFrames[mid].Timestamp < time)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    private LipSyncFrame ProcessSkinFrame(float[] skinFlat)
    {
        var composed = new float[SkinSize];
        var delta = new float[_data.MaskedPositionCount];

        for (var i = 0; i < SkinSize; i++)
        {
            composed[i] =
                _animatorConfig.SkinStrength * skinFlat[i]
                + _data.EyeClosePoseDeltaFlat[i] * (-_animatorConfig.EyelidOpenOffset)
                + _data.LipOpenPoseDeltaFlat[i] * _animatorConfig.LipOpenOffset;
        }

        if (_prevDeltas != null)
        {
            for (var i = 0; i < SkinSize; i++)
            {
                composed[i] = _prevDeltas[i] + (composed[i] - _prevDeltas[i]) * _vertexEmaAlpha[i];
            }
        }

        _prevDeltas ??= new float[SkinSize];
        Array.Copy(composed, _prevDeltas, SkinSize);

        for (var m = 0; m < _data.FrontalMask.Length; m++)
        {
            var vertIdx = _data.FrontalMask[m];
            for (var c = 0; c < 3; c++)
            {
                delta[m * 3 + c] =
                    _data.NeutralSkinFlat[vertIdx * 3 + c]
                    + composed[vertIdx * 3 + c]
                    - _data.NeutralFlat[m * 3 + c];
            }
        }

        var activeWeights = _solver.Solve(delta);
        var fullWeights = new float[TotalBlendshapes];
        for (var k = 0; k < _config.ActiveIndices.Length; k++)
        {
            var idx = _config.ActiveIndices[k];
            fullWeights[idx] = Math.Clamp(
                activeWeights[k] * _config.Multipliers[idx] + _config.Offsets[idx],
                0f,
                1f
            );
        }

        return ARKitToLive2DMapper.Map(fullWeights);
    }

    /// <summary>
    ///     Applies the SDK eye rotation formula: average both eyes, normalize degrees to [-1, 1].
    ///     eye_rot layout: [rightX, rightY, leftX, leftY].
    /// </summary>
    private void ApplyEyeRotation(ref LipSyncFrame frame, float[] eyeRot)
    {
        var strength = _animatorConfig.EyeballsStrength;
        var saccadeStrength = _animatorConfig.SaccadeStrength;

        float saccadeX = 0,
            saccadeY = 0;
        var saccade = _data.SaccadeRotMatrix;
        if (saccade != null && saccade.GetLength(0) > 0)
        {
            var idx = _saccadeFrameCounter % saccade.GetLength(0);
            saccadeX = saccade[idx, 0];
            saccadeY = saccade[idx, 1];
            _saccadeFrameCounter++;
        }

        var rx =
            _animatorConfig.RightEyeRotXOffset + strength * eyeRot[0] + saccadeStrength * saccadeX;
        var ry =
            _animatorConfig.RightEyeRotYOffset + strength * eyeRot[1] + saccadeStrength * saccadeY;
        var lx =
            _animatorConfig.LeftEyeRotXOffset + strength * eyeRot[2] + saccadeStrength * saccadeX;
        var ly =
            _animatorConfig.LeftEyeRotYOffset + strength * eyeRot[3] + saccadeStrength * saccadeY;

        frame.EyeBallX = Math.Clamp((rx + lx) / 2f / 10f, -1f, 1f);
        frame.EyeBallY = Math.Clamp((ry + ly) / 2f / 10f, -1f, 1f);
    }

    private static float[] BuildVertexEmaAlpha(AnimatorSkinConfig config, float[] neutralSkinFlat)
    {
        var n = neutralSkinFlat.Length / 3;
        var dt = 1.0 / 60.0;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        for (var v = 0; v < n; v++)
        {
            var y = neutralSkinFlat[v * 3 + 1];
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }
        var yRange = maxY - minY;
        var yThreshold = minY + config.FaceMaskLevel * yRange;
        var softness = config.FaceMaskSoftness * yRange;
        var alpha = new float[n * 3];
        for (var v = 0; v < n; v++)
        {
            var y = neutralSkinFlat[v * 3 + 1];
            var mask = (float)(1.0 / (1.0 + Math.Exp(-(y - yThreshold) / (softness + 1e-8))));
            var sm = mask * config.UpperFaceSmoothing + (1f - mask) * config.LowerFaceSmoothing;
            var a = sm > 0 ? (float)(1.0 - Math.Pow(0.5, dt / sm)) : 1.0f;
            alpha[v * 3] = a;
            alpha[v * 3 + 1] = a;
            alpha[v * 3 + 2] = a;
        }
        return alpha;
    }

    private static float[] Resample(ReadOnlySpan<float> source, int srcRate, int dstRate)
    {
        if (srcRate == dstRate)
            return source.ToArray();
        var ratio = (double)dstRate / srcRate;
        var outLen = (int)(source.Length * ratio);
        var result = new float[outLen];
        for (var i = 0; i < outLen; i++)
        {
            var srcIdx = i / ratio;
            var lo = (int)srcIdx;
            var hi = Math.Min(lo + 1, source.Length - 1);
            var frac = (float)(srcIdx - lo);
            result[i] = source[lo] * (1f - frac) + source[hi] * frac;
        }
        return result;
    }
}
