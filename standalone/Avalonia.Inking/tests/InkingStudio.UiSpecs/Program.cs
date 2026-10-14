using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Inking;
using Avalonia.Inking.Core;
using Avalonia.Inking.Themes;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using InkingStudio;

AppBuilder.Configure<UiApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var view=new StudioView();
var window=new Window { Width=1440,Height=1000,Content=view };
window.Show();Dispatcher.UIThread.RunJobs();
var results=new List<Outcome>();
void Check(bool value,string detail="Assertion failed") { if(!value) throw new Exception(detail); }
Control Find(string id)=>view.GetVisualDescendants().OfType<Control>().First(c=>AutomationProperties.GetAutomationId(c)==id);
void Click(string id)
{
    var control=Find(id);control.BringIntoView();Dispatcher.UIThread.RunJobs();
    var point=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)??throw new Exception("Detached control");
    window.MouseMove(point);window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);Dispatcher.UIThread.RunJobs();
}
void Test(string name,Action action)
{
    try { view.ResetForValidation();Dispatcher.UIThread.RunJobs();action();results.Add(new(name,true,""));Console.WriteLine("PASS "+name); }
    catch(Exception e) { results.Add(new(name,false,e.ToString()));Console.WriteLine("FAIL "+name+": "+e); }
}
Test("Sample layer panel observes the canvas session",()=>
{
    var panel=view.FindControl<InkLayerPanel>("LayerPanel")!;
    Check(ReferenceEquals(panel.Session,view.Session),"Layer panel is bound to the wrong session");
    Check(panel.Layers.Count==1 && panel.AddLayerCommand.CanExecute(null));
});
Test("Sample add-layer button receives its public command",()=>
{
    var panel=view.FindControl<InkLayerPanel>("LayerPanel")!;var button=(Button)Find("add-layer");
    Check(ReferenceEquals(button.Command,panel.AddLayerCommand),"Template command binding is unresolved");
    Check(button.IsEffectivelyEnabled && button.Command!.CanExecute(button.CommandParameter),"Add-layer button is disabled");
});
Test("Pointer click adds exactly one layer and selects it",()=>
{
    var old=view.Session.ActiveLayerId;Click("add-layer");
    Check(view.Session.Document.Layers.Length==2,"Add-layer pointer click did not execute");
    Check(view.Session.ActiveLayerId!=old && view.Session.UndoCount==1);
});
Test("Layer lock and visibility buttons target the active layer",()=>
{
    Click("add-layer");Click("layer-lock");Check(view.Session.ActiveLayer.IsLocked);
    Click("layer-lock");Check(!view.Session.ActiveLayer.IsLocked);
    Click("layer-visibility");Check(!view.Session.ActiveLayer.IsVisible);
    Click("layer-visibility");Check(view.Session.ActiveLayer.IsVisible);
});
Test("Layer order and removal buttons modify the same document",()=>
{
    Click("add-layer");var id=view.Session.ActiveLayerId;Click("layer-down");Check(view.Session.Document.Layers[0].Id==id);
    Click("layer-up");Check(view.Session.Document.Layers[^1].Id==id);Click("layer-remove");Check(view.Session.Document.Layers.Length==1);
});
Test("Sample undo removes an added layer without stale panel items",()=>
{
    Click("add-layer");Click("undo");Check(view.Session.Document.Layers.Length==1);
    Check(view.FindControl<InkLayerPanel>("LayerPanel")!.Layers.Count==1);
});
Test("Read-only sample disables the layer mutation command",()=>
{
    Click("read-only");Check(view.Session.IsReadOnly);
    Check(!view.FindControl<InkLayerPanel>("LayerPanel")!.AddLayerCommand.CanExecute(null));
});
Test("Recovery provenance survives subsequent draft writes and failures",()=>
{
    var tracker=new RecoveryTracker();tracker.Restored();tracker.Saved();Check(tracker.WasRecovered && tracker.Status.Contains("Recovered"));
    tracker.Failed();Check(tracker.WasRecovered && tracker.Status.Contains("unavailable"));tracker.Reset();Check(!tracker.WasRecovered);
});
window.Close();Dispatcher.UIThread.RunJobs();
var report=new { total=results.Count,passed=results.Count(r=>r.passed),failed=results.Count(r=>!r.passed),tests=results };
if(args.Length>0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true})); }
Console.WriteLine($"STUDIO UI SUMMARY: {report.passed}/{report.total}; {report.failed} failed");
return report.failed==0?0:1;
internal sealed record Outcome(string name,bool passed,string detail);
internal sealed class UiApp:Application
{
    public override void Initialize() { Styles.Add(new FluentTheme());Styles.Add(new InkTheme()); }
}
