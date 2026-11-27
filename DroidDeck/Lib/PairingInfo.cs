using DroidDeck.Auth;
using QRCoder;

namespace DroidDeck.Lib
{
    public static class PairingInfo
    {
        public static int Port { get; } =
            int.TryParse(Environment.GetEnvironmentVariable("KZCOMPANION_PORT"), out var p) && p is > 1023 and < 65536 ? p : 4787;

        public static string BuildUri()
        {
            var ip = NetworkInfo.GetLanIp();
            var key = ApiKeyProvider.GetKey();
            return $"droiddeck://pair?ip={ip}&port={Port}&key={key}";
        }

        public static string BuildBrowserUrl()
        {
            var ip = NetworkInfo.GetLanIp();
            var key = ApiKeyProvider.GetKey();
            return $"http://{ip}:{Port}/?pair={System.Uri.EscapeDataString(key)}";
        }

        public static byte[] BuildQrPng(string uri, int pixelsPerModule = 10)
        {
            var generator = new QRCodeGenerator();
            var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
            return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        }
    }
}
