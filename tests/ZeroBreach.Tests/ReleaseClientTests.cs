using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using ZeroBreach.Core.Updates;

namespace ZeroBreach.Tests;
public class ReleaseClientTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => send(request,token);
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static string Metadata(bool draft = false,bool prerelease = false,string tag = "v0.1.4") => JsonSerializer.Serialize(new {
        draft, prerelease, tag_name = tag, assets = ReleaseClient.RequiredAssets.Select(n => new { name = n, browser_download_url = $"https://github.com/interplains/zero-breach/releases/download/{tag}/{n}" })
    });
    [Fact] public async Task StableReleaseMustContainAllExpectedAssets() {
        using var client = new ReleaseClient(new Handler((_,_)=>Task.FromResult(Json(Metadata()))));
        var release = await client.FetchLatestAsync(CancellationToken.None);
        Assert.Equal("v0.1.4",release.Tag); Assert.Equal(4,release.Assets.Count);
    }
    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public async Task DraftAndPrereleaseAreRejected(bool draft,bool prerelease) {
        using var client = new ReleaseClient(new Handler((_,_)=>Task.FromResult(Json(Metadata(draft,prerelease)))));
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.FetchLatestAsync(CancellationToken.None));
    }
    [Fact] public async Task MetadataCannotRedirectAssetsToAnotherRepository() {
        using var client = new ReleaseClient(new Handler((_,_)=>Task.FromResult(Json(Metadata().Replace("interplains/zero-breach/","other/scanner/")))));
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.FetchLatestAsync(CancellationToken.None));
    }
    [Fact] public async Task OversizedDownloadIsRejected() {
        using var client = new ReleaseClient(new Handler((_,_)=>Task.FromResult(Json(new string('x',500)))));
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(new Uri("https://github.com/file"),10,CancellationToken.None));
    }
    [Fact] public async Task RedirectToUnknownHostIsRejected() {
        using var client = new ReleaseClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/file") } })));
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(new Uri("https://github.com/file"),100,CancellationToken.None));
    }
    [Fact] public async Task CancelledUpdatePreservesPreviousPointer() {
        var root = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"current.json"),"previous pointer");
        using var cts = new CancellationTokenSource(150);
        using var client = new ReleaseClient(new Handler(async (r,t)=>{
            if (r.RequestUri!.Host == "api.github.com") return Json(Metadata());
            await Task.Delay(10000,t); return Json("unreachable");
        }));
        try {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new VerifiedCache(root).UpdateAsync(client,new Progress<string>(),cts.Token));
            Assert.Equal("previous pointer",File.ReadAllText(Path.Combine(root,"current.json")));
            Assert.Empty(Directory.GetDirectories(root,"stage-*"));
        } finally { Directory.Delete(root,true); }
    }
    [Fact] public async Task RedirectLoopIsBounded() {
        int requests = 0;
        using var client = new ReleaseClient(new Handler((_,_)=>{ requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://github.com/again") } }); }));
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.DownloadAsync(new Uri("https://github.com/file"),100,CancellationToken.None));
        Assert.InRange(requests,1,6);
    }
}
