using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using MathNet.Numerics.IntegralTransforms;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PersonaEngine.Lib.Music.MDX;
using PersonaEngine.Lib.Utils.Onnx;

internal struct ProcessedBatch
{
    public int Id;
    public float[] DataL;
    public float[] DataR;
}

public class OnnxMDX : IDisposable
{
    public const int DefaultSR = 44100;
    public const int DefaultMarginSize = 1 * DefaultSR;

    private static readonly Dictionary<string, string> StemNaming = new()
    {
        { "Vocals", "Instrumental" },
        { "Other", "Instruments" },
        { "Instrumental", "Vocals" },
        { "Drums", "Drumless" },
        { "Bass", "Bassless" },
    };

    private readonly InferenceSession _model;
    private readonly MDXModelParameters _params;
    private readonly object _progressLock = new();
    private int _processedChunks;
    private int _totalChunks;

    public OnnxMDX(string modelPath, MDXModelParameters parameters)
    {
        _params = parameters;

        _model = OnnxSessionFactory.Create(modelPath, ExecutionProvider.CudaWithCpuFallback);

        // Warm up the model
        var dummyInput = new DenseTensor<float>(new[] { 1, 4, _params.DimF, _params.DimT });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", dummyInput),
        };
        _model.Run(inputs);
    }

    public void Dispose()
    {
        _model?.Dispose();
    }

    public static string GetHash(string modelPath)
    {
        try
        {
            using (var md5 = MD5.Create())
            using (var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read))
            {
                if (stream.Length > 10000 * 1024)
                    stream.Seek(-10000 * 1024, SeekOrigin.End);
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        catch
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(modelPath))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Process wave with optional denoising (matches Python implementation)
    /// </summary>
    public async Task<(
        float[] Left,
        float[] Right,
        float[] LeftI,
        float[] RightI
    )> ProcessWaveWithDenoise(
        float[] waveL,
        float[] waveR,
        bool denoise = false,
        int mtThreads = 1,
        IProgress<double> progress = null
    )
    {
        // 1. Normalize input (critical for good output)
        var (normalizedL, normalizedR, peak) = NormalizeWave(waveL, waveR);

        if (denoise)
        {
            // Create progress wrappers for each pass if needed
            var progressWrapper1 =
                progress != null ? new Progress<double>(p => progress.Report(p * 0.5)) : null;
            var progressWrapper2 =
                progress != null ? new Progress<double>(p => progress.Report(0.5 + p * 0.5)) : null;

            // Run denoising: process both positive and negative waves
            var negativeL = new float[normalizedL.Length];
            var negativeR = new float[normalizedR.Length];
            for (int i = 0; i < normalizedL.Length; i++)
            {
                negativeL[i] = -normalizedL[i];
            }
            for (int i = 0; i < normalizedR.Length; i++)
            {
                negativeR[i] = -normalizedR[i];
            }

            var (negL, negR) = await ProcessWaveInternal(
                negativeL,
                negativeR,
                mtThreads,
                progressWrapper1
            );

            var (posL, posR) = await ProcessWaveInternal(
                normalizedL,
                normalizedR,
                mtThreads,
                progressWrapper2
            );

            // Average the results: -(neg) + pos = -neg + pos, then * 0.5
            var resultL = new float[posL.Length];
            var resultR = new float[posR.Length];

            for (int i = 0; i < resultL.Length; i++)
            {
                resultL[i] = (-negL[i] + posL[i]) * 0.5f;
            }
            for (int i = 0; i < resultR.Length; i++)
            {
                resultR[i] = (-negR[i] + posR[i]) * 0.5f;
            }

            // Denormalize back to original scale
            var result = DenormalizeWave(resultL, resultR, peak);
            var resultI = CreateInverseMix(
                waveL,
                waveR,
                result.Left,
                result.Right,
                _params.Compensation
            );

            return (result.Left, result.Right, resultI.Left, resultI.Right);
        }
        else
        {
            var (resultL, resultR) = await ProcessWaveInternal(
                normalizedL,
                normalizedR,
                mtThreads,
                progress
            );
            var result = DenormalizeWave(resultL, resultR, peak);
            var resultI = CreateInverseMix(
                waveL,
                waveR,
                result.Left,
                result.Right,
                _params.Compensation
            );

            return (result.Left, result.Right, resultI.Left, resultI.Right);
        }
    }

    /// <summary>
    /// Normalize wave to [-1, 1] range
    /// </summary>
    private (float[] Left, float[] Right, float Peak) NormalizeWave(float[] waveL, float[] waveR)
    {
        // Find peak value across both channels
        float maxL = waveL.Length > 0 ? waveL.Max(Math.Abs) : 0f;
        float maxR = waveR.Length > 0 ? waveR.Max(Math.Abs) : 0f;
        float peak = Math.Max(maxL, maxR);

        // Avoid division by zero with a reasonable epsilon
        // Use a larger epsilon to avoid numerical issues
        if (peak < 1e-6f)
        {
            peak = 1.0f;
        }

        // Normalize
        var normalizedL = new float[waveL.Length];
        var normalizedR = new float[waveR.Length];

        for (int i = 0; i < waveL.Length; i++)
        {
            normalizedL[i] = waveL[i] / peak;
        }

        for (int i = 0; i < waveR.Length; i++)
        {
            normalizedR[i] = waveR[i] / peak;
        }

        return (normalizedL, normalizedR, peak);
    }

    /// <summary>
    /// Denormalize wave back to original scale
    /// </summary>
    private (float[] Left, float[] Right) DenormalizeWave(float[] waveL, float[] waveR, float peak)
    {
        var denormalizedL = waveL.Select(x => x * peak).ToArray();
        var denormalizedR = waveR.Select(x => x * peak).ToArray();
        return (denormalizedL, denormalizedR);
    }

    /// <summary>
    /// Internal processing function (previously ProcessWave)
    /// </summary>
    private async Task<(float[] Left, float[] Right)> ProcessWaveInternal(
        float[] waveL,
        float[] waveR,
        int mtThreads = 1,
        IProgress<double> progress = null
    )
    {
        if (waveL.Length != waveR.Length)
            throw new ArgumentException("Left and Right channels must have the same length.");

        _totalChunks = 0;
        _processedChunks = 0;

        // 1. Segment the wave into batches for parallel processing
        var chunk = waveL.Length / mtThreads;
        var batches = Segment(waveL, waveR, false, chunk, DefaultMarginSize);

        var queue = new ConcurrentQueue<ProcessedBatch>();
        var tasks = new List<Task>();

        // Calculate total chunks for progress reporting
        var totalChunksForProgress = 0;
        for (var i = 0; i < batches.Count; i++)
        {
            var (mixWaves, _, _) = PadWave(batches[i].Left, batches[i].Right);
            totalChunksForProgress += mixWaves.Count;
        }
        _totalChunks = totalChunksForProgress;

        // 2. Start processing tasks
        for (var i = 0; i < batches.Count; i++)
        {
            var (batchL, batchR) = batches[i];
            var batchId = i;

            tasks.Add(
                Task.Run(() =>
                {
                    var (mixWaves, pad, trim) = PadWave(batchL, batchR);
                    _ProcessWave(mixWaves, trim, pad, queue, batchId, progress);
                })
            );
        }

        // 3. Wait for all threads
        await Task.WhenAll(tasks);

        // 4. Retrieve and sort results
        var processedBatches = queue
            .OrderBy(b => b.Id)
            .Select(b => (Left: b.DataL, Right: b.DataR))
            .ToList();

        if (processedBatches.Count != batches.Count)
            throw new Exception("Incomplete processed batches, please reduce batch size!");

        // 5. Combine results
        return Segment(processedBatches, true, chunk, DefaultMarginSize);
    }

    private void _ProcessWave(
        List<(float[] Left, float[] Right)> mixWaves,
        int trim,
        int pad,
        ConcurrentQueue<ProcessedBatch> q,
        int id,
        IProgress<double> progress
    )
    {
        var processedChunks = new List<(float[] Left, float[] Right)>();

        foreach (var (mixL, mixR) in mixWaves)
        {
            // 1. STFT
            var spec = STFT(mixL, mixR);

            // 2. Run ONNX model
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", spec),
            };

            using var results = _model.Run(inputs);
            var processedSpec = results.First().AsTensor<float>();

            // 3. iSTFT
            var (processedWavL, processedWavR) = iSTFT(processedSpec);

            // 4. Trim padding
            var trimmedLength = processedWavL.Length - 2 * trim;
            var trimmedWavL = processedWavL.AsSpan(trim, trimmedLength).ToArray();
            var trimmedWavR = processedWavR.AsSpan(trim, trimmedLength).ToArray();

            processedChunks.Add((trimmedWavL, trimmedWavR));

            // Update progress
            if (progress != null)
            {
                lock (_progressLock)
                {
                    _processedChunks++;
                    progress.Report((double)_processedChunks / _totalChunks);
                }
            }
        }

        // 5. Concatenate chunks
        var (processedSignalL, processedSignalR) = Concatenate(processedChunks);

        // 6. Remove final padding
        var finalLength = processedSignalL.Length - pad;
        var finalSignalL = processedSignalL.AsSpan(0, finalLength).ToArray();
        var finalSignalR = processedSignalR.AsSpan(0, finalLength).ToArray();

        q.Enqueue(
            new ProcessedBatch
            {
                Id = id,
                DataL = finalSignalL,
                DataR = finalSignalR,
            }
        );
    }

    private (List<(float[] Left, float[] Right)> mixWaves, int pad, int trim) PadWave(
        ReadOnlyMemory<float> waveL,
        ReadOnlyMemory<float> waveR
    )
    {
        var nSample = waveL.Length;
        var trim = _params.Trim;
        var genSize = _params.GenSize;
        var pad = genSize - (nSample % genSize);

        // Don't set pad to 0 - match Python behavior exactly

        var paddedLength = trim + nSample + pad + trim;
        var wavePL = new float[paddedLength];
        var wavePR = new float[paddedLength];

        waveL.Span.CopyTo(wavePL.AsSpan(trim, nSample));
        waveR.Span.CopyTo(wavePR.AsSpan(trim, nSample));

        var mixWaves = new List<(float[] Left, float[] Right)>();
        for (var i = 0; i < nSample + pad; i += genSize)
        {
            var chunkL = new float[_params.ChunkSize];
            var chunkR = new float[_params.ChunkSize];
            Array.Copy(wavePL, i, chunkL, 0, _params.ChunkSize);
            Array.Copy(wavePR, i, chunkR, 0, _params.ChunkSize);
            mixWaves.Add((chunkL, chunkR));
        }

        return (mixWaves, pad, trim);
    }

    private DenseTensor<float> STFT(ReadOnlySpan<float> chunkL, ReadOnlySpan<float> chunkR)
    {
        var stftResult = new Complex[2, _params.DimT, _params.NBins];

        // 1. Center padding
        var paddedSignalL = new double[_params.ChunkSize + _params.NFft];
        var paddedSignalR = new double[_params.ChunkSize + _params.NFft];
        for (var i = 0; i < _params.ChunkSize; i++)
        {
            paddedSignalL[_params.Trim + i] = chunkL[i];
            paddedSignalR[_params.Trim + i] = chunkR[i];
        }

        // 2. Process Left Channel
        for (var t = 0; t < _params.DimT; t++)
        {
            var start = t * _params.Hop;
            var frame = new Complex[_params.NFft];

            // Apply window
            for (var i = 0; i < _params.NFft; i++)
                frame[i] = new Complex(paddedSignalL[start + i] * _params.Window[i], 0);

            // FFT with AsymmetricScaling to match PyTorch
            // This gives us no scaling on forward, 1/N on inverse
            Fourier.Forward(frame, FourierOptions.AsymmetricScaling);

            // Copy positive frequencies only
            for (var f = 0; f < _params.NBins; f++)
                stftResult[0, t, f] = frame[f];
        }

        // 3. Process Right Channel
        for (var t = 0; t < _params.DimT; t++)
        {
            var start = t * _params.Hop;
            var frame = new Complex[_params.NFft];

            // Apply window
            for (var i = 0; i < _params.NFft; i++)
                frame[i] = new Complex(paddedSignalR[start + i] * _params.Window[i], 0);

            // FFT with AsymmetricScaling
            Fourier.Forward(frame, FourierOptions.AsymmetricScaling);

            // Copy positive frequencies only
            for (var f = 0; f < _params.NBins; f++)
                stftResult[1, t, f] = frame[f];
        }

        // 4. Reshape to tensor [1, 4, DimF, DimT]
        var tensorData = new float[1 * 4 * _params.DimF * _params.DimT];
        var outputTensor = new DenseTensor<float>(
            tensorData,
            new[] { 1, 4, _params.DimF, _params.DimT }
        );

        for (var f = 0; f < _params.DimF; f++)
        {
            for (var t = 0; t < _params.DimT; t++)
            {
                outputTensor[0, 0, f, t] = (float)stftResult[0, t, f].Real;
                outputTensor[0, 1, f, t] = (float)stftResult[0, t, f].Imaginary;
                outputTensor[0, 2, f, t] = (float)stftResult[1, t, f].Real;
                outputTensor[0, 3, f, t] = (float)stftResult[1, t, f].Imaginary;
            }
        }

        return outputTensor;
    }

    private (float[] Left, float[] Right) iSTFT(Tensor<float> spec)
    {
        var outputWaveL = new float[_params.ChunkSize];
        var outputWaveR = new float[_params.ChunkSize];

        // 1. Extract complex spectrogram from tensor
        var complexSpec = new Complex[2, _params.DimT, _params.NBins];
        for (var f = 0; f < _params.DimF; f++)
        {
            for (var t = 0; t < _params.DimT; t++)
            {
                complexSpec[0, t, f] = new Complex(spec[0, 0, f, t], spec[0, 1, f, t]);
                complexSpec[1, t, f] = new Complex(spec[0, 2, f, t], spec[0, 3, f, t]);
            }
        }

        // Process Left Channel
        ProcessSingleChannel(complexSpec, 0, outputWaveL);

        // Process Right Channel
        ProcessSingleChannel(complexSpec, 1, outputWaveR);

        return (outputWaveL, outputWaveR);
    }

    private void ProcessSingleChannel(Complex[,,] complexSpec, int channel, float[] outputWave)
    {
        var outputSignal = new double[_params.ChunkSize + _params.NFft];
        var overlapAddWindow = new double[_params.ChunkSize + _params.NFft];

        for (var t = 0; t < _params.DimT; t++)
        {
            var start = t * _params.Hop;
            var frame = new Complex[_params.NFft];

            // Copy positive frequencies
            for (var f = 0; f < _params.NBins; f++)
                frame[f] = complexSpec[channel, t, f];

            // Reconstruct negative frequencies with proper Hermitian symmetry
            // DC and Nyquist should be real
            frame[0] = new Complex(frame[0].Real, 0);

            if (_params.NFft % 2 == 0)
            {
                // For even NFft, handle Nyquist bin (should be real)
                var nyquist = _params.NFft / 2;
                frame[nyquist] = new Complex(frame[nyquist].Real, 0);
            }

            // Mirror conjugate for negative frequencies
            for (var f = 1; f < (_params.NFft + 1) / 2; f++)
            {
                if (_params.NFft - f != f) // Skip if it would overwrite itself
                    frame[_params.NFft - f] = Complex.Conjugate(frame[f]);
            }

            // iFFT with asymmetric scaling (to match PyTorch)
            Fourier.Inverse(frame, FourierOptions.AsymmetricScaling);

            // Apply window and overlap-add
            for (var i = 0; i < _params.NFft; i++)
            {
                var realValue = frame[i].Real;
                outputSignal[start + i] += realValue * _params.Window[i];
                overlapAddWindow[start + i] += _params.Window[i] * _params.Window[i];
            }
        }

        // Normalize by window squared sum with small epsilon to avoid division by zero
        const double eps = 1e-8;
        for (var i = 0; i < outputSignal.Length; i++)
        {
            if (overlapAddWindow[i] > eps)
                outputSignal[i] /= overlapAddWindow[i];
        }

        // Remove center padding
        for (var i = 0; i < _params.ChunkSize; i++)
            outputWave[i] = (float)outputSignal[i + _params.Trim];
    }

    // Segment helper methods remain the same
    private static List<(ReadOnlyMemory<float> Left, ReadOnlyMemory<float> Right)> Segment(
        ReadOnlyMemory<float> waveL,
        ReadOnlyMemory<float> waveR,
        bool combine,
        int chunkSize,
        int marginSize
    )
    {
        if (combine)
            throw new NotSupportedException("Use Segment(List<...>) overload for combining.");

        var processedWave = new List<(ReadOnlyMemory<float> Left, ReadOnlyMemory<float> Right)>();
        var sampleCount = waveL.Length;

        if (chunkSize <= 0 || chunkSize > sampleCount)
            chunkSize = sampleCount;

        if (marginSize > chunkSize)
            marginSize = chunkSize;

        var segmentCount = 0;
        for (var skip = 0; skip < sampleCount; skip += chunkSize)
        {
            var margin = segmentCount == 0 ? 0 : marginSize;
            var end = Math.Min(skip + chunkSize + marginSize, sampleCount);
            var start = skip - margin;

            var length = end - start;
            var cutL = waveL.Slice(start, length);
            var cutR = waveR.Slice(start, length);

            processedWave.Add((cutL, cutR));
            segmentCount++;

            if (end == sampleCount)
                break;
        }

        return processedWave;
    }

    private static (float[] Left, float[] Right) Segment(
        List<(float[] Left, float[] Right)> segments,
        bool combine,
        int chunkSize,
        int marginSize
    )
    {
        if (!combine)
            throw new NotSupportedException(
                "Use Segment(ReadOnlyMemory<float>...) overload for segmenting."
            );

        var totalLength = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var start = i == 0 ? 0 : marginSize;
            var endOffset = i == segments.Count - 1 ? 0 : marginSize;
            if (marginSize == 0)
                endOffset = 0;

            totalLength += segments[i].Left.Length - start - endOffset;
        }

        var processedWaveL = new float[totalLength];
        var processedWaveR = new float[totalLength];
        var currentPos = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            var (segmentL, segmentR) = segments[i];
            var start = i == 0 ? 0 : marginSize;
            var end = i == segments.Count - 1 ? segmentL.Length : segmentL.Length - marginSize;
            if (marginSize == 0)
                end = segmentL.Length;

            var length = end - start;
            Array.Copy(segmentL, start, processedWaveL, currentPos, length);
            Array.Copy(segmentR, start, processedWaveR, currentPos, length);
            currentPos += length;
        }

        return (processedWaveL, processedWaveR);
    }

    private static (float[] Left, float[] Right) Concatenate(
        List<(float[] Left, float[] Right)> chunks
    )
    {
        var totalLength = chunks.Sum(chunk => chunk.Left.Length);
        var outputL = new float[totalLength];
        var outputR = new float[totalLength];
        var currentPos = 0;

        foreach (var (chunkL, chunkR) in chunks)
        {
            Array.Copy(chunkL, 0, outputL, currentPos, chunkL.Length);
            Array.Copy(chunkR, 0, outputR, currentPos, chunkR.Length);
            currentPos += chunkL.Length;
        }

        return (outputL, outputR);
    }

    /// <summary>
    /// Create inverse mix (e.g., get vocals from instrumental)
    /// </summary>
    public static (float[] Left, float[] Right) CreateInverseMix(
        float[] originalL,
        float[] originalR,
        float[] processedL,
        float[] processedR,
        float compensation
    )
    {
        // Ensure arrays are the same length
        var length = Math.Min(
            Math.Min(originalL.Length, originalR.Length),
            Math.Min(processedL.Length, processedR.Length)
        );

        var inverseL = new float[length];
        var inverseR = new float[length];

        // Apply compensation correctly for inverse
        // The Python code does: original - (processed * compensation)
        for (int i = 0; i < length; i++)
        {
            inverseL[i] = originalL[i] - (processedL[i] * compensation);
            inverseR[i] = originalR[i] - (processedR[i] * compensation);
        }

        return (inverseL, inverseR);
    }
}
