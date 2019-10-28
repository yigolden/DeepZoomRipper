using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DeepZoomRipperLibrary
{
    internal readonly struct DeepZoomLayer
    {
        public int Width { get; }
        public int Height { get; }

        public DeepZoomLayer(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    internal static class DeepZoomHelper
    {
        internal static DeepZoomLayer[] CalculateDeepZoomLayers(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            var layers = new List<DeepZoomLayer>(20);
            DeepZoomLayer currentLayer = new DeepZoomLayer(width, height);
            layers.Add(currentLayer);
            while (currentLayer.Width != 1 || currentLayer.Height != 1)
            {
                currentLayer = new DeepZoomLayer((currentLayer.Width + 1) / 2, (currentLayer.Height + 1) / 2);
                layers.Add(currentLayer);
            }
            DeepZoomLayer[] layersArray = layers.ToArray();
            Array.Reverse(layersArray);
            return layersArray;
        }

        internal static int FindWrappingLayer(DeepZoomLayer[] layers, int size)
        {
            Debug.Assert(layers != null);
            Debug.Assert(layers.Length > 0);
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            int index = 0;
            int length = layers.Length;
            while (index < layers.Length)
            {
                DeepZoomLayer layer = layers[index];
                if (layer.Width > size || layer.Height > size)
                {
                    return index;
                }
                index++;
            }
            return length - 1;
        }
    }
}
