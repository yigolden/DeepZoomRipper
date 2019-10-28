using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DeepZoomRipperLibrary;

namespace DeepZoomRipperCli
{
    internal class TracedDeepZoomRipper : DefaultDeepZoomRipper
    {
        private int _ioCount;

        public TracedDeepZoomRipper(DeepZoomRipperOptions options, string outputFile) : base(options, outputFile)
        {
            _ioCount = 0;
        }

        protected override Task CopyTileStreamAsync(int layer, int column, int row, Stream destination, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ioCount);
            return base.CopyTileStreamAsync(layer, column, row, destination, cancellationToken);
        }

        public int TileIOCount => _ioCount;
    }
}
