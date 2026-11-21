using System;
using System.IO;
using DroidDeck.Lib;
using Xunit;

namespace DroidDeck.Tests
{
    /// <summary>
    /// Regressao: o configurador do Windows mostrou o deck antigo (Buttons, Profiles, importar do
    /// Stream Deck) porque um wwwroot velho do codigo-fonte (fora do git) ganhou do wwwroot do .exe.
    /// </summary>
    public class WebRootSelectionTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "kz-webroot-" + Guid.NewGuid().ToString("N"));

        private string MakeDir(string name, bool withIndex)
        {
            var dir = Path.Combine(_tmp, name);
            Directory.CreateDirectory(Path.Combine(dir, "wwwroot"));
            if (withIndex) File.WriteAllText(Path.Combine(dir, "wwwroot", "index.html"), "x");
            return dir;
        }

        public void Dispose()
        {
            try { Directory.Delete(_tmp, true); } catch { }
        }

        [Fact]
        public void ExePublicado_ServeOWwwrootAoLadoDele_MesmoComOutroNoDiretorioDeTrabalho()
        {
            var published = MakeDir("dist\\KZCompanion-win-x64", true);
            var sourceTree = MakeDir("repo\\DroidDeck", true); // wwwroot velho do fonte
            Assert.Equal(published, WebRoot.ResolveContentRoot(sourceTree, published));
        }

        [Fact]
        public void EmDesenvolvimento_OWwwrootDoFonteNoDiretorioAtualTemPreferencia()
        {
            var bin = MakeDir("repo\\DroidDeck\\bin\\Debug\\net8.0-windows10.0.17763.0", true);
            var source = MakeDir("repo\\DroidDeck", true);
            Assert.Equal(source, WebRoot.ResolveContentRoot(source, bin));
        }

        [Fact]
        public void BinRelease_NaoEDesenvolvimento()
        {
            Assert.False(WebRoot.IsDevBuildOutput(@"D:\repo\DroidDeck\bin\Release\net8.0-windows10.0.17763.0\win-x64\"));
            Assert.False(WebRoot.IsDevBuildOutput(@"D:\repo\dist\KZCompanion-win-x64\"));
            Assert.True(WebRoot.IsDevBuildOutput(@"D:\repo\DroidDeck\bin\Debug\net8.0-windows10.0.17763.0\"));
        }

        [Fact]
        public void SemWwwrootAoLado_UsaODiretorioDeTrabalho_ESemNenhum_OdoExecutavel()
        {
            var empty = MakeDir("dist\\empty", false);
            var source = MakeDir("repo\\DroidDeck", true);
            Assert.Equal(source, WebRoot.ResolveContentRoot(source, empty));
            var none = MakeDir("other", false);
            Assert.Equal(empty, WebRoot.ResolveContentRoot(none, empty));
        }
    }

    /// <summary>
    /// O front-end embutido (DroidDeck/wwwroot, que nao vai para o git) nao pode ter o deck antigo
    /// nem faltar o menu atual. Sem wwwroot na maquina (CI limpo), o teste nao tem o que conferir.
    /// </summary>
    public class WebFrontendContentTests
    {
        private static readonly string[] Legacy = { "Import from Stream Deck", "Automations", "Profiles", "StreamDeck/profiles", "Edit profile" };
        private static readonly string[] Required = { "Applications", "Integrations", "Appearance" };

        private static string? FindWwwroot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "DroidDeck", "wwwroot", "main.dart.js");
                if (File.Exists(candidate)) return Path.GetDirectoryName(candidate);
            }
            return null;
        }

        [Fact]
        public void WwwrootDoFonte_SemDeckAntigo_ComOMenuAtual()
        {
            var dir = FindWwwroot();
            if (dir == null) return;
            var js = File.ReadAllText(Path.Combine(dir, "main.dart.js"));
            foreach (var word in Legacy) Assert.DoesNotContain(word, js);
            foreach (var word in Required) Assert.Contains(word, js);
            Assert.DoesNotContain("deck de bot", File.ReadAllText(Path.Combine(dir, "index.html")), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ScriptDePublicacao_VerificaOFrontendEnaoSobrescreveExecutavelEmUso()
        {
            var root = AppContext.BaseDirectory;
            string? script = null;
            for (var dir = new DirectoryInfo(root); dir != null && script == null; dir = dir.Parent)
            {
                var c = Path.Combine(dir.FullName, "scripts", "publish-windows.ps1");
                if (File.Exists(c)) script = c;
            }
            Assert.NotNull(script);
            var text = File.ReadAllText(script!);
            Assert.Contains("Assert-WebClean", text);
            Assert.Contains("Remove-Item $wwwroot", text);   // substitui, nao mistura
            Assert.Contains("is running from", text);        // recusa pasta em uso
            Assert.Contains("Get-FileHash", text);           // confere o que foi publicado
        }
    }
}
