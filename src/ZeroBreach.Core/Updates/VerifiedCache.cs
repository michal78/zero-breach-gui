using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZeroBreach.Core.Updates;
public record VerifiedScanner(string ScriptPath, string Tag);
public sealed class VerifiedCache(string root)
{
    private readonly SignatureVerifier verifier = new();
    private sealed record Pointer(string tag, string folder);
    public async Task<VerifiedScanner> OpenSavedAsync(CancellationToken token)
    {
        try {
            var pointer = JsonSerializer.Deserialize<Pointer>(await File.ReadAllTextAsync(Path.Combine(root,"current.json"),token)) ?? throw new InvalidDataException("No verified saved scanner is available.");
            if (!Regex.IsMatch(pointer.tag ?? "", @"^v\d+\.\d+\.\d+$") || !Regex.IsMatch(pointer.folder ?? "", @"^v\d+\.\d+\.\d+-[A-F0-9]{12}-[a-f0-9]{8}$")) throw new InvalidDataException("The saved scanner metadata is invalid.");
            var folder = Path.Combine(root,pointer.folder!);
            await VerifyFolderAsync(folder, token);
            return new VerifiedScanner(Path.Combine(folder,"zero-breach.py"), pointer.tag!);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { throw new InvalidDataException("The saved scanner is missing or has failed verification. Download a fresh version.", ex); }
    }
    public async Task<byte[]> ReadVerifiedBytesAsync(VerifiedScanner scanner, CancellationToken token)
    {
        var folder = Path.GetDirectoryName(scanner.ScriptPath)!;
        return await VerifyFolderAsync(folder, token);
    }
    private async Task<byte[]> VerifyFolderAsync(string folder, CancellationToken token)
    {
        async Task<byte[]> Read(string name, long max) {
            var path = Path.Combine(folder,name);
            if (new FileInfo(path).Length > max) throw new InvalidDataException("The saved scanner file is unexpectedly large.");
            var bytes = await File.ReadAllBytesAsync(path,token); if (bytes.LongLength > max) throw new InvalidDataException("The saved scanner file is unexpectedly large."); return bytes;
        }
        var script = await Read("zero-breach.py", 8*1024*1024);
        var manifest = await Read("SHA256SUMS.txt", 256*1024);
        verifier.Verify(manifest, await Read("SHA256SUMS.txt.asc", 64*1024));
        verifier.Verify(script, await Read("zero-breach.py.asc", 64*1024));
        SignatureVerifier.VerifyChecksum(script,manifest); return script;
    }
    public async Task<VerifiedScanner> UpdateAsync(ReleaseClient client, IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(root); progress.Report("Checking the latest official release…");
        var release = await client.FetchLatestAsync(token);
        VerifiedScanner? saved = null;
        try { saved = await OpenSavedAsync(token); } catch (InvalidDataException) { }
        if (saved != null && Version.Parse(release.Tag[1..]) < Version.Parse(saved.Tag[1..])) throw new InvalidDataException("GitHub offered an older release than the verified saved version. The update was stopped.");
        if (saved?.Tag == release.Tag) { progress.Report($"Verified saved scanner {release.Tag}"); return saved; }
        var staging = Path.Combine(root,"stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try {
            foreach (var name in ReleaseClient.RequiredAssets) {
                progress.Report($"Downloading {release.Tag} · {name}");
                var bytes = await client.DownloadAsync(release.Assets[name], name == "zero-breach.py" ? 8*1024*1024 : name.EndsWith(".asc") ? 64*1024 : 256*1024, token);
                await File.WriteAllBytesAsync(Path.Combine(staging,name), bytes, token);
            }
            progress.Report("Verifying publisher signatures and checksum…");
            var script = await VerifyFolderAsync(staging,token); token.ThrowIfCancellationRequested();
            var folder = release.Tag + "-" + Convert.ToHexString(SHA256.HashData(script))[..12] + "-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(staging,Path.Combine(root,folder));
            var pointerFile = Path.Combine(root,"current-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(pointerFile,JsonSerializer.Serialize(new Pointer(release.Tag,folder)),token);
            File.Move(pointerFile,Path.Combine(root,"current.json"),true);
            return new VerifiedScanner(Path.Combine(root,folder,"zero-breach.py"),release.Tag);
        }
        finally { if (Directory.Exists(staging)) { try { Directory.Delete(staging,true); } catch (IOException) { progress.Report("Temporary download files could not be removed: " + staging); } catch (UnauthorizedAccessException) { progress.Report("Temporary download files could not be removed: " + staging); } } }
    }
}
