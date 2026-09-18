namespace Koma.Core.Packaging;

/// <summary>
/// Thrown when reading an entry produces more bytes than were declared for it.
/// </summary>
public sealed class DeclaredSizeExceededException : IOException
{
    public DeclaredSizeExceededException(string entryName, long declaredBytes) : base($"Entry '{entryName}' produced more than the {declaredBytes} bytes its central directory declares (§13.1).")
    {
        EntryName = entryName;
        DeclaredBytes = declaredBytes;
    }

    public DeclaredSizeExceededException()
    { }

    public DeclaredSizeExceededException(string message) : base(message)
    { }

    public DeclaredSizeExceededException(string message, Exception innerException) : base(message, innerException)
    { }

    public string? EntryName { get; }

    public long DeclaredBytes { get; }
}

/// <summary>
/// A read-only wrapper that stops at a byte budget.
/// </summary>
/// <remarks>
/// §13.1 states that the sizes in the ZIP central directory are chosen by the
/// producer and must not be trusted, and requires the limits to be enforced
/// during decompression as well. An inspection pass over the declared sizes is
/// therefore necessary but never sufficient: an archive that declares a
/// kilobyte and delivers four gigabytes passes every check made before the
/// first byte is read. This is the other half — it makes the declaration
/// binding on the producer instead of merely informative.
/// </remarks>
public sealed class BoundedReadStream : Stream
{
    private readonly Stream inner;
    private readonly long budget;
    private readonly string entryName;
    private readonly bool leaveOpen;
    private long consumed;

    public BoundedReadStream(Stream inner, long budget, string entryName, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(budget);

        this.inner = inner;
        this.budget = budget;
        this.entryName = entryName;
        this.leaveOpen = leaveOpen;
    }

    /// <summary>Bytes read so far.</summary>
    public long Consumed => consumed;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => consumed;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        // Read at most one byte beyond the budget: enough to prove the entry
        // overruns, without allowing an unbounded read to complete first.
        long remaining = budget - consumed + 1;

        if (remaining <= 0)
            throw new DeclaredSizeExceededException(entryName, budget);

        if (buffer.Length > remaining)
            buffer = buffer[..(int)remaining];

        int read = inner.Read(buffer);
        consumed += read;

        if (consumed > budget)
            throw new DeclaredSizeExceededException(entryName, budget);

        return read;
    }

    public override void Flush()
    { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
            inner.Dispose();

        base.Dispose(disposing);
    }
}
