using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Visuals.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace Svg.Skia.Avalonia
{
    internal static class Extensions
    {
        public static T GetService<T>(this IServiceProvider sp) => (T)sp?.GetService(typeof(T));

        public static Uri GetContextBaseUri(this IServiceProvider ctx) => ctx.GetService<IUriContext>().BaseUri;
    }

    internal class SvgTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var s = (string)value;
            var uri = s.StartsWith("/")
                ? new Uri(s, UriKind.Relative)
                : new Uri(s, UriKind.RelativeOrAbsolute);

            var skSvg = new Svg();
            if (uri.IsAbsoluteUri && uri.IsFile)
            {
                skSvg.Load(uri.LocalPath);
                return skSvg;
            }
            else
            {
                var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
                skSvg.Load(assets.Open(uri, context.GetContextBaseUri()));
            }
            return skSvg;
        }
    }

    [TypeConverter(typeof(SvgTypeConverter))]
    public interface ISvg : IDisposable
    {
        SKPicture Picture { get; set; }
    }

    internal class Svg : SKSvg, ISvg
    {
    }

    internal class SKSvgCustomDrawOperation : ICustomDrawOperation
    {
        private readonly ISvg _svg;

        public SKSvgCustomDrawOperation(Rect bounds, ISvg svg)
        {
            _svg = svg;
            Bounds = bounds;
        }

        public void Dispose()
        {
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation other) => false;

        public void Render(IDrawingContextImpl context)
        {
            var canvas = (context as ISkiaDrawingContextImpl)?.SkCanvas;
            if (canvas != null)
            {
                canvas.Save();
                canvas.DrawPicture(_svg.Picture);
                canvas.Restore();
            }
        }
    }

    public class SvgImage : AvaloniaObject, IImage, IAffectsRender
    {
        public static readonly StyledProperty<ISvg> SourceProperty =
            AvaloniaProperty.Register<SvgImage, ISvg>(nameof(Source));

        public event EventHandler Invalidated;

        [Content]
        public ISvg Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public Size Size =>
            Source?.Picture != null ? new Size(Source.Picture.CullRect.Width, Source.Picture.CullRect.Height) : default;

        void IImage.Draw(
            DrawingContext context,
            Rect sourceRect,
            Rect destRect,
            BitmapInterpolationMode bitmapInterpolationMode)
        {
            var source = Source;
            if (source == null)
            {
                return;
            }

            var bounds = source.Picture.CullRect;
            var scale = Matrix.CreateScale(
                destRect.Width / sourceRect.Width,
                destRect.Height / sourceRect.Height);
            var translate = Matrix.CreateTranslation(
                -sourceRect.X + destRect.X - bounds.Top,
                -sourceRect.Y + destRect.Y - bounds.Left);

            using (context.PushClip(destRect))
            using (context.PushPreTransform(translate * scale))
            {
                context.Custom(
                    new SKSvgCustomDrawOperation(
                        new Rect(0, 0, bounds.Width, bounds.Height),
                        source));
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == SourceProperty)
            {
                RaiseInvalidated(EventArgs.Empty);
            }
        }

        protected void RaiseInvalidated(EventArgs e) => Invalidated?.Invoke(this, e);
    }
}
