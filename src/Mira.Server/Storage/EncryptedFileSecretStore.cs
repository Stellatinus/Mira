using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mira.Server.Models;

namespace Mira.Server.Storage;

/// <summary>
/// Token in un file cifrato con AES-GCM, leggibile solo dall'utente (permessi 600 su Linux/macOS,
/// cartella privata del profilo su Windows). La chiave viene da MIRA_MASTER_KEY (base64, 32 byte)
/// oppure, se non è impostata, viene generata al primo avvio in "master.key" nella stessa cartella.
///
/// Protegge da perdite accidentali (commit, backup, condivisioni). Non protegge da un programma
/// malevolo che gira con il tuo stesso utente: per quello serve il portachiavi di sistema.
/// </summary>
public sealed class EncryptedFileSecretStore : ISecretStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _tokensPath;
    private readonly string _keyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EncryptedFileSecretStore(string directory)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _tokensPath = Path.Combine(directory, "tokens.enc");
        _keyPath = Path.Combine(directory, "master.key");
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string name, string secret, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            all[name] = secret;
            Write(all);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(string name, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            if (all.Remove(name))
                Write(all);
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_tokensPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var blob = await File.ReadAllBytesAsync(_tokensPath, ct);
        var plain = Decrypt(blob);
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        return new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
    }

    private void Write(Dictionary<string, string> data)
    {
        var blob = Encrypt(JsonSerializer.SerializeToUtf8Bytes(data));
        var temp = _tokensPath + ".tmp";
        WriteRestricted(temp, blob);
        File.Move(temp, _tokensPath, overwrite: true);
    }

    private byte[] Encrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        cipher.CopyTo(blob, NonceSize + TagSize);
        return blob;
    }

    private byte[] Decrypt(byte[] blob)
    {
        if (blob.Length < NonceSize + TagSize)
            throw new MiraException("secrets-corrupt", "The token file is damaged. Delete tokens.enc and reconnect the accounts.");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(GetKey(), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw new MiraException(
                "secrets-key-mismatch",
                "Cannot decrypt the tokens: the key does not match. " +
                "Restore master.key / MIRA_MASTER_KEY, or delete tokens.enc and reconnect the accounts.");
        }

        return plain;
    }

    private byte[] GetKey()
    {
        var fromEnv = Environment.GetEnvironmentVariable("MIRA_MASTER_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var key = Convert.FromBase64String(fromEnv.Trim());
            if (key.Length != 32)
                throw new InvalidOperationException("MIRA_MASTER_KEY deve contenere 32 byte codificati in base64.");
            return key;
        }

        if (File.Exists(_keyPath))
            return Convert.FromBase64String(File.ReadAllText(_keyPath).Trim());

        var created = RandomNumberGenerator.GetBytes(32);
        WriteRestricted(_keyPath, Encoding.ASCII.GetBytes(Convert.ToBase64String(created)));
        return created;
    }

    private static void WriteRestricted(string path, byte[] data)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var stream = new FileStream(path, options);
        stream.Write(data);
    }
}
