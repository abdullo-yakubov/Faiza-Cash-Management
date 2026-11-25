using System.Security.Cryptography;
using FaizaCash.Infrastructure.Services;
using Xunit;

namespace FaizaCash.UnitTests;

// D-09: backup files are encrypted at rest with AES-256-GCM in authenticated chunks.
public class BackupCryptoTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static byte[] Encrypt(byte[] plain, byte[]? key = null, int chunk = 1024)
    {
        using var input = new MemoryStream(plain);
        using var output = new MemoryStream();
        BackupCrypto.EncryptAsync(input, output, key ?? Key, chunk, CancellationToken.None).GetAwaiter().GetResult();
        return output.ToArray();
    }

    private static byte[] Decrypt(byte[] cipher, byte[]? key = null)
    {
        using var input = new MemoryStream(cipher);
        using var output = new MemoryStream();
        BackupCrypto.DecryptAsync(input, output, key ?? Key, CancellationToken.None).GetAwaiter().GetResult();
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(5000)]
    public void Round_trip_restores_the_exact_bytes(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        var cipher = Encrypt(plain);
        Assert.Equal(plain, Decrypt(cipher));
        Assert.True(BackupCrypto.IsEncrypted(cipher));
    }

    [Fact]
    public void Ciphertext_does_not_contain_the_plain_dump_signature()
    {
        var plain = "PGDMP"u8.ToArray().Concat(new byte[3000]).ToArray();
        var cipher = Encrypt(plain);
        Assert.False(cipher.AsSpan().IndexOf("PGDMP"u8) >= 0);
        Assert.False(BackupCrypto.IsEncrypted(plain));
    }

    [Fact]
    public void Wrong_key_is_rejected()
    {
        var cipher = Encrypt(RandomNumberGenerator.GetBytes(3000));
        Assert.Throws<InvalidDataException>(() => Decrypt(cipher, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void Tampered_byte_is_rejected()
    {
        var cipher = Encrypt(RandomNumberGenerator.GetBytes(3000));
        cipher[cipher.Length / 2] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => Decrypt(cipher));
    }

    [Fact]
    public void Truncation_at_a_chunk_boundary_is_rejected()
    {
        var cipher = Encrypt(RandomNumberGenerator.GetBytes(3000));
        var oneChunk = 4 + 1 + 8 + 4 + 1024 + 16;
        Assert.Throws<InvalidDataException>(() => Decrypt(cipher[..oneChunk]));
    }

    [Fact]
    public void Key_parsing_requires_exactly_32_bytes_and_key_id_is_stable()
    {
        Assert.Null(BackupCrypto.ParseKey(null));
        Assert.Null(BackupCrypto.ParseKey(" "));
        Assert.Throws<InvalidOperationException>(() => BackupCrypto.ParseKey(Convert.ToBase64String(new byte[16])));
        var text = Convert.ToBase64String(Key);
        Assert.Equal(Key, BackupCrypto.ParseKey(text));
        Assert.Equal(BackupCrypto.KeyId(Key), BackupCrypto.KeyId(BackupCrypto.ParseKey(text)!));
        Assert.Equal(16, BackupCrypto.KeyId(Key).Length);
    }
}
