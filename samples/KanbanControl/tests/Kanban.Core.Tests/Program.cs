using System.Collections.ObjectModel;
using System.Diagnostics;
using Kanban.Core;

int assertions = 0, specs = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
void Spec(string name, Action body) { body(); specs++; Console.WriteLine($"PASS {name}"); }
string Order(KanbanController c, string col, string lane = "") => string.Join(",", c.GetCell(col, lane).Select(e => e.Id));
( KanbanController c, ObservableCollection<KanbanItem> items, ObservableCollection<KanbanColumnDefinition> cols ) Fixture()
{
    var items = new ObservableCollection<KanbanItem> {
        new() { Id = "a", Title = "Alpha", ColumnKey = "todo", Order = 0 },
        new() { Id = "b", Title = "Bravo", ColumnKey = "todo", Order = 1 },
        new() { Id = "c", Title = "Charlie", ColumnKey = "doing", Order = 0 },
        new() { Id = "d", Title = "Delta", ColumnKey = "doing", Order = 1 } };
    var cols = new ObservableCollection<KanbanColumnDefinition> { new() { Key = "todo", Header = "To do" }, new() { Key = "doing", Header = "Doing", MaximumWorkInProgress = 3 }, new() { Key = "done", Header = "Done" } };
    var c = new KanbanController(); c.Bind(items, cols); return (c, items, cols);
}
Spec("index preserves source identity and source order", () => { var (c,i,_) = Fixture(); using(c) { Check(c.Entries.Count == 4,"count"); Check(ReferenceEquals(c.Find("a")!.Item,i[0]),"identity"); Check(Order(c,"todo") == "a,b","order"); } });
Spec("move, undo and redo restore exact placement", () => { var (c,i,_) = Fixture(); using(c) { Check(c.TryMove(new(["a"],"doing",BeforeId:"d")).Succeeded,"move"); Check(Order(c,"doing") == "c,a,d","insert"); Check(i[0].Id == "a","source collection untouched"); Check(c.Undo().Succeeded,"undo"); Check(Order(c,"todo") == "a,b" && Order(c,"doing") == "c,d","restore"); Check(c.Redo().Succeeded && Order(c,"doing") == "c,a,d","redo"); } });
Spec("atomic multi-select move and undo", () => { var (c,_,cols) = Fixture(); using(c) { cols[1].MaximumWorkInProgress=4; Check(c.TryMove(new(["b","a"],"doing",BeforeId:"c")).Succeeded,"multi"); Check(Order(c,"doing")=="b,a,c,d","selection order"); Check(c.UndoCount==1,"single transaction"); Check(c.Undo().Succeeded && Order(c,"todo")=="a,b","undo batch"); } });
Spec("WIP rejects whole batch without source changes", () => { var(c,i,_)=Fixture();using(c){var before=i.Select(x=>x.ColumnKey).ToArray();Check(c.TryMove(new(["a","b"],"doing")).Failure==KanbanMoveFailure.WipLimit,"limit");Check(before.SequenceEqual(i.Select(x=>x.ColumnKey)),"atomic reject");Check(c.UndoCount==0,"no history");} });
Spec("WIP warn and ignore are explicit", () => {var(c,_,_)=Fixture();using(c){c.WipLimitMode=WipLimitMode.Warn;var r=c.TryMove(new(["a","b"],"doing"));Check(r.Succeeded&&r.IsWarning,"warning");Check(c.Undo().Succeeded,"undo");c.WipLimitMode=WipLimitMode.Ignore;r=c.TryMove(new(["a","b"],"doing"));Check(r.Succeeded&&!r.IsWarning,"ignore");}});
Spec("reordering permitted at and above WIP limit",()=>{var(c,_,cols)=Fixture();using(c){cols[1].MaximumWorkInProgress=0;Check(c.TryMove(new(["d"],"doing",BeforeId:"c")).Succeeded,"reorder");Check(Order(c,"doing")=="d,c","order");}});
Spec("same-cell insertion uses post-removal anchors",()=>{var(c,_,_)=Fixture();using(c){Check(c.TryMove(new(["a"],"todo")).Succeeded,"append");Check(Order(c,"todo")=="b,a","no off by one");Check(c.TryMove(new(["a"],"todo",BeforeId:"b")).Succeeded,"before");Check(Order(c,"todo")=="a,b","restore");}});
Spec("self anchor, foreign anchor and duplicate IDs rejected",()=>{var(c,_,_)=Fixture();using(c){Check(c.TryMove(new(["a"],"todo",BeforeId:"a")).Failure==KanbanMoveFailure.InvalidAnchor,"self");Check(c.TryMove(new(["a"],"done",BeforeId:"c")).Failure==KanbanMoveFailure.InvalidAnchor,"foreign");Check(c.TryMove(new(["a","a"],"done")).Failure==KanbanMoveFailure.InvalidData,"duplicate");Check(c.TryMove(new([""],"done")).Failure==KanbanMoveFailure.InvalidData,"empty id");}});
Spec("unknown items, targets and empty moves rejected",()=>{var(c,_,_)=Fixture();using(c){Check(c.TryMove(new(["missing"],"done")).Failure==KanbanMoveFailure.UnknownItem,"item");Check(c.TryMove(new(["a"],"missing")).Failure==KanbanMoveFailure.UnknownTarget,"target");Check(c.TryMove(new([],"done")).Failure==KanbanMoveFailure.EmptySelection,"empty");}});
Spec("board read-only blocks moves and history",()=>{var(c,_,_)=Fixture();using(c){c.TryMove(new(["a"],"done"));c.IsReadOnly=true;Check(c.TryMove(new(["b"],"done")).Failure==KanbanMoveFailure.ReadOnly,"move");Check(c.Undo().Failure==KanbanMoveFailure.ReadOnly&&!c.CanUndo,"history");}});
Spec("locked card prevents entire batch",()=>{var(c,i,_)=Fixture();using(c){i[1].IsLocked=true;Check(c.TryMove(new(["a","b"],"done")).Failure==KanbanMoveFailure.Locked,"locked");Check(Order(c,"todo")=="a,b","all unchanged");}});
Spec("source and destination column read-only respected",()=>{var(c,_,cols)=Fixture();using(c){cols[0].IsReadOnly=true;Check(c.TryMove(new(["a"],"done")).Failure==KanbanMoveFailure.ReadOnly,"source");cols[0].IsReadOnly=false;cols[2].IsReadOnly=true;Check(c.TryMove(new(["a"],"done")).Failure==KanbanMoveFailure.ReadOnly,"destination");cols[2].IsReadOnly=false;cols[2].AllowDrop=false;Check(!c.TryMove(new(["a"],"done")).Succeeded,"allowdrop");}});
Spec("cancellable preview has zero side effects",()=>{var(c,_,_)=Fixture();using(c){c.Moving+=(_,e)=>{e.Cancel=true;e.Reason="Approval required";};var r=c.TryMove(new(["a"],"done"));Check(r.Failure==KanbanMoveFailure.Canceled&&r.Message=="Approval required","cancel");Check(Order(c,"todo")=="a,b"&&c.UndoCount==0,"unchanged");}});
Spec("custom policy rejects transitions",()=>{var(c,_,_)=Fixture();using(c){c.MovePolicy=_=>KanbanMoveResult.Reject(KanbanMoveFailure.Policy,"Custom workflow");Check(c.TryMove(new(["a"],"done")).Failure==KanbanMoveFailure.Policy,"policy");}});
Spec("stale revisions reject after metadata change",()=>{var(c,i,_)=Fixture();using(c){long r=c.Revision;i[0].Title="Changed";Check(c.TryMove(new(["a"],"done",ExpectedRevision:r)).Failure==KanbanMoveFailure.StaleRevision,"stale");}});
Spec("preview side effects invalidate operation",()=>{var(c,i,_)=Fixture();using(c){c.Moving+=(_,_)=>i[1].Title="Changed";Check(c.TryMove(new(["a"],"done")).Failure==KanbanMoveFailure.StaleRevision,"preview stale");}});
Spec("policy side effects invalidate operation",()=>{var(c,i,_)=Fixture();using(c){c.MovePolicy=_=>{i[1].Title+="!";return KanbanMoveResult.Success();};Check(c.TryMove(new(["a"],"done")).Failure==KanbanMoveFailure.StaleRevision,"policy stale");}});
Spec("reentrant moves are rejected",()=>{var(c,_,_)=Fixture();using(c){KanbanMoveResult? inner=null;c.Moving+=(_,_)=>inner=c.TryMove(new(["b"],"done"));Check(c.TryMove(new(["a"],"done")).Succeeded,"outer");Check(inner?.Failure==KanbanMoveFailure.Busy,"inner");}});
Spec("caller cannot mutate captured selection in preview",()=>{var(c,_,_)=Fixture();using(c){var ids=new List<string>{"a"};c.Moving+=(_,_)=>ids[0]="b";Check(c.TryMove(new(ids,"done")).Succeeded,"move");Check(Order(c,"done")=="a","snapshot");}});
Spec("no-op does not create history",()=>{var(c,_,_)=Fixture();using(c){Check(c.TryMove(new(["b"],"todo")).Succeeded,"no-op");Check(c.UndoCount==0,"no history");}});
Spec("history is bounded and redo invalidated by new move",()=>{var(c,_,_)=Fixture();using(c){c.HistoryLimit=2;c.TryMove(new(["a"],"done"));c.TryMove(new(["b"],"done"));c.TryMove(new(["c"],"done"));Check(c.UndoCount==2,"bounded");c.Undo();Check(c.CanRedo,"redo");c.TryMove(new(["d"],"done"));Check(!c.CanRedo,"forked history");}});
Spec("history can be disabled",()=>{var(c,_,_)=Fixture();using(c){c.HistoryLimit=0;c.TryMove(new(["a"],"done"));Check(!c.CanUndo,"disabled");}});
Spec("content edits preserve history, external placements invalidate it",()=>{var(c,i,_)=Fixture();using(c){c.TryMove(new(["a"],"done"));i[0].Title="Edited";Check(c.CanUndo,"metadata retains");i[1].ColumnKey="done";Check(!c.CanUndo&&!c.CanRedo,"placement clears");}});
Spec("removed and replaced model identities clear history",()=>{var(c,i,_)=Fixture();using(c){c.TryMove(new(["a"],"done"));i[0]=new(){Id="a",ColumnKey="done"};Check(!c.CanUndo,"replacement");Check(ReferenceEquals(c.Find("a")!.Item,i[0]),"new identity");i.RemoveAt(0);Check(c.Find("a") is null,"removal");}});
Spec("live collection add and clear",()=>{var(c,i,_)=Fixture();using(c){i.Add(new(){Id="e",ColumnKey="done"});Check(c.Entries.Count==5&&c.GetColumnCount("done")==1,"add");i.Clear();Check(c.Entries.Count==0,"clear");}});
Spec("duplicate IDs and unmapped cards are diagnosed",()=>{var(c,i,_)=Fixture();using(c){i.Add(new(){Id="a",ColumnKey="missing"});Check(c.Diagnostics.Count>=2,"diagnostics");Check(c.TryMove(new(["b"],"done")).Failure==KanbanMoveFailure.InvalidData,"invalid source blocked");}});
Spec("duplicate column keys and default-lane mapping diagnosed",()=>{var(c,i,cols)=Fixture();using(c){cols.Add(new(){Key="todo"});Check(c.Diagnostics.Count>0,"duplicate columns");cols.RemoveAt(cols.Count-1);i[0].LaneKey="unknown";Check(c.Diagnostics.Count>0,"unknown lane");}});
Spec("column and lane notifications update index",()=>{var(c,_,cols)=Fixture();using(c){cols[0].Key="renamed";Check(c.Diagnostics.Count>0,"column renamed");cols[0].Key="todo";Check(c.Diagnostics.Count==0,"column restored");}});
Spec("swimlane transfer obeys board and lane rules",()=>{var(c,i,cols)=Fixture();using(c){var lanes=new ObservableCollection<KanbanLaneDefinition>{new(){Key=""},new(){Key="other"}};c.Bind(i,cols,lanes);c.AllowLaneChange=false;Check(c.TryMove(new(["a"],"todo","other")).Failure==KanbanMoveFailure.LaneChangeNotAllowed,"disallowed");c.AllowLaneChange=true;lanes[1].IsReadOnly=true;Check(c.TryMove(new(["a"],"todo","other")).Failure==KanbanMoveFailure.ReadOnly,"lane readonly");lanes[1].IsReadOnly=false;Check(c.TryMove(new(["a"],"todo","other")).Succeeded,"lane move");Check(Order(c,"todo","other")=="a","lane destination");}});
Spec("WIP counts across lanes, not filtered or per-cell counts",()=>{var(c,i,cols)=Fixture();using(c){var lanes=new[]{new KanbanLaneDefinition{Key=""},new KanbanLaneDefinition{Key="other"}};c.Bind(i,cols,lanes);cols[1].MaximumWorkInProgress=2;Check(c.TryMove(new(["a"],"doing","other")).Failure==KanbanMoveFailure.WipLimit,"global WIP");Check(c.TryMove(new(["c"],"doing","other")).Succeeded,"same column transfer");Check(c.GetColumnCount("doing")==2,"count unchanged");}});
Spec("undo cannot modify newly locked cards",()=>{var(c,i,_)=Fixture();using(c){c.TryMove(new(["a"],"done"));i[0].IsLocked=true;Check(c.Undo().Failure==KanbanMoveFailure.Locked,"lock");i[0].IsLocked=false;Check(c.Undo().Succeeded,"unlock");}});
Spec("partial throwing adapter rolls back all prior writes",()=>{var(c,i,cols)=Fixture();using(c){var adapter=new FaultAdapter();c.Bind(i,cols,adapter:adapter);var original=i.Select(x=>DefaultKanbanAdapter.Instance.GetPlacement(x)).ToArray();adapter.FailOn="b";Check(c.TryMove(new(["a","b"],"done")).Failure==KanbanMoveFailure.AdapterFailure,"failure");Check(original.SequenceEqual(i.Select(x=>DefaultKanbanAdapter.Instance.GetPlacement(x))),"rollback");Check(c.UndoCount==0,"no partial history");}});
Spec("non-default models work through adapter",()=>{using var c=new KanbanController();var objects=new[]{new Plain("x","todo",5),new Plain("y","todo",6)};c.Bind(objects,new[]{new KanbanColumnDefinition{Key="todo"},new KanbanColumnDefinition{Key="done"}},adapter:new PlainAdapter());Check(c.TryMove(new(["x"],"done")).Succeeded&&objects[0].Column=="done","custom");Check(c.Undo().Succeeded&&objects[0].Column=="todo"&&objects[0].Order==5,"restore");});
Spec("unnotified external change is detected before undo",()=>{using var c=new KanbanController();var p=new Plain("x","todo",0);c.Bind(new[]{p},new[]{new KanbanColumnDefinition{Key="todo"},new KanbanColumnDefinition{Key="done"}},adapter:new PlainAdapter());c.TryMove(new(["x"],"done"));p.Order=99;Check(c.Undo().Failure==KanbanMoveFailure.HistoryConflict,"conflict");Check(p.Order==99,"not clobbered");});
Spec("batch refresh is nested and idempotent",()=>{var(c,i,_)=Fixture();using(c){int changes=0;c.Changed+=(_,_)=>changes++;var outer=c.DeferRefresh();using(c.DeferRefresh()){i[0].Title="one";i[1].Title="two";Check(changes==0,"deferred");}Check(changes==0,"outer");outer.Dispose();outer.Dispose();Check(changes==1,"once");}});
Spec("rebind and dispose unsubscribe old sources",()=>{var(c,i,cols)=Fixture();c.Bind(new[]{new KanbanItem{Id="new",ColumnKey="todo"}},cols);long rev=c.Revision;i[0].Title="old";Check(c.Revision==rev,"old source detached");c.Dispose();cols[0].Header="after dispose";i.Clear();Check(c.Entries.Count==0,"disposed empty");});
Spec("model value constraints reject nonfinite sizes and invalid counts",()=>{var w=new KanbanItem();bool order=false,width=false,tasks=false,wip=false;try{w.Order=double.NaN;}catch(ArgumentOutOfRangeException){order=true;}try{new KanbanColumnDefinition().Width=double.PositiveInfinity;}catch(ArgumentOutOfRangeException){width=true;}try{w.TotalTasks=-1;}catch(ArgumentOutOfRangeException){tasks=true;}try{new KanbanColumnDefinition().MaximumWorkInProgress=-1;}catch(ArgumentOutOfRangeException){wip=true;}Check(order&&width&&tasks&&wip,"constraints");w.TotalTasks=3;w.CompletedTasks=5;Check(w.Progress==100,"clamp");});
Spec("10,000-card batch indexes and moves without duplicate loss",()=>{var watch=Stopwatch.StartNew();using var c=new KanbanController();var items=Enumerable.Range(0,10_000).Select(i=>new KanbanItem{Id=$"i{i}",ColumnKey="todo",Order=i}).ToArray();c.Bind(items,new[]{new KanbanColumnDefinition{Key="todo"},new KanbanColumnDefinition{Key="done"}});Check(c.Entries.Count==10_000,"index");Check(c.TryMove(new(["i9999","i5000"],"done")).Succeeded,"move");Check(c.GetCell("todo").Count==9998&&Order(c,"done")=="i9999,i5000","no loss");Check(c.Undo().Succeeded&&c.GetCell("todo").Count==10000,"restore");Console.WriteLine($"10k index + move + undo: {watch.Elapsed.TotalMilliseconds:F1} ms (this machine)");});
Console.WriteLine($"RESULT: {specs} specifications; {assertions} assertions passed.");

sealed class FaultAdapter : IKanbanAdapter
{
    public string? FailOn {get;set;}
    public string GetId(object i)=>DefaultKanbanAdapter.Instance.GetId(i);
    public string GetTitle(object i)=>DefaultKanbanAdapter.Instance.GetTitle(i);
    public string GetSearchText(object i)=>GetTitle(i);
    public KanbanPlacement GetPlacement(object i)=>DefaultKanbanAdapter.Instance.GetPlacement(i);
    public bool CanMove(object i)=>true;
    public void SetPlacement(object i,KanbanPlacement p){DefaultKanbanAdapter.Instance.SetPlacement(i,p);if(GetId(i)==FailOn){FailOn=null;throw new InvalidOperationException("Injected post-write failure");}}
}
sealed class Plain(string id,string column,double order) { public string Id=id;public string Column=column;public double Order=order; }
sealed class PlainAdapter : IKanbanAdapter
{
    public string GetId(object i)=>((Plain)i).Id;
    public string GetTitle(object i)=>GetId(i);
    public string GetSearchText(object i)=>GetId(i);
    public KanbanPlacement GetPlacement(object i)=>new(((Plain)i).Column,"",((Plain)i).Order);
    public bool CanMove(object i)=>true;
    public void SetPlacement(object i,KanbanPlacement p){((Plain)i).Column=p.ColumnKey;((Plain)i).Order=p.Order;}
}
