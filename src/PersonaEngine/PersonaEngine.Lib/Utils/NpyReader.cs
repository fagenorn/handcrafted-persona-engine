using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace PersonaEngine.Lib.Utils;

/// <summary>
///     Reads NumPy .npy files into typed arrays.
///     Supports float32 and int64 dtypes with C-contiguous (row-major) layout.
/// </summary>
internal static class NpyReader
{
    private static ReadOnlySpan<byte> MagicBytes => [0x93, 0x4E, 0x55, 0x4D, 0x50, 0x59];

    /// <summary>
    ///     Reads a .npy file containing float32 data.
    /// </summary>
    public static (float[] Data, int[] Shape) ReadFloat32(string path)
    {
        using var stream = File.OpenRead(path);

        return ReadFloat32(stream, path);
    }

    /// <summary>
    ///     Reads a .npy stream containing float32 data.
    /// </summary>
    public static (float[] Data, int[] Shape) ReadFloat32(Stream stream, string? sourceName = null)
    {
        var (dtype, shape) = ReadHeader(stream);

        if (dtype is not ("<f4" or "f4"))
        {
            throw new InvalidDataException(
                $"Expected float32 dtype ('<f4'), got '{dtype}' in {sourceName ?? "stream"}"
            );
        }

        var totalElements = ComputeTotalElements(shape);
        var data = new float[totalElements];
        var byteBuffer = ArrayPool<byte>.Shared.Rent(totalElements * sizeof(float));

        try
        {
            stream.ReadExactly(byteBuffer, 0, totalElements * sizeof(float));

            Buffer.BlockCopy(byteBuffer, 0, data, 0, totalElements * sizeof(float));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
        }

        return (data, shape);
    }

    /// <summary>
    ///     Reads a .npy file containing int32 data.
    /// </summary>
    public static (int[] Data, int[] Shape) ReadInt32(string path)
    {
        using var stream = File.OpenRead(path);

        return ReadInt32(stream, path);
    }

    /// <summary>
    ///     Reads a .npy stream containing int32 data.
    /// </summary>
    public static (int[] Data, int[] Shape) ReadInt32(Stream stream, string? sourceName = null)
    {
        var (dtype, shape) = ReadHeader(stream);

        if (dtype is not ("<i4" or "i4"))
        {
            throw new InvalidDataException(
                $"Expected int32 dtype ('<i4'), got '{dtype}' in {sourceName ?? "stream"}"
            );
        }

        var totalElements = ComputeTotalElements(shape);
        var data = new int[totalElements];
        var byteBuffer = ArrayPool<byte>.Shared.Rent(totalElements * sizeof(int));

        try
        {
            stream.ReadExactly(byteBuffer, 0, totalElements * sizeof(int));

            Buffer.BlockCopy(byteBuffer, 0, data, 0, totalElements * sizeof(int));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
        }

        return (data, shape);
    }

    /// <summary>
    ///     Reads a .npy file containing float32 data into a pre-allocated span.
    /// </summary>
    public static int[] ReadFloat32Into(string path, Span<float> destination)
    {
        using var stream = File.OpenRead(path);
        var (dtype, shape) = ReadHeader(stream);

        if (dtype is not ("<f4" or "f4"))
        {
            throw new InvalidDataException(
                $"Expected float32 dtype ('<f4'), got '{dtype}' in {path}"
            );
        }

        var totalElements = ComputeTotalElements(shape);
        if (destination.Length < totalElements)
        {
            throw new ArgumentException(
                $"Destination too small: need {totalElements}, got {destination.Length}"
            );
        }

        var destBytes = MemoryMarshal.AsBytes(destination[..totalElements]);
        stream.ReadExactly(destBytes);

        return shape;
    }

    private static (string Dtype, int[] Shape) ReadHeader(Stream stream)
    {
        Span<byte> preamble = stackalloc byte[10];
        stream.ReadExactly(preamble);

        // Validate magic
        if (!preamble[..6].SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Not a valid .npy file: bad magic bytes");
        }

        var majorVersion = preamble[6];
        var headerLen = majorVersion switch
        {
            1 => BinaryPrimitives.ReadUInt16LittleEndian(preamble[8..]),
            2 => (int)BinaryPrimitives.ReadUInt32LittleEndian(preamble[8..]),
            _ => throw new InvalidDataException($"Unsupported .npy version: {majorVersion}"),
        };

        // For v2, we read 4 bytes for header length but only consumed 2 bytes of it in the preamble
        // The preamble is always 10 bytes for v1 (6 magic + 1 major + 1 minor + 2 header_len)
        // For v2, it's 12 bytes (6 magic + 1 major + 1 minor + 4 header_len), so read 2 more
        if (majorVersion == 2)
        {
            Span<byte> extra = stackalloc byte[2];
            stream.ReadExactly(extra);
            headerLen = (int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    [preamble[8], preamble[9], extra[0], extra[1]]
                );
        }

        var headerBytes = ArrayPool<byte>.Shared.Rent(headerLen);

        try
        {
            stream.ReadExactly(headerBytes, 0, headerLen);
            var header = Encoding.ASCII.GetString(headerBytes, 0, headerLen).Trim();

            return ParseHeader(header);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBytes);
        }
    }

    private static (string Dtype, int[] Shape) ParseHeader(string header)
    {
        // Header format: {'descr': '<f4', 'fortran_order': False, 'shape': (768, 1024), }
        var dtype = ExtractValue(header, "'descr':");
        var shapeStr = ExtractValue(header, "'shape':");

        // Parse shape: (768, 1024) or (768,)
        shapeStr = shapeStr.Trim('(', ')', ' ');
        var shape = string.IsNullOrEmpty(shapeStr)
            ? [1]
            : shapeStr
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToArray();

        return (dtype, shape);
    }

    private static string ExtractValue(string header, string key)
    {
        var idx = header.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            throw new InvalidDataException($"Key '{key}' not found in .npy header: {header}");
        }

        var valueStart = idx + key.Length;

        // Skip whitespace
        while (valueStart < header.Length && header[valueStart] == ' ')
        {
            valueStart++;
        }

        if (header[valueStart] == '\'')
        {
            // String value
            var endQuote = header.IndexOf('\'', valueStart + 1);

            return header[(valueStart + 1)..endQuote];
        }

        if (header[valueStart] == '(')
        {
            // Tuple value
            var endParen = header.IndexOf(')', valueStart);

            return header[valueStart..(endParen + 1)];
        }

        // Other value (e.g., False)
        var end = header.IndexOfAny([',', '}'], valueStart);

        return header[valueStart..end].Trim();
    }

    private static int ComputeTotalElements(int[] shape)
    {
        var total = 1;
        foreach (var dim in shape)
        {
            total *= dim;
        }

        return total;
    }
}
