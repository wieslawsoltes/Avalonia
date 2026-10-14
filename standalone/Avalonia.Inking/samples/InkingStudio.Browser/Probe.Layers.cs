using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Inking;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace InkingStudio.Browser;

public static partial class Probe
{
    private static Button? _observedAdd;
    private static int _addPresses,_addReleases,_addClicks;
    /// <summary>Sample diagnostics only: observe real template bindings and routed button input.</summary>
    [JSExport] public static string LayerDiagnostics() => Json(w=>
    {
        var panel=View.FindControl<InkLayerPanel>("LayerPanel");
        var button=View.GetVisualDescendants().OfType<Button>().FirstOrDefault(c=>AutomationProperties.GetAutomationId(c)=="add-layer");
        if(button is not null && !ReferenceEquals(button,_observedAdd))
        {
            _observedAdd=button; _addPresses=_addReleases=_addClicks=0;
            button.AddHandler(InputElement.PointerPressedEvent,(_,_)=>_addPresses++,RoutingStrategies.Tunnel,true);
            button.AddHandler(InputElement.PointerReleasedEvent,(_,_)=>_addReleases++,RoutingStrategies.Tunnel,true);
            button.Click+=(_,_)=>_addClicks++;
        }
        w.WriteStartObject(); w.WriteBoolean("panelFound",panel is not null); w.WriteBoolean("buttonFound",button is not null);
        w.WriteBoolean("sameSession",ReferenceEquals(panel?.Session,View.Session));
        w.WriteBoolean("panelSessionNull",panel?.Session is null);
        w.WriteNumber("panelLayers",panel?.Layers.Count??-1); w.WriteNumber("documentLayers",View.Session.Document.Layers.Length);
        w.WriteBoolean("panelCanAdd",panel?.AddLayerCommand.CanExecute(null)??false);
        w.WriteBoolean("buttonCommandMatches",ReferenceEquals(button?.Command,panel?.AddLayerCommand));
        w.WriteBoolean("buttonCommandNull",button?.Command is null);
        w.WriteBoolean("buttonEnabled",button?.IsEffectivelyEnabled??false);
        w.WriteBoolean("buttonCanExecute",button?.Command?.CanExecute(button.CommandParameter)??false);
        w.WriteNumber("presses",_addPresses); w.WriteNumber("releases",_addReleases); w.WriteNumber("clicks",_addClicks);
        if(button?.TranslatePoint(default,View) is {} origin)
        {
            var center=new Point(origin.X+button.Bounds.Width/2,origin.Y+button.Bounds.Height/2);
            w.WritePropertyName("bounds"); WriteRect(w,new Rect(origin,button.Bounds.Size));
            w.WriteStartArray("hitPath");
            if(View.InputHitTest(center) is Visual hit)
            {
                for(Visual? v=hit;v is not null;v=v.GetVisualParent())
                    w.WriteStringValue(v.GetType().Name+":"+(v as Control)?.Name+":"+(v is Control c?AutomationProperties.GetAutomationId(c):""));
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    });
}
