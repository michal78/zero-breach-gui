using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using ZeroBreach.Core.Scanning;

namespace ZeroBreach.Tests;
public class RuntimeTests
{
    private static byte[] Zip(string name) { using var m = new MemoryStream(); using (var z = new ZipArchive(m,ZipArchiveMode.Create,true)) { using var w = new StreamWriter(z.CreateEntry(name).Open()); w.Write("runtime fixture"); } return m.ToArray(); }
    [Fact] public void RuntimeTamperingIsRejected() {
        var root = Path.Combine(Path.GetTempPath(),"ZB test æ " + Guid.NewGuid()); var zip = Zip("python.exe");
        try {
            var runtime = RuntimeManager.Ensure(zip,Convert.ToHexString(SHA256.HashData(zip)),root);
            Assert.Equal("runtime fixture",File.ReadAllText(runtime));
            File.WriteAllText(runtime,"tampered");
            Assert.Throws<InvalidDataException>(() => RuntimeManager.Ensure(zip,Convert.ToHexString(SHA256.HashData(zip)),root));
        } finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Theory] [InlineData("../escape")] [InlineData("/escape")] [InlineData("C:/escape")] [InlineData("folder/../../escape")]
    public void RuntimeArchiveCannotEscape(string path) {
        var zip = Zip(path); var root = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try { Assert.Throws<InvalidDataException>(() => RuntimeManager.Ensure(zip,Convert.ToHexString(SHA256.HashData(zip)),root)); }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact] public void EmbeddedArchiveHashMustMatch() => Assert.Throws<InvalidDataException>(() => RuntimeManager.Ensure(Zip("python.exe"),new string('0',64),Path.GetTempPath()));
    [Fact] public void WorkspacePreventsConcurrentInstances() {
        var root = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try { using var first = PortableWorkspace.Acquire(root); Assert.Throws<IOException>(() => PortableWorkspace.Acquire(root)); }
        finally { Directory.Delete(root,true); }
    }
    [Fact] public void WorkspaceCreationFailureExplainsHowToProceed() {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); File.WriteAllText(path,"obstruction");
        try { var ex=Assert.Throws<IOException>(()=>PortableWorkspace.Acquire(path)); Assert.Contains("writable local folder",ex.Message); }
        finally { File.Delete(path); }
    }
}
