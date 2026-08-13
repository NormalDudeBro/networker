using System.Security.Cryptography;
using Networker.Update.Security;

namespace Networker.Update.Tests.Security;

public sealed class ReleaseFeedVerifierTests
{
    [Fact]
    public void AcceptsExactSignedBytesAndRejectsMutation()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] feed = "{\"Assets\":[]}"u8.ToArray();
        byte[] signature = ReleaseFeedVerifier.CreateSignatureDocument(feed, key, "test-1");
        var verifier = new ReleaseFeedVerifier(new Dictionary<string, byte[]> { ["test-1"] = publicKey });

        Assert.True(verifier.Verify(feed, signature, out string keyId));
        Assert.Equal("test-1", keyId);
        feed[0] ^= 1;
        Assert.False(verifier.Verify(feed, signature, out _));
    }

    [Fact]
    public void RejectsUnknownKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] feed = "{}"u8.ToArray();
        byte[] signature = ReleaseFeedVerifier.CreateSignatureDocument(feed, key, "other");
        Assert.False(new ReleaseFeedVerifier(new Dictionary<string, byte[]>()).Verify(feed, signature, out _));
    }
}
