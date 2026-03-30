using System.Buffers;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.Music.AudioSeperator.MelBandRoformer;

public class OnnxMelBandRoformer : IAudioSourceSeparator, IAsyncDisposable
{
    private readonly SeparatorConfig _cfg;
    private InferenceSession? _session;
    private string? _inName;
    private string? _outName;

    public int SampleRate => _cfg.SampleRate;
    public int Channels => _cfg.Channels;
    public int ChunkSize => _cfg.ChunkSize;
    public int NumOverlap => _cfg.NumOverlap;
    public int HopSize => _cfg.ChunkSize / _cfg.NumOverlap;

    // Windows & buffers
    private float[]? _hann; // default Hann for L=ChunkSize
    private float[]? _inputChunkDeinterleaved; // planar [C * L]

    // Ring accumulators for overlap-add result and weights (target only)
    private FloatRingBuffer? _accum; // interleaved, pending overlapped target samples
    private FloatRingBuffer? _weights; // mono weights (one per frame)

    // FIFO for incoming mixture audio (interleaved)
    private FloatRingBuffer? _mixtureFifo;

    // Interleaved mixture timeline ring aligned to the OLA timeline.
    // We append *exactly* the same frames we conceptually add to out_accum/w_accum,
    // so later emits can subtract mixture-target in perfect sync.
    private FloatRingBuffer? _mixTimeline;

    // Pools
    private readonly ArrayPool<float> _pool = ArrayPool<float>.Shared;

    public OnnxMelBandRoformer(SeparatorConfig cfg)
    {
        if (cfg.Channels != 2)
            throw new NotSupportedException("Only stereo models (C=2). Extend as needed.");
        if (cfg.ChunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cfg.ChunkSize));
        if (cfg.NumOverlap <= 0)
            throw new ArgumentOutOfRangeException(nameof(cfg.NumOverlap));
        if (cfg.ChunkSize % cfg.NumOverlap != 0)
            throw new ArgumentException("ChunkSize must be divisible by NumOverlap.");
        _cfg = cfg;
    }

    public async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (_session is not null)
            return;

        var so = new SessionOptions
        {
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };

        // Safe runtime perf toggles
        so.AddSessionConfigEntry("session.set_denormal_as_zero", "1");
        so.AddSessionConfigEntry("session.intra_op.allow_spinning", "1");
        so.AddSessionConfigEntry("session.inter_op.allow_spinning", "1");
        so.AddSessionConfigEntry("session.enable_quant_qdq_cleanup", "1");
        so.AddSessionConfigEntry("session.qdq_matmulnbits_accuracy_level", "4");
        so.AddSessionConfigEntry("optimization.enable_gelu_approximation", "1");
        so.AddSessionConfigEntry("disable_synchronize_execution_providers", "1");
        so.AddSessionConfigEntry("session.use_device_allocator_for_initializers", "1");

        // Try CUDA if available; otherwise CPU is fine.
        try
        {
            so.AppendExecutionProvider_CUDA();
        }
        catch
        { /* ignore if not available */
        }

        _session = new InferenceSession(_cfg.ModelPath, so);
        var inputs = _session.InputMetadata;
        var outputs = _session.OutputMetadata;
        _inName = GetSingleName(inputs, preferContains: "input");
        _outName = GetSingleName(outputs, preferContains: "output");

        _hann = _pool.Rent(_cfg.ChunkSize);
        FillHann(_hann.AsSpan(0, _cfg.ChunkSize));

        _inputChunkDeinterleaved = _pool.Rent(_cfg.Channels * _cfg.ChunkSize);

        int capFrames = NextPow2(_cfg.ChunkSize * 3);
        _mixtureFifo = new FloatRingBuffer(capFrames * _cfg.Channels); // interleaved queue
        _accum = new FloatRingBuffer(capFrames * _cfg.Channels); // interleaved target accum
        _weights = new FloatRingBuffer(capFrames); // mono weights
        _mixTimeline = new FloatRingBuffer(capFrames * _cfg.Channels); // interleaved mixture timeline

        await ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<SeparationBlock> ProcessAsync(
        IAsyncEnumerable<AudioBlock> input,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        EnsureInitialized();

        var session = _session!;
        var inName = _inName!;
        var outName = _outName!;

        int C = _cfg.Channels;
        int L = _cfg.ChunkSize;
        int hop = HopSize;

        bool firstChunkDone = false;

        await foreach (var block in input.WithCancellation(ct))
        {
            if (block.Interleaved.IsEmpty)
                continue;

            // NOTE: we assume caller feeds the correct sample rate (44.1k by default).

            _mixtureFifo!.Write(block.Interleaved.Span);

            while (
                (!firstChunkDone ? _mixtureFifo.Length >= L * C : _mixtureFifo.Length >= hop * C)
            )
            {
                // Build planar chunk [C][L]
                var deint = _inputChunkDeinterleaved!.AsSpan(0, C * L);
                if (!firstChunkDone)
                {
                    _mixtureFifo.ReadInterleavedToPlanar(deint, C, L, peekOnly: false);
                    firstChunkDone = true;
                }
                else
                {
                    // Slide per channel and append per channel (no cross-channel memcpy!)
                    SlidePlanarLeft(deint, C, L, hop);
                    AppendHopFromFifoToPlanarTail(_mixtureFifo!, deint, C, L, hop);
                }

                // Prepare ONNX input (N=1, C, L) in channel-first
                var inTensor = new DenseTensor<float>(new[] { 1, C, L });
                PlanarToTensor(deint, inTensor, C, L);

                var inputOnnx = NamedOnnxValue.CreateFromTensor(inName, inTensor);
                using var results = session.Run([inputOnnx]);
                var outTensor = results.First(v => v.Name == outName).AsTensor<float>();
                var outDims = outTensor.Dimensions.ToArray();

                // Detect layout and get Lout
                int Lout;
                bool isChannelFirst;
                if (outDims.Length == 3 && outDims[0] == 1 && outDims[1] == C)
                {
                    // [1, C, T]
                    isChannelFirst = true;
                    Lout = outDims[2];
                }
                else if (outDims.Length == 3 && outDims[0] == 1 && outDims[^1] == C)
                {
                    // [1, T, C]
                    isChannelFirst = false;
                    Lout = outDims[1];
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unexpected output shape: [{string.Join(",", outDims)}], expected [1,{C},T] or [1,T,{C}]."
                    );
                }

                var localWin = GetWindowForLength(Lout);

                // Get flat span of output. If channel-last, reorder to [C, Lout].
                var oSpanRaw = ((DenseTensor<float>)outTensor).Buffer.Span;
                float[]? chFirstTmp = null; // from pool if needed
                Span<float> oChFirst = isChannelFirst
                    ? oSpanRaw
                    : (chFirstTmp = _pool.Rent(C * Lout)).AsSpan(0, C * Lout);

                if (!isChannelFirst)
                {
                    // Reorder: [1, Lout, C] -> [C, Lout]
                    // raw stride is (Lout*C)
                    for (int i = 0; i < Lout; i++)
                    for (int c = 0; c < C; c++)
                        oChFirst[c * Lout + i] = oSpanRaw[i * C + c];
                }

                // How many frames already pending (before we add this chunk)?
                int pendingBefore = Math.Min(_weights!.Length, Lout); // frames already in rings (earliest region)

                // Append the **mixture timeline** for the "new" (non-overlap) region of this chunk.
                // That region is exactly (Lout - pendingBefore) tail frames of the current planar chunk.
                int framesToAppend = Lout - pendingBefore;
                if (framesToAppend > 0)
                {
                    var mixTail = _pool.Rent(framesToAppend * C);
                    try
                    {
                        PlanarTailToInterleaved(
                            deint,
                            mixTail.AsSpan(0, framesToAppend * C),
                            C,
                            L,
                            framesToAppend
                        );
                        _mixTimeline!.Write(mixTail.AsSpan(0, framesToAppend * C));
                    }
                    finally
                    {
                        _pool.Return(mixTail);
                    }
                }

                // Overlap-add model output into target accumulators.
                OverlapAddChannelFirst(
                    oChFirst,
                    localWin,
                    _accum!,
                    _weights!,
                    Lout,
                    C,
                    pendingBefore
                );

                // Recycle channel-last reorder buffer if we allocated it
                if (chFirstTmp is not null)
                    _pool.Return(chFirstTmp);

                // Emit as many frames as our hop allows (and available).
                int availableFrames = Math.Min(
                    _weights.Length,
                    Math.Min(_accum.Length / C, _mixTimeline!.Length / C)
                );
                int emit = Math.Min(hop, availableFrames);
                if (emit <= 0)
                    continue;

                var targetBlock = _pool.Rent(emit * C);
                var mixBlock = _pool.Rent(emit * C);
                var instrBlock = _pool.Rent(emit * C);

                try
                {
                    // target = accum / weights   (also clears consumed)
                    ReadNormalizedFromAccum(
                        _accum!,
                        _weights!,
                        targetBlock.AsSpan(0, emit * C),
                        emit,
                        C
                    );

                    // mixture segment aligned with those same frames
                    for (int i = 0; i < emit * C; i++)
                        mixBlock[i] = _mixTimeline.ReadOne();

                    // instrumental = mixture - target
                    SubtractInPlace(
                        mixBlock.AsSpan(0, emit * C),
                        targetBlock.AsSpan(0, emit * C),
                        instrBlock.AsSpan(0, emit * C)
                    );

                    yield return new SeparationBlock(
                        Target: targetBlock.AsMemory(0, emit * C),
                        Instrumental: instrBlock.AsMemory(0, emit * C)
                    );
                }
                finally
                {
                    // Caller consumes the Memory; we don't return the rented blocks here.
                }
            }
        }

        // Tail flush: emit any leftovers, using the remaining mixture timeline (proper subtraction)
        await foreach (var tail in FlushAsync())
            yield return tail;
    }

    public void Reset()
    {
        _mixtureFifo?.Clear();
        _accum?.Clear();
        _weights?.Clear();
        _mixTimeline?.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _session = null;

        if (_hann is not null)
            _pool.Return(_hann);
        if (_inputChunkDeinterleaved is not null)
            _pool.Return(_inputChunkDeinterleaved);
        _hann = null;
        _inputChunkDeinterleaved = null;

        _mixtureFifo?.Dispose();
        _accum?.Dispose();
        _weights?.Dispose();
        _mixTimeline?.Dispose();

        await ValueTask.CompletedTask;
    }

    // ----------------- Private helpers -----------------

    private void EnsureInitialized()
    {
        if (_session is null)
            throw new InvalidOperationException("Call InitializeAsync() first.");
    }

    private static string GetSingleName(
        IReadOnlyDictionary<string, NodeMetadata> meta,
        string preferContains
    )
    {
        foreach (var kv in meta)
            if (kv.Key.Contains(preferContains, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        foreach (var kv in meta)
            return kv.Key; // fallback: first
        throw new InvalidOperationException("No inputs/outputs found in the ONNX model.");
    }

    private static void FillHann(Span<float> window)
    {
        int n = window.Length;
        for (int i = 0; i < n; i++)
            window[i] = 0.5f * (1f - (float)Math.Cos((2.0 * Math.PI * i) / (n - 1)));
    }

    private float[] GetWindowForLength(int len)
    {
        if (len == _hann!.Length)
            return _hann;
        var temp = new float[len];
        FillHann(temp);
        return temp;
    }

    private static void PlanarToTensor(Span<float> planar, DenseTensor<float> dst, int C, int L)
    {
        var mem = dst.Buffer.Span;
        int idx = 0;
        for (int c = 0; c < C; c++)
        {
            var src = planar.Slice(c * L, L);
            src.CopyTo(mem.Slice(idx, L));
            idx += L;
        }
    }

    private static void PlanarTailToInterleaved(
        Span<float> planarChunk,
        Span<float> dstInterleaved,
        int C,
        int L,
        int framesTail
    )
    {
        // Write the LAST 'framesTail' frames from the planar chunk as interleaved.
        int start = L - framesTail;
        for (int i = 0; i < framesTail; i++)
        for (int c = 0; c < C; c++)
            dstInterleaved[i * C + c] = planarChunk[c * L + (start + i)];
    }

    // Overlap-add given a channel-first contiguous buffer [C, Lout].
    private static void OverlapAddChannelFirst(
        Span<float> oChFirst,
        float[] localWin,
        FloatRingBuffer accum,
        FloatRingBuffer weights,
        int Lout,
        int C,
        int pendingBefore // frames already in rings before adding this chunk
    )
    {
        // 1) Accumulate into the already-pending region (the overlap): first 'pendingBefore' frames.
        int overlapFrames = Math.Min(pendingBefore, Lout);
        for (int i = 0; i < overlapFrames; i++)
        {
            float w = localWin[i];
            weights.AddAtFromHead(i, w);
            for (int c = 0; c < C; c++)
            {
                float v = oChFirst[c * Lout + i] * w;
                accum.AddAtFromHead(i * C + c, v);
            }
        }

        // 2) Append the remaining tail frames.
        for (int i = overlapFrames; i < Lout; i++)
        {
            float w = localWin[i];
            for (int c = 0; c < C; c++)
                accum.WriteOne(oChFirst[c * Lout + i] * w);
            weights.WriteOne(w);
        }
    }

    private static void ReadNormalizedFromAccum(
        FloatRingBuffer accum,
        FloatRingBuffer weights,
        Span<float> dstInterleaved,
        int frames,
        int C
    )
    {
        const float eps = 1e-8f;
        for (int i = 0; i < frames; i++)
        {
            float w = MathF.Max(weights.ReadOne(), eps);
            for (int c = 0; c < C; c++)
            {
                float v = accum.ReadOne();
                dstInterleaved[i * C + c] = v / w;
            }
        }
    }

    private static void SubtractInPlace(Span<float> a, Span<float> b, Span<float> dst)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            int vsz = Vector<float>.Count;
            for (; i <= dst.Length - vsz; i += vsz)
            {
                var va = new Vector<float>(a.Slice(i, vsz));
                var vb = new Vector<float>(b.Slice(i, vsz));
                (va - vb).CopyTo(dst.Slice(i, vsz));
            }
        }
        for (; i < dst.Length; i++)
            dst[i] = a[i] - b[i];
    }

    private async IAsyncEnumerable<SeparationBlock> FlushAsync(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var accum = _accum!;
        var weights = _weights!;
        var mix = _mixTimeline!;
        int C = _cfg.Channels;
        int hop = HopSize;

        while (accum.Length >= C && weights.Length >= 1 && mix.Length >= C)
        {
            int frames = Math.Min(Math.Min(accum.Length / C, weights.Length), mix.Length / C);
            frames = Math.Min(frames, hop);
            if (frames <= 0)
                break;

            var target = _pool.Rent(frames * C);
            var instr = _pool.Rent(frames * C);
            var mixt = _pool.Rent(frames * C);
            try
            {
                ReadNormalizedFromAccum(accum, weights, target.AsSpan(0, frames * C), frames, C);
                for (int i = 0; i < frames * C; i++)
                    mixt[i] = mix.ReadOne();
                SubtractInPlace(
                    mixt.AsSpan(0, frames * C),
                    target.AsSpan(0, frames * C),
                    instr.AsSpan(0, frames * C)
                );

                yield return new SeparationBlock(
                    target.AsMemory(0, frames * C),
                    instr.AsMemory(0, frames * C)
                );
            }
            finally { }
            await Task.Yield();
        }
    }

    private static int NextPow2(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v++;
        return v;
    }

    private static void SlidePlanarLeft(Span<float> planar, int C, int L, int hop)
    {
        // planar layout: [ch0: L][ch1: L]...[chC-1: L]
        for (int c = 0; c < C; c++)
        {
            // move [c*L + hop .. c*L + L) -> [c*L .. c*L + (L-hop))
            var src = planar.Slice(c * L + hop, L - hop);
            src.CopyTo(planar.Slice(c * L, L - hop));
        }
    }

    private void AppendHopFromFifoToPlanarTail(
        FloatRingBuffer fifo,
        Span<float> planar,
        int C,
        int L,
        int hop
    )
    {
        // Read new hop frames into a small planar scratch, then scatter to each channel tail.
        var scratch = _pool.Rent(C * hop);
        try
        {
            var s = scratch.AsSpan(0, C * hop);
            fifo.ReadInterleavedToPlanar(s, C, hop, peekOnly: false);

            for (int c = 0; c < C; c++)
            {
                // copy scratch[c][0..hop) -> planar[c][L-hop .. L)
                s.Slice(c * hop, hop).CopyTo(planar.Slice(c * L + (L - hop), hop));
            }
        }
        finally
        {
            _pool.Return(scratch);
        }
    }

    public sealed record SeparatorConfig(
        string ModelPath,
        int SampleRate = 44100,
        int Channels = 2,
        int ChunkSize = 352800, // 8.0 s at 44.1 kHz
        int NumOverlap = 2 // 50% overlap
    );
}
