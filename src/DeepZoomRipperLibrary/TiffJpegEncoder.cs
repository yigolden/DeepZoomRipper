using System;
using System.Buffers;
using JpegLibrary;

namespace DeepZoomRipperLibrary
{
    internal class TiffJpegEncoder : JpegEncoder
    {
        public void WriteTables(IBufferWriter<byte> buffer)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var writer = new JpegWriter(buffer, minimumBufferSize: MinimumBufferSegmentSize);

            WriteStartOfImage(ref writer);
            WriteQuantizationTables(ref writer);
            WriteEndOfImage(ref writer);

            writer.Flush();
        }
    }
}
