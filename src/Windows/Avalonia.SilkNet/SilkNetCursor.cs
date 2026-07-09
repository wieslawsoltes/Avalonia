using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Silk.NET.Core;
using Silk.NET.Input;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Avalonia.SilkNet
{
    internal sealed class SilkNetCursorImpl : ICursorImpl
    {
        public SilkNetCursorImpl(StandardCursorType cursorType)
        {
            CursorMode = cursorType == StandardCursorType.None
                ? Silk.NET.Input.CursorMode.Hidden
                : Silk.NET.Input.CursorMode.Normal;
            CursorType = Silk.NET.Input.CursorType.Standard;
            StandardCursor = MapStandardCursor(cursorType);
        }

        public SilkNetCursorImpl(byte[] pixels, int width, int height, PixelPoint hotSpot)
        {
            CursorMode = Silk.NET.Input.CursorMode.Normal;
            CursorType = Silk.NET.Input.CursorType.Custom;
            StandardCursor = Silk.NET.Input.StandardCursor.Arrow;
            Image = new RawImage(width, height, pixels);
            HotSpot = new PixelPoint(
                Math.Clamp(hotSpot.X, 0, width - 1),
                Math.Clamp(hotSpot.Y, 0, height - 1));
        }

        public Silk.NET.Input.CursorMode CursorMode { get; }
        public Silk.NET.Input.CursorType CursorType { get; }
        public Silk.NET.Input.StandardCursor StandardCursor { get; }
        public RawImage? Image { get; }
        public PixelPoint HotSpot { get; }

        internal static Silk.NET.Input.StandardCursor MapStandardCursor(StandardCursorType cursorType)
        {
            return cursorType switch
            {
                StandardCursorType.Ibeam => Silk.NET.Input.StandardCursor.IBeam,
                StandardCursorType.Wait => Silk.NET.Input.StandardCursor.Wait,
                StandardCursorType.Cross => Silk.NET.Input.StandardCursor.Crosshair,
                StandardCursorType.SizeWestEast or
                    StandardCursorType.LeftSide or
                    StandardCursorType.RightSide => Silk.NET.Input.StandardCursor.HResize,
                StandardCursorType.SizeNorthSouth or
                    StandardCursorType.TopSide or
                    StandardCursorType.BottomSide => Silk.NET.Input.StandardCursor.VResize,
                StandardCursorType.SizeAll or
                    StandardCursorType.DragMove => Silk.NET.Input.StandardCursor.ResizeAll,
                StandardCursorType.No => Silk.NET.Input.StandardCursor.NotAllowed,
                StandardCursorType.Hand or
                    StandardCursorType.DragLink => Silk.NET.Input.StandardCursor.Hand,
                StandardCursorType.AppStarting => Silk.NET.Input.StandardCursor.WaitArrow,
                StandardCursorType.TopLeftCorner or
                    StandardCursorType.BottomRightCorner => Silk.NET.Input.StandardCursor.NwseResize,
                StandardCursorType.TopRightCorner or
                    StandardCursorType.BottomLeftCorner => Silk.NET.Input.StandardCursor.NeswResize,
                _ => Silk.NET.Input.StandardCursor.Arrow,
            };
        }

        public void Dispose()
        {
        }
    }

    public class SilkNetCursorFactory : ICursorFactory
    {
        private readonly Dictionary<StandardCursorType, SilkNetCursorImpl> _standardCursors = new();

        public ICursorImpl GetCursor(StandardCursorType cursorType)
        {
            lock (_standardCursors)
            {
                if (!_standardCursors.TryGetValue(cursorType, out var cursor))
                {
                    cursor = new SilkNetCursorImpl(cursorType);
                    _standardCursors.Add(cursorType, cursor);
                }

                return cursor;
            }
        }

        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot)
        {
            using var stream = new MemoryStream();
            cursor.Save(stream);
            stream.Position = 0;

            using var image = Image.Load<Rgba32>(stream);
            var pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);
            return new SilkNetCursorImpl(pixels, image.Width, image.Height, hotSpot);
        }
    }
}
