namespace WslcAgent.Server.Containers;

/// <summary>
/// The staged file on its way to the client, counted as it goes. The response
/// is written after the endpoint has returned, so the stream is what owns the
/// job: every read moves the ring, and disposing it — the framework does that
/// when the last byte has gone, and also when the client walks away mid-file —
/// is what ends the transfer, finished or failed.
/// <para>
/// It seeks like the file it wraps, so the response still carries a length and
/// the browser's own download shows how big the file is.
/// </para>
/// </summary>
internal sealed class TransferStream(Stream file, ContainerTransfers.Transfer transfer) : Stream
{
    private readonly long _total = file.Length;
    private long _sent;

    public override bool CanRead => true;

    public override bool CanSeek => file.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _total;

    public override long Position
    {
        get => file.Position;
        set => file.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(file.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await file.ReadAsync(buffer, cancellationToken));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await file.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override void Flush() => file.Flush();

    public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_sent >= _total)
            {
                transfer.Finish();
            }
            else
            {
                transfer.Fail($"The download stopped after {_sent} of {_total} bytes");
            }

            // The file's own stream deletes the staged copy as it closes.
            file.Dispose();
            transfer.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Count(int read)
    {
        _sent += read;
        transfer.Progress(_sent);
        return read;
    }
}
