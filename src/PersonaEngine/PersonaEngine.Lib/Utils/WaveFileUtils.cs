using System.Buffers;
using PersonaEngine.Lib.Audio;

namespace PersonaEngine.Lib.Utils;

/// <summary>
///     Utility class for reading and writing wave files.
/// </summary>
internal class WaveFileUtils
{
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

            if (
                chunkID != 0x46464952 /*'RIFF'*/
                || riffType != 0x45564157 /*'WAVE'*/
            )
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

                if (
                    chunkType == 0x20746D66 /*'fmt '*/
                )
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

                    if (formatTag != 1 && formatTag != 0xFFFE) // PCM or WAVE_FORMAT_EXTENSIBLE
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
                    var remainingBytes = chunkSize - (formatTag == 1 ? 16u : 40u);
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
                else if (
                    chunkType == 0x61746164 /*'data'*/
                )
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

        if (
            chunkID != 0x46464952 /*'RIFF'*/
            || riffType != 0x45564157 /*'WAVE'*/
        )
        {
            return HeaderParseResult.Corrupt("Invalid wave file header.");
        }

        // Read chunks until we find 'fmt ' and 'data'
        while (headerChunks.Position + 8 <= headerChunks.Length)
        {
            var chunkType = headerChunks.ReadUInt32LittleEndian();
            var chunkSize = headerChunks.ReadUInt32LittleEndian();

            if (
                chunkType == 0x20746D66 /*'fmt '*/
            )
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

                if (formatTag != 1 && formatTag != 0xFFFE) // PCM or WAVE_FORMAT_EXTENSIBLE
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
                if (!headerChunks.TrySkip(chunkSize - (formatTag == 1 ? 16u : 40u)))
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
            else if (
                chunkType == 0x61746164 /*'data'*/
            )
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

    /// <summary>
    ///     Ensures that the given number of bytes can be read from the memory chunks.
    /// </summary>
    /// <returns></returns>
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
}
