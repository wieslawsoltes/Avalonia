namespace InkingStudio;

/// <summary>Recovery provenance is distinct from a subsequent successful recovery write.</summary>
public sealed class RecoveryTracker
{
    public bool WasRecovered { get; private set; }
    public string Status { get; private set; } = "Recovery ready";
    public void Reset() { WasRecovered=false; Status="Recovery ready"; }
    public void Restored() { WasRecovered=true; Status="Recovered local draft"; }
    public void Saved() => Status=WasRecovered?"Recovered draft · recovery saved":"Recovery saved";
    public void Failed() => Status=WasRecovered?"Recovered draft · recovery unavailable":"Recovery unavailable";
}
