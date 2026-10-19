using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZeroBreach.Core.Reports;

public record Finding(string Title, string Text, int Rank);
public record ScanReport(string OriginalJson, string Version, string Timestamp, string Title, string Summary,
    IReadOnlyList<Finding> Findings, IReadOnlyList<string> Coverage, IReadOnlyList<string> Gaps, string NextSteps)
{
    public string ScanMode { get; init; } = "Standard scan";
}

public static class ReportReader
{
    public static ScanReport Parse(string json, int exitCode)
    {
        try { return Read(json, exitCode); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InvalidDataException("The scanner report is missing, incompatible or inconsistent. No completed result can be shown.", ex); }
    }

    private static string S(JsonElement e, string key) => e.GetProperty(key).GetString() ?? throw new InvalidDataException($"Missing report field: {key}.");
    private static string O(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
    private static void Require(bool valid) { if (!valid) throw new InvalidDataException("The scanner report is incompatible or inconsistent. No completed result can be shown."); }

    private static ScanReport Read(string json, int exitCode)
    {
        Require(exitCode is 0 or 2 && json.Length <= 32 * 1024 * 1024);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = doc.RootElement;
        Require(S(root.GetProperty("tool"), "name") == "adint-scan");
        var version = S(root.GetProperty("tool"), "version");
        const string buildSuffix = " (single-file build)";
        if (version.EndsWith(buildSuffix,StringComparison.Ordinal)) version = version[..^buildSuffix.Length];
        Require(Version.TryParse(version, out var v) && v.Major == 0 && v.Minor == 1);
        var timestamp = S(root, "timestamp");
        Require(DateTime.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture, out _));
        var verdict = root.GetProperty("verdict");
        var gaps = verdict.GetProperty("gaps").EnumerateArray().Select(x => x.GetString() ?? throw new InvalidDataException("Invalid gap.")).ToList();
        var findings = new List<Finding>(); var coverage = new List<string>(); var indicators = new List<string>();
        int action = 0; int modules = 0;
        foreach (var module in root.GetProperty("modules").EnumerateObject())
        {
            modules++;
            var m = module.Value; var status = S(m, "status");
            Require(status is "CLEAN" or "PARTIAL" or "UNKNOWN" or "HITS");
            var count = m.GetProperty("checked").GetInt64(); var skipped = m.GetProperty("skipped").GetInt64(); Require(count >= 0 && skipped >= 0);
            var hits = m.GetProperty("hits").EnumerateArray().ToArray();
            var checks = m.GetProperty("checks").EnumerateArray().ToArray();
            var ran = checks.Count(c => c.GetProperty("ran").GetBoolean());
            var calculated = hits.Length > 0 ? "HITS" : ran == 0 ? "UNKNOWN" : ran == checks.Length ? "CLEAN" : "PARTIAL";
            Require(status == calculated);
            coverage.Add($"{module.Name}: {count:N0} examined; {skipped:N0} skipped. {status}." +
                "\n" + string.Join("\n", checks.Select(c => $"  {S(c, "name")}: {(c.GetProperty("ran").GetBoolean() ? "checked" : "not checked — " + S(c, "reason"))}")));
            if (m.TryGetProperty("notes",out var notes))
                foreach (var note in notes.EnumerateArray()) coverage.Add($"{module.Name}: {note.GetString()}");
            foreach (var c in checks.Where(c => !c.GetProperty("ran").GetBoolean()))
            { var gap = $"{module.Name}/{S(c,"name")}: {S(c,"reason")}"; if (!gaps.Contains(gap)) gaps.Add(gap); }
            if (status == "UNKNOWN" && checks.Length == 0) gaps.Add($"{module.Name}: nothing could be checked.");
            if (m.TryGetProperty("reach", out var reach) && reach.ValueKind == JsonValueKind.Object)
                foreach (var r in reach.EnumerateObject())
                {
                    var earliest = O(r.Value,"earliest");
                    var period = DateTime.TryParse(earliest,System.Globalization.CultureInfo.InvariantCulture,out var first) ? first.Date > new DateTime(2025,12,31) ? "This record does not reach the documented 2024–2025 campaign period." : first.Date > new DateTime(2024,1,1) ? "This record reaches only part of the documented 2024–2025 campaign period." : "The earliest record begins at or before the documented 2024–2025 campaign period. This does not establish complete coverage." : "No surviving evidence window was established for this record.";
                    coverage.Add($"{module.Name}/{r.Name}: earliest surviving evidence: {(earliest.Length == 0 ? "none recorded" : earliest)}\n{O(r.Value,"basis")}\n{O(r.Value,"note")}\n{period}");
                }
            foreach (var h in hits)
            {
                var indicator = S(h, "indicator"); Require(indicator.Length > 0); indicators.Add(indicator);
                var evidence = O(h, "evidence"); var tier = O(h, "tier");
                var (title, meaning, rank) = Explain(evidence, tier);
                var role = Role(tier);
                var text = $"{indicator}\n{meaning}\n{role}\n\nEvidence: {evidence} | Classification: {tier} | Confidence: {O(h,"confidence")}\nLocation: {S(h,"location")}\nDetail: {S(h,"detail")}\nProfile: {O(h,"profile")}\nObserved: {O(h,"observed")}\nSource: {O(h,"source")}";
                findings.Add(new Finding(title, text, rank));
                int a = evidence == "referenced" ? 0 : evidence == "on-disk" && tier == "vuln-driver" ? 1 : evidence is "on-disk" or "persistence" ? 3 :
                    (tier is "delivery" or "candiru-ip") && (evidence is "contacted" or "dns-record" or "attempted" or "configured") ? 1 : 2;
                action = Math.Max(action, a);
            }
        }
        Require(modules > 0 && verdict.GetProperty("hits_total").GetInt32() == findings.Count);
        var expected = findings.Count > 0 ? "INDICATORS_FOUND" : verdict.GetProperty("gaps").GetArrayLength() > 0 ? "NO_KNOWN_INDICATORS_WITH_GAPS" : "NO_KNOWN_INDICATORS";
        Require(S(verdict, "status") == expected && exitCode == (findings.Count > 0 ? 2 : 0));
        if (root.TryGetProperty("ioc_set", out var ioc) && ioc.TryGetProperty("stale", out var stale) && stale.GetBoolean())
            gaps.Add("The embedded indicator set is more than 30 days old. A current scanner may still contain historical indicators.");
        gaps.Add("Optional Candiru pattern scanning is not included. This scan does not inspect memory or replace a forensic examination.");
        string Clean(string text) => Defang(text, indicators);
        var next = action switch {
            3 => "Seek expert help promptly. Preserve the report and named files or registrations. Do not delete, move, run or attempt to clean them. Contact a qualified incident responder or your organisation's security team. Presence or configuration alone does not prove execution.",
            2 => "Ask a qualified security professional to interpret the evidence, dates and locations. Preserve the report and original records. Missing or unfamiliar classifications need review. Do not delete or clean the evidence.",
            1 => "Review the saved context, date and location. Advertising records alone do not show infection. A legitimate driver may have an ordinary explanation; ask trusted IT support about its installation history. Historically attributed IP addresses may have changed ownership. Keep the report and ask for help if the context is unclear.",
            _ => findings.Count > 0 ? "Review the saved context. A reference may come from an article or blocklist and does not establish contact. Keep the report; ask trusted IT support if the evidence is unexplained." : "Review what could not be checked. Keep Windows and browsers updated. If you have a credible reason to suspect targeting, seek qualified help regardless of this result."
        };
        next += "\n\nDo not visit or look up indicator addresses. Keep the [.] spelling when sharing evidence. Reports may contain usernames, local file paths and browsing details; share them securely.";
        return new ScanReport(json, version, timestamp, findings.Count == 0 ? "No known indicators found" : "Indicators found — review needed",
            findings.Count == 0 ? "No known indicators were found in the places that could be checked. This does not prove the computer is free of spyware. Read the coverage gaps below." : $"{findings.Count} indicator match(es). A match is evidence to investigate; it does not by itself prove spyware executed. The evidence type and its role determine the next steps.",
            findings.OrderBy(f => f.Rank).Select(f => f with { Text = Clean(f.Text) }).ToList(), coverage.Select(Clean).ToList(), gaps.Distinct().Select(Clean).ToList(), next);
    }

    public static string Defang(string text, IEnumerable<string> indicators)
    {
        foreach (var indicator in indicators.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(i => i.Length))
        {
            var safe = indicator.Replace(".", "[.]");
            if (!safe.Contains("[.]")) safe = safe.Replace(":", "[:]");
            text = Regex.Replace(text, Regex.Escape(indicator), _ => safe, RegexOptions.IgnoreCase);
        }
        return Regex.Replace(text, "(?i)https?://|ftp://", m => m.Value.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "hxxps://" : m.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "hxxp://" : "fxp://");
    }

    private static (string, string, int) Explain(string evidence, string tier) => evidence switch {
        "on-disk" when tier == "vuln-driver" => ("Legitimate driver", "A legitimate driver known to be abused was found. The file itself is not spyware; check its installation context.", 4),
        "on-disk" when tier == "sample" => ("Known spyware file", "A file matches the fingerprint of a published spyware sample. Preserve it and seek expert help promptly.", 0),
        "on-disk" => ("File fingerprint needs review", "A file fingerprint matched, but its classification does not identify a known spyware sample. Expert review is needed.", 1),
        "file-pattern" => ("File pattern needs review", "A detection rule matched a file. Expert verification is required; this is not an exact sample identification.", 1),
        "persistence" => ("Auto-start evidence", "An indicator was found in an auto-start registration. Preserve the evidence and seek expert review.", 2),
        "contacted" => ("Connection record", "A local connection record shows communication with this infrastructure. Its significance depends on its role and the date.", 3),
        "dns-record" => ("DNS record", "An indicator appears in local resolver records. A lookup may have failed; this does not establish a connection.", 5),
        "attempted" => ("Blocked lookup", "A lookup was observed and a filter blocked it. This does not establish infection.", 6),
        "configured" => ("Configuration record", "An indicator appears in local configuration pointing at a real address. Establish how it was created.", 7),
        "referenced" => ("Saved reference", "An indicator appears in saved content, possibly a news article or blocklist. This does not establish contact.", 8),
        _ => ("Unclassified evidence", "The evidence classification is missing or unfamiliar. Qualified review is needed; do not assume it is harmless.", 3)
    };
    private static string Role(string tier) => tier switch {
        "delivery" => "Role: advertising/tracking delivery. An ordinary advertisement may explain a trace; this is not evidence of infection.",
        "cc" => "Role: attributed command-and-control infrastructure; connection records warrant specialist review.",
        "validator" => "Role: attributed device assessment infrastructure; interpret the evidence type before drawing conclusions.",
        "candiru-ip" => "Role: historically attributed IP address. Current ownership and ownership at the recorded time are not established.",
        "historical" => "Role: infrastructure from older campaigns. Check the date and cited research; current activity is not established.",
        "sample" => "Role: published spyware sample fingerprint.",
        "vuln-driver" => "Role: legitimate software also installed for ordinary reasons.",
        "candiru" => "Role: attributed Candiru infrastructure; its exact role is unspecified.",
        _ => "Role: consult the original classification and source; no additional role is inferred."
    };
}
public static class ReportExporter
{
    public static string ToText(ScanReport r)
    {
        var b = new StringBuilder($"ZERO BREACH\nScanner {r.Version} | {r.Timestamp} | {r.ScanMode}\n\n{r.Title}\n{r.Summary}\n\nRECOMMENDED NEXT STEPS\n{r.NextSteps}\n\nFINDINGS\n");
        foreach (var f in r.Findings) b.AppendLine($"\n{f.Title}\n{f.Text}");
        b.AppendLine("\nWHAT WAS CHECKED"); foreach (var c in r.Coverage) b.AppendLine(c);
        b.AppendLine("\nWHAT COULD NOT BE CHECKED"); foreach (var g in r.Gaps) b.AppendLine("- " + g);
        return b.ToString();
    }
}
