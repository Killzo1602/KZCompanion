using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using DroidDeck.Models;

namespace DroidDeck.Services
{
    public class BackupService
    {
        public const int FormatVersion = 1;
        public const long MaxSize = 50 * 1024 * 1024;

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        private readonly StreamDeckConfigService _config;

        public BackupService(StreamDeckConfigService config) => _config = config;

        public byte[] Export()
        {
            var profiles = _config.GetProfiles();
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write(zip, "manifest.json", new Manifest
                {
                    Format = FormatVersion,
                    App = "DroidDeck",
                    Version = typeof(BackupService).Assembly.GetName().Version?.ToString(3),
                    ExportedAt = DateTime.UtcNow,
                    Profiles = profiles.Count,
                });
                foreach (var p in profiles)
                    Write(zip, $"profiles/{p.Id}.json", p);
            }
            return ms.ToArray();
        }

        public static List<DeckProfile> Read(Stream stream)
        {
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException("Not a KZ Companion backup (the file is not a valid .droiddeck).");
            }

            try
            {
                using var _ = zip;
                var manifestEntry = zip.GetEntry("manifest.json")
                    ?? throw new InvalidDataException("Not a KZ Companion backup (manifest.json is missing).");
                Manifest? manifest;
                using (var s = manifestEntry.Open())
                    manifest = JsonSerializer.Deserialize<Manifest>(s, Json);
                if (manifest == null || manifest.App != "DroidDeck")
                    throw new InvalidDataException("Not a KZ Companion backup.");
                if (manifest.Format > FormatVersion)
                    throw new InvalidDataException("Backup from a newer KZ Companion version. Update KZ Companion on the PC.");

                var profiles = new List<DeckProfile>();
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("profiles/") && e.FullName.EndsWith(".json")))
                {
                    using var s = entry.Open();
                    var profile = JsonSerializer.Deserialize<DeckProfile>(s, Json);
                    if (profile != null && StreamDeckConfigService.IsValidProfileId(profile.Id))
                        profiles.Add(profile);
                }
                return profiles;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                throw new InvalidDataException("Corrupted backup file.", ex);
            }
        }

        public int Import(IEnumerable<DeckProfile> profiles)
        {
            var count = 0;
            foreach (var p in profiles)
            {
                _config.SaveProfile(p);
                count++;
            }
            return count;
        }

        private static void Write(ZipArchive zip, string name, object value)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            JsonSerializer.Serialize(s, value, Json);
        }

        public class Manifest
        {
            public int Format { get; set; }
            public string? App { get; set; }
            public string? Version { get; set; }
            public DateTime ExportedAt { get; set; }
            public int Profiles { get; set; }
        }
    }
}
