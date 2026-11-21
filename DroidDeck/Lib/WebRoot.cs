using System;
using System.IO;

namespace DroidDeck.Lib
{
    /// <summary>
    /// Qual wwwroot o configurador serve. Um .exe publicado serve SEMPRE o que esta ao lado dele:
    /// antes, qualquer wwwroot no diretorio de trabalho ganhava, e um KZCompanion.exe iniciado a
    /// partir de DroidDeck\ (ou de um atalho com "Iniciar em" la) mostrava o front-end velho do
    /// codigo-fonte (que nao vai para o git e fica para tras) em vez do proprio. So em
    /// desenvolvimento (bin\Debug) o wwwroot do fonte tem preferencia, para recarregar sem copiar.
    /// </summary>
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
