using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Inking;
using Avalonia.Inking.Core;
using Avalonia.Inking.Themes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

AppBuilder.Configure<SpecApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var results = new List<Result>();
void Check(bool condition, string detail = "Assertion failed") { if (!condition) throw new Exception(detail); }
void Near(double a, double b) => Check(Math.Abs(a-b) < .01, $"Expected {b}; got {a}");
void Test(string name, Action<InkCanvas,Window> action)
{
    var canvas = new InkCanvas { Smoothing = 0, GridMode = InkGrid.None };
    var window = new Window { Width = 600, Height = 500, Content = canvas };
    try
    {
        window.Show(); Dispatcher.UIThread.RunJobs();
        action(canvas, window);
        Check(canvas.OperationFailureCount == 0, canvas.StatusText);
        results.Add(new(name, true, "")); Console.WriteLine("PASS " + name);
    }
    catch (Exception e) { results.Add(new(name, false, e.ToString())); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
}
void Draw(Window w, double y=100)
{
    w.MouseDown(new(80,y), MouseButton.Left);
    w.MouseMove(new(180,y+10), RawInputModifiers.LeftMouseButton);
    w.MouseMove(new(280,y), RawInputModifiers.LeftMouseButton);
    w.MouseUp(new(280,y), MouseButton.Left);
}
FuncControlTemplate<InkCanvas> AlternateTemplate() => new((owner, scope) =>
{
    var retained = new InkPresenter { IsHitTestVisible=false };
    var live = new InkAdornerPresenter { IsHitTestVisible=false };
    scope.Register("PART_Presenter", retained); scope.Register("PART_Adorner", live);
    return new Grid { Background=Brushes.White, Children={retained,live} };
});
Test("Default theme supplies both replaceable template parts", (c,w) => Check(c.Presenter is not null && c.Adorner is not null));
Test("Default sessions are per-control and DataContext remains host-owned", (c,w) =>
{
    var other=new InkCanvas(); var host=new object(); c.DataContext=host; Draw(w);
    Check(c.Session.Document.StrokeCount==1 && other.Session.Document.StrokeCount==0);
    Check(ReferenceEquals(c.DataContext,host) && !ReferenceEquals(c.Session,other.Session));
});
Test("Real headless mouse input collects one undoable stroke", (c,w) =>
{
    Draw(w); Check(c.Session.Document.StrokeCount==1 && c.Session.UndoCount==1);
    var saved=c.Session.Document; c.UndoCommand.Execute(null); Check(c.Session.Document.StrokeCount==0);
    c.RedoCommand.Execute(null); Check(ReferenceEquals(saved,c.Session.Document));
});
Test("Cancellable routed stroke event prevents insertion", (c,w) =>
{
    var proposed=0; var collected=0;
    c.StrokeCollecting+=(_,e)=>{ proposed++; e.Cancel=true; };
    c.StrokeCollected+=(_,_)=>collected++;
    Draw(w); Check(proposed==1 && collected==0 && c.Session.Document.StrokeCount==0 && c.Session.UndoCount==0);
});
Test("Retemplating preserves session, document and history", (c,w) =>
{
    Draw(w); var session=c.Session; var document=session.Document; var old=c.Presenter;
    c.Template=AlternateTemplate(); c.ApplyTemplate(); Dispatcher.UIThread.RunJobs();
    Check(!ReferenceEquals(old,c.Presenter) && ReferenceEquals(session,c.Session) && ReferenceEquals(document,c.Session.Document));
    Check(c.Session.UndoCount==1); Draw(w,190); Check(c.Session.Document.StrokeCount==2);
});
Test("Retemplating cancels uncommitted input", (c,w) =>
{
    var document=c.Session.Document; w.MouseDown(new(90,100),MouseButton.Left); w.MouseMove(new(250,140),RawInputModifiers.LeftMouseButton);
    c.Template=AlternateTemplate(); c.ApplyTemplate(); Dispatcher.UIThread.RunJobs(); w.MouseUp(new(250,140),MouseButton.Left);
    Check(ReferenceEquals(document,c.Session.Document) && c.Session.UndoCount==0 && c.CancelledGestureCount>0);
    Draw(w); Check(c.Session.Document.StrokeCount==1);
});
Test("Detaching during partial erasure restores the exact document", (c,w) =>
{
    Draw(w); var original=c.Session.Document; var undo=c.Session.UndoCount; c.SelectTool(InkTool.PointEraser);
    w.MouseDown(new(180,80),MouseButton.Left); w.MouseMove(new(180,140),RawInputModifiers.LeftMouseButton);
    Check(c.Session.IsEditing); w.Content=null; Dispatcher.UIThread.RunJobs();
    Check(!c.Session.IsEditing && ReferenceEquals(original,c.Session.Document) && c.Session.UndoCount==undo);
    w.MouseUp(new(180,140),MouseButton.Left); w.Content=c; Dispatcher.UIThread.RunJobs();
    c.SelectTool(InkTool.Pen); Draw(w,200); Check(c.Session.Document.StrokeCount==2);
});
Test("Replacing a session mid-gesture cannot insert into either document", (c,w) =>
{
    var old=c.Session; var next=new InkSession();
    w.MouseDown(new(80,100),MouseButton.Left); w.MouseMove(new(280,100),RawInputModifiers.LeftMouseButton);
    c.Session=next; w.MouseUp(new(280,100),MouseButton.Left);
    Check(old.Document.StrokeCount==0 && next.Document.StrokeCount==0);
    Draw(w); Check(next.Document.StrokeCount==1 && old.Document.StrokeCount==0);
});
Test("External edits invalidate stale live input", (c,w) =>
{
    w.MouseDown(new(80,100),MouseButton.Left); w.MouseMove(new(280,100),RawInputModifiers.LeftMouseButton);
    c.Session.AddStroke(new InkStroke([new InkPoint(10,20)])); Dispatcher.UIThread.RunJobs();
    w.MouseUp(new(280,100),MouseButton.Left); Check(c.Session.Document.StrokeCount==1 && c.CancelledGestureCount>0);
});
Test("View-local read-only blocks input without locking the shared model", (c,w) =>
{
    c.IsReadOnly=true; Draw(w); Check(c.Session.Document.StrokeCount==0 && !c.Session.IsReadOnly);
    c.Session.AddStroke(new InkStroke([new InkPoint(10,20)])); Check(c.Session.Document.StrokeCount==1);
});
Test("Document read-only cancels live partial erasure", (c,w) =>
{
    Draw(w); var original=c.Session.Document; c.SelectTool(InkTool.PointEraser);
    w.MouseDown(new(180,80),MouseButton.Left); w.MouseMove(new(180,140),RawInputModifiers.LeftMouseButton);
    c.Session.IsReadOnly=true; Dispatcher.UIThread.RunJobs(); w.MouseUp(new(180,140),MouseButton.Left);
    Check(ReferenceEquals(original,c.Session.Document) && !c.Session.IsEditing && !c.UndoCommand.CanExecute(null));
});
Test("Locked layer refuses pointer ink", (c,w) =>
{
    c.Session.UpdateLayer(c.Session.ActiveLayerId,locked:true); Draw(w); Check(c.Session.Document.StrokeCount==0);
});
Test("Straightedge projects each real sample", (c,w) =>
{
    c.RulerEnabled=true; c.RulerOrigin=new(0,240); c.RulerAngle=0; Draw(w);
    Check(c.Session.Document.StrokeCount==1); foreach(var point in c.Session.Document.AllStrokes.Single().Points) Near(point.Y,240);
});
Test("Pointer-anchored zoom is invertible", (c,w) =>
{
    var anchor=new Point(160,140); var before=c.ViewToDocument(anchor); c.ZoomAt(anchor,1.8);
    var after=c.ViewToDocument(anchor); Near(before.X,after.X); Near(before.Y,after.Y);
});
Test("One-finger pan does not create ink", (c,w) =>
{
    using var touch=w.TouchBegin(new(100,100)); w.TouchMove(touch,new(160,130)); w.TouchEnd(touch,new(160,130));
    Check(c.Session.Document.StrokeCount==0); Near(c.PanOffset.X,60); Near(c.PanOffset.Y,30);
});
Test("Opt-in touch drawing commits editable ink", (c,w) =>
{
    c.AcceptsTouchInk=true; using var touch=w.TouchBegin(new(100,100)); w.TouchMove(touch,new(260,140)); w.TouchEnd(touch,new(280,150));
    Check(c.Session.Document.StrokeCount==1 && c.LastPointerKind=="Touch");
});
Test("Second touch cancels provisional ink and starts pinch", (c,w) =>
{
    c.AcceptsTouchInk=true; using var a=w.TouchBegin(new(150,150)); w.TouchMove(a,new(160,160));
    using var b=w.TouchBegin(new(280,160)); w.TouchMove(b,new(340,160));
    w.TouchEnd(b,new(340,160)); w.TouchEnd(a,new(160,160));
    Check(c.Session.Document.StrokeCount==0 && c.Zoom>1.4);
});
Test("Invalid styled values are rejected", (c,w) =>
{
    var rejected=0;
    try { c.BrushSize=double.NaN; } catch(ArgumentException) { rejected++; }
    try { c.Zoom=0; } catch(ArgumentException) { rejected++; }
    try { c.Smoothing=3; } catch(ArgumentException) { rejected++; }
    Check(rejected==3);
});
var report=new { total=results.Count, passed=results.Count(r=>r.passed), failed=results.Count(r=>!r.passed), tests=results };
var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
if(args.Length>0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); File.WriteAllText(args[0],json); }
Console.WriteLine($"HEADLESS SUMMARY: {report.passed}/{report.total}; {report.failed} failed");
return report.failed==0?0:1;

internal sealed record Result(string name,bool passed,string detail);
internal sealed class SpecApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); Styles.Add(new InkTheme()); }
}
