using System.Security.Cryptography;
using ZeroBreach.Core.Reports;
using ZeroBreach.Core.Scanning;
using ZeroBreach.Core.Updates;

// Explicit, opt-in online integration check. No raw personal reports are printed or retained.
if (args.Length == 3 && args[0] == "--parse-report") {
    var fixture = ReportReader.Parse(await File.ReadAllTextAsync(args[1]),int.Parse(args[2]));
    if (!ReportExporter.ToText(fixture).Contains("WHAT COULD NOT BE CHECKED")) throw new Exception("Export failed.");
    Console.WriteLine($"PASS: upstream report parsed and exported; findings={fixture.Findings.Count}; gaps={fixture.Gaps.Count}.");
    return;
}
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".tools/smoke/ZeroBreach-data");
var quick = !args.Contains("--full");
using var workspace = PortableWorkspace.Acquire(root);
using var input = typeof(ScanRunner).Assembly.GetManifestResourceStream("python.zip")!;
using var memory = new MemoryStream(); input.CopyTo(memory);
var runtime = RuntimeManager.Ensure(memory.ToArray(),"97DAE5274CC54867065E8D5A3226E48C35017ED332A0FDB0E27D5B5821961297",root);
using var client = new ReleaseClient();
var cache = new VerifiedCache(Path.Combine(root,"scanners"));
var progress = new Progress<string>(s => { if (!s.Contains("profile:",StringComparison.OrdinalIgnoreCase)) Console.WriteLine(s); });
var scanner = await cache.UpdateAsync(client,progress,CancellationToken.None);
var runner = new ScanRunner(runtime,root,cache);
var report = await runner.RunAsync(scanner,quick,progress,CancellationToken.None);
if (report.Version != scanner.Tag[1..] || report.Coverage.Count == 0) throw new Exception("Invalid integration result.");
var text = ReportExporter.ToText(report);
if (!text.Contains("WHAT COULD NOT BE CHECKED")) throw new Exception("Coverage gap export missing.");
if (Directory.GetDirectories(root,"run-*").Length != 0 || runner.CleanupWarning != null) throw new Exception("Temporary report cleanup failed.");
Console.WriteLine($"PASS: {scanner.Tag}; mode={(quick ? "quick" : "full")}; {report.Findings.Count} finding(s); {report.Gaps.Count} gap(s); report parsed/exported in memory; temporary report cleaned.");
using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
try { await runner.RunAsync(scanner,false,progress,cancelled.Token); throw new Exception("Cancelled scan unexpectedly completed."); }
catch (OperationCanceledException) { Console.WriteLine("PASS: scan cancellation and process termination."); }
if (Directory.GetDirectories(root,"run-*").Length != 0) throw new Exception("Cancelled run files were not cleaned.");
var saved = await cache.OpenSavedAsync(CancellationToken.None);
if (saved.Tag != scanner.Tag) throw new Exception("Saved version changed unexpectedly.");
Console.WriteLine("PASS: saved version signature revalidation.");
