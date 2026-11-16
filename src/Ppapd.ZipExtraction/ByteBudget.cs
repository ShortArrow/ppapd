namespace Ppapd.ZipExtraction;

/// <summary>The bytes one extraction may still write, shared by every entry of the archive (docs/PRD.md, F-4.6).</summary>
internal sealed class ByteBudget(long maxTotalBytes)
{
    private long remaining = maxTotalBytes;

    /// <summary>Takes <paramref name="count"/> bytes out of the budget.</summary>
    /// <exception cref="ZipTooLargeException">Fewer than <paramref name="count"/> bytes remain; nothing has been taken.</exception>
    public void Spend(int count)
    {
        if (count > remaining)
        {
            throw new ZipTooLargeException();
        }
        remaining -= count;
    }

    /// <summary>A write-only stream that passes each write to <paramref name="inner"/> only after spending its size here.</summary>
    public Stream Guard(Stream inner) => new GuardedStream(inner, this);

    private sealed class GuardedStream(Stream inner, ByteBudget budget) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            budget.Spend(buffer.Length);
            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
