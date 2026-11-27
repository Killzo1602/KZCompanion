using DroidDeck.Lib;
using System;
using System.IO;
using System.Security.Cryptography;

namespace DroidDeck.Auth
{
    public static class ApiKeyProvider
    {
        private static readonly object _lock = new();
        private static string? _cached;

        private static string KeyFilePath
        {
            get
            {
                return Path.Combine(AppPaths.LocalDir, "apikey");
            }
        }

        public static string GetKey()
        {
            if (_cached != null) return _cached;
            lock (_lock)
            {
                if (_cached != null) return _cached;
                try
                {
                    var path = KeyFilePath;
                    if (File.Exists(path))
                    {
                        var existing = SecretFile.ReadAllText(path).Trim();
                        if (!string.IsNullOrWhiteSpace(existing))
                            return _cached = existing;
                    }
                    var generated = Generate();
                    SecretFile.WriteAllText(path, generated);
                    return _cached = generated;
                }
                catch
                {
                    return _cached ??= Generate();
                }
            }
        }

        private static string Generate()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }
}
