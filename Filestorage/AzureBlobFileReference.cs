using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using Telligent.Evolution.Extensibility.Storage.Version1;
using System.Linq;
using Telligent.Common.Diagnostics.Tracing;

namespace AlexCrome.Telligent.Azure.Filestorage
{
    public class AzureBlobFileReference : ICentralizedFile
    {
        private readonly CloudBlob _blob;


        public AzureBlobFileReference(CloudBlob blob, string fileStoreKey)
        {
            _blob = blob;
            FileStoreKey = fileStoreKey;
        }

        public int ContentLength => (int)_blob.Properties.Length;
        public string FileName => _blob.Uri.Segments[_blob.Uri.Segments.Length - 1];
        public string FileStoreKey { get; }
        public string Path
        {
            get
            {
                if (_blob.Parent == null)
                    return string.Empty;

                var containerSegmentCount = _blob.Container.Uri.Segments.Length;
                var pathSegments = _blob.Parent.Uri.Segments
                    .Skip(containerSegmentCount)
                    .Select(x => x.TrimEnd('/'))
                    .ToArray();

                return CentralizedFileStorage.MakePath(pathSegments);
            }
        }

        public string GetDownloadUrl() => _blob.Uri.ToString();

        public Stream OpenReadStream() => new TracedStream(_blob.OpenRead(), $"[cfs] ReadStream '{FileStoreKey}' '{Path}' '{FileName}'");

        private class TracedStream : Stream
        {
            private readonly Stream _inner;
            private readonly TracePoint _tracePoint;

            public TracedStream(Stream inner, string tracePointName)
            {
                _inner = inner;
                _tracePoint = new TracePoint(tracePointName);
            }

            protected override void Dispose(bool disposing)
            {
                _inner.Dispose();
                _tracePoint.Dispose();
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override long Position
            {
                get { return _inner.Position; }
                set { _inner.Position = value; }
            }

        }
    }
}
