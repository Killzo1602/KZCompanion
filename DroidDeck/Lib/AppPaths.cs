using System;
using System.IO;

namespace DroidDeck.Lib
{
    public static class AppPaths
    {
        public const string DataDirEnvVar = "DROIDDECK_DATA_DIR";

        private static string? Override =>
            Environment.GetEnvironmentVariable(DataDirEnvVar) is { Length: > 0 } dir ? dir : null;

        public static string LocalDir => Ensure(Override ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KZCompanion"));

        public static string RoamingDir => Ensure(Override is { } dir
            ? Path.Combine(dir, "Roaming")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KZCompanion"));

        private static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
