using System;
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
    
    public static class CanvasRenderer
    {
        public static void Render(Control target, SKCanvas canvas, double dpi = 96, bool useDeferredRenderer = false)
        {
            var renderTarget = new CanvasRenderTarget(canvas, dpi);
            if (useDeferredRenderer)
            {
                using var renderer = new DeferredRenderer(target, renderTarget);
                renderer.Start();
                var renderLoopTask = renderer as IRenderLoopTask;
                renderLoopTask.Update(TimeSpan.Zero);
                renderLoopTask.Render();
                renderLoopTask.Update(TimeSpan.FromSeconds(1));
                renderLoopTask.Render();
            }
            else
            {
                ImmediateRenderer.Render(target, renderTarget);
            }
        }
    }
    
    public static class SkpRenderer
    {
        public static void Render(Control target, Size size, Stream stream, double dpi = 96, bool useDeferredRenderer = false)
        {
            var bounds = SKRect.Create(new SKSize((float)size.Width, (float)size.Height));
            using var pictureRecorder = new SKPictureRecorder();
            using var canvas = pictureRecorder.BeginRecording(bounds);
            target.Measure(size);
            target.Arrange(new Rect(size));
            CanvasRenderer.Render(target, canvas, dpi, useDeferredRenderer);
            using var picture = pictureRecorder.EndRecording();
            picture.Serialize(stream);
        }
    }
    
    public static class SvgRenderer
    {
        public static void Render(Control target, Size size, Stream stream, double dpi = 96, bool useDeferredRenderer = false)
        {
            using var wstream = new SKManagedWStream(stream);
            var bounds = SKRect.Create(new SKSize((float)size.Width, (float)size.Height));
            using var canvas = SKSvgCanvas.Create(bounds, wstream);
            target.Measure(size);
            target.Arrange(new Rect(size));
            CanvasRenderer.Render(target, canvas, dpi, useDeferredRenderer);
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
                        //SkpRenderer.Render(this, this.Bounds.Size, stream, 96, false);
                        SkpRenderer.Render(this, this.Bounds.Size, stream, 96, true);
                        //SvgRenderer.Render(this, this.Bounds.Size, stream, 96, false);
                        //SvgRenderer.Render(this, this.Bounds.Size, stream, 96, true);
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
