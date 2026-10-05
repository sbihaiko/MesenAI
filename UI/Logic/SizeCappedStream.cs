using System;
using System.IO;

namespace Mesen.Logic
{
	//A read-only Stream wrapper that refuses to hand out more than maxBytes from
	//its inner stream (#860). It is the enforcement half of the nested-archive
	//gate: the wrapper pack's declared entry Length is checked first as a cheap
	//early refusal, but that header is attacker-controlled, so the cap is also
	//enforced on the bytes actually read. Read throws InvalidDataException the
	//moment the inner stream would deliver more than the cap, so the refusal
	//happens while the entry is being read - never after the whole (possibly
	//inflating) archive has been materialized. BCL only, so UI.Tests dual-compiles
	//it (ADR-0123).
	public sealed class SizeCappedStream : Stream
	{
		private readonly Stream _inner;
		private readonly long _maxBytes;
		private long _read;

		public SizeCappedStream(Stream inner, long maxBytes)
		{
			_inner = inner ?? throw new ArgumentNullException(nameof(inner));
			if(maxBytes < 0) {
				throw new ArgumentOutOfRangeException(nameof(maxBytes));
			}
			_maxBytes = maxBytes;
		}

		//Bytes actually delivered from the inner stream so far - the caller can
		//assert the refusal happened before the source was drained.
		public long BytesRead => _read;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position {
			get => _read;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if(count == 0) {
				return 0;
			}
			long remaining = _maxBytes - _read;
			if(remaining <= 0) {
				//Exactly at the cap: hand back EOF if the source is finished,
				//otherwise refuse - one extra byte proves the cap was exceeded.
				if(_inner.ReadByte() < 0) {
					return 0;
				}
				throw new InvalidDataException("stream exceeds the " + _maxBytes + "-byte cap");
			}
			int read = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
			_read += read;
			return read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
