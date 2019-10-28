using System;
using System.Collections.Generic;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DeepZoomRipperLibrary
{
    internal abstract class ImageCache : IDisposable
    {
        public abstract bool TryFind(int x, int y, out Image<Rgb24> image);
        public abstract void SetEntry(int x, int y, Image<Rgb24> image);
        public abstract void RemoveEntry(int x, int y);
        public abstract void Clear();
        public abstract void Dispose();
    }

    internal sealed class ListBasedImageCache : ImageCache
    {
        private List<Entry> _entries;

        private bool TryFind(int x, int y, out int index, out Image<Rgb24> image)
        {
            List<Entry> entries = _entries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    if (entry.X == x && entry.Y == y)
                    {
                        index = i;
                        image = entry.Image;
                        return true;
                    }
                }
            }
            index = 0;
            image = null;
            return false;
        }

        public override bool TryFind(int x, int y, out Image<Rgb24> image)
        {
            return TryFind(x, y, out _, out image);
        }

        public override void SetEntry(int x, int y, Image<Rgb24> image)
        {
            Debug.Assert(image != null);

            List<Entry> entries = _entries;
            if (entries is null)
            {
                _entries = entries = new List<Entry>();
                entries.Add(new Entry()
                {
                    X = x,
                    Y = y,
                    Image = image
                });
                return;
            }

            if (TryFind(x, y, out int index, out Image<Rgb24> oldImage))
            {
                oldImage.Dispose();
                Entry entry = entries[index];
                entry.Image = image;
                entries[index] = entry;
                return;
            }

            entries.Add(new Entry()
            {
                X = x,
                Y = y,
                Image = image
            });
        }

        public override void RemoveEntry(int x, int y)
        {
            if (TryFind(x, y, out int index, out _))
            {
                _entries.RemoveAt(index);
            }
        }

        public override void Clear()
        {
            List<Entry> entries = _entries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].Image.Dispose();
                }
                entries.Clear();
            }
        }

        public override void Dispose()
        {
            Clear();
        }

        private struct Entry
        {
            public int X;
            public int Y;
            public Image<Rgb24> Image;
        }
    }

    internal class DictionaryBasedImageCache : ImageCache
    {
        private Dictionary<ulong, Image<Rgb24>> _entries;

        public override bool TryFind(int x, int y, out Image<Rgb24> image)
        {
            Dictionary<ulong, Image<Rgb24>> entries = _entries;
            if (entries != null)
            {
                ulong key = ((ulong)(uint)x) << 32 | (uint)y;
                return entries.TryGetValue(key, out image);
            }
            image = null;
            return false;
        }

        public override void SetEntry(int x, int y, Image<Rgb24> image)
        {
            Debug.Assert(image != null);

            Dictionary<ulong, Image<Rgb24>> entries = _entries;
            if (entries is null)
            {
                _entries = entries = new Dictionary<ulong, Image<Rgb24>>();
            }

            ulong key = ((ulong)(uint)x) << 32 | (uint)y;
            if (_entries.TryGetValue(key, out Image<Rgb24> oldImage))
            {
                oldImage.Dispose();
            }
            _entries[key] = image;
        }

        public override void RemoveEntry(int x, int y)
        {
            Dictionary<ulong, Image<Rgb24>> entries = _entries;
            if (entries != null)
            {
                ulong key = ((ulong)(uint)x) << 32 | (uint)y;
                entries.Remove(key);
            }
        }

        public override void Clear()
        {
            Dictionary<ulong, Image<Rgb24>> entries = _entries;
            if (entries != null)
            {
                foreach (KeyValuePair<ulong, Image<Rgb24>> item in entries)
                {
                    item.Value.Dispose();
                }
                entries.Clear();
            }
        }

        public override void Dispose()
        {
            Clear();
        }

    }
}
