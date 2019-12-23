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
using Svg.Skia;

namespace ControlCatalog.Pages
{
    internal static class Extensions
    {
        public static T GetService<T>(this IServiceProvider sp) => (T)sp?.GetService(typeof(T));

        public static Uri GetContextBaseUri(this IServiceProvider ctx) => ctx.GetService<IUriContext>().BaseUri;
    }

    public class SvgTypeConverter : TypeConverter
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

            var skSvg = new SKSvg();
            if (uri.IsAbsoluteUri && uri.IsFile)
            {
                skSvg.Load(uri.LocalPath);
                return new Svg(skSvg);
            }
            else
            {
                var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
                skSvg.Load(assets.Open(uri, context.GetContextBaseUri()));
            }
            return new Svg(skSvg);
        }
    }

    [TypeConverter(typeof(SvgTypeConverter))]
    public class Svg
    {
        public Svg(SKSvg value)
        {
            Value = value;
        }

        public SKSvg Value { get; }
    }

    internal class SKSvgCustomDrawOperation : ICustomDrawOperation
    {
        private readonly SKSvg _svg;

        public SKSvgCustomDrawOperation(Rect bounds, SKSvg svg)
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
        public static readonly StyledProperty<Svg> SourceProperty =
            AvaloniaProperty.Register<SvgImage, Svg>(nameof(Source));

        public event EventHandler Invalidated;

        [Content]
        public Svg Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public Size Size => Source?.Value?.Picture != null ?
            new Size(Source.Value.Picture.CullRect.Width, Source.Value.Picture.CullRect.Height) : default;

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

            var bounds = source.Value.Picture.CullRect;
            var scale = Matrix.CreateScale(
                destRect.Width / sourceRect.Width,
                destRect.Height / sourceRect.Height);
            var translate = Matrix.CreateTranslation(
                -sourceRect.X + destRect.X - bounds.Top,
                -sourceRect.Y + destRect.Y - bounds.Left);

            using (context.PushClip(destRect))
            using (context.PushPreTransform(translate * scale))
            {
                context.Custom(new SKSvgCustomDrawOperation(new Rect(0, 0, bounds.Width, bounds.Height), source.Value));
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
