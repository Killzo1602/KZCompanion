using System;
using System.IO;

namespace DroidDeck.Lib
{
    public static class WebRoot
    {
        public static string ResolveContentRoot(string workingDir, string baseDir)
        {
            bool baseHas = HasIndex(baseDir), cwdHas = HasIndex(workingDir);
            if (cwdHas && IsDevBuildOutput(baseDir)) return workingDir;
            if (baseHas) return baseDir;
            if (cwdHas) return workingDir;
            return baseDir;
        }

        public static bool IsDevBuildOutput(string baseDir)
        {
            var p = baseDir.Replace('/', '\\');
            return p.Contains("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasIndex(string dir) => File.Exists(Path.Combine(dir, "wwwroot", "index.html"));
    }
}
