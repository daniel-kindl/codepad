using System.Security.Cryptography;

namespace Codepad.ReleaseTool;

/// <summary>
/// Makes keys and signatures in the form that the app checks: ECDSA on the curve P-256 with SHA-256, in the fixed 64 byte form. The
/// private key stays with the owner. This class never writes it anywhere except to the file that the owner names.
/// </summary>
internal static class ReleaseSigning
{
    /// <summary>Makes a new key pair.</summary>
    /// <returns>The private key (PKCS#8 PEM) and the public key (SubjectPublicKeyInfo PEM).</returns>
    public static (string PrivatePem, string PublicPem) NewKeyPair()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>Signs bytes.</summary>
    /// <param name="privatePem">The private key.</param>
    /// <param name="data">The exact bytes to sign.</param>
    /// <returns>The 64 byte signature.</returns>
    /// <exception cref="InvalidOperationException">The key is not an ECDSA P-256 private key.</exception>
    public static byte[] Sign(string privatePem, byte[] data)
    {
        using ECDsa key = ECDsa.Create();
        try
        {
            key.ImportFromPem(privatePem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("The file is not a private key in PEM form.", exception);
        }

        if (key.KeySize != 256)
        {
            throw new InvalidOperationException("The key must be on the curve P-256.");
        }

        return key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
