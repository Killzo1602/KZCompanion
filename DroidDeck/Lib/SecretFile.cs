using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DroidDeck.Lib
{
    public static class SecretFile
    {
        private const string Header = "DDSECRET1";

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DroidDeck.SecretFile.v1");

        public static string ReadAllText(string path)
        {
            var content = File.ReadAllText(path);
            if (TryUnwrap(content, out var plain)) return plain;

            try { WriteAllText(path, content); } catch {  }
            return content;
        }

        public static void WriteAllText(string path, string contents)
        {
            var tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, Wrap(contents));
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        internal static string Wrap(string plain)
        {
            var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Header + "\n" + Convert.ToBase64String(blob);
        }

        internal static bool TryUnwrap(string content, out string plain)
        {
            plain = string.Empty;
            if (!content.StartsWith(Header + "\n", StringComparison.Ordinal)) return false;

            var blob = Convert.FromBase64String(content.Substring(Header.Length + 1).Trim());
            plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));
            return true;
        }
    }
}
