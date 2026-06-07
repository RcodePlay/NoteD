using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

namespace NoteD;

public class SecurityModule
{
    private static readonly byte[] MagicBytes = [0x4E, 0x6F, 0x74, 0x65, 0x44];
    
    private const int DegreeOfParallelism = 4;
    private const int Iterations = 4;
    private const int MemorySize = 1024 * 128; 

    // Metadata Lengths (Total Header: 81 bytes)
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HashSize = 32;
    private const int HeaderSize = 5 + SaltSize + NonceSize + TagSize + HashSize;

    private (byte[] encryptionKey, byte[] verificationHash) DeriveKeys(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = DegreeOfParallelism;
        argon2.Iterations = Iterations;
        argon2.MemorySize = MemorySize;
        
        var combined = argon2.GetBytes(64);
        return (combined[..32], combined[32..]);
    }

    /// <summary>
    /// Reads an encrypted file, decrypts it, and overwrites it with Plaintext.
    /// </summary>
    public void UnprotectFile(string filePath, string password)
    {
        var fileData = File.ReadAllBytes(filePath);
        
        if (fileData.Length < HeaderSize || !fileData[..5].SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("This is not a valid NoteD encrypted file.");
        }
        
        var salt = fileData[5..21];
        var nonce = fileData[21..33];
        var tag = fileData[33..49];
        var expectedHash = fileData[49..81];
        var ciphertext = fileData[81..];
        
        var (key, vHash) = DeriveKeys(password, salt);
        
        if (!FixedTimeEquals(vHash, expectedHash))
            throw new CryptographicException("Invalid Password.");
        
        using var aes = new AesGcm(key, 16);
        var plaintextBytes = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        File.WriteAllBytes(filePath, plaintextBytes);
    }

    /// <summary>
    /// Reads a plaintext file, encrypts it, and overwrites it with Binary Header and Ciphertext.
    /// </summary>
    public void ProtectFile(string filePath, string password)
    {
        var content = File.ReadAllText(filePath);
        var salt = CreateRandomSalt();
        var (key, vHash) = DeriveKeys(password, salt);
        
        using var aes = new AesGcm(key, 16);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[TagSize];
        var plaintextBytes = Encoding.UTF8.GetBytes(content);
        var ciphertext = new byte[plaintextBytes.Length];
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(MagicBytes);
        writer.Write(salt);         // 16
        writer.Write(nonce);        // 12
        writer.Write(tag);          // 16
        writer.Write(vHash);        // 32
        writer.Write(ciphertext);   // Rest

        File.WriteAllBytes(filePath, ms.ToArray());
    }
    
    public byte[] UnprotectToMemory(byte[] encryptedData, string password)
    {
        if (encryptedData.Length < HeaderSize || !encryptedData[..5].SequenceEqual(MagicBytes))
            throw new InvalidDataException("Not a valid NoteD encrypted file.");
        
        var salt = encryptedData[5..21];
        var nonce = encryptedData[21..33];
        var tag = encryptedData[33..49];
        var expectedHash = encryptedData[49..81];
        var ciphertext = encryptedData[81..];
        
        var (key, vHash) = DeriveKeys(password, salt);
        
        if (!CryptographicOperations.FixedTimeEquals(vHash, expectedHash))
            throw new CryptographicException("Invalid Password.");
        
        using var aes = new AesGcm(key, TagSize);
        
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        
        return plaintext;
    }

    public byte[] ProtectFromMemory(ReadOnlySpan<byte> plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (key, vHash) = DeriveKeys(password, salt);
        
        using var aes = new AesGcm(key, TagSize);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];
        
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        
        using var ms = new MemoryStream(HeaderSize + ciphertext.Length);
        using var writer = new BinaryWriter(ms);
        
        writer.Write(MagicBytes);
        writer.Write(salt);
        writer.Write(nonce);
        writer.Write(tag);
        writer.Write(vHash);
        writer.Write(ciphertext);
        
        return ms.ToArray();
    }

    private byte[] CreateRandomSalt() => RandomNumberGenerator.GetBytes(SaltSize);
    
    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}

public sealed class InMemoryFile : IDisposable
{
    private byte[]? _plaintext;
    private readonly SecurityModule _security;

    public string FilePath { get; }
    public bool IsModified { get; private set; }
    public bool WasSuccessfullyDecrypted { get; }

    public InMemoryFile(string encryptedFilePath, string password, SecurityModule security, string? initialContent = null)
    {
        FilePath = encryptedFilePath;
        if (initialContent != null)
        {
            _plaintext = Encoding.UTF8.GetBytes(initialContent);
        }
        else if (!string.IsNullOrEmpty(encryptedFilePath))
        {
            _security = security ?? throw new ArgumentNullException(nameof(security));

            try
            {
                var encrypted = File.ReadAllBytes(encryptedFilePath);
                _plaintext = _security.UnprotectToMemory(encrypted, password);
                WasSuccessfullyDecrypted = true;
            }
            catch (CryptographicException)
            {
                WasSuccessfullyDecrypted = false;
                throw;
            }
            catch (Exception)
            {
                WasSuccessfullyDecrypted = false;
                throw;
            }
        }
        else
        {
            throw new ArgumentNullException(nameof(encryptedFilePath), "Either an encrypted file path or initial content must be provided.");
        }
        
        

        IsModified = false;
    }

    public string GetTextContent(Encoding? encoding = null)
    {
        if (_plaintext == null)
            throw new ObjectDisposedException(nameof(InMemoryFile));

        encoding ??= Encoding.UTF8;
        return encoding.GetString(_plaintext);
    }

    public void SetTextContent(string newContent, Encoding? encoding = null)
    {
        if (_plaintext == null)
            throw new ObjectDisposedException(nameof(InMemoryFile));

        encoding ??= Encoding.UTF8;

        var newBytes = encoding.GetBytes(newContent);
        
        CryptographicOperations.ZeroMemory(_plaintext.AsSpan());
        _plaintext = newBytes;

        IsModified = true;
    }

    /// <summary>
    /// Encrypts current content (if modified) and **returns** the encrypted bytes.
    /// Does NOT write to disk — caller decides what to do.
    /// </summary>
    public byte[]? GetEncryptedContentIfModified(string password)
    {
        if (_plaintext == null || !IsModified)
            return null;

        return _security.ProtectFromMemory(_plaintext, password);
    }

    /// <summary>
    /// Convenience: encrypt and overwrite the original file if modified
    /// </summary>
    public void SaveIfModified(string password)
    {
        var encrypted = GetEncryptedContentIfModified(password);
        if (encrypted == null) return;

        File.WriteAllBytes(FilePath, encrypted);
        IsModified = false;
    }

    public void Dispose()
    {
        if (_plaintext == null) return;
        CryptographicOperations.ZeroMemory(_plaintext.AsSpan());
        _plaintext = null;
    }
}