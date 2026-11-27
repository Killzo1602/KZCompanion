using System.Collections.Generic;
using DroidDeck.Services.Tuya;
using Xunit;

namespace DroidDeck.Tests
{
    public class TuyaCryptoTests
    {
        private const string Rid = "3f2b1a4c-0000-4d5e-8f90-abcdef123456";
        private const string RefreshToken = "REFRESH_TOKEN_FIXO_PARA_TESTE";
        private const string ExpectedHashKey = "88dbef1587b3290076f84e81e415a0d8";
        private const string ExpectedSecret = "f7c97b12c2beb4f1";
        private const string Plaintext = "{\"homeId\":\"158770227\"}";

        private const string PythonCiphertext =
            "c01mTTRQaXp4OFN3aoNlUIF2Zz88FfnLQS5bmK90lpT1QFOrPio5rTl/dYcOtVK5yv4=";

        private const string ExpectedSign =
            "b415ce5df9e0cc41b6779c5e991cf4727372feb2f2b9fcc19b3964466f2d5ac7";

        private static Dictionary<string, string> Headers() => new()
        {
            ["X-appKey"] = "HA_test",
            ["X-requestId"] = Rid,
            ["X-sid"] = "",
            ["X-time"] = "1787000000000",
            ["X-token"] = "TOKEN_TESTE",
        };

        [Fact]
        public void HashKey_MatchesReferenceImplementation()
        {
            Assert.Equal(ExpectedHashKey, TuyaCrypto.HashKey(Rid, RefreshToken));
        }

        [Fact]
        public void GenerateSecret_MatchesReferenceImplementation()
        {
            Assert.Equal(ExpectedSecret, TuyaCrypto.GenerateSecret(Rid, "", ExpectedHashKey));
        }

        [Fact]
        public void GenerateSecret_IsSixteenChars()
        {
            Assert.Equal(16, TuyaCrypto.GenerateSecret(Rid, "", ExpectedHashKey).Length);
        }

        [Fact]
        public void Decrypt_ReadsCiphertextProducedByPythonSdk()
        {
            Assert.Equal(Plaintext, TuyaCrypto.Decrypt(PythonCiphertext, ExpectedSecret));
        }

        [Fact]
        public void EncryptDecrypt_RoundTrips()
        {
            var encrypted = TuyaCrypto.Encrypt(Plaintext, ExpectedSecret);

            Assert.NotEqual(PythonCiphertext, encrypted);
            Assert.Equal(Plaintext, TuyaCrypto.Decrypt(encrypted, ExpectedSecret));
        }

        [Fact]
        public void Encrypt_PrefixIsSixteenCharBase64Nonce()
        {
            var encrypted = TuyaCrypto.Encrypt(Plaintext, ExpectedSecret);
            Assert.DoesNotContain("=", encrypted[..16]);
        }

        [Fact]
        public void Sign_MatchesReferenceImplementation()
        {
            var sign = TuyaCrypto.Sign(ExpectedHashKey, PythonCiphertext, "", Headers());
            Assert.Equal(ExpectedSign, sign);
        }

        [Fact]
        public void Sign_SkipsEmptyHeaders()
        {
            var headers = Headers();
            var withEmptySid = TuyaCrypto.Sign(ExpectedHashKey, PythonCiphertext, "", headers);

            headers.Remove("X-sid");
            var withoutSid = TuyaCrypto.Sign(ExpectedHashKey, PythonCiphertext, "", headers);

            Assert.Equal(withEmptySid, withoutSid);
        }

        [Fact]
        public void Sign_ChangesWhenBodyChanges()
        {
            var a = TuyaCrypto.Sign(ExpectedHashKey, "", "corpo-a", Headers());
            var b = TuyaCrypto.Sign(ExpectedHashKey, "", "corpo-b", Headers());
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Sign_ConcatenatesQueryThenBodyWithoutSeparator()
        {
            var queryOnly = TuyaCrypto.Sign(ExpectedHashKey, "XYZ", "", Headers());
            var bodyOnly = TuyaCrypto.Sign(ExpectedHashKey, "", "XYZ", Headers());
            var split = TuyaCrypto.Sign(ExpectedHashKey, "XY", "Z", Headers());

            Assert.Equal(queryOnly, bodyOnly);
            Assert.Equal(queryOnly, split);
        }
    }
}
