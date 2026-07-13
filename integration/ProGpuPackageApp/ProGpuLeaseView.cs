using System;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.ProGpu;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Threading;
using ProGPU.Backend;
using ProGPU.Scene.Extensions;
using ProGpuBrush = ProGPU.Vector.SolidColorBrush;
using ProGpuRect = ProGPU.Scene.Rect;

namespace ProGpuPackageApp;

internal sealed class ProGpuLeaseView : Control
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private int _frame;

    public ProGpuLeaseView()
    {
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(1.0 / 30.0),
            DispatcherPriority.Render,
            (_, _) => InvalidateVisual());
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new ProGpuDrawOperation(
            new Rect(0, 0, Bounds.Width, Bounds.Height),
            (float)_clock.Elapsed.TotalSeconds,
            _frame++));
    }

    private sealed class ProGpuDrawOperation : ICustomDrawOperation
    {
        private static readonly string s_shaderSource =
            ShaderResource.Load<ProGpuDrawOperation>("ApiLeaseWave.wgsl");

        private readonly float _time;
        private readonly int _frame;

        public ProGpuDrawOperation(Rect bounds, float time, int frame)
        {
            Bounds = bounds;
            _time = time;
            _frame = frame;
        }

        public Rect Bounds { get; }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<IProGpuApiLeaseFeature>() ??
                throw new InvalidOperationException("The ProGPU API lease feature is unavailable.");

            using var lease = feature.Lease();
            var width = (float)Bounds.Width;
            var height = (float)Bounds.Height;
            if (width <= 0 || height <= 0)
                return;

            var surface = new ProGpuBrush(new Vector4(0.08f, 0.1f, 0.16f, 1f));
            var cyan = new ProGpuBrush(new Vector4(0.09f, 0.72f, 0.96f, 1f));
            var violet = new ProGpuBrush(new Vector4(0.56f, 0.3f, 0.94f, 1f));

            lease.DrawingContext.DrawRoundedRectangle(
                surface,
                null,
                new ProGpuRect(0, 0, width, height),
                8,
                8,
                lease.CurrentTransform);
            lease.DrawingContext.DrawRoundedRectangle(
                cyan,
                null,
                new ProGpuRect(14, 18, Math.Max(0, width * 0.38f), Math.Max(0, height - 36)),
                7,
                7,
                lease.CurrentTransform);
            lease.DrawingContext.DrawEllipse(
                violet,
                null,
                new Vector2(width * 0.43f, height / 2),
                16,
                16,
                lease.CurrentTransform);

            var shaderRect = new ProGpuRect(
                width * 0.52f,
                10,
                Math.Max(0, width * 0.48f - 10),
                Math.Max(0, height - 20));
            var shader = new ShaderToyParams
            {
                Rect = shaderRect,
                Resolution = new Vector3(shaderRect.Width, shaderRect.Height, 1),
                Time = _time,
                TimeDelta = 1f / 30f,
                Frame = _frame,
                FrameRate = 30,
                ShaderKey = "AvaloniaProGpuApiLeaseWgslV1",
                ShaderSource = s_shaderSource
            };
            lease.DrawingContext.DrawExtension(
                ProGPU.Scene.CompositorBuiltInExtensions.ShaderToy,
                dataParam: shader,
                transform: lease.CurrentTransform);
        }

        public bool HitTest(Point point) => Bounds.Contains(point);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
