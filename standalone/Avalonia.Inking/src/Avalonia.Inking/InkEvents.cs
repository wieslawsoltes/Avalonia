using Avalonia.Inking.Core;
using Avalonia.Interactivity;
namespace Avalonia.Inking;
public sealed class InkStrokeEventArgs(RoutedEvent routedEvent, InkStroke stroke) : RoutedEventArgs(routedEvent)
{
    public InkStroke Stroke { get; } = stroke;
    public bool Cancel { get; set; }
}
public sealed class InkDocumentEventArgs(RoutedEvent routedEvent, InkDocument document, string reason) : RoutedEventArgs(routedEvent)
{
    public InkDocument Document { get; } = document;
    public string Reason { get; } = reason;
}
public sealed class InkOperationFailedEventArgs(string operation, Exception error) : EventArgs
{
    public string Operation { get; } = operation;
    public Exception Error { get; } = error;
}
