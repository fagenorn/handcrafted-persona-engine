using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PersonaEngine.Lib.Bootstrapper.Verification;
using Xunit;

namespace PersonaEngine.Lib.Bootstrapper.Tests.Verification;

public class Sha256VerifyingStreamTests
{
    [Fact]
    public void Read_to_end_with_correct_hash_succeeds()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        var expected = ComputeSha256Hex(payload);
        using var inner = new MemoryStream(payload);
        using var stream = new Sha256VerifyingStream(inner, expected);

        var buffer = new byte[1024];
        int total = 0;
        int n;
        while ((n = stream.Read(buffer, total, buffer.Length - total)) > 0)
            total += n;

        total.Should().Be(payload.Length);
    }

    [Fact]
    public void Read_to_end_with_wrong_hash_throws_at_eof()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        using var inner = new MemoryStream(payload);
        using var stream = new Sha256VerifyingStream(
            inner,
            "0000000000000000000000000000000000000000000000000000000000000000"
        );

        var buffer = new byte[1024];
        Action readToEnd = () =>
        {
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) { }
        };

        readToEnd.Should().Throw<IntegrityException>().WithMessage("*sha256 mismatch*");
    }

    [Fact]
    public async Task ReadAsync_to_end_validates_hash()
    {
        var payload = Encoding.UTF8.GetBytes("async path");
        var expected = ComputeSha256Hex(payload);
        using var inner = new MemoryStream(payload);
        await using var stream = new Sha256VerifyingStream(inner, expected);

        var buffer = new byte[4];
        int total = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            total += n;

        total.Should().Be(payload.Length);
    }

    [Fact]
    public void Hash_comparison_is_case_insensitive()
    {
        var payload = Encoding.UTF8.GetBytes("x");
        var upper = ComputeSha256Hex(payload).ToUpperInvariant();
        using var inner = new MemoryStream(payload);
        using var stream = new Sha256VerifyingStream(inner, upper);

        var buffer = new byte[8];
        Action readToEnd = () =>
        {
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) { }
        };

        readToEnd.Should().NotThrow();
    }

    private static string ComputeSha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
