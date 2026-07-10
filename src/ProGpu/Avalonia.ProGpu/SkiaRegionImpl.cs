using System;
using System.Collections.Generic;
using Avalonia.Platform;

namespace Avalonia.ProGpu
{
    internal class SkiaRegionImpl : IPlatformRenderInterfaceRegion
    {
        private readonly List<LtrbPixelRect> _rects = new();
        private LtrbPixelRect _bounds;

        public void Dispose()
        {
            _rects.Clear();
        }

        public void AddRect(LtrbPixelRect rect)
        {
            if (ProGpuRectUtilities.IsEmpty(rect)) return;
            _rects.Add(rect);
            _bounds = ProGpuRectUtilities.IsEmpty(_bounds)
                ? rect
                : ProGpuRectUtilities.Union(_bounds, rect);
        }

        public void Reset()
        {
            _rects.Clear();
            _bounds = default;
        }

        public bool IsEmpty => _rects.Count == 0;
        public LtrbPixelRect Bounds => _bounds;
        public IList<LtrbPixelRect> Rects => _rects;

        public bool Intersects(LtrbRect rect)
        {
            if (IsEmpty) return false;
            var boundsRect = ProGpuRectUtilities.ToRect(_bounds);
            if (!ProGpuRectUtilities.Intersects(boundsRect, rect)) return false;

            foreach (var r in _rects)
            {
                var ltrb = ProGpuRectUtilities.ToRect(r);
                if (ProGpuRectUtilities.Intersects(ltrb, rect)) return true;
            }
            return false;
        }

        public bool Contains(Point pt)
        {
            if (IsEmpty) return false;
            if (pt.X < _bounds.Left || pt.X > _bounds.Right || pt.Y < _bounds.Top || pt.Y > _bounds.Bottom)
                return false;

            foreach (var r in _rects)
            {
                if (pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom)
                    return true;
            }
            return false;
        }
    }
}
