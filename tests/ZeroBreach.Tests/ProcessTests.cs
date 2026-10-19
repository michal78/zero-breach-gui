using Xunit;
using ZeroBreach.Core.Scanning;

namespace ZeroBreach.Tests;
public class ProcessTests
{
    [Fact] public async Task CancellationKillsChildProcess() {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync("powershell.exe",["-NoProfile","-Command","Start-Sleep -Seconds 60"],Path.GetTempPath(),new Progress<string>(),cts.Token));
    }
    [Fact] public async Task UnicodeArgumentsAndExitCodeArePreserved() {
        var r = await ProcessRunner.RunAsync("powershell.exe",["-NoProfile","-Command","[Console]::OutputEncoding=[Text.Encoding]::UTF8; Write-Output 'æ ø test'; exit 2"],Path.GetTempPath(),new Progress<string>(),CancellationToken.None);
        Assert.Equal(2,r.ExitCode); Assert.Contains("æ ø test",r.Output);
    }
    [Fact] public async Task LargeOutputIsDrainedButBounded() {
        var r = await ProcessRunner.RunAsync("powershell.exe",["-NoProfile","-Command","1..20000 | ForEach-Object { 'diagnostic line' }"],Path.GetTempPath(),new Progress<string>(),CancellationToken.None);
        Assert.True(r.Output.Length <= 65536); Assert.Equal(0,r.ExitCode);
    }
    [Fact] public async Task ScannerTemporaryDirectoryIsIsolatedWithoutChangingHashSearchRoots() {
        var r = await ProcessRunner.RunAsync("powershell.exe",["-NoProfile","-Command","Write-Output $env:TMPDIR; Write-Output $env:TEMP"],Path.GetTempPath(),new Progress<string>(),CancellationToken.None,new Dictionary<string,string>{{"TMPDIR","C:\\Test run"}});
        Assert.Contains("C:\\Test run",r.Output);
        Assert.Contains(Environment.GetEnvironmentVariable("TEMP")!,r.Output);
    }
}
