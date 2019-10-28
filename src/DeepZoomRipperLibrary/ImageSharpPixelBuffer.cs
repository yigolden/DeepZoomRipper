using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using TiffLibrary;
using TiffLibrary.PixelFormats;

namespace DeepZoomRipperLibrary
{
    class ImageSharpPixelBuffer : ITiffPixelBuffer<TiffRgb24>
    {
        private readonly Image<Rgb24> _image;

        public ImageSharpPixelBuffer(Image<Rgb24> image)
        {
            _image = image;
        }

        public int Width => _image.Width;

        public int Height => _image.Height;

        public Span<TiffRgb24> GetSpan()
        {
            return MemoryMarshal.Cast<Rgb24, TiffRgb24>(_image.GetPixelSpan());
        }
    }
}
