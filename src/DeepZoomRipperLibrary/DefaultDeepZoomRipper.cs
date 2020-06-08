using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TiffLibrary;

namespace DeepZoomRipperLibrary
{
    public class DefaultDeepZoomRipper : DeepZoomRipper
    {
        private Uri _baseUri;
        private string _baseName;

        private TiffFileReader _tiff;
        private TiffImageDecoder _decoder;

        private Image<Rgb24> _image;

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

            // Deep Zoom format
            if (".dzi".Equals(Path.GetExtension(filename), StringComparison.OrdinalIgnoreCase) ||
                ".xml".Equals(Path.GetExtension(filename), StringComparison.OrdinalIgnoreCase))
            {
                using (var ms = new MemoryStream())
                {
                    await GetUriContentAsync(manifestUri, ms).ConfigureAwait(false);
                    ms.Seek(0, SeekOrigin.Begin);
                    return await DeepZoomManifest.ParseAsync(ms).ConfigureAwait(false);
                }
            }

            // local TIFF file
            if ("file".Equals(manifestUri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                TiffFileReader tiff = null;
                try
                {
                    tiff = await TiffFileReader.OpenAsync(manifestUri.LocalPath).ConfigureAwait(false);
                    TiffImageDecoder decoder = await tiff.CreateImageDecoderAsync().ConfigureAwait(false);

                    _tiff = tiff;
                    _decoder = decoder;
                    tiff = null;

                    return new DeepZoomManifest { Format = "jpeg", Width = decoder.Width, Height = decoder.Height, TileSize = 256, Overlap = 0 };
                }
                catch (InvalidDataException)
                {
                    // Do nothing
                }
                finally
                {
                    if (!(tiff is null))
                    {
                        await tiff.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            // Other images
            if ("file".Equals(manifestUri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _image = Image.Load<Rgb24>(manifestUri.LocalPath);

                    return new DeepZoomManifest { Format = "jpeg", Width = _image.Width, Height = _image.Height, TileSize = 256, Overlap = 0 };
                }
                catch (Exception)
                {
                    // Do nothing
                }
            }


            throw new ArgumentException(nameof(manifestUri), $"{filename} is not a dzi file.");
        }

        protected override async Task CopyTileStreamAsync(int layer, int column, int row, Stream destination, CancellationToken cancellationToken)
        {
            // local TIFF file
            if (!(_decoder is null))
            {
                // We are assuming `layer` is always the base layer.
                int width = Math.Min(256, ImageWidth - column * 256);
                int height = Math.Min(256, ImageHeight - row * 256);

                using (var tile = new Image<Rgb24>(width, height))
                {
                    await _decoder.DecodeAsync(new TiffPoint(column * 256, row * 256), tile, cancellationToken).ConfigureAwait(false);
                    tile.SaveAsJpeg(destination);
                }
                return;
            }

            // Local images
            if (!(_image is null))
            {
                // We are assuming `layer` is always the base layer.
                int width = Math.Min(256, ImageWidth - column * 256);
                int height = Math.Min(256, ImageHeight - row * 256);

                using (var tile = new Image<Rgb24>(width, height))
                {
                    tile.Mutate(ctx => ctx.DrawImage(_image, new Point(-256 * column, -256 * row), opacity: 1));
                    tile.SaveAsJpeg(destination);
                }
                return;
            }

            // Deep Zoom files
            var uri = new Uri(_baseUri, $"{_baseName}_files/{layer}/{column}_{row}.{ImageFormat}");
            await GetUriContentAsync(uri, destination, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (!(_tiff is null))
            {
                _tiff.Dispose();

                _tiff = null;
                _decoder = null;
            }

            if (!(_image is null))
            {
                _image.Dispose();

                _image = null;
            }

            base.Dispose(disposing);
        }
    }
}
