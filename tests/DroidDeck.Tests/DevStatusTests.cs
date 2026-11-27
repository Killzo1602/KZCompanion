using System;
using System.IO;
using System.Linq;
using DroidDeck.Services;
using Xunit;

namespace DroidDeck.Tests
{
    public class DevStatusTests
    {
        [Fact]
        public void Sessoes_SoAsVivas_AsQueEsperamPrimeiro_SemCaminhoCompleto()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dd-cc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "sessions"));
            try
            {
                void Write(int pid, string status, string cwd) => File.WriteAllText(Path.Combine(dir, "sessions", pid + ".json"),
                    $$"""{"pid":{{pid}},"sessionId":"s{{pid}}","cwd":"{{cwd}}","status":"{{status}}","statusUpdatedAt":1790000000000,"name":"secret title"}""");
                Write(1, "shell", "D:\\\\Projects\\\\Alpha");
                Write(2, "waiting", "/home/me/Beta/");
                Write(3, "busy", "D:\\\\Dead");
                Write(4, "idle", "D:\\\\Projects\\\\Gamma");
                File.WriteAllText(Path.Combine(dir, "sessions", "bad.json"), "{");

                var list = DevStatusService.ClaudeSessions(dir, pid => pid != 3);

                Assert.Equal(new[] { "Beta", "Alpha", "Gamma" }, list.Select(s => s.Project));
                Assert.Equal(new[] { "waiting", "busy", "idle" }, list.Select(s => s.Status));
                Assert.NotNull(list[0].Since);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void GitHub_EstadoDoCiPorRepositorioEPr()
        {
            var g = DevStatusService.ParseGitHub("""
                {"data":{"viewer":{
                  "repositories":{"nodes":[
                    {"name":"app","defaultBranchRef":{"name":"main","target":{"messageHeadline":"fix","committedDate":"2026-11-27T10:00:00Z","statusCheckRollup":{"state":"FAILURE"}}}},
                    {"name":"noci","defaultBranchRef":{"name":"main","target":{"messageHeadline":"x","statusCheckRollup":null}}},
                    {"name":"empty","defaultBranchRef":null}]},
                  "pullRequests":{"nodes":[
                    {"title":"Add thing","number":7,"repository":{"name":"app"},"commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"PENDING"}}}]}}]}}}}
                """);
            Assert.Equal(new[] { "failure", "none" }, g.Repos.Select(r => r.State));
            var pr = Assert.Single(g.PullRequests);
            Assert.Equal(("app", "#7", "pending"), (pr.Repo, pr.Branch, pr.State));
        }

        [Fact]
        public void Docker_RodandoPrimeiro_LinhaRuimIgnorada()
        {
            var list = DevStatusService.ParseContainers(
                "{\"ID\":\"aaaaaaaaaaaa\",\"Names\":\"db\",\"Image\":\"postgres\",\"State\":\"exited\",\"Status\":\"Exited (0)\"}\n" +
                "oops\n" +
                "{\"ID\":\"bbbbbbbbbbbb\",\"Names\":\"web\",\"Image\":\"nginx\",\"State\":\"running\",\"Status\":\"Up 2 hours\"}\n");
            Assert.Equal(new[] { "web", "db" }, list.Select(c => c.Name));
            Assert.True(list[0].Running);
        }

        [Fact]
        public async System.Threading.Tasks.Task Docker_IdInvalidoNaoViraComando()
        {
            var (found, _) = await new DevStatusService().DockerActionAsync("--help", "stop", default);
            Assert.False(found);
            var (_, error) = await new DevStatusService().DockerActionAsync("aaaaaaaaaaaa", "rm", default);
            Assert.NotNull(error);
        }
    }
}
