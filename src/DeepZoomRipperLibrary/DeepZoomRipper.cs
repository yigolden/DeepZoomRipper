using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JpegLibrary;
using PooledGrowableBufferHelper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TiffLibrary;
using TiffLibrary.ImageEncoder;

namespace DeepZoomRipperLibrary
{
    public abstract class DeepZoomRipper : IDisposable
    {
        // Options
        private readonly DeepZoomRipperOptions _options;

        // HTTP request related
        private readonly CookieContainer _cookieContainer;
        private readonly HttpClient _http;

        // Manifests
        private DeepZoomManifest _manifest;
        private DeepZoomLayer[] _layers;

        // Tiff file related
        private readonly string _outputFile;
        private Stream _stream;
        private TiffFileWriter _fileWriter;
        private TiffFileReader _fileReader;
        private TiffImageEncoder<Rgb24> _encoder;

        // Tools
        private Configuration _configuration;
        private byte[] _jpegTables;

        // Configuration
        public string Software { get; set; }
        public int JpegQuality { get; set; } = 75;
        public bool UseSharedQuantizationTables { get; set; } = true;

        public enum HttpClientInitializationOptions
        {
            DisableHttpClient = 0,
            InitializeWithoutCookieContainer = 1,
            InitializeWithCookieContainer = 2,
        }

        #region Properties

        public string ImageFormat => _manifest?.Format;

        public int DeepZoomTileSize => _manifest?.TileSize ?? 0;

        public int DeepZoomOverlap => _manifest?.Overlap ?? 0;

        public int ImageWidth => _manifest?.Width ?? 0;

        public int ImageHeight => _manifest?.Height ?? 0;

        public int DeepZoomLayerCount => _layers?.Length ?? 0;

        public int OutputTileSize => _options.OutputTileSize;

        private bool UseBigTiff => (_manifest.Width * _manifest.Height) > 536870912;

        #endregion

        #region Construction

        public DeepZoomRipper(DeepZoomRipperOptions options, HttpClientInitializationOptions httpClientInitializationOptions, string outputFile)
        {
            _options = options ??= DeepZoomRipperOptions.Default;

            if (httpClientInitializationOptions == HttpClientInitializationOptions.InitializeWithCookieContainer)
            {
                _cookieContainer = new CookieContainer();
                HttpClientHandler handler = new HttpClientHandler
                {
                    CookieContainer = _cookieContainer
                };
                _http = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
            }
            else if (httpClientInitializationOptions == HttpClientInitializationOptions.InitializeWithoutCookieContainer)
            {
                _http = new HttpClient()
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
            }

            _outputFile = outputFile;

            _configuration = Configuration.Default.Clone();
            _configuration.MaxDegreeOfParallelism = 1;

            var builder = new TiffImageEncoderBuilder();
            builder.PhotometricInterpretation = TiffPhotometricInterpretation.YCbCr;
            builder.IsTiled = true;
            builder.TileSize = new TiffSize(OutputTileSize, OutputTileSize);
            builder.HorizontalChromaSubSampling = 2;
            builder.VerticalChromaSubSampling = 2;
            builder.Compression = TiffCompression.Jpeg;
            builder.JpegOptions = new TiffJpegEncodingOptions { Quality = JpegQuality, UseSharedQuantizationTables = UseSharedQuantizationTables, OptimizeCoding = true };

            _encoder = builder.BuildForImageSharp<Rgb24>();

            if (UseSharedQuantizationTables)
            {
                var encoder = new TiffJpegEncoder();
                encoder.SetQuantizationTable(JpegStandardQuantizationTable.ScaleByQuality(JpegStandardQuantizationTable.GetLuminanceTable(JpegElementPrecision.Precision8Bit, 0), JpegQuality));
                encoder.SetQuantizationTable(JpegStandardQuantizationTable.ScaleByQuality(JpegStandardQuantizationTable.GetChrominanceTable(JpegElementPrecision.Precision8Bit, 1), JpegQuality));
                using (PooledMemoryStream ms = PooledMemoryStreamManager.Shared.GetStream())
                {
                    encoder.WriteTables(ms);
                    _jpegTables = ms.ToArray();
                }
            }
        }

        #endregion

        #region Initialization

        public async Task InitializeAsync(Uri manifestUri, CancellationToken cancellationToken)
        {
            if (manifestUri == null)
            {
                throw new ArgumentNullException(nameof(manifestUri));
            }

            // Parse DZI file.
            _manifest = await InitializeManifestAsync(manifestUri).ConfigureAwait(false);

            // Calculate Deep Zoom layers
            _layers = DeepZoomHelper.CalculateDeepZoomLayers(_manifest.Width, _manifest.Height);

            // Get output file ready
            _stream = new FileStream(_outputFile, FileMode.Create, FileAccess.ReadWrite);
            _fileWriter = await TiffFileWriter.OpenAsync(_stream, leaveOpen: true, useBigTiff: UseBigTiff).ConfigureAwait(false);
        }

        protected abstract Task<DeepZoomManifest> InitializeManifestAsync(Uri manifestUri);

        #endregion

        #region HTTP Helpers

        protected async Task GetUriContentAsync(Uri uri, Stream stream, CancellationToken cancellationToken = default)
        {
            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                using (var fs = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read))
                {
                    await fs.CopyToAsync(stream).ConfigureAwait(false);
                }
                return;
            }
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using (HttpResponseMessage response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken))
                {
                    using (HttpContent content = response.Content)
                    using (Stream s = await content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        await s.CopyToAsync(stream).ConfigureAwait(false);
                    }
                }
                return;
            }
            throw new ArgumentException(nameof(uri), $"Unsupported URI schema. ({uri.Scheme})");
        }

        protected async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFunc, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;

            int MaxRetryCount = _options.RequestMaxRetryCount;

            List<Exception> capturedExceptions = null;
            for (int i = 0; i < MaxRetryCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (HttpRequestMessage request = requestFunc())
                    {
                        response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        return Interlocked.Exchange(ref response, null);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    if (capturedExceptions == null)
                    {
                        capturedExceptions = new List<Exception>(MaxRetryCount);
                    }
                    capturedExceptions.Add(e);
                }
                finally
                {
                    response?.Dispose();
                }

                await Task.Delay(_options.RequestRetryInterval, cancellationToken);
            }

            throw new AggregateException(capturedExceptions.ToArray());
        }

        #endregion

        #region Layer 0 ripper

        protected abstract Task CopyTileStreamAsync(int layer, int column, int row, Stream destination, CancellationToken cancellationToken);

        protected virtual async Task<Image<Rgb24>> ReadTileImageAsync(int layer, int column, int row, CancellationToken cancellationToken)
        {
            using (PooledMemoryStream ms = PooledMemoryStreamManager.Shared.GetStream())
            {
                await CopyTileStreamAsync(layer, column, row, ms, cancellationToken).ConfigureAwait(false);
                ms.Seek(0, SeekOrigin.Begin);
                return Image.Load<Rgb24>(_configuration, ms);
            }
        }

        public async Task RipBaseLayerAsync(IRipperInitialLayerAcquisitionReporter reporter, CancellationToken cancellationToken)
        {
            int outputTileSize = _options.OutputTileSize;

            int tiffRowCount = (ImageHeight + outputTileSize - 1) / outputTileSize;
            int tiffColCount = (ImageWidth + outputTileSize - 1) / outputTileSize;

            int index = 0;
            ulong[] offsets = new ulong[tiffRowCount * tiffColCount];
            ulong[] byteCounts = new ulong[tiffRowCount * tiffColCount];
            ulong totalByteCount = 0;

            reporter?.ReportStartInitialLayerAcquisition(offsets.Length);

            using (var regionReader = new TileRegionReader(this))
            using (Image<Rgb24> canvas = new Image<Rgb24>(_configuration, outputTileSize, outputTileSize))
            {
                for (int row = 0; row < tiffRowCount; row++)
                {
                    int rowYOffset = row * outputTileSize;

                    for (int col = 0; col < tiffColCount; col++)
                    {
                        int colXOffset = col * outputTileSize;

                        ClearImage(canvas);

                        await regionReader.FillRegionAsync(colXOffset, rowYOffset, canvas, cancellationToken).ConfigureAwait(false);

                        cancellationToken.ThrowIfCancellationRequested();
                        TiffStreamRegion region = await _encoder.EncodeAsync(_fileWriter, canvas).ConfigureAwait(false);

                        offsets[index] = (ulong)region.Offset.Offset;
                        byteCounts[index] = (uint)region.Length;
                        totalByteCount += (uint)region.Length;
                        index++;

                        reporter?.ReportInitialLayerAcquisitionProgress(index, offsets.Length);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            TiffStreamOffset ifdOffset;
            using (TiffImageFileDirectoryWriter ifdWriter = _fileWriter.CreateImageFileDirectory())
            {
                await ifdWriter.WriteTagAsync(TiffTag.PhotometricInterpretation, new TiffValueCollection<ushort>((ushort)TiffPhotometricInterpretation.YCbCr)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.Compression, new TiffValueCollection<ushort>((ushort)TiffCompression.Jpeg)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.SamplesPerPixel, new TiffValueCollection<ushort>((ushort)3)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.TileWidth, new TiffValueCollection<ushort>((ushort)outputTileSize)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.TileLength, new TiffValueCollection<ushort>((ushort)outputTileSize)).ConfigureAwait(false);
                //ifdWriter.AddTag(TiffTag.ResolutionUnit, (ushort)TiffResolutionUnit.Inch);

                //await ifdWriter.WriteTagAsync(TiffTag.XResolution, new TiffValueCollection<TiffRational>(new TiffRational(72, 1)));
                //await ifdWriter.WriteTagAsync(TiffTag.YResolution, new TiffValueCollection<TiffRational>(new TiffRational(72, 1)));
                await ifdWriter.WriteTagAsync(TiffTag.SampleFormat, new TiffValueCollection<ushort>(new ushort[] { 1, 1, 1 })).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.BitsPerSample, new TiffValueCollection<ushort>(new ushort[] { 8, 8, 8 })).ConfigureAwait(false);

                if (UseBigTiff)
                {
                    await ifdWriter.WriteTagAsync(TiffTag.ImageWidth, new TiffValueCollection<ulong>((ulong)_manifest.Width)).ConfigureAwait(false);
                    await ifdWriter.WriteTagAsync(TiffTag.ImageLength, new TiffValueCollection<ulong>((ulong)_manifest.Height)).ConfigureAwait(false);

                    await ifdWriter.WriteTagAsync(TiffTag.TileOffsets, new TiffValueCollection<ulong>(offsets)).ConfigureAwait(false);
                    await ifdWriter.WriteTagAsync(TiffTag.TileByteCounts, new TiffValueCollection<ulong>(byteCounts)).ConfigureAwait(false);
                }
                else
                {
                    await ifdWriter.WriteTagAsync(TiffTag.ImageWidth, new TiffValueCollection<uint>((uint)_manifest.Width)).ConfigureAwait(false);
                    await ifdWriter.WriteTagAsync(TiffTag.ImageLength, new TiffValueCollection<uint>((uint)_manifest.Height)).ConfigureAwait(false);

                    uint[] tempArr = new uint[offsets.Length];
                    for (int i = 0; i < tempArr.Length; i++)
                    {
                        tempArr[i] = (uint)offsets[i];
                    }
                    await ifdWriter.WriteTagAsync(TiffTag.TileOffsets, new TiffValueCollection<uint>(tempArr)).ConfigureAwait(false);

                    for (int i = 0; i < tempArr.Length; i++)
                    {
                        tempArr[i] = (uint)byteCounts[i];
                    }
                    await ifdWriter.WriteTagAsync(TiffTag.TileByteCounts, new TiffValueCollection<uint>(tempArr)).ConfigureAwait(false);
                }

                if (!(_jpegTables is null))
                {
                    await ifdWriter.WriteTagAsync(TiffTag.JPEGTables, TiffFieldType.Undefined, TiffValueCollection.UnsafeWrap(_jpegTables)).ConfigureAwait(false);
                }

                string software = Software;
                if (!string.IsNullOrEmpty(software))
                {
                    await ifdWriter.WriteTagAsync(TiffTag.Software, new TiffValueCollection<string>(software));
                }

                ifdOffset = await ifdWriter.FlushAsync().ConfigureAwait(false);
            }

            _fileWriter.SetFirstImageFileDirectoryOffset(ifdOffset);
            await _fileWriter.FlushAsync().ConfigureAwait(false);

            reporter?.ReportCompleteInitialLayerAcquisition(offsets.Length, (long)totalByteCount);
        }

        private sealed class TileRegionReader : IDisposable
        {
            private ImageCache _verticalCache = new ListBasedImageCache();
            private ImageCache _horizontalCache = new DictionaryBasedImageCache();
            private ImageCache _backupVerticalCache = new ListBasedImageCache();
            private ImageCache _backupHorizontalCache = new DictionaryBasedImageCache();
            private readonly DeepZoomRipper _ripper;

            private readonly int _deepZoomTileSize;
            private readonly int _deepZoomTileOverlap;
            private readonly int _deepZoomTileRowCount;
            private readonly int _deepZoomTileColCount;

            public TileRegionReader(DeepZoomRipper ripper)
            {
                _ripper = ripper;

                _deepZoomTileSize = _ripper.DeepZoomTileSize;
                _deepZoomTileOverlap = ripper.DeepZoomOverlap;
                _deepZoomTileRowCount = (_ripper.ImageHeight + _deepZoomTileSize - 1) / _deepZoomTileSize;
                _deepZoomTileColCount = (_ripper.ImageWidth + _deepZoomTileSize - 1) / _deepZoomTileSize;
            }

            public async Task FillRegionAsync(int x, int y, Image<Rgb24> region, CancellationToken cancellationToken)
            {
                int layer = _ripper._layers.Length - 1;

                int tileYIndex = y / _deepZoomTileSize;
                int tileYOffset = tileYIndex * _deepZoomTileSize;
                int tileYCount = (y - tileYOffset + region.Height + _deepZoomTileSize - 1) / _deepZoomTileSize;
                tileYCount = Math.Min(tileYCount, _deepZoomTileRowCount - tileYIndex);

                int tileXIndex = x / _deepZoomTileSize;
                int tileXOffset = tileXIndex * _deepZoomTileSize;
                int tileXCount = (x - tileXOffset + region.Width + _deepZoomTileSize - 1) / _deepZoomTileSize;
                tileXCount = Math.Min(tileXCount, _deepZoomTileColCount - tileXIndex);

                ImageCache nextVerticalCache = _backupVerticalCache;
                ImageCache nextHorizontalCache = _backupHorizontalCache;

                for (int i = 0; i < tileXCount; i++)
                {
                    int tileCol = tileXIndex + i;

                    for (int j = 0; j < tileYCount; j++)
                    {
                        int tileRow = tileYIndex + j;

                        int tilePointX = tileXOffset + i * _deepZoomTileSize;
                        int tilePointY = tileYOffset + j * _deepZoomTileSize;

                        int drawPointX = tilePointX - x - _deepZoomTileOverlap;
                        int drawPointY = tilePointY - y - _deepZoomTileOverlap;

                        cancellationToken.ThrowIfCancellationRequested();

                        Image<Rgb24> tile = null;
                        bool cachedOnce = false;
                        try
                        {
                            // First column
                            if (i == 0)
                            {
                                if (_verticalCache.TryFind(tilePointX, tilePointY, out Image<Rgb24> cachedImage))
                                {
                                    _verticalCache.RemoveEntry(tilePointX, tilePointY);
                                    tile = cachedImage;
                                }
                            }
                            // First row
                            if (tile == null && j == 0)
                            {
                                if (_horizontalCache.TryFind(tilePointX, tilePointY, out Image<Rgb24> cachedImage))
                                {
                                    _horizontalCache.RemoveEntry(tilePointX, tilePointY);
                                    tile = cachedImage;
                                }
                            }

                            // Not found in the cache
                            if (tile == null)
                            {
                                tile = await _ripper.ReadTileImageAsync(layer, tileCol, tileRow, cancellationToken).ConfigureAwait(false);
                            }

                            // Draw on the canvas
                            region.Mutate(ctx =>
                            {
                                ctx.DrawImage(tile, new Point(drawPointX, drawPointY), opacity: 1f);
                            });

                            // Last column
                            if (tilePointX + _deepZoomTileSize > x + region.Width)
                            {
                                nextVerticalCache.SetEntry(tilePointX, tilePointY, tile);
                                cachedOnce = true;
                            }
                            // Lat row
                            if (tilePointY + _deepZoomTileSize > y + region.Height)
                            {
                                nextHorizontalCache.SetEntry(tilePointX, tilePointY, cachedOnce ? tile.Clone() : tile);
                                cachedOnce = true;
                            }

                            if (cachedOnce)
                            {
                                tile = null;
                            }
                        }
                        finally
                        {
                            tile?.Dispose();
                        }
                    }
                }

                SwapCacheAndClear();
            }

            private void SwapCacheAndClear()
            {
                ImageCache temp;

                temp = _verticalCache;
                _verticalCache = _backupVerticalCache;
                _backupVerticalCache = temp;
                temp.Clear();

                temp = _horizontalCache;
                _horizontalCache = _backupHorizontalCache;
                _backupHorizontalCache = temp;
                temp.Clear();
            }

            public void Dispose()
            {
                _verticalCache.Dispose();
                _horizontalCache.Dispose();
            }
        }

        #endregion

        #region Reduced resolution layer

        public async Task GenerateReducedResolutionLayerAsync(IRipperReducedResolutionGenerationReporter reporter, CancellationToken cancellationToken)
        {
            _fileReader = await TiffFileReader.OpenAsync(_stream, leaveOpen: true);

            TiffStreamOffset ifd = _fileReader.FirstImageFileDirectoryOffset;
            int width = _manifest.Width;
            int height = _manifest.Height;

            // Calculate layer count
            int outputTileSize = _options.OutputTileSize;
            int layerCount = 0;
            int calcWidth = width, calcHeight = height;

            while (Math.Min(calcWidth, calcHeight) > outputTileSize && Math.Min(calcWidth, calcHeight) >= 32)
            {
                calcWidth = (calcWidth + 1) / 2;
                calcHeight = (calcHeight + 1) / 2;

                layerCount++;
            }

            // Actually generate the layers
            int layer = 0;
            reporter?.ReportStartReducedResolutionGeneration(layerCount);

            while (Math.Min(width, height) > outputTileSize && Math.Min(width, height) >= 32)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ifd = await GenerateReducedResolutionLayerAsync(++layer, ifd, reporter, cancellationToken).ConfigureAwait(false);

                width = (width + 1) / 2;
                height = (width + 1) / 2;
            }

            reporter?.ReportCompleteReducedResolutionGeneration(layerCount);
        }

        internal async Task<TiffStreamOffset> GenerateReducedResolutionLayerAsync(int layer, TiffStreamOffset ifdOffset, IRipperReducedResolutionGenerationReporter reporter, CancellationToken cancellationToken)
        {
            int outputTileSize = _options.OutputTileSize;
            int outputTileSize2 = 2 * outputTileSize;
            TiffImageFileDirectory ifd = await _fileReader.ReadImageFileDirectoryAsync(ifdOffset).ConfigureAwait(false);
            TiffImageDecoder image = await _fileReader.CreateImageDecoderAsync(ifd);

            int width = image.Width;
            int height = image.Height;

            int tiffRowCount = ((width + 1) / 2 + outputTileSize - 1) / outputTileSize;
            int tiffColCount = ((height + 1) / 2 + outputTileSize - 1) / outputTileSize;

            int index = 0;
            ulong[] offsets = new ulong[tiffRowCount * tiffColCount];
            ulong[] byteCounts = new ulong[tiffRowCount * tiffColCount];
            ulong totalByteCount = 0;

            reporter?.ReportStartReducedResolutionLayerGeneration(layer, offsets.Length, (width + 1) / 2, (height + 1) / 2);

            using (Image<Rgb24> canvas2 = new Image<Rgb24>(_configuration, outputTileSize2, outputTileSize2))
            {
                for (int y = 0; y < height; y += outputTileSize2)
                {
                    for (int x = 0; x < width; x += outputTileSize2)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ClearImage(canvas2);

                        await image.DecodeAsync(new TiffPoint(x, y), canvas2).ConfigureAwait(false);

                        using (Image<Rgb24> tile24 = canvas2.Clone(ctx =>
                        {
                            ctx.Resize(outputTileSize, outputTileSize);
                        }))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            TiffStreamRegion region = await _encoder.EncodeAsync(_fileWriter, tile24).ConfigureAwait(false);

                            offsets[index] = (ulong)region.Offset.Offset;
                            byteCounts[index] = (uint)region.Length;
                            totalByteCount += (uint)region.Length;
                            index++;

                            reporter?.ReportReducedResolutionLayerGenerationProgress(layer, index, offsets.Length);
                        }
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            using (TiffImageFileDirectoryWriter ifdWriter = _fileWriter.CreateImageFileDirectory())
            {
                await ifdWriter.WriteTagAsync(TiffTag.NewSubfileType, new TiffValueCollection<ushort>((ushort)TiffNewSubfileType.ReducedResolution)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.PhotometricInterpretation, new TiffValueCollection<ushort>((ushort)TiffPhotometricInterpretation.YCbCr)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.Compression, new TiffValueCollection<ushort>((ushort)TiffCompression.Jpeg)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.SamplesPerPixel, new TiffValueCollection<ushort>((ushort)3)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.TileWidth, new TiffValueCollection<ushort>((ushort)outputTileSize)).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.TileLength, new TiffValueCollection<ushort>((ushort)outputTileSize)).ConfigureAwait(false);
                //ifdWriter.AddTag(TiffTag.ResolutionUnit, (ushort)TiffResolutionUnit.Inch);

                //await ifdWriter.WriteTagAsync(TiffTag.XResolution, new TiffValueCollection<TiffRational>(new TiffRational(72, 1)));
                //await ifdWriter.WriteTagAsync(TiffTag.YResolution, new TiffValueCollection<TiffRational>(new TiffRational(72, 1)));
                await ifdWriter.WriteTagAsync(TiffTag.SampleFormat, new TiffValueCollection<ushort>(new ushort[] { 1, 1, 1 })).ConfigureAwait(false);
                await ifdWriter.WriteTagAsync(TiffTag.BitsPerSample, new TiffValueCollection<ushort>(new ushort[] { 8, 8, 8 })).ConfigureAwait(false);

                if (UseBigTiff)
                {
                    await ifdWriter.WriteTagAsync(TiffTag.ImageWidth, new TiffValueCollection<ulong>((ulong)((width + 1) / 2))).ConfigureAwait(false);
                    await ifdWriter.WriteTagAsync(TiffTag.ImageLength, new TiffValueCollection<ulong>((ulong)((height + 1) / 2))).ConfigureAwait(false);

                    await ifdWriter.WriteTagAsync(TiffTag.TileOffsets, new TiffValueCollection<ulong>(offsets)).ConfigureAwait(false);
                    await ifdWriter.WriteTagAsync(TiffTag.TileByteCounts, new TiffValueCollection<ulong>(byteCounts)).ConfigureAwait(false);
                }
                else
                {
                    await ifdWriter.WriteTagAsync(TiffTag.ImageWidth, new TiffValueCollection<uint>((uint)((width + 1) / 2))).ConfigureAwait(false);
                    await ifdWriter.WriteTagAsync(TiffTag.ImageLength, new TiffValueCollection<uint>((uint)((height + 1) / 2))).ConfigureAwait(false);

                    uint[] tempArr = new uint[offsets.Length];
                    for (int i = 0; i < tempArr.Length; i++)
                    {
                        tempArr[i] = (uint)offsets[i];
                    }
                    await ifdWriter.WriteTagAsync(TiffTag.TileOffsets, new TiffValueCollection<uint>(tempArr)).ConfigureAwait(false);

                    for (int i = 0; i < tempArr.Length; i++)
                    {
                        tempArr[i] = (uint)byteCounts[i];
                    }
                    await ifdWriter.WriteTagAsync(TiffTag.TileByteCounts, new TiffValueCollection<uint>(tempArr)).ConfigureAwait(false);
                }

                string software = Software;
                if (!string.IsNullOrEmpty(software))
                {
                    await ifdWriter.WriteTagAsync(TiffTag.Software, new TiffValueCollection<string>(software));
                }

                if (!(_jpegTables is null))
                {
                    await ifdWriter.WriteTagAsync(TiffTag.JPEGTables, TiffFieldType.Undefined, TiffValueCollection.UnsafeWrap(_jpegTables)).ConfigureAwait(false);
                }

                ifdOffset = await ifdWriter.FlushAsync(ifdOffset).ConfigureAwait(false);
            }

            reporter?.ReportCompleteReducedResolutionLayerGeneration(layer, offsets.Length, (long)totalByteCount);


            return ifdOffset;
        }

        #endregion

        #region IDisposable Support

        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _stream?.Dispose();
                    _fileWriter?.Dispose();
                    _fileReader?.Dispose();
                }

                _stream = null;
                _fileWriter = null;
                _fileReader = null;

                disposedValue = true;
            }
        }

        ~DeepZoomRipper()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        private static void ClearImage<T>(Image<T> image) where T : unmanaged, IPixel<T>
        {
            foreach (Memory<T> memory in image.GetPixelMemoryGroup())
            {
                MemoryMarshal.AsBytes(memory.Span).Clear();
            }
        }
    }
}
