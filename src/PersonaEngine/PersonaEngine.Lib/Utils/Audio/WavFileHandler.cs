using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Utils.Pooling;

namespace PersonaEngine.Lib.Utils.Audio;

/// <summary>
///     Unified utility class for reading, writing, and parsing WAV files.
/// </summary>
internal static class WavFileHandler
{
    private const int RIFF_CHUNK_ID = 0x46464952; // "RIFF" in ASCII

    private const int WAVE_FORMAT = 0x45564157; // "WAVE" in ASCII

    private const int FMT_CHUNK_ID = 0x20746D66; // "fmt " in ASCII

    private const int DATA_CHUNK_ID = 0x61746164; // "data" in ASCII

    private const int PCM_FORMAT = 1; // PCM audio format

    private static readonly byte[] ExpectedSubFormatForPcm =
    [
        0x01,
        0x00,
        0x00,
        0x00,
        0x00,
        0x00,
        0x10,
        0x00,
        0x80,
        0x00,
        0x00,
        0xaa,
        0x00,
        0x38,
        0x9b,
        0x71,
    ];

    #region Write / Append

    public static void SaveToWav(
        Memory<float> samples,
        string filePath,
        int sampleRate = 44100,
        int channels = 1
    )
    {
        using (var writer = new BinaryWriter(File.OpenWrite(filePath)))
        {
            WriteWavFile(writer, samples, sampleRate, channels);
        }
    }

    public static void AppendToWav(Memory<float> samples, string filePath)
    {
        // 1) Read header to find data start and current sizes
        WavHeader header;
        long dataStart;
        using (
            var reader = new BinaryReader(
                File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            )
        )
        {
            header = ReadWavHeader(reader);
            dataStart = reader.BaseStream.Position; // start of data (right after the 'data' size field)
        }

        if (samples.Length % header.Channels != 0)
            throw new ArgumentException(
                "Sample buffer length must be a multiple of channel count."
            );

        // 2) Open for R/W and position EXACTLY at end of valid audio payload
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read
        );
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        long writePos = dataStart + header.DataSize;
        stream.Position = writePos;
        stream.SetLength(writePos); // drop any stale bytes beyond current data

        // 3) Write new samples
        var span = samples.Span;
        for (int i = 0; i < span.Length; i++)
        {
            // Consider clamping to [-1,1] to avoid wrap
            float f = MathF.Max(-1f, MathF.Min(1f, span[i]));
            short pcm = (short)(f * short.MaxValue);
            writer.Write(pcm);
        }

        // 4) Update sizes (and keep them block-aligned)
        int addedBytes = samples.Length * sizeof(short);
        int newDataSize = header.DataSize + addedBytes;
        if (newDataSize % header.BlockAlign != 0)
            throw new InvalidOperationException("Data size became unaligned to blockAlign.");

        // RIFF size = 4 ("WAVE") + (8+fmt) + (8+data)
        int riffSize = 4 + (8 + 16) + (8 + newDataSize);

        stream.Position = 4; // RIFF chunk size
        writer.Write(riffSize);

        stream.Position = dataStart - 4; // 'data' chunk size field
        writer.Write(newDataSize);
    }

    private static void WriteWavFile(
        BinaryWriter writer,
        Memory<float> samples,
        int sampleRate,
        int channels
    )
    {
        var bytesPerSample = sizeof(short); // We'll convert float to 16-bit PCM
        var dataSize = samples.Length * bytesPerSample;
        var headerSize = 44; // Standard WAV header size
        var fileSize = headerSize + dataSize - 8; // Total file size - 8 bytes

        // Write WAV header
        writer.Write(RIFF_CHUNK_ID); // "RIFF" chunk
        writer.Write(fileSize); // File size - 8
        writer.Write(WAVE_FORMAT); // "WAVE" format

        // Write format chunk
        writer.Write(FMT_CHUNK_ID); // "fmt " chunk
        writer.Write(16); // Format chunk size (16 for PCM)
        writer.Write((short)PCM_FORMAT); // Audio format (1 for PCM)
        writer.Write((short)channels); // Number of channels
        writer.Write(sampleRate); // Sample rate
        writer.Write(sampleRate * channels * bytesPerSample); // Byte rate
        writer.Write((short)(channels * bytesPerSample)); // Block align
        writer.Write((short)(bytesPerSample * 8)); // Bits per sample

        // Write data chunk
        writer.Write(DATA_CHUNK_ID); // "data" chunk
        writer.Write(dataSize); // Data size

        // Write audio samples
        var span = samples.Span;
        for (var i = 0; i < span.Length; i++)
        {
            var pcm = (short)(span[i] * short.MaxValue);
            writer.Write(pcm);
        }
    }

    #endregion

    #region Read

    public static async IAsyncEnumerable<float> ReadWavAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true
        );

        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
        var header = ReadWavHeader(br); // Must position stream at start of data

        if (header.BitsPerSample != 16)
            throw new NotSupportedException("Only 16-bit PCM WAV files are supported.");
        if (header.Channels != 1 && header.Channels != 2)
            throw new NotSupportedException("Only mono or interleaved stereo are supported.");

        int bytesPerSample = header.BitsPerSample / 8; // 2
        int frameSize = bytesPerSample * header.Channels; // 2 (mono) or 4 (stereo)
        int remaining = header.DataSize;

        // Make buffer a multiple of frameSize so we can process whole frames.
        int baseSize = 64 * 1024;
        int bufferLen = Math.Max(frameSize, (baseSize / frameSize) * frameSize);
        var buffer = new byte[bufferLen];

        int carry = 0; // leftover bytes < frameSize from previous read

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int maxToRead = Math.Min(buffer.Length - carry, remaining);
            int read = await fs.ReadAsync(buffer.AsMemory(carry, maxToRead), cancellationToken);
            if (read == 0)
                break;

            int total = carry + read;
            int usable = total - (total % frameSize); // only full frames

            if (header.Channels == 1)
            {
                // Mono: yield one float per sample
                for (int i = 0; i < usable; i += 2)
                {
                    short pcm = (short)(buffer[i] | (buffer[i + 1] << 8)); // little-endian
                    yield return pcm / (float)short.MaxValue; // [-1, 1]
                }
            }
            else
            {
                // Stereo (interleaved L,R): yield L then R
                for (int i = 0; i < usable; i += 4)
                {
                    short l = (short)(buffer[i] | (buffer[i + 1] << 8));
                    short r = (short)(buffer[i + 2] | (buffer[i + 3] << 8));
                    yield return l / (float)short.MaxValue;
                    yield return r / (float)short.MaxValue;
                }
            }

            remaining -= usable;

            // Move leftover (partial frame) to front for next read
            carry = total - usable;
            if (carry > 0)
            {
                Array.Copy(buffer, usable, buffer, 0, carry);
            }
        }
    }

    public static bool ValidateWavFile(string filePath)
    {
        try
        {
            using (var reader = new BinaryReader(File.OpenRead(filePath)))
            {
                ReadWavHeader(reader);

                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    #endregion

    #region Streaming Header Parse

    public static async Task<HeaderParseResult> ParseHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var toReturn = new List<byte[]>();
        try
        {
            var headerChunks = new MergedMemoryChunks();

            // We load the first 44 bytes directly as we know that is the minimum size for a wave file header.
            if (!await HaveEnoughDataAsync(headerChunks, 44, stream, toReturn, cancellationToken))
            {
                return HeaderParseResult.WaitingForMoreData;
            }

            AudioSourceHeader? header = null;

            var chunkID = headerChunks.ReadUInt32LittleEndian();
            // Skip the file size
            headerChunks.TrySkip(4);
            var riffType = headerChunks.ReadUInt32LittleEndian();

            if (chunkID != RIFF_CHUNK_ID || riffType != WAVE_FORMAT)
            {
                return HeaderParseResult.Corrupt("Invalid wave file header.");
            }

            // Read chunks until we find 'fmt ' and 'data'
            while (true)
            {
                if (
                    !await HaveEnoughDataAsync(headerChunks, 8, stream, toReturn, cancellationToken)
                )
                {
                    return HeaderParseResult.WaitingForMoreData;
                }

                var chunkType = headerChunks.ReadUInt32LittleEndian();
                var chunkSize = headerChunks.ReadUInt32LittleEndian();

                if (chunkType == FMT_CHUNK_ID)
                {
                    if (chunkSize < 16)
                    {
                        return HeaderParseResult.Corrupt("Invalid wave file format chunk.");
                    }

                    if (
                        !await HaveEnoughDataAsync(
                            headerChunks,
                            chunkSize,
                            stream,
                            toReturn,
                            cancellationToken
                        )
                    )
                    {
                        return HeaderParseResult.WaitingForMoreData;
                    }

                    var formatTag = headerChunks.ReadUInt16LittleEndian();
                    var channels = headerChunks.ReadUInt16LittleEndian();
                    var sampleRate = headerChunks.ReadUInt32LittleEndian();
                    var avgBytesPerSec = headerChunks.ReadUInt32LittleEndian();
                    var blockAlign = headerChunks.ReadUInt16LittleEndian();
                    var bitsPerSample = headerChunks.ReadUInt16LittleEndian();

                    if (formatTag != PCM_FORMAT && formatTag != 0xFFFE) // PCM or WAVE_FORMAT_EXTENSIBLE
                    {
                        return HeaderParseResult.NotSupported("Unsupported wave file format.");
                    }

                    ushort? cbSize = null;
                    ushort? validBitsPerSample = null;
                    uint? channelMask = null;
                    if (formatTag == 0xFFFE)
                    {
                        // WAVE_FORMAT_EXTENSIBLE
                        if (chunkSize < 40)
                        {
                            return HeaderParseResult.Corrupt("Invalid wave file format chunk.");
                        }

                        cbSize = headerChunks.ReadUInt16LittleEndian();
                        validBitsPerSample = headerChunks.ReadUInt16LittleEndian();
                        channelMask = headerChunks.ReadUInt32LittleEndian();
                        var subFormatChunk = headerChunks.GetChunk(16);

                        if (!subFormatChunk.Span.SequenceEqual(ExpectedSubFormatForPcm))
                        {
                            return HeaderParseResult.NotSupported("Unsupported wave file format.");
                        }
                    }

                    if (channels == 0)
                    {
                        return HeaderParseResult.NotSupported(
                            "Cannot read wave file with 0 channels."
                        );
                    }

                    // Skip any remaining bytes in fmt chunk
                    var remainingBytes = chunkSize - (formatTag == PCM_FORMAT ? 16u : 40u);
                    if (!SkipData(headerChunks, remainingBytes, stream))
                    {
                        return HeaderParseResult.WaitingForMoreData;
                    }

                    header = new AudioSourceHeader
                    {
                        Channels = channels,
                        SampleRate = sampleRate,
                        BitsPerSample = bitsPerSample,
                    };
                }
                else if (chunkType == DATA_CHUNK_ID)
                {
                    if (header == null)
                    {
                        return HeaderParseResult.Corrupt("Data chunk found before format chunk.");
                    }

                    // Found 'data' chunk
                    // We can start processing samples after this point
                    var dataOffset = (int)(
                        headerChunks.Position - headerChunks.AbsolutePositionOfCurrentChunk
                    );

                    return HeaderParseResult.Success(header, dataOffset, chunkSize);
                }
                else
                {
                    if (!SkipData(headerChunks, chunkSize, stream))
                    {
                        return HeaderParseResult.WaitingForMoreData;
                    }
                }
            }
        }
        catch
        {
            foreach (var rented in toReturn)
            {
                ArrayPool<byte>.Shared.Return(rented, ArrayPoolConfig.ClearOnReturn);
            }
        }

        return HeaderParseResult.WaitingForMoreData;
    }

    public static HeaderParseResult ParseHeader(MergedMemoryChunks headerChunks)
    {
        if (headerChunks.Length < 12)
        {
            return HeaderParseResult.WaitingForMoreData;
        }

        AudioSourceHeader? header = null;

        var chunkID = headerChunks.ReadUInt32LittleEndian();
        // Skip the file size
        headerChunks.TrySkip(4);
        var riffType = headerChunks.ReadUInt32LittleEndian();

        if (chunkID != RIFF_CHUNK_ID || riffType != WAVE_FORMAT)
        {
            return HeaderParseResult.Corrupt("Invalid wave file header.");
        }

        // Read chunks until we find 'fmt ' and 'data'
        while (headerChunks.Position + 8 <= headerChunks.Length)
        {
            var chunkType = headerChunks.ReadUInt32LittleEndian();
            var chunkSize = headerChunks.ReadUInt32LittleEndian();

            if (chunkType == FMT_CHUNK_ID)
            {
                if (chunkSize < 16)
                {
                    return HeaderParseResult.Corrupt("Invalid wave file format chunk.");
                }

                if (headerChunks.Position + chunkSize > headerChunks.Length)
                {
                    return HeaderParseResult.WaitingForMoreData;
                }

                var formatTag = headerChunks.ReadUInt16LittleEndian();
                var channels = headerChunks.ReadUInt16LittleEndian();
                var sampleRate = headerChunks.ReadUInt32LittleEndian();
                headerChunks.TrySkip(6); // avgBytesPerSec + blockAlign

                var bitsPerSample = headerChunks.ReadUInt16LittleEndian();

                if (formatTag != PCM_FORMAT && formatTag != 0xFFFE) // PCM or WAVE_FORMAT_EXTENSIBLE
                {
                    return HeaderParseResult.NotSupported("Unsupported wave file format.");
                }

                if (formatTag == 0xFFFE)
                {
                    // WAVE_FORMAT_EXTENSIBLE
                    if (chunkSize < 40)
                    {
                        return HeaderParseResult.Corrupt("Invalid wave file format chunk.");
                    }

                    headerChunks.TrySkip(8); // cbSize + validBitsPerSample + channelMask
                    var subFormatChunk = headerChunks.GetChunk(16);

                    if (!subFormatChunk.Span.SequenceEqual(ExpectedSubFormatForPcm))
                    {
                        return HeaderParseResult.NotSupported("Unsupported wave file format.");
                    }
                }

                if (channels == 0)
                {
                    return HeaderParseResult.NotSupported("Cannot read wave file with 0 channels.");
                }

                // Skip any remaining bytes in fmt chunk
                if (!headerChunks.TrySkip(chunkSize - (formatTag == PCM_FORMAT ? 16u : 40u)))
                {
                    return HeaderParseResult.WaitingForMoreData;
                }

                header = new AudioSourceHeader
                {
                    Channels = channels,
                    SampleRate = sampleRate,
                    BitsPerSample = bitsPerSample,
                };
            }
            else if (chunkType == DATA_CHUNK_ID)
            {
                if (header == null)
                {
                    return HeaderParseResult.Corrupt("Data chunk found before format chunk.");
                }

                // Found 'data' chunk
                // We can start processing samples after this point
                var dataOffset = (int)(
                    headerChunks.Position - headerChunks.AbsolutePositionOfCurrentChunk
                );

                return HeaderParseResult.Success(header, dataOffset, chunkSize);
            }
            else
            {
                if (!headerChunks.TrySkip(chunkSize))
                {
                    return HeaderParseResult.WaitingForMoreData;
                }
            }
        }

        return HeaderParseResult.WaitingForMoreData;
    }

    #endregion

    #region Private Helpers

    private static WavHeader ReadWavHeader(BinaryReader reader)
    {
        // Verify RIFF header
        if (reader.ReadInt32() != RIFF_CHUNK_ID)
        {
            throw new InvalidDataException("Not a valid RIFF file");
        }

        var header = new WavHeader { FileSize = reader.ReadInt32() };

        // Verify WAVE format
        if (reader.ReadInt32() != WAVE_FORMAT)
        {
            throw new InvalidDataException("Not a valid WAVE file");
        }

        // Read format chunk
        if (reader.ReadInt32() != FMT_CHUNK_ID)
        {
            throw new InvalidDataException("Missing format chunk");
        }

        var fmtSize = reader.ReadInt32();
        if (reader.ReadInt16() != PCM_FORMAT)
        {
            throw new InvalidDataException("Unsupported audio format (must be PCM)");
        }

        header.Channels = reader.ReadInt16();
        header.SampleRate = reader.ReadInt32();
        header.ByteRate = reader.ReadInt32();
        header.BlockAlign = reader.ReadInt16();
        header.BitsPerSample = reader.ReadInt16();

        // Skip any extra format bytes
        if (fmtSize > 16)
        {
            reader.BaseStream.Seek(fmtSize - 16, SeekOrigin.Current);
        }

        // Find data chunk
        while (true)
        {
            var chunkId = reader.ReadInt32();
            var chunkSize = reader.ReadInt32();

            if (chunkId == DATA_CHUNK_ID)
            {
                header.DataSize = chunkSize;

                break;
            }

            reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);

            if (reader.BaseStream.Position >= reader.BaseStream.Length)
            {
                throw new InvalidDataException("Missing data chunk");
            }
        }

        return header;
    }

    /// <summary>
    ///     Ensures that the given number of bytes can be read from the memory chunks.
    /// </summary>
    private static async Task<bool> HaveEnoughDataAsync(
        MergedMemoryChunks headerChunks,
        uint requiredBytes,
        Stream stream,
        List<byte[]> returnItems,
        CancellationToken cancellationToken
    )
    {
        var extraBytesNeeded = (int)(requiredBytes - (headerChunks.Length - headerChunks.Position));
        if (extraBytesNeeded <= 0)
        {
            return true;
        }

        // We try to read the next chunk from the stream
        var nextChunk = ArrayPool<byte>.Shared.Rent(extraBytesNeeded);
        returnItems.Add(nextChunk);
        var chunkMemory = nextChunk.AsMemory(0, extraBytesNeeded);

        var actualReadNext = await stream.ReadAsync(chunkMemory, cancellationToken);

        if (actualReadNext != extraBytesNeeded)
        {
            return false;
        }

        headerChunks.AddChunk(chunkMemory);

        return true;
    }

    private static bool SkipData(MergedMemoryChunks headerChunks, uint skipBytes, Stream stream)
    {
        var extraBytesNeededToSkip = (int)(
            skipBytes - (headerChunks.Length - headerChunks.Position)
        );
        if (extraBytesNeededToSkip <= 0)
        {
            return headerChunks.TrySkip(skipBytes);
        }

        // We skip all the bytes we have in the current chunks + the remaining bytes from the stream directly
        if (!headerChunks.TrySkip((uint)(headerChunks.Length - headerChunks.Position)))
        {
            return false;
        }

        var skipped = stream.Seek(skipBytes, SeekOrigin.Current);

        return skipped == extraBytesNeededToSkip;
    }

    #endregion

    #region Inner Types

    private struct WavHeader
    {
        public int FileSize;

        public int Channels;

        public int SampleRate;

        public int ByteRate;

        public short BlockAlign;

        public short BitsPerSample;

        public int DataSize;
    }

    internal class HeaderParseResult(
        bool isSuccess,
        bool isIncomplete,
        bool isCorrupt,
        bool isNotSupported,
        int dataOffset,
        long dataChunkSize,
        AudioSourceHeader? header,
        string? errorMessage
    )
    {
        public static HeaderParseResult WaitingForMoreData { get; } =
            new(false, true, false, false, 0, 0, null, "Not enough data available.");

        public bool IsSuccess { get; } = isSuccess;

        public bool IsIncomplete { get; } = isIncomplete;

        public bool IsCorrupt { get; } = isCorrupt;

        public bool IsNotSupported { get; } = isNotSupported;

        public int DataOffset { get; } = dataOffset;

        public long DataChunkSize { get; } = dataChunkSize;

        public AudioSourceHeader? Header { get; } = header;

        public string? ErrorMessage { get; } = errorMessage;

        public static HeaderParseResult Corrupt(string message)
        {
            return new HeaderParseResult(true, false, true, true, 0, 0, null, message);
        }

        public static HeaderParseResult NotSupported(string message)
        {
            return new HeaderParseResult(false, false, false, true, 0, 0, null, message);
        }

        public static HeaderParseResult Success(
            AudioSourceHeader header,
            int dataOffset,
            long dataChunkSize
        )
        {
            return new HeaderParseResult(
                true,
                false,
                false,
                false,
                dataOffset,
                dataChunkSize,
                header,
                null
            );
        }
    }

    #endregion
}
