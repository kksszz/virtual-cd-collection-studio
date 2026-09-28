using System.IO;

namespace ZipMp3Player;

public sealed class BoundedFileStream : Stream
{
    private readonly FileStream _file;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public BoundedFileStream(string path, long start, long length)
    {
        // Keep archive reads compatible with verified atomic replacement (tag edits,
        // storage conversion, and ZIP-internal artwork deletion).
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            64 * 1024, FileOptions.RandomAccess | FileOptions.SequentialScan);
        _start = start;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _length) return 0;
        count = (int)Math.Min(count, _length - _position);
        _file.Position = _start + _position;
        var read = _file.Read(buffer, offset, count);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length) return 0;
        var count = (int)Math.Min(buffer.Length, _length - _position);
        _file.Position = _start + _position;
        var read = _file.Read(buffer[..count]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        // Some MP3 duration-to-byte calculations round a few bytes past EOF.
        // Keep the virtual stream bounded instead of turning a harmless end seek into a playback error.
        return _position = Math.Clamp(next, 0, _length);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _file.Dispose(); base.Dispose(disposing); }
}
