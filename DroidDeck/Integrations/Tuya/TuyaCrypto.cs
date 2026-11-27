using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace DroidDeck.Services.Tuya
{
    internal static class TuyaCrypto
    {
        private const string NonceAlphabet = "ABCDEFGHJKMNPQRSTWXYZabcdefhijkmnprstwxyz2345678";

        private static readonly string[] SignedHeaders =
            { "X-appKey", "X-requestId", "X-sid", "X-time", "X-token" };

        public static string Md5Hex(string input)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static string HmacSha256Hex(string key, string message)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static string HashKey(string rid, string refreshToken) => Md5Hex(rid + refreshToken);

        public static string GenerateSecret(string rid, string sid, string hashKey)
        {
            var message = hashKey;
            if (!string.IsNullOrEmpty(sid))
            {
                const int mod = 16;
                var length = Math.Min(sid.Length, mod);
                var sb = new StringBuilder();
                for (var i = 0; i < length; i++) sb.Append(sid[sid[i] % mod]);
                message += "_" + sb;
            }

            return HmacSha256Hex(rid, message)[..16];
        }

        public static string RandomNonce(int size = 12)
        {
            var sb = new StringBuilder(size);
            for (var i = 0; i < size; i++)
                sb.Append(NonceAlphabet[RandomNumberGenerator.GetInt32(NonceAlphabet.Length)]);
            return sb.ToString();
        }

        public static string Encrypt(string plaintext, string secret)
        {
            var nonce = Encoding.UTF8.GetBytes(RandomNonce());
            var key = Encoding.UTF8.GetBytes(secret);
            var data = Encoding.UTF8.GetBytes(plaintext);

            var cipher = new byte[data.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(key, tag.Length))
                aes.Encrypt(nonce, data, cipher, tag);

            var combined = new byte[cipher.Length + tag.Length];
            Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
            Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);

            return Convert.ToBase64String(nonce) + Convert.ToBase64String(combined);
        }

        public static string Decrypt(string encoded, string secret)
        {
            var raw = Convert.FromBase64String(encoded);
            var nonce = raw.AsSpan(0, 12).ToArray();
            var payload = raw.AsSpan(12).ToArray();

            var cipher = payload.AsSpan(0, payload.Length - 16).ToArray();
            var tag = payload.AsSpan(payload.Length - 16).ToArray();

            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(Encoding.UTF8.GetBytes(secret), tag.Length))
                aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }

        public static string Sign(string hashKey, string queryEnc, string bodyEnc,
                                  IReadOnlyDictionary<string, string> headers)
        {
            var parts = new List<string>();
            foreach (var name in SignedHeaders)
            {
                if (headers.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                    parts.Add($"{name}={value}");
            }

            var signStr = string.Join("||", parts);
            if (!string.IsNullOrEmpty(queryEnc)) signStr += queryEnc;
            if (!string.IsNullOrEmpty(bodyEnc)) signStr += bodyEnc;

            return HmacSha256Hex(hashKey, signStr);
        }
    }
}
