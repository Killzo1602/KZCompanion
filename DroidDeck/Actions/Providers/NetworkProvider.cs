using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DroidDeck.Actions.Providers
{
    public class NetworkProvider : IActionProvider
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public string Type => "network";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Network (OSC, TCP, UDP)",
            Icon = "lan",
            Category = "Utilities",
            Description = "Sends an OSC, UDP or TCP message from the PC: mixing consoles, QLab and other devices on the network.",
            Fields =
            {
                ActionField.Select("protocol", "Protocol", ("osc", "OSC"), ("udp", "UDP"), ("tcp", "TCP")),
                ActionField.Text("host", "Address", "192.168.0.50", required: true),
                ActionField.Number("port", "Port", 8000, 1, 65535),
                ActionField.Text("path", "OSC path", "/ch/01/mix/on", required: true).When("protocol", "osc"),
                ActionField.Multiline("args", "Arguments (optional)", "i:1",
                    "One per line: i:1 (integer), f:0.5 (decimal), s:text, T or F. With no prefix, the type is inferred.")
                    .When("protocol", "osc"),
                ActionField.Multiline("message", "Message", "PLAY").When("protocol", "udp", "tcp"),
                ActionField.Select("encoding", "Format",
                    ("text", "Text"),
                    ("hex", "Hexadecimal"))
                    .When("protocol", "udp", "tcp"),
                ActionField.Select("ending", "Message ending",
                    ("none", "Nothing"),
                    ("lf", "New line (LF)"),
                    ("crlf", "CR + LF"),
                    ("cr", "CR"))
                    .When("encoding", "text")
                    .When("protocol", "udp", "tcp"),
            },
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            var host = c.Get("host").Trim();
            var port = c.Int("port", 0);
            if (host.Length == 0 || port is < 1 or > 65535)
                throw new InvalidOperationException("Preencha o endereço e a porta.");

            var protocol = c.Get("protocol", "osc").ToLowerInvariant();
            var payload = protocol == "osc"
                ? OscMessage.Encode(c.Get("path"), OscMessage.ParseArgs(c.Get("args")))
                : BuildPayload(c.Get("message"), c.Get("encoding", "text"), c.Get("ending", "none"));

            using var cts = new CancellationTokenSource(Timeout);
            if (protocol == "tcp")
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, cts.Token);
                await tcp.GetStream().WriteAsync(payload, cts.Token);
            }
            else
            {
                using var udp = new UdpClient();
                await udp.SendAsync(payload, host, port, cts.Token);
            }
        }

        public static byte[] BuildPayload(string message, string encoding, string ending)
        {
            if (string.Equals(encoding, "hex", StringComparison.OrdinalIgnoreCase))
            {
                var hex = message.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("-", "");
                try { return Convert.FromHexString(hex); }
                catch (FormatException) { throw new InvalidOperationException("Hexadecimal inválido."); }
            }
            var end = ending.ToLowerInvariant() switch
            {
                "lf" => "\n",
                "crlf" => "\r\n",
                "cr" => "\r",
                _ => "",
            };
            return Encoding.UTF8.GetBytes(message + end);
        }
    }

    public static class OscMessage
    {
        public static List<object> ParseArgs(string args)
        {
            var result = new List<object>();
            foreach (var raw in args.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result.Add(ParseArg(raw));
            return result;
        }

        private static object ParseArg(string arg)
        {
            if (arg.Length >= 2 && arg[1] == ':')
            {
                var value = arg[2..];
                switch (arg[0])
                {
                    case 'i':
                        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                            ? i : throw new InvalidOperationException("Argumento OSC inválido.");
                    case 'f':
                        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                            ? f : throw new InvalidOperationException("Argumento OSC inválido.");
                    case 's':
                        return value;
                }
            }
            if (arg is "T" or "true") return true;
            if (arg is "F" or "false") return false;
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)) return whole;
            if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return real;
            return arg;
        }

        public static byte[] Encode(string address, IReadOnlyList<object> args)
        {
            var path = address.Trim();
            if (!path.StartsWith('/') || path.Contains(' '))
                throw new InvalidOperationException("O caminho OSC começa com / e não tem espaços.");

            var tags = new StringBuilder(",");
            foreach (var a in args)
                tags.Append(a switch
                {
                    int => 'i',
                    float => 'f',
                    string => 's',
                    bool b => b ? 'T' : 'F',
                    _ => throw new InvalidOperationException("Argumento OSC inválido."),
                });

            using var ms = new MemoryStream();
            WriteString(ms, path);
            WriteString(ms, tags.ToString());
            Span<byte> number = stackalloc byte[4];
            foreach (var a in args)
            {
                switch (a)
                {
                    case int i:
                        BinaryPrimitives.WriteInt32BigEndian(number, i);
                        ms.Write(number);
                        break;
                    case float f:
                        BinaryPrimitives.WriteSingleBigEndian(number, f);
                        ms.Write(number);
                        break;
                    case string s:
                        WriteString(ms, s);
                        break;
                }
            }
            return ms.ToArray();
        }

        private static void WriteString(Stream output, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            output.Write(bytes);
            output.Write(new byte[4 - bytes.Length % 4]);
        }
    }
}
