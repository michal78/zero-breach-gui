using System.IO.Compression;
using System.Security.Cryptography;
namespace ZeroBreach.Core.Scanning;
public static class RuntimeManager
{
    public static string Ensure(byte[] archive, string hash, string root)
    {
        if (!Convert.ToHexString(SHA256.HashData(archive)).Equals(hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The embedded Python runtime failed its integrity check.");
        Directory.CreateDirectory(root); PortableWorkspace.RejectReparse(root);
        var destination = Path.Combine(root,"python-" + hash[..16].ToLowerInvariant());
        using var zip = new ZipArchive(new MemoryStream(archive),ZipArchiveMode.Read);
        long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries) {
            var name = entry.FullName.Replace('\\','/');
            if (name.Length == 0 || name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is ".." or "." or "") || !names.Add(name) || entry.Length > 64*1024*1024 || (total += entry.Length) > 128*1024*1024)
                throw new InvalidDataException("The embedded runtime contains an invalid archive entry.");
        }
        if (!names.Contains("python.exe")) throw new InvalidDataException("The embedded runtime has no Python executable.");
        if (Directory.Exists(destination)) {
            PortableWorkspace.RejectReparse(destination);
            foreach (var path in Directory.EnumerateFileSystemEntries(destination,"*",SearchOption.AllDirectories)) PortableWorkspace.RejectReparse(path);
            var actual = Directory.EnumerateFiles(destination,"*",SearchOption.AllDirectories).Select(p => Path.GetRelativePath(destination,p).Replace('\\','/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.SetEquals(actual)) throw new InvalidDataException("The extracted runtime contains unexpected or missing files. Move the executable to a fresh writable folder to restore it.");
            foreach (var entry in zip.Entries) {
                using var expected = entry.Open(); using var actualFile = File.OpenRead(Path.Combine(destination,entry.FullName));
                if (!SHA256.HashData(expected).AsSpan().SequenceEqual(SHA256.HashData(actualFile))) throw new InvalidDataException("The extracted runtime was modified. Move the executable to a fresh writable folder to restore it.");
            }
        } else {
            var staging = Path.Combine(root,"runtime-stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            try {
                foreach (var entry in zip.Entries) {
                    var path = Path.Combine(staging,entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var input = entry.Open(); using var output = new FileStream(path,FileMode.CreateNew); input.CopyTo(output);
                }
                Directory.Move(staging,destination);
            } finally { if (Directory.Exists(staging)) Directory.Delete(staging,true); }
        }
        return Path.Combine(destination,"python.exe");
    }
}
