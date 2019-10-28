using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DeepZoomRipperLibrary
{
    public class DefaultDeepZoomRipper : DeepZoomRipper
    {
        private Uri _baseUri;
        private string _baseName;

        public DefaultDeepZoomRipper(DeepZoomRipperOptions options, string outputFile) : base(options, HttpClientInitializationOptions.InitializeWithCookieContainer, outputFile)
        {

        }

        private static string GetFileNameFromUri(Uri uri)
        {
            string path = uri.AbsolutePath;
            int pos = path.LastIndexOf('/');
            if (pos == -1)
            {
                throw new ArgumentException(nameof(uri), $"Cannot determine file name from path. ({uri.AbsolutePath})");
            }
            return path.Substring(pos + 1);
        }

        protected override async Task<DeepZoomManifest> InitializeManifestAsync(Uri manifestUri)
        {
            string filename = GetFileNameFromUri(manifestUri);
            _baseUri = new Uri(manifestUri, "./");
            _baseName = Path.ChangeExtension(filename, null);

            if (!".dzi".Equals(Path.GetExtension(filename), StringComparison.OrdinalIgnoreCase) &&
                !".xml".Equals(Path.GetExtension(filename), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(nameof(manifestUri), $"{filename} is not a dzi file.");
            }

            using (var ms = new MemoryStream())
            {
                await GetUriContentAsync(manifestUri, ms).ConfigureAwait(false);
                ms.Seek(0, SeekOrigin.Begin);
                return await DeepZoomManifest.ParseAsync(ms).ConfigureAwait(false);
            }
        }

        protected override async Task CopyTileStreamAsync(int layer, int column, int row, Stream destination, CancellationToken cancellationToken)
        {
            var uri = new Uri(_baseUri, $"{_baseName}_files/{layer}/{column}_{row}.{ImageFormat}");
            await GetUriContentAsync(uri, destination).ConfigureAwait(false);
        }
    }
}
