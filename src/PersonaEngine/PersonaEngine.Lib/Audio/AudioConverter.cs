using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Provides functionality for converting audio between different formats.
/// </summary>
public static class AudioConverter
{
    /// <summary>
    ///     Calculates the required size for a buffer to hold the converted audio.
    /// </summary>
    /// <param name="sourceBuffer">The source audio buffer.</param>
    /// <param name="sourceFormat">The format of the source audio.</param>
    /// <param name="targetFormat">The format for the target audio.</param>
    /// <returns>The size in bytes needed for the target buffer.</returns>
    public static int CalculateTargetBufferSize(
        ReadOnlyMemory<byte> sourceBuffer,
        AudioFormat sourceFormat,
        AudioFormat targetFormat
    )
    {
        // Calculate the number of frames in the source buffer
        var framesCount = sourceBuffer.Length / sourceFormat.BytesPerFrame;

        // If resampling is needed, adjust the frame count
        if (sourceFormat.SampleRate != targetFormat.SampleRate)
        {
            framesCount = CalculateResampledFrameCount(
                framesCount,
                sourceFormat.SampleRate,
                targetFormat.SampleRate
            );
        }

        // Calculate the expected size of the target buffer
        return framesCount * targetFormat.BytesPerFrame;
    }

    /// <summary>
    ///     Calculates the number of frames after resampling.
    /// </summary>
    /// <param name="sourceFrameCount">Number of frames in the source buffer.</param>
    /// <param name="sourceSampleRate">Sample rate of the source audio.</param>
    /// <param name="targetSampleRate">Target sample rate.</param>
    /// <returns>The number of frames after resampling.</returns>
    public static int CalculateResampledFrameCount(
        int sourceFrameCount,
        uint sourceSampleRate,
        uint targetSampleRate
    )
    {
        return (int)Math.Ceiling(sourceFrameCount * ((double)targetSampleRate / sourceSampleRate));
    }

    /// <summary>
    ///     Converts audio data between different formats.
    /// </summary>
    /// <param name="sourceBuffer">The source audio buffer.</param>
    /// <param name="targetBuffer">The target audio buffer to write the converted data to.</param>
    /// <param name="sourceFormat">The format of the source audio.</param>
    /// <param name="targetFormat">The format for the target audio.</param>
    /// <returns>The number of bytes written to the target buffer.</returns>
    /// <exception cref="ArgumentException">Thrown if the target buffer is too small.</exception>
    public static int Convert(
        ReadOnlyMemory<byte> sourceBuffer,
        Memory<byte> targetBuffer,
        AudioFormat sourceFormat,
        AudioFormat targetFormat
    )
    {
        // Calculate the number of frames in the source buffer
        var sourceFramesCount = sourceBuffer.Length / sourceFormat.BytesPerFrame;

        // Check if resampling is needed
        var needsResampling = sourceFormat.SampleRate != targetFormat.SampleRate;

        // Calculate the expected number of frames in the target buffer
        var targetFramesCount = needsResampling
            ? CalculateResampledFrameCount(
                sourceFramesCount,
                sourceFormat.SampleRate,
                targetFormat.SampleRate
            )
            : sourceFramesCount;

        // Calculate the expected size of the target buffer
        var expectedTargetSize = targetFramesCount * targetFormat.BytesPerFrame;

        if (targetBuffer.Length < expectedTargetSize)
        {
            throw new ArgumentException("Target buffer is too small for the converted audio.");
        }

        // Fast path for same format conversion with no resampling
        if (
            !needsResampling
            && sourceFormat.Channels == targetFormat.Channels
            && sourceFormat.BitsPerSample == targetFormat.BitsPerSample
        )
        {
            sourceBuffer.CopyTo(targetBuffer);

            return sourceBuffer.Length;
        }

        // If only resampling is needed (same format otherwise)
        if (
            needsResampling
            && sourceFormat.Channels == targetFormat.Channels
            && sourceFormat.BitsPerSample == targetFormat.BitsPerSample
        )
        {
            return ResampleDirect(
                sourceBuffer,
                targetBuffer,
                sourceFormat,
                targetFormat,
                sourceFramesCount,
                targetFramesCount
            );
        }

        // For mono-to-stereo int16 conversion, use optimized path
        if (
            !needsResampling
            && sourceFormat.Channels == 1
            && targetFormat.Channels == 2
            && sourceFormat.BitsPerSample == 32
            && targetFormat.BitsPerSample == 16
        )
        {
            ConvertMonoFloat32ToStereoInt16Direct(sourceBuffer, targetBuffer, sourceFramesCount);

            return expectedTargetSize;
        }

        // For stereo-to-mono conversion, use optimized path
        if (
            !needsResampling
            && sourceFormat.Channels == 2
            && targetFormat.Channels == 1
            && sourceFormat.BitsPerSample == 16
            && targetFormat.BitsPerSample == 32
        )
        {
            ConvertStereoInt16ToMonoFloat32Direct(sourceBuffer, targetBuffer, sourceFramesCount);

            return expectedTargetSize;
        }

        // For mono-int16 to stereo-float32 conversion, use optimized path
        if (
            !needsResampling
            && sourceFormat.Channels == 1
            && targetFormat.Channels == 2
            && sourceFormat.BitsPerSample == 16
            && targetFormat.BitsPerSample == 32
        )
        {
            ConvertMonoInt16ToStereoFloat32Direct(sourceBuffer, targetBuffer, sourceFramesCount);

            return expectedTargetSize;
        }

        // General case: convert through intermediate float format
        return ConvertGeneral(
            sourceBuffer,
            targetBuffer,
            sourceFormat,
            targetFormat,
            sourceFramesCount,
            targetFramesCount,
            needsResampling
        );
    }

    /// <summary>
    ///     Specialized fast path to convert 48kHz stereo float32 audio to 16kHz mono int16 audio.
    ///     This optimized method combines resampling, channel conversion, and bit depth conversion in one pass.
    /// </summary>
    /// <param name="stereoFloat32Buffer">The 48kHz stereo float32 audio buffer.</param>
    /// <param name="targetBuffer">The target buffer to receive the 16kHz mono int16 data.</param>
    /// <returns>The number of bytes written to the target buffer.</returns>
    public static int ConvertStereoFloat32_48kTo_MonoInt16_16k(
        ReadOnlyMemory<byte> stereoFloat32Buffer,
        Memory<byte> targetBuffer
    )
    {
        var sourceFormat = new AudioFormat(2, 32, 48000);
        var targetFormat = new AudioFormat(1, 16, 16000);

        // Calculate number of source and target frames
        var sourceFramesCount = stereoFloat32Buffer.Length / sourceFormat.BytesPerFrame;
        var targetFramesCount = CalculateResampledFrameCount(sourceFramesCount, 48000, 16000);

        // Calculate expected target size and verify buffer is large enough
        var expectedTargetSize = targetFramesCount * targetFormat.BytesPerFrame;
        if (targetBuffer.Length < expectedTargetSize)
        {
            throw new ArgumentException("Target buffer is too small for the converted audio.");
        }

        // Process the conversion directly
        ConvertStereoFloat32_48kTo_MonoInt16_16kDirect(
            stereoFloat32Buffer.Span,
            targetBuffer.Span,
            sourceFramesCount,
            targetFramesCount
        );

        return expectedTargetSize;
    }

    /// <summary>
    ///     Direct conversion implementation for 48kHz stereo float32 to 16kHz mono int16.
    ///     Combines downsampling (48kHz to 16kHz - 3:1 ratio), stereo to mono mixing, and float32 to int16 conversion.
    /// </summary>
    private static void ConvertStereoFloat32_48kTo_MonoInt16_16kDirect(
        ReadOnlySpan<byte> source,
        Span<byte> target,
        int sourceFramesCount,
        int targetFramesCount
    )
    {
        // The resampling ratio is exactly 3:1 (48000/16000)
        const int resampleRatio = 3;

        // For optimal quality, we'll use a simple low-pass filter when downsampling
        // by averaging 3 consecutive frames before picking every 3rd one

        for (var targetFrame = 0; targetFrame < targetFramesCount; targetFrame++)
        {
            // Calculate source frame index (center of 3-frame window)
            var sourceFrameBase = targetFrame * resampleRatio;

            // Initialize accumulator for filtered sample
            var monoSampleAccumulator = 0f;
            var sampleCount = 0;

            // Apply a simple averaging filter over a window of frames
            for (var offset = -1; offset <= 1; offset++)
            {
                var sourceFrameIndex = sourceFrameBase + offset;

                // Skip samples outside buffer boundary
                if (sourceFrameIndex < 0 || sourceFrameIndex >= sourceFramesCount)
                {
                    continue;
                }

                // Read left and right float32 samples and average them to mono
                var sourceByteIndex = sourceFrameIndex * 8; // 8 bytes per stereo float32 frame
                var leftSample = BinaryPrimitives.ReadSingleLittleEndian(
                    source.Slice(sourceByteIndex, 4)
                );
                var rightSample = BinaryPrimitives.ReadSingleLittleEndian(
                    source.Slice(sourceByteIndex + 4, 4)
                );

                // Average stereo to mono
                var monoSample = (leftSample + rightSample) * 0.5f;

                // Accumulate filtered sample
                monoSampleAccumulator += monoSample;
                sampleCount++;
            }

            // Average samples if we have any
            var filteredSample = sampleCount > 0 ? monoSampleAccumulator / sampleCount : 0f;

            // Convert float32 to int16 (with scaling and clamping)
            var int16Sample = ClampToInt16(filteredSample * 32767f);

            // Write to target buffer
            var targetByteIndex = targetFrame * 2; // 2 bytes per mono int16 frame
            BinaryPrimitives.WriteInt16LittleEndian(target.Slice(targetByteIndex, 2), int16Sample);
        }
    }

    /// <summary>
    ///     Resamples audio using a higher quality filter.
    ///     Uses a sinc filter for better frequency response.
    /// </summary>
    private static void ResampleWithFilter(
        float[] source,
        float[] target,
        uint sourceRate,
        uint targetRate
    )
    {
        // For 48kHz to 16kHz, we have a 3:1 ratio
        var ratio = (double)sourceRate / targetRate;

        // Use a simple windowed-sinc filter with 8 taps for anti-aliasing
        var filterSize = 8;

        for (var targetIndex = 0; targetIndex < target.Length; targetIndex++)
        {
            // Calculate the corresponding position in the source
            var sourcePos = targetIndex * ratio;
            var sourceCenterIndex = (int)sourcePos;

            // Apply the filter
            var sum = 0f;
            var totalWeight = 0f;

            for (var tap = -filterSize / 2; tap < filterSize / 2; tap++)
            {
                var sourceIndex = sourceCenterIndex + tap;

                // Skip samples outside buffer boundary
                if (sourceIndex < 0 || sourceIndex >= source.Length)
                {
                    continue;
                }

                // Calculate the sinc weight
                var x = sourcePos - sourceIndex;
                var weight = x == 0 ? 1.0f : (float)(Math.Sin(Math.PI * x) / (Math.PI * x));

                // Apply a Hann window to reduce ringing
                weight *=
                    0.5f * (1 + (float)Math.Cos(2 * Math.PI * (tap + filterSize / 2) / filterSize));

                sum += source[sourceIndex] * weight;
                totalWeight += weight;
            }

            // Normalize the output
            target[targetIndex] = totalWeight > 0 ? sum / totalWeight : 0f;
        }
    }

    /// <summary>
    ///     Resamples audio data to a different sample rate.
    /// </summary>
    /// <param name="sourceBuffer">The source audio buffer.</param>
    /// <param name="targetBuffer">The target audio buffer to write the resampled data to.</param>
    /// <param name="sourceFormat">The format of the source audio.</param>
    /// <param name="targetSampleRate">The target sample rate.</param>
    /// <returns>The number of bytes written to the target buffer.</returns>
    public static int Resample(
        ReadOnlyMemory<byte> sourceBuffer,
        Memory<byte> targetBuffer,
        AudioFormat sourceFormat,
        uint targetSampleRate
    )
    {
        // Create target format with the new sample rate but same other parameters
        var targetFormat = new AudioFormat(
            sourceFormat.Channels,
            sourceFormat.BitsPerSample,
            targetSampleRate
        );

        return Convert(sourceBuffer, targetBuffer, sourceFormat, targetFormat);
    }

    /// <summary>
    ///     Resamples floating-point audio samples directly.
    /// </summary>
    /// <param name="sourceSamples">The source float samples.</param>
    /// <param name="targetSamples">The target buffer to write the resampled samples to.</param>
    /// <param name="channels">Number of channels in the audio.</param>
    /// <param name="sourceSampleRate">Source sample rate.</param>
    /// <param name="targetSampleRate">Target sample rate.</param>
    /// <returns>The number of frames written to the target buffer.</returns>
    public static int ResampleFloat(
        ReadOnlyMemory<float> sourceSamples,
        Memory<float> targetSamples,
        ushort channels,
        uint sourceSampleRate,
        uint targetSampleRate
    )
    {
        // Fast path for same sample rate
        if (sourceSampleRate == targetSampleRate)
        {
            sourceSamples.CopyTo(targetSamples);

            return sourceSamples.Length / channels;
        }

        var sourceFramesCount = sourceSamples.Length / channels;
        var targetFramesCount = CalculateResampledFrameCount(
            sourceFramesCount,
            sourceSampleRate,
            targetSampleRate
        );

        // Ensure target buffer is large enough
        if (targetSamples.Length < targetFramesCount * channels)
        {
            throw new ArgumentException("Target buffer is too small for the resampled audio.");
        }

        // Perform the resampling
        ResampleFloatBuffer(
            sourceSamples.Span,
            targetSamples.Span,
            channels,
            sourceFramesCount,
            targetFramesCount
        );

        return targetFramesCount;
    }

    /// <summary>
    ///     Converts float samples to a different format (channels, sample rate) and outputs as byte array.
    /// </summary>
    /// <param name="sourceSamples">The source float samples.</param>
    /// <param name="targetBuffer">The target buffer to write the converted data to.</param>
    /// <param name="sourceChannels">Number of channels in the source.</param>
    /// <param name="sourceSampleRate">Source sample rate.</param>
    /// <param name="targetFormat">The desired output format.</param>
    /// <returns>The number of bytes written to the target buffer.</returns>
    public static int ConvertFloat(
        ReadOnlyMemory<float> sourceSamples,
        Memory<byte> targetBuffer,
        ushort sourceChannels,
        uint sourceSampleRate,
        AudioFormat targetFormat
    )
    {
        var sourceFramesCount = sourceSamples.Length / sourceChannels;
        var needsResampling = sourceSampleRate != targetFormat.SampleRate;

        // Calculate target frames count after potential resampling
        var targetFramesCount = needsResampling
            ? CalculateResampledFrameCount(
                sourceFramesCount,
                sourceSampleRate,
                targetFormat.SampleRate
            )
            : sourceFramesCount;

        // Calculate expected target buffer size
        var expectedTargetSize = targetFramesCount * targetFormat.BytesPerFrame;

        if (targetBuffer.Length < expectedTargetSize)
        {
            throw new ArgumentException("Target buffer is too small for the converted audio.");
        }

        // Handle resampling if needed
        ReadOnlyMemory<float> resampledSamples;
        if (needsResampling)
        {
            var resampledBuffer = new float[targetFramesCount * sourceChannels];
            ResampleFloatBuffer(
                sourceSamples.Span,
                resampledBuffer.AsSpan(),
                sourceChannels,
                sourceFramesCount,
                targetFramesCount
            );

            resampledSamples = resampledBuffer;
        }
        else
        {
            resampledSamples = sourceSamples;
        }

        // Handle channel conversion if needed
        ReadOnlyMemory<float> convertedSamples;
        if (sourceChannels != targetFormat.Channels)
        {
            var convertedBuffer = new float[targetFramesCount * targetFormat.Channels];
            ConvertChannels(
                resampledSamples.Span,
                convertedBuffer.AsSpan(),
                sourceChannels,
                targetFormat.Channels,
                targetFramesCount
            );

            convertedSamples = convertedBuffer;
        }
        else
        {
            convertedSamples = resampledSamples;
        }

        // Serialize to the target format
        SampleSerializer.Serialize(convertedSamples, targetBuffer, targetFormat.BitsPerSample);

        return expectedTargetSize;
    }

    /// <summary>
    ///     Converts audio format with direct access to float samples.
    /// </summary>
    /// <param name="sourceSamples">The source float samples.</param>
    /// <param name="targetSamples">The target buffer to write the converted samples to.</param>
    /// <param name="sourceChannels">Number of channels in the source.</param>
    /// <param name="targetChannels">Number of channels for the output.</param>
    /// <param name="sourceSampleRate">Source sample rate.</param>
    /// <param name="targetSampleRate">Target sample rate.</param>
    /// <returns>The number of frames written to the target buffer.</returns>
    public static int ConvertFloat(
        ReadOnlyMemory<float> sourceSamples,
        Memory<float> targetSamples,
        ushort sourceChannels,
        ushort targetChannels,
        uint sourceSampleRate,
        uint targetSampleRate
    )
    {
        var sourceFramesCount = sourceSamples.Length / sourceChannels;
        var needsResampling = sourceSampleRate != targetSampleRate;
        var needsChannelConversion = sourceChannels != targetChannels;

        // If no conversion needed, just copy
        if (!needsResampling && !needsChannelConversion)
        {
            sourceSamples.CopyTo(targetSamples);

            return sourceFramesCount;
        }

        // Calculate target frames count after potential resampling
        var targetFramesCount = needsResampling
            ? CalculateResampledFrameCount(sourceFramesCount, sourceSampleRate, targetSampleRate)
            : sourceFramesCount;

        // Ensure target buffer is large enough
        if (targetSamples.Length < targetFramesCount * targetChannels)
        {
            throw new ArgumentException("Target buffer is too small for the converted audio.");
        }

        // Optimize the common path where only channel conversion or only resampling is needed
        if (needsResampling && !needsChannelConversion)
        {
            // Only resample
            ResampleFloatBuffer(
                sourceSamples.Span,
                targetSamples.Span,
                sourceChannels, // same as targetChannels in this case
                sourceFramesCount,
                targetFramesCount
            );

            return targetFramesCount;
        }

        if (!needsResampling && needsChannelConversion)
        {
            // Only convert channels
            ConvertChannels(
                sourceSamples.Span,
                targetSamples.Span,
                sourceChannels,
                targetChannels,
                sourceFramesCount
            );

            return sourceFramesCount;
        }

        // If we need both resampling and channel conversion
        // First resample, then convert channels
        var resampledBuffer = needsResampling
            ? new float[targetFramesCount * sourceChannels]
            : null;

        if (needsResampling)
        {
            ResampleFloatBuffer(
                sourceSamples.Span,
                resampledBuffer.AsSpan(),
                sourceChannels,
                sourceFramesCount,
                targetFramesCount
            );
        }

        // Then convert channels
        ConvertChannels(
            needsResampling ? resampledBuffer.AsSpan() : sourceSamples.Span,
            targetSamples.Span,
            sourceChannels,
            targetChannels,
            targetFramesCount
        );

        return targetFramesCount;
    }

    /// <summary>
    ///     Direct resampling of audio data without format conversion.
    /// </summary>
    private static int ResampleDirect(
        ReadOnlyMemory<byte> sourceBuffer,
        Memory<byte> targetBuffer,
        AudioFormat sourceFormat,
        AudioFormat targetFormat,
        int sourceFramesCount,
        int targetFramesCount
    )
    {
        // Convert to float for processing
        var floatSamples = new float[sourceFramesCount * sourceFormat.Channels];
        SampleSerializer.Deserialize(
            sourceBuffer,
            floatSamples.AsMemory(),
            sourceFormat.BitsPerSample
        );

        // Resample the float samples
        var resampledBuffer = new float[targetFramesCount * targetFormat.Channels];
        ResampleFloatBuffer(
            floatSamples.AsSpan(),
            resampledBuffer.AsSpan(),
            sourceFormat.Channels,
            sourceFramesCount,
            targetFramesCount
        );

        // Serialize back to the target format
        SampleSerializer.Serialize(resampledBuffer, targetBuffer, targetFormat.BitsPerSample);

        return targetFramesCount * targetFormat.BytesPerFrame;
    }

    /// <summary>
    ///     Resamples floating-point audio samples.
    /// </summary>
    private static void ResampleFloatBuffer(
        ReadOnlySpan<float> sourceBuffer,
        Span<float> targetBuffer,
        ushort channels,
        int sourceFramesCount,
        int targetFramesCount
    )
    {
        // Calculate the step size for linear interpolation
        var step = (double)(sourceFramesCount - 1) / (targetFramesCount - 1);

        for (var targetFrame = 0; targetFrame < targetFramesCount; targetFrame++)
        {
            // Calculate source position (as a floating point value)
            var sourcePos = targetFrame * step;

            // Get indices of the two source frames to interpolate between
            var sourceFrameLow = (int)sourcePos;
            var sourceFrameHigh = Math.Min(sourceFrameLow + 1, sourceFramesCount - 1);

            // Calculate interpolation factor
            var fraction = (float)(sourcePos - sourceFrameLow);

            // Interpolate each channel
            for (var channel = 0; channel < channels; channel++)
            {
                var sourceLowIndex = sourceFrameLow * channels + channel;
                var sourceHighIndex = sourceFrameHigh * channels + channel;
                var targetIndex = targetFrame * channels + channel;

                // Linear interpolation
                targetBuffer[targetIndex] = Lerp(
                    sourceBuffer[sourceLowIndex],
                    sourceBuffer[sourceHighIndex],
                    fraction
                );
            }
        }
    }

    /// <summary>
    ///     Linear interpolation between two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    /// <summary>
    ///     Converts mono float32 audio to stereo int16 PCM format.
    /// </summary>
    /// <param name="monoFloat32Buffer">The mono float32 audio buffer.</param>
    /// <param name="targetBuffer">The target buffer to receive the stereo int16 PCM data.</param>
    /// <param name="sampleRate">The sample rate of the audio (preserved in the conversion).</param>
    /// <returns>The number of bytes written to the target buffer.</returns>
    public static int ConvertMonoFloat32ToStereoInt16(
        ReadOnlyMemory<byte> monoFloat32Buffer,
        Memory<byte> targetBuffer,
        uint sampleRate = 44100
    )
    {
        var sourceFormat = AudioFormat.CreateMono(32, sampleRate);
        var targetFormat = AudioFormat.CreateStereo(16, sampleRate);

        return Convert(monoFloat32Buffer, targetBuffer, sourceFormat, targetFormat);
    }

    /// <summary>
    ///     Converts mono int16 audio to stereo float32 PCM format.
    /// </summary>
    /// <param name="monoInt16Buffer">The mono int16 audio buffer.</param>
    /// <param name="targetBuffer">The target buffer to receive the stereo float32 PCM data.</param>
    /// <param name="sampleRate">The sample rate of the audio (preserved in the conversion).</param>
    /// <returns>The number of bytes written to the target buffer.</returns>
    public static int ConvertMonoInt16ToStereoFloat32(
        ReadOnlyMemory<byte> monoInt16Buffer,
        Memory<byte> targetBuffer,
        uint sampleRate = 44100
    )
    {
        var sourceFormat = AudioFormat.CreateMono(16, sampleRate);
        var targetFormat = AudioFormat.CreateStereo(32, sampleRate);

        // Use fast path for this specific conversion
        var framesCount = monoInt16Buffer.Length / sourceFormat.BytesPerFrame;
        var expectedTargetSize = framesCount * targetFormat.BytesPerFrame;

        if (targetBuffer.Length < expectedTargetSize)
        {
            throw new ArgumentException("Target buffer is too small for the converted audio.");
        }

        ConvertMonoInt16ToStereoFloat32Direct(monoInt16Buffer, targetBuffer, framesCount);

        return expectedTargetSize;
    }

    /// <summary>
    ///     Optimized direct conversion from mono float32 to stereo int16.
    /// </summary>
    private static void ConvertMonoFloat32ToStereoInt16Direct(
        ReadOnlyMemory<byte> source,
        Memory<byte> target,
        int framesCount
    )
    {
        var sourceSpan = source.Span;
        var targetSpan = target.Span;

        for (var frame = 0; frame < framesCount; frame++)
        {
            var sourceIndex = frame * 4; // 4 bytes per float32
            var targetIndex = frame * 4; // 2 bytes per int16 * 2 channels

            // Read float32 value
            var floatValue = BinaryPrimitives.ReadSingleLittleEndian(
                sourceSpan.Slice(sourceIndex, 4)
            );

            // Convert to int16 (with clamping)
            var int16Value = ClampToInt16(floatValue * 32767f);

            // Write the same value to both left and right channels
            BinaryPrimitives.WriteInt16LittleEndian(targetSpan.Slice(targetIndex, 2), int16Value);
            BinaryPrimitives.WriteInt16LittleEndian(
                targetSpan.Slice(targetIndex + 2, 2),
                int16Value
            );
        }
    }

    /// <summary>
    ///     Optimized direct conversion from stereo int16 to mono float32.
    /// </summary>
    private static void ConvertStereoInt16ToMonoFloat32Direct(
        ReadOnlyMemory<byte> source,
        Memory<byte> target,
        int framesCount
    )
    {
        var sourceSpan = source.Span;
        var targetSpan = target.Span;

        for (var frame = 0; frame < framesCount; frame++)
        {
            var sourceIndex = frame * 4; // 2 bytes per int16 * 2 channels
            var targetIndex = frame * 4; // 4 bytes per float32

            // Read int16 values for left and right channels
            var leftValue = BinaryPrimitives.ReadInt16LittleEndian(
                sourceSpan.Slice(sourceIndex, 2)
            );
            var rightValue = BinaryPrimitives.ReadInt16LittleEndian(
                sourceSpan.Slice(sourceIndex + 2, 2)
            );

            // Convert to float32 and average the channels
            var floatValue = (leftValue + rightValue) * 0.5f / 32768f;

            // Write to target buffer
            BinaryPrimitives.WriteSingleLittleEndian(targetSpan.Slice(targetIndex, 4), floatValue);
        }
    }

    /// <summary>
    ///     Optimized direct conversion from mono int16 to stereo float32.
    /// </summary>
    private static void ConvertMonoInt16ToStereoFloat32Direct(
        ReadOnlyMemory<byte> source,
        Memory<byte> target,
        int framesCount
    )
    {
        var sourceSpan = source.Span;
        var targetSpan = target.Span;

        for (var frame = 0; frame < framesCount; frame++)
        {
            var sourceIndex = frame * 2; // 2 bytes per int16
            var targetIndex = frame * 8; // 4 bytes per float32 * 2 channels

            // Read int16 value
            var int16Value = BinaryPrimitives.ReadInt16LittleEndian(
                sourceSpan.Slice(sourceIndex, 2)
            );

            // Convert to float32
            var floatValue = int16Value / 32768f;

            // Write the same float value to both left and right channels
            BinaryPrimitives.WriteSingleLittleEndian(targetSpan.Slice(targetIndex, 4), floatValue);
            BinaryPrimitives.WriteSingleLittleEndian(
                targetSpan.Slice(targetIndex + 4, 4),
                floatValue
            );
        }
    }

    /// <summary>
    ///     General case conversion using intermediate float format.
    /// </summary>
    private static int ConvertGeneral(
        ReadOnlyMemory<byte> sourceBuffer,
        Memory<byte> targetBuffer,
        AudioFormat sourceFormat,
        AudioFormat targetFormat,
        int sourceFramesCount,
        int targetFramesCount,
        bool needsResampling
    )
    {
        // Deserialize to float samples - this will give us interleaved float samples
        var floatSamples = new float[sourceFramesCount * sourceFormat.Channels];
        SampleSerializer.Deserialize(
            sourceBuffer,
            floatSamples.AsMemory(),
            sourceFormat.BitsPerSample
        );

        // Perform resampling if needed
        Memory<float> resampledSamples;
        int actualFrameCount;

        if (needsResampling)
        {
            var resampledBuffer = new float[targetFramesCount * sourceFormat.Channels];
            ResampleFloatBuffer(
                floatSamples.AsSpan(),
                resampledBuffer.AsSpan(),
                sourceFormat.Channels,
                sourceFramesCount,
                targetFramesCount
            );

            resampledSamples = resampledBuffer;
            actualFrameCount = targetFramesCount;
        }
        else
        {
            resampledSamples = floatSamples;
            actualFrameCount = sourceFramesCount;
        }

        // Convert channel configuration if needed
        Memory<float> convertedSamples;

        if (sourceFormat.Channels != targetFormat.Channels)
        {
            var convertedBuffer = new float[actualFrameCount * targetFormat.Channels];
            ConvertChannels(
                resampledSamples.Span,
                convertedBuffer.AsSpan(),
                sourceFormat.Channels,
                targetFormat.Channels,
                actualFrameCount
            );

            convertedSamples = convertedBuffer;
        }
        else
        {
            // No channel conversion needed
            convertedSamples = resampledSamples;
        }

        // Serialize to the target format
        SampleSerializer.Serialize(convertedSamples, targetBuffer, targetFormat.BitsPerSample);

        return actualFrameCount * targetFormat.BytesPerFrame;
    }

    /// <summary>
    ///     Converts between different channel configurations.
    /// </summary>
    private static void ConvertChannels(
        ReadOnlySpan<float> source,
        Span<float> target,
        ushort sourceChannels,
        ushort targetChannels,
        int framesCount
    )
    {
        // If source and target have the same number of channels, just copy
        if (sourceChannels == targetChannels)
        {
            source.CopyTo(target);

            return;
        }

        // Handle specific conversions with optimized implementations
        if (sourceChannels == 1 && targetChannels == 2)
        {
            // Mono to stereo conversion
            ConvertMonoToStereo(source, target, framesCount);
        }
        else if (sourceChannels == 2 && targetChannels == 1)
        {
            // Stereo to mono conversion
            ConvertStereoToMono(source, target, framesCount);
        }
        else
        {
            // More general conversion implementation
            ConvertChannelsGeneral(source, target, sourceChannels, targetChannels, framesCount);
        }
    }

    /// <summary>
    ///     Converts mono audio to stereo by duplicating each sample.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertMonoToStereo(
        ReadOnlySpan<float> source,
        Span<float> target,
        int framesCount
    )
    {
        for (var frame = 0; frame < framesCount; frame++)
        {
            var sourceSample = source[frame];
            var targetIndex = frame * 2;

            target[targetIndex] = sourceSample; // Left channel
            target[targetIndex + 1] = sourceSample; // Right channel
        }
    }

    /// <summary>
    ///     Converts stereo audio to mono by averaging the channels.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertStereoToMono(
        ReadOnlySpan<float> source,
        Span<float> target,
        int framesCount
    )
    {
        for (var frame = 0; frame < framesCount; frame++)
        {
            var sourceIndex = frame * 2;
            var leftSample = source[sourceIndex];
            var rightSample = source[sourceIndex + 1];

            target[frame] = (leftSample + rightSample) * 0.5f; // Average the channels
        }
    }

    /// <summary>
    ///     General method for converting between different channel configurations.
    /// </summary>
    private static void ConvertChannelsGeneral(
        ReadOnlySpan<float> source,
        Span<float> target,
        ushort sourceChannels,
        ushort targetChannels,
        int framesCount
    )
    {
        ConvertChannelsChunk(source, target, sourceChannels, targetChannels, 0, framesCount);
    }

    /// <summary>
    ///     Converts a chunk of frames between different channel configurations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertChannelsChunk(
        ReadOnlySpan<float> source,
        Span<float> target,
        ushort sourceChannels,
        ushort targetChannels,
        int startFrame,
        int endFrame
    )
    {
        for (var frame = startFrame; frame < endFrame; frame++)
        {
            var sourceFrameOffset = frame * sourceChannels;
            var targetFrameOffset = frame * targetChannels;

            // Find the minimum of source and target channels
            var minChannels = Math.Min(sourceChannels, targetChannels);

            // Copy the available channels
            for (var channel = 0; channel < minChannels; channel++)
            {
                target[targetFrameOffset + channel] = source[sourceFrameOffset + channel];
            }

            // If target has more channels than source, duplicate the last channel
            for (var channel = minChannels; channel < targetChannels; channel++)
            {
                target[targetFrameOffset + channel] = source[sourceFrameOffset + (minChannels - 1)];
            }
        }
    }

    /// <summary>
    ///     Clamps a float value to the range of a 16-bit integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short ClampToInt16(float value)
    {
        if (value > 32767f)
        {
            return 32767;
        }

        if (value < -32768f)
        {
            return -32768;
        }

        return (short)value;
    }
}
