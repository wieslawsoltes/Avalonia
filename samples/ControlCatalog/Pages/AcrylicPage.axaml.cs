using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Skia.Helpers;
using SkiaSharp;

namespace ControlCatalog.Pages
{
    public class CanvasRenderTarget : IRenderTarget
    {
        private readonly SKCanvas _canvas;
        private readonly double _dpi;
            
        public CanvasRenderTarget(SKCanvas canvas, double dpi)
        {
            _canvas = canvas;
            _dpi = dpi;
        }

        public IDrawingContextImpl CreateDrawingContext(IVisualBrushRenderer visualBrushRenderer)
        {
            return DrawingContextHelper.WrapSkiaCanvas(_canvas, new Vector(_dpi, _dpi), visualBrushRenderer);
        }

        public void Dispose()
        {
        }
    }
    
    public static class SkpRenderer
    {
        public static void Render(Control target, Size size, Stream stream, double dpi = 96)
        {
            var bounds = SKRect.Create(new SKSize((float)size.Width, (float)size.Height));
            using var pictureRecorder = new SKPictureRecorder();
            using var canvas = pictureRecorder.BeginRecording(bounds);
            using var renderer = new ImmediateRenderer(target);
            target.Measure(size);
            target.Arrange(new Rect(size));
            using var renderTarget = new CanvasRenderTarget(canvas, dpi);
            ImmediateRenderer.Render(target, renderTarget);
            using var picture = pictureRecorder.EndRecording();
            picture.Serialize(stream);
        }
    }
    
    
    public class AcrylicPage : UserControl
    {
        public static readonly StyledProperty<bool> ButtonEnableProperty = AvaloniaProperty.Register<AcrylicPage, bool>("ButtonEnable");

        public AcrylicPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            
            (VisualRoot as Window).AddHandler(InputElement.KeyDownEvent, async (sender, args) =>
            {
                if (args.Key == Key.F6)
                {
                    var dlg = new SaveFileDialog();
                    var result = await dlg.ShowAsync(this.VisualRoot as Window);
                    if (result is { } path)
                    {
                        using var stream = File.Create(path);
                        SkpRenderer.Render(this, this.Bounds.Size, stream);
                    }
                }
            }, RoutingStrategies.Tunnel);
        }

        public bool ButtonEnable
        {
            get { return GetValue(ButtonEnableProperty); }
            set { SetValue(ButtonEnableProperty, value); }
        }


        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
