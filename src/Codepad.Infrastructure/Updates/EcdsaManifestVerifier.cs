using System.Security.Cryptography;
using Codepad.Application.Updates;

namespace Codepad.Infrastructure.Updates;

/// <summary>
/// Checks the signature of the update manifest with the public key of the publisher: ECDSA on the curve P-256 with SHA-256, in the
/// fixed 64 byte format (r and s joined). The key comes with the app. Without a valid key the verifier trusts nothing.
/// A checksum from the same release page would not prove who published it. A signature does.
/// </summary>
public sealed class EcdsaManifestVerifier : IManifestVerifier
{
    private const int SIGNATURE_BYTES = 64;

    private readonly ECDsa? key;

    /// <summary>Initializes a new instance of the <see cref="EcdsaManifestVerifier"/> class.</summary>
    /// <param name="publicKeyPem">The public key in PEM form (SubjectPublicKeyInfo), or null or empty when the build has no key.</param>
    public EcdsaManifestVerifier(string? publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return;
        }

        try
        {
            ECDsa candidate = ECDsa.Create();
            candidate.ImportFromPem(publicKeyPem);
            ECParameters parameters = candidate.ExportParameters(includePrivateParameters: false);
            if (parameters.Curve.Oid?.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                candidate.Dispose();
                return;
            }

            key = candidate;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException)
        {
            // A bad key is the same as no key. The app then installs nothing.
            key = null;
        }
    }

    /// <inheritdoc />
    public bool HasKey => key is not null;

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signature)
    {
        if (key is null || signature.Length != SIGNATURE_BYTES)
        {
            return false;
        }

        try
        {
            return key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
