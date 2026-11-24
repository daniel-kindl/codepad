using System.Security.Cryptography;
using System.Text;
using Codepad.Infrastructure.Updates;

namespace Codepad.Tests.Updates;

public sealed class EcdsaManifestVerifierTests
{
    internal static (string PublicPem, ECDsa Key) NewKey()
    {
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportSubjectPublicKeyInfoPem(), key);
    }

    internal static byte[] Sign(ECDsa key, byte[] data) => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    [Fact]
    public void ASignatureOfThePublisherIsValid()
    {
        (string pem, ECDsa key) = NewKey();
        byte[] data = Encoding.UTF8.GetBytes("{\"version\":\"0.2.0\"}");

        EcdsaManifestVerifier verifier = new EcdsaManifestVerifier(pem);

        Assert.True(verifier.HasKey);
        Assert.True(verifier.Verify(data, Sign(key, data)));
    }

    [Fact]
    public void AChangedByteInTheManifestBreaksTheSignature()
    {
        (string pem, ECDsa key) = NewKey();
        byte[] data = Encoding.UTF8.GetBytes("{\"version\":\"0.2.0\"}");
        byte[] signature = Sign(key, data);
        data[5] ^= 1;

        Assert.False(new EcdsaManifestVerifier(pem).Verify(data, signature));
    }

    [Fact]
    public void AChangedByteInTheSignatureIsRejected()
    {
        (string pem, ECDsa key) = NewKey();
        byte[] data = Encoding.UTF8.GetBytes("manifest");
        byte[] signature = Sign(key, data);
        signature[10] ^= 1;

        Assert.False(new EcdsaManifestVerifier(pem).Verify(data, signature));
    }

    [Fact]
    public void ASignatureOfAnotherKeyIsRejected()
    {
        (string pem, ECDsa _) = NewKey();
        (string _, ECDsa attacker) = NewKey();
        byte[] data = Encoding.UTF8.GetBytes("manifest");

        Assert.False(new EcdsaManifestVerifier(pem).Verify(data, Sign(attacker, data)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(4096)]
    public void ASignatureOfTheWrongLengthIsRejectedWithoutAnException(int length)
    {
        (string pem, ECDsa _) = NewKey();

        Assert.False(new EcdsaManifestVerifier(pem).Verify(Encoding.UTF8.GetBytes("manifest"), new byte[length]));
    }

    [Fact]
    public void AnEmptyManifestWithAValidSignatureOfNothingIsStillJustASignatureCheck()
    {
        (string pem, ECDsa key) = NewKey();

        Assert.True(new EcdsaManifestVerifier(pem).Verify([], Sign(key, [])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a pem")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    public void NoKeyOrABadKeyMeansNothingIsTrusted(string? pem)
    {
        EcdsaManifestVerifier verifier = new EcdsaManifestVerifier(pem);

        Assert.False(verifier.HasKey);
        Assert.False(verifier.Verify(Encoding.UTF8.GetBytes("manifest"), new byte[64]));
    }

    [Fact]
    public void AnRsaKeyIsNotAnAcceptedAnchor()
    {
        using RSA rsa = RSA.Create(2048);

        EcdsaManifestVerifier verifier = new EcdsaManifestVerifier(rsa.ExportSubjectPublicKeyInfoPem());

        Assert.False(verifier.HasKey);
    }

    [Fact]
    public void ACurveOtherThanP256IsNotAccepted()
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.False(new EcdsaManifestVerifier(other.ExportSubjectPublicKeyInfoPem()).HasKey);
    }
}
