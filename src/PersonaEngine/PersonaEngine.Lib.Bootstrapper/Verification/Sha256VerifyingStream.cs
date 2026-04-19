using System.Security.Cryptography;

namespace PersonaEngine.Lib.Bootstrapper.Verification;

public sealed class Sha256VerifyingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;
    private readonly string _expectedSha256Hex;
    private bool _verified;

    public Sha256VerifyingStream(Stream inner, string expectedSha256Hex)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _expectedSha256Hex =
            expectedSha256Hex ?? throw new ArgumentNullException(nameof(expectedSha256Hex));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        if (n > 0)
            _hash.AppendData(buffer, offset, n);
        else
            VerifyOrThrow();
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        var n = _inner.Read(buffer);
        if (n > 0)
            _hash.AppendData(buffer[..n]);
        else
            VerifyOrThrow();
        return n;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken ct = default
    )
    {
        var n = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (n > 0)
            _hash.AppendData(buffer.Span[..n]);
        else
            VerifyOrThrow();
        return n;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken ct
    )
    {
        var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
        if (n > 0)
            _hash.AppendData(buffer, offset, n);
        else
            VerifyOrThrow();
        return n;
    }

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    private void VerifyOrThrow()
    {
        if (_verified)
            return;
        _verified = true;
        var actual = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        if (!actual.Equals(_expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException(
                $"sha256 mismatch: expected {_expectedSha256Hex.ToLowerInvariant()}, got {actual}"
            );
    }
}
