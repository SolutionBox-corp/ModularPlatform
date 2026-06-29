using System.Security.Cryptography;
using System.Text;
using ModularPlatform.Gdpr.Security;

namespace ModularPlatform.Gdpr.Tests;

public sealed class CryptoShredderTests
{
    [Fact]
    public void Encrypt_then_Decrypt_with_same_dek_round_trips()
    {
        var dek = CryptoShredder.GenerateDek();
        var plaintext = Encoding.UTF8.GetBytes("personal data for a subject");

        var blob = CryptoShredder.Encrypt(plaintext, dek);
        var recovered = CryptoShredder.Decrypt(blob, dek);

        Assert.Equal(plaintext, recovered);
        Assert.NotEqual(plaintext, blob);
    }

    [Fact]
    public void Decrypt_with_a_different_dek_fails_modeling_crypto_shredding()
    {
        // After erasure the subject's DEK is deleted; a different key cannot recover the ciphertext.
        var dek = CryptoShredder.GenerateDek();
        var otherDek = CryptoShredder.GenerateDek();
        var blob = CryptoShredder.Encrypt(Encoding.UTF8.GetBytes("secret"), dek);

        Assert.ThrowsAny<CryptographicException>(() => CryptoShredder.Decrypt(blob, otherDek));
    }

    [Fact]
    public void Decrypt_with_matching_aad_round_trips_and_wrong_aad_fails()
    {
        var dek = CryptoShredder.GenerateDek();
        var plaintext = Encoding.UTF8.GetBytes("subject-bound secret");
        var aad = Guid.CreateVersion7().ToByteArray();
        var wrongAad = Guid.CreateVersion7().ToByteArray();

        var blob = CryptoShredder.Encrypt(plaintext, dek, aad);

        Assert.Equal(plaintext, CryptoShredder.Decrypt(blob, dek, aad));
        Assert.ThrowsAny<CryptographicException>(() => CryptoShredder.Decrypt(blob, dek, wrongAad));
    }

    [Fact]
    public void Decrypt_rejects_blob_too_short_for_nonce_and_tag()
    {
        var dek = CryptoShredder.GenerateDek();
        var tooShort = new byte[27];

        var ex = Assert.Throws<CryptographicException>(() => CryptoShredder.Decrypt(tooShort, dek));
        Assert.Contains("too short", ex.Message);
    }
}
