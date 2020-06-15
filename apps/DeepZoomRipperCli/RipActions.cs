using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DeepZoomRipperLibrary;

namespace DeepZoomRipperCli
{
    internal class RipActions
    {
        public static async Task<int> Rip(string source, FileInfo output, int tileSize, bool noSoftwareField, bool useSharedQuantizationTables, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException(nameof(source));
            }
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            if (tileSize <= 0 || (tileSize % 16) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileSize));
            }

            var uri = new Uri(source);
            var reporter = new RipperReporter();

            var options = new DeepZoomRipperOptions
            {
                OutputTileSize = tileSize
            };

            using (var ripper = new TracedDeepZoomRipper(options, output.FullName))
            {
                await ripper.InitializeAsync(uri, cancellationToken);

                PrintSourceInformation(ripper, uri);
                Console.WriteLine();

                Console.WriteLine("Output Information");
                Console.WriteLine("TileSize: " + tileSize);
                Console.WriteLine();
                Console.WriteLine(output.Exists ? $"Overriding TIFF file at {output.FullName}" : $"Saving TIFF file to {output.FullName}");
                Console.WriteLine();

                if (!noSoftwareField)
                {
                    ripper.Software = ThisAssembly.AssemblyTitle + " " + ThisAssembly.AssemblyInformationalVersion;
                }
                ripper.UseSharedQuantizationTables = useSharedQuantizationTables;

                var sw = new Stopwatch();
                sw.Start();

                await ripper.RipBaseLayerAsync(reporter, cancellationToken);
#if DEBUG
                Console.WriteLine("Tile IO Count: " + ripper.TileIOCount);
#endif
                Console.WriteLine();

                await ripper.GenerateReducedResolutionLayerAsync(reporter, cancellationToken);
                Console.WriteLine();

                sw.Stop();
                Console.WriteLine("Operation completed.");
                Console.WriteLine("Elapsed time: " + sw.Elapsed);
            }

            return 0;
        }

        private static void PrintSourceInformation(DefaultDeepZoomRipper ripper, Uri uri)
        {
            Console.WriteLine("Source Information");
            Console.WriteLine($"DeepZoom URL: {uri}");
            Console.WriteLine($"Format: {ripper.ImageFormat} ");
            Console.WriteLine($"Size: ({ripper.ImageWidth}, {ripper.ImageHeight})");
            Console.WriteLine($"DeepZoom TileSize: {ripper.DeepZoomTileSize}");
            Console.WriteLine($"DeepZoom TileOverlap: {ripper.DeepZoomOverlap}");
            Console.WriteLine($"DeepZoom LayerCount: {ripper.DeepZoomLayerCount}");
        }

        private class RipperReporter : IRipperInitialLayerAcquisitionReporter, IRipperReducedResolutionGenerationReporter
        {
            void IRipperInitialLayerAcquisitionReporter.ReportInitialLayerAcquisitionProgress(int completedTileCount, int totalTileCount)
            {
                Console.Write('\r');
                Console.Write($"{completedTileCount}/{totalTileCount}");
            }

            void IRipperInitialLayerAcquisitionReporter.ReportStartInitialLayerAcquisition(int tileCount)
            {
                Console.WriteLine("Downloading the base layer...");
            }

            void IRipperInitialLayerAcquisitionReporter.ReportCompleteInitialLayerAcquisition(int tileCount, long fileSize)
            {
                Console.Write('\r');
                Console.WriteLine($"The base layer has been downloaded, containing {tileCount} tiles ({GetBytesReadable(fileSize)}).");
            }

            private int layerCount = 0;
            private int width = 0;
            private int height = 0;

            void IRipperReducedResolutionGenerationReporter.ReportStartReducedResolutionGeneration(int layers)
            {
                layerCount = layers;
                Console.WriteLine($"Generating reduced resolution layers... ({layers} layers to generate.)");
            }

            void IRipperReducedResolutionGenerationReporter.ReportStartReducedResolutionLayerGeneration(int layer, int tileCount, int width, int height)
            {
                this.width = width;
                this.height = height;
            }

            void IRipperReducedResolutionGenerationReporter.ReportReducedResolutionLayerGenerationProgress(int layer, int completedTileCount, int totalTileCount)
            {
                Console.Write('\r');
                Console.Write($"Layer {layer}: {completedTileCount}/{totalTileCount}");
            }

            void IRipperReducedResolutionGenerationReporter.ReportCompleteReducedResolutionLayerGeneration(int layer, int tileCount, long fileSize)
            {
                Console.Write('\r');
                Console.WriteLine($"Layer {layer} ({width}, {height}) generated, containing {tileCount} tiles ({GetBytesReadable(fileSize)}).");
            }

            void IRipperReducedResolutionGenerationReporter.ReportCompleteReducedResolutionGeneration(int layers)
            {
                Console.WriteLine($"{layers} reduced resolution layers have been generated.");
            }

            private static string GetBytesReadable(long i)
            {
                // Get absolute value
                long absolute_i = (i < 0 ? -i : i);
                // Determine the suffix and readable value
                string suffix;
                double readable;
                if (absolute_i >= 0x1000000000000000) // Exabyte
                {
                    suffix = "EB";
                    readable = (i >> 50);
                }
                else if (absolute_i >= 0x4000000000000) // Petabyte
                {
                    suffix = "PB";
                    readable = (i >> 40);
                }
                else if (absolute_i >= 0x10000000000) // Terabyte
                {
                    suffix = "TB";
                    readable = (i >> 30);
                }
                else if (absolute_i >= 0x40000000) // Gigabyte
                {
                    suffix = "GB";
                    readable = (i >> 20);
                }
                else if (absolute_i >= 0x100000) // Megabyte
                {
                    suffix = "MB";
                    readable = (i >> 10);
                }
                else if (absolute_i >= 0x400) // Kilobyte
                {
                    suffix = "KB";
                    readable = i;
                }
                else
                {
                    return i.ToString("0 B"); // Byte
                }
                // Divide by 1024 to get fractional value
                readable = (readable / 1024);
                // Return formatted number with suffix
                return readable.ToString("0.### ") + suffix;
            }
        }
    }
}
