using System.Security.Cryptography;
using System.Text;
using Xunit;
using ZeroBreach.Core.Updates;

namespace ZeroBreach.Tests;
public class UpdateTests
{
    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    [Fact] public void OfficialSignatureVerifies() => new SignatureVerifier().Verify(Asset("SHA256SUMS.txt"), Asset("SHA256SUMS.txt.asc"));
    [Fact] public void TamperedContentIsRejected() {
        var content = Asset("SHA256SUMS.txt"); content[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => new SignatureVerifier().Verify(content, Asset("SHA256SUMS.txt.asc")));
    }
    [Fact] public void MissingAndCorruptSignaturesAreRejected() {
        Assert.Throws<InvalidDataException>(() => new SignatureVerifier().Verify(Asset("SHA256SUMS.txt"), []));
        Assert.Throws<InvalidDataException>(() => new SignatureVerifier().Verify(Asset("SHA256SUMS.txt"), Encoding.UTF8.GetBytes("not a signature")));
    }
    [Fact] public void CryptographicallyValidSignatureFromAnotherKeyIsRejected() {
        var generator = new Org.BouncyCastle.Crypto.Generators.RsaKeyPairGenerator();
        generator.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom(),2048));
        var pair = new Org.BouncyCastle.Bcpg.OpenPgp.PgpKeyPair(Org.BouncyCastle.Bcpg.PublicKeyAlgorithmTag.RsaSign,generator.GenerateKeyPair(),DateTime.UtcNow);
        var signature = new Org.BouncyCastle.Bcpg.OpenPgp.PgpSignatureGenerator(pair.PublicKey.Algorithm,Org.BouncyCastle.Bcpg.HashAlgorithmTag.Sha256);
        signature.InitSign(Org.BouncyCastle.Bcpg.OpenPgp.PgpSignature.BinaryDocument,pair.PrivateKey);
        var content = Encoding.UTF8.GetBytes("attacker-controlled scanner"); signature.Update(content);
        using var stream = new MemoryStream(); signature.Generate().Encode(stream);
        Assert.Throws<InvalidDataException>(()=>new SignatureVerifier().Verify(content,stream.ToArray()));
    }
    [Fact] public void ManifestRequiresUniqueExactFilenameAndHash() {
        var content = Encoding.UTF8.GetBytes("hello");
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        SignatureVerifier.VerifyChecksum(content, Encoding.UTF8.GetBytes(hash + "  zero-breach.py\n"));
        Assert.Throws<InvalidDataException>(() => SignatureVerifier.VerifyChecksum(content, Encoding.UTF8.GetBytes(hash + "  path/zero-breach.py\n")));
        Assert.Throws<InvalidDataException>(() => SignatureVerifier.VerifyChecksum(content, Encoding.UTF8.GetBytes(hash + "  zero-breach.py\n" + hash + "  zero-breach.py\n")));
        Assert.Throws<InvalidDataException>(() => SignatureVerifier.VerifyChecksum([1], Encoding.UTF8.GetBytes(hash + "  zero-breach.py\n")));
    }
    [Theory]
    [InlineData("http://github.com/a")]
    [InlineData("https://evil.example/a")]
    [InlineData("https://github.com.evil.example/a")]
    [InlineData("https://user@github.com/a")]
    public void UnexpectedDownloadLocationsAreRejected(string url) => Assert.Throws<InvalidDataException>(() => ReleaseClient.ValidateUrl(new Uri(url)));
    [Fact] public async Task TamperedCacheNeverOpens() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try {
            File.WriteAllText(Path.Combine(dir,"current.json"), "{\"tag\":\"v0.1.4\",\"folder\":\"version\"}");
            Directory.CreateDirectory(Path.Combine(dir,"version"));
            File.WriteAllText(Path.Combine(dir,"version","zero-breach.py"), "tampered");
            await Assert.ThrowsAsync<InvalidDataException>(() => new VerifiedCache(dir).OpenSavedAsync(CancellationToken.None));
        } finally { Directory.Delete(dir,true); }
    }
}
