using System.Diagnostics;
using System.Text;
namespace ZeroBreach.Core.Scanning;
public record ProcessResult(int ExitCode,string Output,string Error);
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args, string cwd, IProgress<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = cwd,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Remove("PYTHONPATH"); start.Environment.Remove("PYTHONHOME");
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("The local scanner process could not be started.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = Drain(process.StandardOutput,progress); var error = Drain(process.StandardError,progress);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(output,error); throw; }
        var both = await Task.WhenAll(output,error); token.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode,both[0],both[1]);
    }
    private static async Task<string> Drain(StreamReader stream,IProgress<string> progress)
    {
        var capture = new StringBuilder(); var line = new StringBuilder(); var buffer = new char[2048]; int n;
        while ((n = await stream.ReadAsync(buffer)) > 0) {
            if (capture.Length < 65536) capture.Append(buffer,0,Math.Min(n,65536-capture.Length));
            for (int i=0;i<n;i++) {
                if (buffer[i] == '\n') {
                    var s = line.ToString().Trim(); line.Clear();
                    if (s.StartsWith("[*] running check:") || s.StartsWith("[*]   ") || s.StartsWith("[*] skipping")) progress.Report(s.Replace("[*]","").Trim());
                } else if (line.Length < 2048) line.Append(buffer[i]);
            }
        }
        return capture.ToString();
    }
}
