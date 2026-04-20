using System.Buffers.Binary;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Serializer for converting float samples to and from PCM byte buffers.
/// </summary>
/// <remarks>
///     For now, this class only supports 8, 16, 24, 32 and 64 bits per sample.
/// </remarks>
public static class SampleSerializer
{
    /// <summary>
    ///     Deserialize the PCM byte buffer into a new float samples.
    /// </summary>
    /// <returns></returns>
    public static Memory<float> Deserialize(ReadOnlyMemory<byte> buffer, ushort bitsPerSample)
    {
        var floatBuffer = new float[buffer.Length / (bitsPerSample / 8)];
        Deserialize(buffer, floatBuffer, bitsPerSample);

        return floatBuffer.AsMemory();
    }

    /// <summary>
    ///     Serializes the float samples into a PCM byte buffer.
    /// </summary>
    /// <returns></returns>
    public static Memory<byte> Serialize(ReadOnlyMemory<float> samples, ushort bitsPerSample)
    {
        // Transform to long as we might overflow the int because of the multiplications (even if we cast to int later)
        var memoryBufferLength = (long)samples.Length * bitsPerSample / 8;

        var buffer = new byte[(int)memoryBufferLength];
        Serialize(samples, buffer, bitsPerSample);

        return buffer.AsMemory();
    }

    /// <summary>
    ///     Serializes the float samples into a PCM byte buffer.
    /// </summary>
    public static void Serialize(
        ReadOnlyMemory<float> samples,
        Memory<byte> buffer,
        ushort bitsPerSample
    )
    {
        var bytesPerSample = bitsPerSample / 8;
        var totalSamples = samples.Length;
        var totalBytes = totalSamples * bytesPerSample;

        if (buffer.Length < totalBytes)
        {
            throw new ArgumentException("Buffer too small to hold the serialized data.");
        }

        var samplesSpan = samples.Span;
        var bufferSpan = buffer.Span;

        var sampleIndex = 0;
        var bufferIndex = 0;

        while (sampleIndex < totalSamples)
        {
            var sampleValue = samplesSpan[sampleIndex];
            WriteSample(bufferSpan, bufferIndex, sampleValue, bitsPerSample);
            bufferIndex += bytesPerSample;
            sampleIndex++;
        }
    }

    /// <summary>
    ///     Deserializes the PCM byte buffer into float samples.
    /// </summary>
    public static void Deserialize(
        ReadOnlyMemory<byte> buffer,
        Memory<float> samples,
        ushort bitsPerSample
    )
    {
        var bytesPerSample = bitsPerSample / 8;
        var totalSamples = buffer.Length / bytesPerSample;

        if (samples.Length < totalSamples)
        {
            throw new ArgumentException(
                "Samples buffer is too small to hold the deserialized data."
            );
        }

        var bufferSpan = buffer.Span;
        var samplesSpan = samples.Span;

        var sampleIndex = 0;
        var bufferIndex = 0;

        while (bufferIndex < bufferSpan.Length)
        {
            var sampleValue = ReadSample(bufferSpan, ref bufferIndex, bitsPerSample);
            samplesSpan[sampleIndex++] = sampleValue;
            // bufferIndex is already incremented inside ReadSample
        }
    }

    /// <summary>
    ///     Reads a single sample from the byte span, considering the bit index and sample bit depth.
    /// </summary>
    internal static float ReadSample(ReadOnlySpan<byte> span, ref int index, ushort bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;

        float sampleValue;

        switch (bitsPerSample)
        {
            case 8:
                var sampleByte = span[index];
                sampleValue = sampleByte / 127.5f - 1.0f;

                break;

            case 16:
                var sampleShort = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(index, 2));
                sampleValue = sampleShort / 32768f;

                break;

            case 24:
                var sample24Bit = ReadInt24LittleEndian(span.Slice(index, 3));
                sampleValue = sample24Bit / 8388608f;

                break;

            case 32:
                var sampleInt = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(index, 4));
                sampleValue = sampleInt / 2147483648f;

                break;

            case 64:
                var sampleLong = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(index, 8));
                sampleValue = sampleLong / 9223372036854775808f;

                break;

            default:
                throw new NotSupportedException(
                    $"Bits per sample {bitsPerSample} is not supported."
                );
        }

        index += bytesPerSample;

        return sampleValue;
    }

    /// <summary>
    ///     Writes a single sample into the byte span at the specified index.
    /// </summary>
    internal static void WriteSample(
        Span<byte> span,
        int index,
        float sampleValue,
        ushort bitsPerSample
    )
    {
        switch (bitsPerSample)
        {
            case 8:
                var sampleByte = (byte)((sampleValue + 1.0f) * 127.5f);
                span[index] = sampleByte;

                break;

            case 16:
                var sampleShort = (short)(sampleValue * 32767f);
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(index, 2), sampleShort);

                break;

            case 24:
                var sample24Bit = (int)(sampleValue * 8388607f);
                WriteInt24LittleEndian(span.Slice(index, 3), sample24Bit);

                break;

            case 32:
                var sampleInt = (int)(sampleValue * 2147483647f);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(index, 4), sampleInt);

                break;

            case 64:
                var sampleLong = (long)(sampleValue * 9223372036854775807f);
                BinaryPrimitives.WriteInt64LittleEndian(span.Slice(index, 8), sampleLong);

                break;

            default:
                throw new NotSupportedException(
                    $"Bits per sample {bitsPerSample} is not supported."
                );
        }
    }

    /// <summary>
    ///     Reads a 24-bit integer from a byte span in little-endian order.
    /// </summary>
    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> span)
    {
        int b0 = span[0];
        int b1 = span[1];
        int b2 = span[2];
        var sample = (b2 << 16) | (b1 << 8) | b0;

        // Sign-extend to 32 bits if necessary
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return sample;
    }

    /// <summary>
    ///     Writes a 24-bit integer to a byte span in little-endian order.
    /// </summary>
    private static void WriteInt24LittleEndian(Span<byte> span, int value)
    {
        span[0] = (byte)(value & 0xFF);
        span[1] = (byte)((value >> 8) & 0xFF);
        span[2] = (byte)((value >> 16) & 0xFF);
    }
}
