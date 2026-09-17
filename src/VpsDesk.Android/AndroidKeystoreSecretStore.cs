using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Android;

internal sealed class AndroidKeystoreSecretStore : ISecretStore
{
    private const string KeyAlias = "vpsdesk.secrets.v1";
    private const string PreferencesName = "vpsdesk.secure.preferences";
    private const string CipherTransformation = "AES/GCM/NoPadding";
    private const int GcmTagBits = 128;

    private readonly ISharedPreferences _preferences;

    public AndroidKeystoreSecretStore()
    {
        _preferences = global::Android.App.Application.Context
            .GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Android secure preferences are unavailable.");
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);

        var payload = _preferences.GetString(key, null);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var parts = payload.Split(':', 2);
            if (parts.Length != 2)
            {
                RemoveValue(key);
                return Task.FromResult<string?>(null);
            }

            var iv = Convert.FromBase64String(parts[0]);
            var cipherText = Convert.FromBase64String(parts[1]);
            var secretKey = GetOrCreateSecretKey();

            using var cipher = Cipher.GetInstance(CipherTransformation)
                ?? throw new InvalidOperationException("AES/GCM cipher is unavailable.");
            using var parameters = new GCMParameterSpec(GcmTagBits, iv);
            cipher.Init(CipherMode.DecryptMode, secretKey, parameters);
            var plainBytes = cipher.DoFinal(cipherText)
                ?? throw new CryptographicException("Android Keystore returned no plaintext.");

            return Task.FromResult<string?>(Encoding.UTF8.GetString(plainBytes));
        }
        catch (Exception ex) when (ex is FormatException or GeneralSecurityException or CryptographicException)
        {
            RemoveValue(key);
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        var secretKey = GetOrCreateSecretKey();
        using var cipher = Cipher.GetInstance(CipherTransformation)
            ?? throw new InvalidOperationException("AES/GCM cipher is unavailable.");
        cipher.Init(CipherMode.EncryptMode, secretKey);

        var iv = cipher.GetIV() ?? throw new CryptographicException("Android Keystore returned no IV.");
        var cipherText = cipher.DoFinal(Encoding.UTF8.GetBytes(value))
            ?? throw new CryptographicException("Android Keystore returned no ciphertext.");
        var payload = $"{Convert.ToBase64String(iv)}:{Convert.ToBase64String(cipherText)}";

        using var editor = _preferences.Edit();
        editor.PutString(key, payload);
        if (!editor.Commit())
        {
            throw new IOException("Could not persist the encrypted secret.");
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        RemoveValue(key);
        return Task.CompletedTask;
    }

    private static Javax.Crypto.ISecretKey GetOrCreateSecretKey()
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore")
            ?? throw new InvalidOperationException("Android Keystore is unavailable.");
        keyStore.Load(null);

        if (keyStore.GetKey(KeyAlias, null) is Javax.Crypto.ISecretKey existing)
        {
            return existing;
        }

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
            ?? throw new InvalidOperationException("Android Keystore AES generator is unavailable.");
        using var spec = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetRandomizedEncryptionRequired(true)
            .Build();

        generator.Init(spec);
        return generator.GenerateKey()
            ?? throw new InvalidOperationException("Android Keystore did not create a secret key.");
    }

    private void RemoveValue(string key)
    {
        using var editor = _preferences.Edit();
        editor.Remove(key);
        editor.Apply();
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Secret keys must be 128 characters or fewer.");
        }
    }
}
