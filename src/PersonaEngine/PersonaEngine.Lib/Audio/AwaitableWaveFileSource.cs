using PersonaEngine.Lib.Utils.Audio;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Similar with <seealso cref="AwaitableAudioSource" /> allows writing data from a wave file instead of writing the
///     samples directly.
/// </summary>
/// <remarks>
///     Important: This should be used at most by one writer and one reader.
///     It can store samples as floats or as bytes, or both. By default, it stores samples as floats.
///     Based on your usage, you can choose to store samples as bytes, floats, or both.
///     If storing them as floats, they will be deserialized from bytes when they are added and returned directly when
///     requested.
///     If storing them as bytes, they will be serialized from floats when they are added and returned directly when
///     requested.
///     If you want to optimize your memory usage, you can store them in the same format as they are added.
///     If you want to optimize your CPU usage, you can store them in the format you want to use them in.
/// </remarks>
public class AwaitableWaveFileSource(
    IReadOnlyDictionary<string, string> metadata,
    bool storeSamples = true,
    bool storeBytes = false,
    int initialSizeFloats = BufferedMemoryAudioSource.DefaultInitialSize,
    int initialSizeBytes = BufferedMemoryAudioSource.DefaultInitialSize,
    IChannelAggregationStrategy? aggregationStrategy = null
)
    : AwaitableAudioSource(
        metadata,
        storeSamples,
        storeBytes,
        initialSizeFloats,
        initialSizeBytes,
        aggregationStrategy
    )
{
    private MergedMemoryChunks? headerChunks;

    private MergedMemoryChunks? sampleDataChunks;

    public void WriteData(ReadOnlyMemory<byte> data)
    {
        if (IsFlushed)
        {
            throw new InvalidOperationException("Cannot write to flushed stream.");
        }

        // We need the dataOffset in case the data contains both header and samples
        var dataOffset = 0;
        if (!IsInitialized)
        {
            if (headerChunks == null)
            {
                headerChunks = new MergedMemoryChunks(data);
            }
            else
            {
                headerChunks.AddChunk(data);
            }

            var headerParseResult = WavFileHandler.ParseHeader(headerChunks);
            if (headerParseResult.IsIncomplete)
            {
                // Need more data for header
                // We need to copy the last chunk as we'll keep it for the next iteration
                headerChunks = new MergedMemoryChunks(headerChunks.ToArray());

                return;
            }

            if (headerParseResult.IsCorrupt)
            {
                throw new InvalidOperationException(headerParseResult.ErrorMessage);
            }

            if (!headerParseResult.IsSuccess || headerParseResult.Header == null)
            {
                throw new NotSupportedException(headerParseResult.ErrorMessage);
            }

            base.Initialize(headerParseResult.Header);
            dataOffset = headerParseResult.DataOffset;
        }

        // Now process sample data
        var sampleData = dataOffset == 0 ? data : data.Slice(dataOffset);
        lock (syncRoot)
        {
            ProcessSamples(sampleData);
        }

        NotifyNewSamples();
    }

    private void ProcessSamples(ReadOnlyMemory<byte> sampleData)
    {
        if (sampleDataChunks != null)
        {
            sampleDataChunks.AddChunk(sampleData);
        }
        else
        {
            sampleDataChunks = new MergedMemoryChunks(sampleData);
        }

        // Calculate how many complete frames we can process
        var framesToProcess = sampleDataChunks.Length / FrameSize;

        if (framesToProcess == 0)
        {
            // Not enough data to process even a single frame, wait for more data
            return;
        }

        for (var frameIndex = 0; frameIndex < framesToProcess; frameIndex++)
        {
            var frame = sampleDataChunks.GetChunk(FrameSize);
            AddFrame(frame);
        }

        // After processing, check if there are remaining bytes and store them for the next iteration
        // We need to copy the memory (with ToArray()) as otherwise, it might be overriden by the new chunk.
        sampleDataChunks =
            sampleDataChunks.Length > sampleDataChunks.Position
                ? new MergedMemoryChunks(
                    sampleDataChunks
                        .GetChunk((int)(sampleDataChunks.Length - sampleDataChunks.Position))
                        .ToArray()
                        .AsMemory()
                )
                : null;
    }

    protected override void Dispose(bool disposing)
    {
        headerChunks = null;
        sampleDataChunks = null;
        base.Dispose(disposing);
    }
}
