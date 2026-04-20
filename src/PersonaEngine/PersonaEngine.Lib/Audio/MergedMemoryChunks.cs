using System.Buffers.Binary;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     This is a little helper for using multiple chunks of memory without writing it to auxiliary buffers
/// </summary>
/// <remarks>
///     It is used to consume the data that was appended in multiple calls and couldn't be used previously.
///     Note: If the requested chunks are not contiguous, the data will be copied to a new buffer.
/// </remarks>
public class MergedMemoryChunks
{
    private readonly List<ReadOnlyMemory<byte>> chunks = [];

    private int currentChunkIndex;

    public MergedMemoryChunks(ReadOnlyMemory<byte> initialChunk)
    {
        chunks.Add(initialChunk);
        Length = initialChunk.Length;
    }

    public MergedMemoryChunks() { }

    /// <summary>
    ///     The total length of the chunks
    /// </summary>
    public long Length { get; private set; }

    /// <summary>
    ///     The current position in the chunks
    /// </summary>
    public long Position { get; private set; }

    /// <summary>
    ///     The absolute position of the current chunk
    /// </summary>
    public long AbsolutePositionOfCurrentChunk { get; private set; }

    public void AddChunk(ReadOnlyMemory<byte> newChunk)
    {
        chunks.Add(newChunk);
        Length += newChunk.Length;
    }

    /// <summary>
    ///     Tries to skip the given number of bytes in the chunks, advancing the position.
    /// </summary>
    public bool TrySkip(uint count)
    {
        var bytesToSkip = count;
        while (bytesToSkip > 0)
        {
            var positionInCurrentChunk = Position - AbsolutePositionOfCurrentChunk;
            var currentChunk = chunks[currentChunkIndex];
            if (positionInCurrentChunk + bytesToSkip <= currentChunk.Length)
            {
                Position += bytesToSkip;

                return true;
            }

            if (currentChunkIndex + 1 == chunks.Count)
            {
                return false;
            }

            var remainingInCurrentChunk = (int)(currentChunk.Length - positionInCurrentChunk);

            currentChunkIndex++;

            Position += remainingInCurrentChunk;
            AbsolutePositionOfCurrentChunk = Position;
            bytesToSkip -= (uint)remainingInCurrentChunk;
        }

        return true;
    }

    /// <summary>
    ///     Restarts the reading from the beginning of the chunks
    /// </summary>
    public void RestartRead()
    {
        currentChunkIndex = 0;
        AbsolutePositionOfCurrentChunk = 0;
        Position = 0;
    }

    /// <summary>
    ///     Gets a slice of the given size from the chunks
    /// </summary>
    /// <remarks>
    ///     If the data will span multiple chunks, it will be copied to a new buffer.
    /// </remarks>
    public ReadOnlyMemory<byte> GetChunk(int size)
    {
        var positionInCurrentChunk = (int)(Position - AbsolutePositionOfCurrentChunk);
        var currentChunk = chunks[currentChunkIndex];
        // First, we try to just slice the current chunk if possible
        if (currentChunk.Length >= positionInCurrentChunk + size)
        {
            Position += size;
            if (currentChunk.Length == positionInCurrentChunk + size)
            {
                currentChunkIndex++;
                AbsolutePositionOfCurrentChunk = Position;
            }

            return currentChunk.Slice(positionInCurrentChunk, size);
        }

        // We cannot slice it, so we need to compose it
        var buffer = new byte[size];
        var bufferIndex = 0;
        var remainingSize = size;
        while (remainingSize > 0)
        {
            var currentChunkAddressable = currentChunk.Slice(
                positionInCurrentChunk,
                Math.Min(remainingSize, currentChunk.Length - positionInCurrentChunk)
            );

            remainingSize -= currentChunkAddressable.Length;
            Position += currentChunkAddressable.Length;
            currentChunkAddressable.CopyTo(buffer.AsMemory(bufferIndex));
            bufferIndex += currentChunkAddressable.Length;
            if (remainingSize > 0 && currentChunkIndex >= chunks.Count)
            {
                throw new InvalidOperationException(
                    $"Not enough data was available in the chunks to read {size} bytes."
                );
            }

            if (remainingSize > 0)
            {
                positionInCurrentChunk = 0;
                currentChunkIndex++;
                AbsolutePositionOfCurrentChunk = Position;
                currentChunk = chunks[currentChunkIndex];
            }
        }

        return buffer.AsMemory();
    }

    /// <summary>
    ///     Reads a 32-bit unsigned integer from the chunks
    /// </summary>
    public uint ReadUInt32LittleEndian()
    {
        var chunk = GetChunk(4);

        return BinaryPrimitives.ReadUInt32LittleEndian(chunk.Span);
    }

    /// <summary>
    ///     Reads a 16-bit unsigned integer from the chunks
    /// </summary>
    public ushort ReadUInt16LittleEndian()
    {
        var chunk = GetChunk(2);

        return BinaryPrimitives.ReadUInt16LittleEndian(chunk.Span);
    }

    /// <summary>
    ///     Reads a 8-bit unsigned integer from the chunks
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public byte ReadByte()
    {
        var chunk = GetChunk(1);

        return chunk.Span[0];
    }

    public short ReadInt16LittleEndian()
    {
        var chunk = GetChunk(2);

        return BinaryPrimitives.ReadInt16LittleEndian(chunk.Span);
    }

    public int ReadInt24LittleEndian()
    {
        var chunk = GetChunk(3);

        return chunk.Span[0] | (chunk.Span[1] << 8) | (chunk.Span[2] << 16);
    }

    public int ReadInt32LittleEndian()
    {
        var chunk = GetChunk(4);

        return BinaryPrimitives.ReadInt32LittleEndian(chunk.Span);
    }

    public long ReadInt64LittleEndian()
    {
        var chunk = GetChunk(8);

        return BinaryPrimitives.ReadInt64LittleEndian(chunk.Span);
    }

    /// <summary>
    ///     Copies the content of the current chunks to a single buffer
    /// </summary>
    /// <returns></returns>
    public byte[] ToArray()
    {
        var buffer = new byte[Length];
        var bufferIndex = 0;
        foreach (var chunk in chunks)
        {
            chunk.Span.CopyTo(buffer.AsSpan(bufferIndex));
            bufferIndex += chunk.Length;
        }

        return buffer;
    }
}
