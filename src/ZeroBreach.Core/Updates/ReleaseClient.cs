using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZeroBreach.Core.Updates;
public record ReleaseDescriptor(string Tag, IReadOnlyDictionary<string, Uri> Assets);
public sealed class ReleaseClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeSpan downloadTimeout;
    public static readonly string[] RequiredAssets = ["zero-breach.py", "zero-breach.py.asc", "SHA256SUMS.txt", "SHA256SUMS.txt.asc"];
    public ReleaseClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public ReleaseClient(HttpMessageHandler handler,TimeSpan? timeout = null)
    {
        downloadTimeout = timeout ?? TimeSpan.FromSeconds(60);
        http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZeroBreach-Windows-GUI/0.1");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    public static void ValidateUrl(Uri url)
    {
        if (url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            throw new InvalidDataException("The update refers to an unexpected download location.");
    }
    public async Task<byte[]> DownloadAsync(Uri url, int limit, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(downloadTimeout);
        try { return await DownloadCoreAsync(url,limit,deadline.Token); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new TimeoutException("The official release download timed out. Check your connection and retry, or explicitly choose a verified saved version if available.",ex); }
    }
    private async Task<byte[]> DownloadCoreAsync(Uri url, int limit, CancellationToken token)
    {
        for (int redirect = 0; redirect <= 5; redirect++) {
            ValidateUrl(url);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) {
                var location = response.Headers.Location ?? throw new InvalidDataException("The download redirect is missing its destination.");
                url = location.IsAbsoluteUri ? location : new Uri(url, location); continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new IOException($"GitHub download failed ({(int)response.StatusCode}). Check your connection and try again. GitHub may also have temporarily limited requests.");
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The update file exceeds its allowed size.");
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var target = new MemoryStream(); var buffer = new byte[16384]; int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0) {
                if (target.Length + read > limit) throw new InvalidDataException("The update file exceeds its allowed size.");
                target.Write(buffer, 0, read);
            }
            return target.ToArray();
        }
        throw new InvalidDataException("Too many download redirects.");
    }
    public async Task<ReleaseDescriptor> FetchLatestAsync(CancellationToken token)
    {
        var bytes = await DownloadAsync(new Uri("https://api.github.com/repos/interplains/zero-breach/releases/latest"), 1024 * 1024, token);
        try {
            using var doc = JsonDocument.Parse(bytes); var r = doc.RootElement;
            if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("A stable release is required.");
            var tag = r.GetProperty("tag_name").GetString()!;
            if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$")) throw new InvalidDataException("Unsupported release version.");
            var assets = new Dictionary<string, Uri>();
            foreach (var asset in r.GetProperty("assets").EnumerateArray()) {
                var name = asset.GetProperty("name").GetString()!;
                if (!RequiredAssets.Contains(name)) continue;
                var url = new Uri(asset.GetProperty("browser_download_url").GetString()!); ValidateUrl(url);
                if (url.Host != "github.com" || url.AbsolutePath != $"/interplains/zero-breach/releases/download/{tag}/{name}" || !assets.TryAdd(name,url))
                    throw new InvalidDataException("The release has unexpected or duplicate assets.");
            }
            if (RequiredAssets.Any(name => !assets.ContainsKey(name))) throw new InvalidDataException("The release is missing its scanner or verification files.");
            return new ReleaseDescriptor(tag, assets);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException) {
            throw new InvalidDataException("GitHub returned incompatible release information.", ex);
        }
    }
    public void Dispose() => http.Dispose();
}
