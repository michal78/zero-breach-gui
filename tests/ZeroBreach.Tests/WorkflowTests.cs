using Xunit;
using ZeroBreach.Core.Scanning;
namespace ZeroBreach.Tests;
public class WorkflowTests
{
    [Fact] public void StartupDoesNotScanAndDuplicateStartIsBlocked() {
        var flow = new ScanWorkflow(); Assert.Equal(ScanState.Ready,flow.State);
        Assert.True(flow.TryStart()); Assert.False(flow.TryStart()); Assert.Equal(ScanState.Updating,flow.State);
    }
    [Fact] public void CancelledRunCannotBecomeCompleted() {
        var flow = new ScanWorkflow(); flow.TryStart(); flow.Cancel();
        Assert.False(flow.Complete()); Assert.Equal(ScanState.Cancelled,flow.State);
        Assert.True(flow.TryStart()); Assert.Equal(ScanState.Updating,flow.State);
    }
}
