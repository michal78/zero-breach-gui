using System.Text.Json.Nodes;
using Xunit;
using ZeroBreach.Core.Reports;

namespace ZeroBreach.Tests;

public class ReportTests
{
    internal static string Fixture(string evidence = "", string tier = "campaign", bool gap = false)
    {
        var hit = new { indicator = "danger.example", location = @"C:\Users\Test\History", detail = "https://danger.example/a", tier, confidence = "high", source = "Published research", evidence, observed = "2025-03-01", profile = "Personal" };
        return System.Text.Json.JsonSerializer.Serialize(new {
            tool = new { name = "adint-scan", version = "0.1.4" }, timestamp = "2026-10-19T14:00:00",
            platform = new { system = "Windows", release = "11" }, ioc_set = new { count = 405, stale = false },
            modules = new { browser = new { name = "browser", status = evidence != "" ? "HITS" : gap ? "UNKNOWN" : "CLEAN", checked_ = 42, skipped = 0,
                hits = evidence == "" ? [] : new[] { hit }, checks = new[] { new { name = "history", ran = !gap, reason = gap ? "Browser is locked" : "" } }, notes = Array.Empty<string>(), submetrics = new { sqlite = 3 }, reach = new { } } },
            verdict = new { status = evidence != "" ? "INDICATORS_FOUND" : gap ? "NO_KNOWN_INDICATORS_WITH_GAPS" : "NO_KNOWN_INDICATORS", hits_total = evidence == "" ? 0 : 1, gaps = gap ? new[] { "browser/history: Browser is locked" } : [] }
        }).Replace("checked_", "checked");
    }

    [Fact] public void NoMatchesExplainsLimits() {
        var r = ReportReader.Parse(Fixture(), 0);
        Assert.Equal("No known indicators found", r.Title);
        Assert.Contains("does not prove", r.Summary);
        Assert.Empty(r.Findings);
    }
    [Theory]
    [InlineData("referenced", "campaign", "Saved reference")]
    [InlineData("on-disk", "vuln-driver", "Legitimate driver")]
    [InlineData("on-disk", "sample", "Known spyware file")]
    [InlineData("on-disk", "campaign", "File fingerprint needs review")]
    [InlineData("persistence", "campaign", "Auto-start evidence")]
    [InlineData("future-grade", "future-tier", "Unclassified evidence")]
    public void EvidenceIsNotCollapsed(string evidence, string tier, string title) {
        var r = ReportReader.Parse(Fixture(evidence, tier), 2);
        Assert.Equal(title, r.Findings.Single().Title);
        Assert.Contains("danger[.]example", r.Findings.Single().Text);
        Assert.DoesNotContain("https://danger.example", ReportExporter.ToText(r));
        Assert.Contains("Personal", r.Findings.Single().Text);
    }
    [Fact] public void CoverageGapsRemainVisible() {
        var r = ReportReader.Parse(Fixture(gap:true), 0);
        Assert.Contains(r.Gaps, x => x.Contains("Browser is locked"));
        Assert.Contains("42", r.Coverage.Single());
    }
    [Theory]
    [InlineData("{}", 0)] [InlineData("{", 0)]
    public void InvalidReportsNeverBecomeClean(string json, int code) => Assert.Throws<InvalidDataException>(() => ReportReader.Parse(json, code));
    [Fact] public void InconsistentExitAndCountAreRejected() {
        Assert.Throws<InvalidDataException>(() => ReportReader.Parse(Fixture("on-disk"), 0));
        var n = JsonNode.Parse(Fixture())!; n["verdict"]!["hits_total"] = 3;
        Assert.Throws<InvalidDataException>(() => ReportReader.Parse(n.ToJsonString(), 2));
    }
}
