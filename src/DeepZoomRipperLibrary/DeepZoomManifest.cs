using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace DeepZoomRipperLibrary
{
    public class DeepZoomManifest
    {
        public string? Format { get; set; }
        public int TileSize { get; set; }
        public int Overlap { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }

        public static async Task<DeepZoomManifest> ParseAsync(Stream stream)
        {
            DeepZoomManifest manifest = new DeepZoomManifest();
            string tmp;
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true }))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.Name == "Image")
                        {
                            manifest.Format = reader.GetAttribute("Format");
                            tmp = reader.GetAttribute("TileSize");
                            int.TryParse(tmp, out int tileSize);
                            manifest.TileSize = tileSize;
                            tmp = reader.GetAttribute("Overlap");
                            int.TryParse(tmp, out int overlap);
                            manifest.Overlap = overlap;
                        }
                        else if (reader.Name == "Size")
                        {
                            tmp = reader.GetAttribute("Width");
                            int.TryParse(tmp, out int width);
                            manifest.Width = width;
                            tmp = reader.GetAttribute("Height");
                            int.TryParse(tmp, out int height);
                            manifest.Height = height;
                        }
                    }
                }
            }
            return manifest;
        }

    }
}
