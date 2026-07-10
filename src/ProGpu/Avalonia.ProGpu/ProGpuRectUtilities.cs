using System;
using Avalonia.Platform;

namespace Avalonia.ProGpu
{
    internal static class ProGpuRectUtilities
    {
        public static bool IsEmpty(in LtrbPixelRect rect) =>
            rect.Left == rect.Right && rect.Top == rect.Bottom;

        public static LtrbPixelRect Union(in LtrbPixelRect left, in LtrbPixelRect right)
        {
            if (IsEmpty(left))
                return right;
            if (IsEmpty(right))
                return left;

            return new LtrbPixelRect
            {
                Left = Math.Min(left.Left, right.Left),
                Top = Math.Min(left.Top, right.Top),
                Right = Math.Max(left.Right, right.Right),
                Bottom = Math.Max(left.Bottom, right.Bottom)
            };
        }

        public static LtrbRect ToRect(in LtrbPixelRect rect) => new()
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom
        };

        public static bool Intersects(in LtrbRect left, in LtrbRect right) =>
            right.Left < left.Right && left.Left < right.Right &&
            right.Top < left.Bottom && left.Top < right.Bottom;
    }
}
