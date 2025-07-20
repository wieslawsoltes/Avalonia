using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Layout;

namespace ControlCatalog.CodeOnly
{
    public class CanvasPage : UserControl
    {
        public CanvasPage()
        {
            var canvas = new Canvas
            {
                Background = Brushes.Yellow,
                Width = 300,
                Height = 400
            };

            canvas.Children.Add(new Rectangle
            {
                Fill = Brushes.Blue,
                Width = 63,
                Height = 41,
                RadiusX = 10,
                RadiusY = 10,
                [Canvas.LeftProperty] = 40,
                [Canvas.TopProperty] = 31,
                OpacityMask = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0,0,RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1,1,RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Colors.Black,0),
                        new GradientStop(Colors.Transparent,1)
                    }
                }
            });

            canvas.Children.Add(new Rectangle
            {
                Fill = new SolidColorBrush(Color.Parse("hsva(240,83%,73%,90%)")),
                Stroke = new SolidColorBrush(Color.Parse("hsl(5,85%,85%)")),
                StrokeThickness = 2,
                Width = 40,
                Height = 20,
                RadiusX = 10,
                RadiusY = 5,
                [Canvas.LeftProperty] = 150,
                [Canvas.TopProperty] = 10
            });

            canvas.Children.Add(new Ellipse
            {
                Fill = Brushes.Green,
                Width = 58,
                Height = 58,
                [Canvas.LeftProperty] = 88,
                [Canvas.TopProperty] = 100
            });

            canvas.Children.Add(new Path
            {
                Fill = Brushes.Orange,
                Data = Geometry.Parse("M 0,0 c 0,0 50,0 50,-50 c 0,0 50,0 50,50 h -50 v 50 l -50,-50 Z"),
                [Canvas.LeftProperty] = 30,
                [Canvas.TopProperty] = 250
            });

            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(0,0), IsClosed = true };
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(50,0), Point2 = new Point(50,-50) });
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(100,-50), Point2 = new Point(100,0) });
            figure.Segments.Add(new LineSegment { Point = new Point(50,0) });
            figure.Segments.Add(new LineSegment { Point = new Point(50,50) });
            geometry.Figures.Add(figure);

            canvas.Children.Add(new Path
            {
                Fill = Brushes.OrangeRed,
                Data = geometry,
                [Canvas.LeftProperty] = 180,
                [Canvas.TopProperty] = 250
            });

            canvas.Children.Add(new Line
            {
                StartPoint = new Point(120,185),
                EndPoint = new Point(30,115),
                Stroke = Brushes.Red,
                StrokeThickness = 2
            });

            canvas.Children.Add(new Polygon
            {
                Points = new Points { new Point(75,0), new Point(120,120), new Point(0,45), new Point(150,45), new Point(30,120) },
                Stroke = Brushes.DarkBlue,
                StrokeThickness = 1,
                Fill = Brushes.Violet,
                [Canvas.LeftProperty] = 150,
                [Canvas.TopProperty] = 31
            });

            canvas.Children.Add(new Polyline
            {
                Points = new Points { new Point(0,0), new Point(65,0), new Point(78,-26), new Point(91,39), new Point(104,-39), new Point(117,13), new Point(130,0), new Point(195,0) },
                Stroke = Brushes.Brown,
                [Canvas.LeftProperty] = 30,
                [Canvas.TopProperty] = 350
            });

            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Classes = { "h2" }, Text = "A panel which lays out its children by explicit coordinates" },
                    canvas
                }
            };
        }
    }
}
