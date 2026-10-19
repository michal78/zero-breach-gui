using ZeroBreach.Core.Reports;
using ZeroBreach.Core.Updates;

namespace ZeroBreach.Core.Scanning;
public sealed class ScanRunner(string python, string root, VerifiedCache cache)
{
    public string Diagnostics { get; private set; } = "";
    public string? CleanupWarning { get; private set; }
    public async Task<ScanReport> RunAsync(VerifiedScanner scanner, bool quick, IProgress<string> progress, CancellationToken token)
    {
        Diagnostics = ""; CleanupWarning = null;
        var folder = Path.Combine(root,"run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            PortableWorkspace.RejectReparse(folder);
            var bytes = await cache.ReadVerifiedBytesAsync(scanner,token);
            var script = Path.Combine(folder,"zero-breach.py"); var report = Path.Combine(folder,"report.json");
            await File.WriteAllBytesAsync(script,bytes,token);
            // Keep the authenticated snapshot immutable while the interpreter reads it.
            using var guard = new FileStream(script,FileMode.Open,FileAccess.Read,FileShare.Read);
            var args = new List<string> { "-I", "-S", "-B", "-u", "-X", "utf8", script, "--json", report };
            if (quick) args.Add("--quick");
            progress.Report("Scanning this Windows account…");
            // TMPDIR confines upstream database snapshots to this run without changing
            // TEMP/TMP, which are also part of the scanner's file-fingerprint search.
            var result = await ProcessRunner.RunAsync(python,args,folder,progress,token,new Dictionary<string,string> { ["TMPDIR"] = folder });
            Diagnostics = result.Output + "\n" + result.Error;
            if (result.ExitCode is not (0 or 2)) throw new InvalidDataException("The scanner could not complete this run. Review diagnostic details or try again.");
            if (!File.Exists(report) || new FileInfo(report).Length > 32*1024*1024) throw new InvalidDataException("The scanner did not produce a valid report. No completed result can be shown.");
            var parsed = ReportReader.Parse(await File.ReadAllTextAsync(report,token),result.ExitCode);
            if (parsed.Version != scanner.Tag.TrimStart('v')) throw new InvalidDataException("The scanner version does not match the selected release.");
            return parsed with { ScanMode = quick ? "Quick scan — file fingerprints skipped" : "Standard scan" };
        }
        finally {
            try { Directory.Delete(folder,true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CleanupWarning = "Temporary scan files could not be removed: " + folder; progress.Report(CleanupWarning); }
        }
    }
}
