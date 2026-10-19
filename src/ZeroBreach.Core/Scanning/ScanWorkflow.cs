namespace ZeroBreach.Core.Scanning;
public enum ScanState { Ready, Updating, Scanning, Completed, Cancelled, Failed }
public sealed class ScanWorkflow
{
    public ScanState State { get; private set; } = ScanState.Ready;
    public bool IsRunning => State is ScanState.Updating or ScanState.Scanning;
    public bool TryStart() { if (IsRunning) return false; State = ScanState.Updating; return true; }
    public void Scan() { if (State != ScanState.Updating) throw new InvalidOperationException(); State = ScanState.Scanning; }
    public void Cancel() { if (IsRunning) State = ScanState.Cancelled; }
    public void Fail() { if (IsRunning) State = ScanState.Failed; }
    public bool Complete() { if (!IsRunning) return false; State = ScanState.Completed; return true; }
}
