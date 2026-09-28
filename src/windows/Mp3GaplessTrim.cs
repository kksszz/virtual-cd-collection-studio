using System.Buffers.Binary;
using NAudio.Wave;

namespace ZipMp3Player;

internal static class Mp3GaplessTrim
{
    // NAudio 2.2.1 skips the Xing frame but does not trim the LAME extension.
    // The Windows ACM MPEG-1 decoder delay is 528+1 PCM frames (verified with
    // encoded impulse fixtures). MPEG-2 ACM output has different priming/draining;
    // preserve its full decoded stream rather than risk trimming actual music.
    public static WaveStream Apply(Mp3FileReader reader)
    {
        var header = reader.XingHeader;
        if (header is null || header.Mp3Frame.MpegVersion != MpegVersion.Version1
            || !TryRead(header.Mp3Frame.RawData, out var delay, out var padding)) return reader;
        var samples = reader.Length / reader.WaveFormat.BlockAlign;
        if ((long)header.Frames * header.Mp3Frame.SampleCount != samples) return reader;
        var start = delay + 529;
        var end = padding - 529;
        if (end < 0 || start + end >= samples) return reader;
        return new TrimmedStream(reader, (long)start * reader.WaveFormat.BlockAlign,
            (long)end * reader.WaveFormat.BlockAlign);
    }

    internal static bool TryRead(byte[] frame, out int delay, out int padding)
    {
        delay = padding = 0;
        for (var offset = 4; offset <= 64 && offset + 8 <= frame.Length; offset++)
        {
            var marker = frame.AsSpan(offset, 4);
            if (!marker.SequenceEqual("Xing"u8) && !marker.SequenceEqual("Info"u8)) continue;
            var flags = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(offset + 4, 4));
            if ((flags & ~15u) != 0 || (flags & 1) == 0) return false;
            var lame = offset + 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 2) != 0 ? 4 : 0)
                + ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);
            if (lame + 36 > frame.Length || !frame.AsSpan(lame, 4).SequenceEqual("LAME"u8)) return false;
            if ((frame[lame + 9] >> 4) != 0) return false; // Only the known tag revision.
            // The tag CRC guards against trimming music using damaged metadata.
            ushort crc = 0;
            foreach (var value in frame.AsSpan(0, lame + 34))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xa001 : crc >> 1);
            }
            if (crc != BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(lame + 34, 2))) return false;
            delay = (frame[lame + 21] << 4) | (frame[lame + 22] >> 4);
            padding = ((frame[lame + 22] & 15) << 8) | frame[lame + 23];
            return delay < 4095 && padding is >= 529 and < 4095;
        }
        return false;
    }

    private sealed class TrimmedStream : WaveStream
    {
        private readonly WaveStream _source;
        private readonly long _start;
        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length { get; }
        public override long Position
        {
            get => Math.Clamp(_source.Position - _start, 0, Length);
            set => _source.Position = _start + Math.Clamp(value, 0, Length) / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
        }
        public TrimmedStream(WaveStream source, long start, long end)
        {
            _source = source; _start = start; Length = source.Length - start - end;
            Position = 0;
        }
        public override int Read(byte[] buffer, int offset, int count) =>
            _source.Read(buffer, offset, (int)Math.Min(count, Length - Position));
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }
}
