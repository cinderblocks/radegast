/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Radegast.Veles.Models;

namespace Radegast.Veles.Core;

public sealed class CredentialManager : IDisposable
{
    private const string StoreFileName = "credentials.dat";
    private const string KeyFileName = "credentials.key";
    private const string AccountListKey = "saved_accounts";
    private const int KeySize = 32;   // AES-256
    private const int NonceSize = 12; // 96-bit nonce for AES-GCM
    private const int TagSize = 16;   // 128-bit authentication tag

    private readonly string _storeFilePath;
    private readonly string _keyFilePath;
    private readonly byte[] _key;

    public CredentialManager()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RadegastVeles");
        Directory.CreateDirectory(dataDir);

        _storeFilePath = Path.Combine(dataDir, StoreFileName);
        _keyFilePath = Path.Combine(dataDir, KeyFileName);

        _key = LoadOrCreateKey();
        EnsureStoreExists();
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyFilePath))
        {
            try
            {
                var encoded = File.ReadAllText(_keyFilePath).Trim();
                var keyBytes = Convert.FromBase64String(encoded);
                if (keyBytes.Length == KeySize)
                    return keyBytes;
            }
            catch { }
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllText(_keyFilePath, Convert.ToBase64String(key));
        return key;
    }

    private void EnsureStoreExists()
    {
        if (File.Exists(_storeFilePath) && new FileInfo(_storeFilePath).Length > 0)
        {
            try
            {
                LoadAllSecrets();
                return;
            }
            catch
            {
                // Corrupted store, recreate
            }
        }

        if (File.Exists(_storeFilePath)) File.Delete(_storeFilePath);

        SaveAllSecrets(new Dictionary<string, string> { [AccountListKey] = "[]" });
    }

    private Dictionary<string, string> LoadAllSecrets()
    {
        var json = File.ReadAllText(_storeFilePath);
        var envelope = JsonSerializer.Deserialize<StoreEnvelope>(json)
            ?? throw new InvalidDataException("Invalid store format.");

        var nonce = Convert.FromBase64String(envelope.Nonce);
        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            Encoding.UTF8.GetString(plaintext)) ?? [];
    }

    private void SaveAllSecrets(Dictionary<string, string> secrets)
    {
        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets));
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var envelope = new StoreEnvelope
        {
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };
        File.WriteAllText(_storeFilePath, JsonSerializer.Serialize(envelope));
    }

    public List<SavedAccount> GetSavedAccounts()
    {
        try
        {
            var secrets = LoadAllSecrets();
            if (secrets.TryGetValue(AccountListKey, out var json))
                return JsonSerializer.Deserialize<List<SavedAccount>>(json) ?? [];
        }
        catch { }
        return [];
    }

    public string? GetPassword(string username, string gridId)
    {
        try
        {
            var secrets = LoadAllSecrets();
            if (secrets.TryGetValue(PasswordKey(username, gridId), out var pwd))
                return string.IsNullOrEmpty(pwd) ? null : pwd;
        }
        catch { }
        return null;
    }

    public void SaveAccount(string username, string password, string gridId, string gridName)
    {
        var secrets = LoadAllSecrets();

        List<SavedAccount> accounts = [];
        if (secrets.TryGetValue(AccountListKey, out var existingJson))
            accounts = JsonSerializer.Deserialize<List<SavedAccount>>(existingJson) ?? [];

        var existing = accounts.Find(a =>
            a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
            a.GridId == gridId);

        if (existing != null)
        {
            existing.GridName = gridName;
            accounts.Remove(existing);
        }
        else
        {
            existing = new SavedAccount
            {
                Username = username,
                GridId = gridId,
                GridName = gridName
            };
        }

        // Most-recently-used account always at index 0
        accounts.Insert(0, existing);

        secrets[AccountListKey] = JsonSerializer.Serialize(accounts);
        secrets[PasswordKey(username, gridId)] = password;
        SaveAllSecrets(secrets);
    }

    public void RemoveAccount(string username, string gridId)
    {
        var secrets = LoadAllSecrets();

        List<SavedAccount> accounts = [];
        if (secrets.TryGetValue(AccountListKey, out var existingJson))
            accounts = JsonSerializer.Deserialize<List<SavedAccount>>(existingJson) ?? [];

        accounts.RemoveAll(a =>
            a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
            a.GridId == gridId);

        secrets[AccountListKey] = JsonSerializer.Serialize(accounts);
        secrets.Remove(PasswordKey(username, gridId));
        SaveAllSecrets(secrets);
    }

    public string? GetMfaHash(string username, string gridId)
    {
        try
        {
            var secrets = LoadAllSecrets();
            if (secrets.TryGetValue(MfaHashKey(username, gridId), out var hash))
                return string.IsNullOrEmpty(hash) ? null : hash;
        }
        catch { }
        return null;
    }

    public void SaveMfaHash(string username, string gridId, string mfaHash)
    {
        try
        {
            var secrets = LoadAllSecrets();
            secrets[MfaHashKey(username, gridId)] = mfaHash;
            SaveAllSecrets(secrets);
        }
        catch { }
    }

    private static string PasswordKey(string username, string gridId)
        => $"pwd:{username.ToLowerInvariant()}:{gridId}";

    private static string MfaHashKey(string username, string gridId)
        => $"mfa:{username.ToLowerInvariant()}:{gridId}";

    public void SaveLoginPreferences(int locationIndex, string customLocation)
    {
        var secrets = LoadAllSecrets();
        secrets["pref:login_location_index"] = locationIndex.ToString();
        secrets["pref:login_custom_location"] = customLocation ?? string.Empty;
        SaveAllSecrets(secrets);
    }

    public (int LocationIndex, string CustomLocation) LoadLoginPreferences()
    {
        try
        {
            var secrets = LoadAllSecrets();
            int idx = 0;
            string custom = string.Empty;
            if (secrets.TryGetValue("pref:login_location_index", out var idxStr))
                int.TryParse(idxStr, out idx);
            if (secrets.TryGetValue("pref:login_custom_location", out var loc))
                custom = loc ?? string.Empty;
            return (idx, custom);
        }
        catch { return (0, string.Empty); }
    }

    public void SaveFavoriteLocations(string accountKey, List<(string Name, string Location)> locations)
    {
        try
        {
            var secrets = LoadAllSecrets();
            secrets[$"favlocs:{accountKey}"] = JsonSerializer.Serialize(
                locations.ConvertAll(l => new FavoriteLocationEntry { Name = l.Name, Location = l.Location }));
            SaveAllSecrets(secrets);
        }
        catch { }
    }

    public List<(string Name, string Location)> GetFavoriteLocations(string accountKey)
    {
        try
        {
            var secrets = LoadAllSecrets();
            if (secrets.TryGetValue($"favlocs:{accountKey}", out var json))
            {
                var entries = JsonSerializer.Deserialize<List<FavoriteLocationEntry>>(json) ?? [];
                return entries.ConvertAll(e => (e.Name, e.Location));
            }
        }
        catch { }
        return [];
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
    }

    private sealed class FavoriteLocationEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    private sealed class StoreEnvelope
    {
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }
}
