using PersonaEngine.Lib.Utils;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class NpyReaderTests
{
    /// <summary>
    ///     Builds a minimal valid NumPy .npy v1 file in memory.
    /// </summary>
    private static byte[] BuildNpyV1(string dtype, int[] shape, byte[] rawData)
    {
        // Header dict, e.g. "{'descr': '<f4', 'fortran_order': False, 'shape': (4,), }"
        var shapeStr = shape.Length == 1 ? $"({shape[0]},)" : $"({string.Join(", ", shape)})";

        var headerDict = $"{{'descr': '{dtype}', 'fortran_order': False, 'shape': {shapeStr}, }}";

        // Preamble: 6 magic + 1 major + 1 minor + 2 header_len = 10 bytes
        // Total of (preamble + header) must be aligned to 64 bytes
        const int preambleLen = 10;
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(headerDict);

        // Pad header to 64-byte alignment (including preamble), ending with '\n'
        var totalUnpadded = preambleLen + headerBytes.Length + 1; // +1 for trailing \n
        var paddedHeaderLen = (int)Math.Ceiling((double)totalUnpadded / 64) * 64 - preambleLen;

        var paddedHeader = new byte[paddedHeaderLen];
        Array.Copy(headerBytes, paddedHeader, headerBytes.Length);
        for (var i = headerBytes.Length; i < paddedHeaderLen - 1; i++)
        {
            paddedHeader[i] = (byte)' ';
        }

        paddedHeader[paddedHeaderLen - 1] = (byte)'\n';

        // Build file
        using var ms = new MemoryStream();
        // Magic
        ms.Write(new byte[] { 0x93, 0x4E, 0x55, 0x4D, 0x50, 0x59 });
        // Version 1.0
        ms.WriteByte(1);
        ms.WriteByte(0);
        // Header length (u16 LE)
        ms.WriteByte((byte)(paddedHeaderLen & 0xFF));
        ms.WriteByte((byte)((paddedHeaderLen >> 8) & 0xFF));
        // Header
        ms.Write(paddedHeader);
        // Raw data
        ms.Write(rawData);

        return ms.ToArray();
    }

    private static byte[] FloatsToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);

        return bytes;
    }

    [Fact]
    public void ReadFloat32Into_1D_WritesDataIntoDestinationSpan()
    {
        // Arrange
        float[] expected = [1.0f, 2.5f, -3.0f, 42.0f];
        var npyBytes = BuildNpyV1("<f4", [4], FloatsToBytes(expected));
        var tmpPath = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(tmpPath, npyBytes);

            var destination = new float[4];

            // Act
            var shape = NpyReader.ReadFloat32Into(tmpPath, destination);

            // Assert
            Assert.Equal([4], shape);
            Assert.Equal(expected, destination);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void ReadFloat32Into_2D_WritesDataIntoDestinationSpan()
    {
        // Arrange: 2x3 matrix
        float[] expected = [1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f];
        var npyBytes = BuildNpyV1("<f4", [2, 3], FloatsToBytes(expected));
        var tmpPath = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(tmpPath, npyBytes);

            var destination = new float[6];

            // Act
            var shape = NpyReader.ReadFloat32Into(tmpPath, destination);

            // Assert
            Assert.Equal([2, 3], shape);
            Assert.Equal(expected, destination);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void ReadFloat32Into_DestinationTooSmall_Throws()
    {
        float[] data = [1.0f, 2.0f, 3.0f];
        var npyBytes = BuildNpyV1("<f4", [3], FloatsToBytes(data));
        var tmpPath = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(tmpPath, npyBytes);

            var destination = new float[2]; // too small

            Assert.Throws<ArgumentException>(() => NpyReader.ReadFloat32Into(tmpPath, destination));
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }
}
