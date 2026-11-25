using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FaizaCash.Infrastructure.Services;

/// <summary>
/// D-09: streaming AES-256-GCM encryption for backup files.
/// Layout: "FGBK" | version(1) | nonce prefix(8) | chunks of [plain length int32 LE | ciphertext | tag(16)].
/// The nonce is prefix + chunk counter; associated data binds the header, the counter and a final-chunk flag,
/// so reordering, truncation and modification all fail authentication.
/// </summary>
public static class BackupCrypto
{
    private static readonly byte[] Magic = "FGBK"u8.ToArray();
    private const byte Version = 1;
    private const int HeaderLength = 13, TagLength = 16, MaxChunk = 16 * 1024 * 1024;
    public const int DefaultChunk = 1024 * 1024;

    public static bool IsEncrypted(ReadOnlySpan<byte> start) => start.Length >= 5 && start[..4].SequenceEqual(Magic) && start[4] == Version;

    /// <summary>Parses Backups:EncryptionKey (base64, 32 bytes). Null/blank means encryption is not configured.</summary>
    public static byte[]? ParseKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        byte[] key;
        try { key = Convert.FromBase64String(value.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Backups:EncryptionKey должен быть base64-строкой длиной 32 байта."); }
        if (key.Length != 32) throw new InvalidOperationException("Backups:EncryptionKey должен содержать ровно 32 байта (AES-256).");
        return key;
    }

    /// <summary>Non-secret fingerprint stored in the manifest so a wrong key gives a clear error.</summary>
    public static string KeyId(byte[] key) =>
        Convert.ToHexString(SHA256.HashData([.. Encoding.ASCII.GetBytes("faiza-backup-key-id:"), .. key]))[..16].ToLowerInvariant();

    public static async Task EncryptAsync(Stream input, Stream output, byte[] key, int chunkSize, CancellationToken ct)
    {
        if (chunkSize is <= 0 or > MaxChunk) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0); header[4] = Version;
        RandomNumberGenerator.Fill(header.AsSpan(5, 8));
        await output.WriteAsync(header, ct);

        using var aes = new AesGcm(key, TagLength);
        var current = new byte[chunkSize];
        var next = new byte[chunkSize];
        var currentLength = await ReadFull(input, current, ct);
        var cipher = new byte[chunkSize];
        var tag = new byte[TagLength];
        var lengthBytes = new byte[4];
        for (uint counter = 0; ; counter++)
        {
            var nextLength = currentLength == chunkSize ? await ReadFull(input, next, ct) : 0;
            var final = nextLength == 0;
            aes.Encrypt(Nonce(header, counter), current.AsSpan(0, currentLength), cipher.AsSpan(0, currentLength), tag, Aad(header, counter, final));
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, currentLength);
            await output.WriteAsync(lengthBytes, ct);
            await output.WriteAsync(cipher.AsMemory(0, currentLength), ct);
            await output.WriteAsync(tag, ct);
            if (final) break;
            (current, next) = (next, current);
            currentLength = nextLength;
            if (counter == uint.MaxValue) throw new InvalidDataException("Резервная копия слишком велика для шифрования.");
        }
        await output.FlushAsync(ct);
        CryptographicOperations.ZeroMemory(current); CryptographicOperations.ZeroMemory(next);
    }

    public static async Task DecryptAsync(Stream input, Stream output, byte[] key, CancellationToken ct)
    {
        var header = new byte[HeaderLength];
        if (await ReadFull(input, header, ct) != HeaderLength || !IsEncrypted(header)) throw Invalid();
        using var aes = new AesGcm(key, TagLength);
        var lengthBytes = new byte[4];
        if (await ReadFull(input, lengthBytes, ct) != 4) throw Invalid();
        var tag = new byte[TagLength];
        for (uint counter = 0; ; counter++)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length is < 0 or > MaxChunk) throw Invalid();
            var cipher = new byte[length];
            if (await ReadFull(input, cipher, ct) != length || await ReadFull(input, tag, ct) != TagLength) throw Invalid();
            var read = await ReadFull(input, lengthBytes, ct);
            if (read is not (0 or 4)) throw Invalid();
            var final = read == 0;
            var plain = new byte[length];
            try { aes.Decrypt(Nonce(header, counter), cipher, tag, plain, Aad(header, counter, final)); }
            catch (CryptographicException) { throw Invalid(); }
            await output.WriteAsync(plain, ct);
            CryptographicOperations.ZeroMemory(plain);
            if (final) break;
        }
        await output.FlushAsync(ct);
    }

    private static InvalidDataException Invalid() =>
        new("Не удалось расшифровать резервную копию: неверный ключ, файл повреждён или обрезан.");

    private static byte[] Nonce(byte[] header, uint counter)
    {
        var nonce = new byte[12];
        header.AsSpan(5, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), counter);
        return nonce;
    }

    private static byte[] Aad(byte[] header, uint counter, bool final)
    {
        var aad = new byte[HeaderLength + 5];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(HeaderLength), counter);
        aad[^1] = final ? (byte)1 : (byte)0;
        return aad;
    }

    private static async Task<int> ReadFull(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
