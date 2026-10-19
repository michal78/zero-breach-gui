using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace ZeroBreach.Core.Updates;
public sealed class SignatureVerifier
{
    public const string Fingerprint = "C24EC92B12D6670A2516065F9B4D575499AFA53C";
    public void Verify(byte[] content, byte[] signature)
    {
        try {
            using var resource = typeof(SignatureVerifier).Assembly.GetManifestResourceStream("publisher.asc")!;
            using var keyStream = PgpUtilities.GetDecoderStream(resource);
            var keys = new PgpPublicKeyRingBundle(keyStream);
            using var input = PgpUtilities.GetDecoderStream(new MemoryStream(signature));
            var factory = new PgpObjectFactory(input);
            if (factory.NextPgpObject() is not PgpSignatureList list || list.Count != 1)
                throw new InvalidDataException("The release signature is missing or invalid.");
            var sig = list[0]; var key = keys.GetPublicKey(sig.KeyId);
            if (key == null || Convert.ToHexString(key.GetFingerprint()) != Fingerprint)
                throw new InvalidDataException("The release was not signed by the trusted publisher key.");
            if (sig.SignatureType != PgpSignature.BinaryDocument)
                throw new InvalidDataException("Unsupported release signature format.");
            sig.InitVerify(key); sig.Update(content);
            if (!sig.Verify()) throw new InvalidDataException("Release signature verification failed. The file will not be run.");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new InvalidDataException("The release signature could not be verified. The file will not be run.", ex); }
    }
    public static void VerifyChecksum(byte[] script, byte[] manifest)
    {
        var matches = Encoding.UTF8.GetString(manifest).Split('\n')
            .Select(line => Regex.Match(line.TrimEnd('\r'), @"^([a-fA-F0-9]{64}) [ *]zero-breach\.py$"))
            .Where(m => m.Success).ToArray();
        if (matches.Length != 1 || !Convert.ToHexString(SHA256.HashData(script)).Equals(matches[0].Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The scanner does not match the signed checksum manifest.");
    }
}
