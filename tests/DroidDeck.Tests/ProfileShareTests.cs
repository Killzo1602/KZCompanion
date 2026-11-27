using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using DroidDeck.Auth;
using DroidDeck.Models;
using DroidDeck.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DroidDeck.Tests
{
    public class ProfileShareTests
    {
        private static DeckProfile Sample() => new()
        {
            Id = "orig",
            Name = "Live",
            IsDefault = true,
            Buttons =
            {
                new DeckButton { Id = "b1", Row = 0, Column = 0, Label = "Scene", IconName = "videocam", IconBase64 = new string('A', 5000),
                    ColumnSpan = 2, Action = new DeckAction { Type = "obs", Parameters = { ["operation"] = "setScene", ["scene"] = "Game" } } },
                new DeckButton { Id = "b2", Row = 1, Column = 0, Label = "Copy",
                    Action = new DeckAction { Type = "hotkey", Parameters = { ["keys"] = "^c" } } },
            },
        };

        [Fact]
        public void IdaEVolta_SemImagensEComIdsNovos()
        {
            var data = ProfileShare.Encode(Sample());
            Assert.True(data.Length < 600, $"texto grande demais: {data.Length}");
            Assert.DoesNotContain('+', data);
            Assert.DoesNotContain('/', data);

            var p = ProfileShare.Decode(data);
            Assert.NotEqual("orig", p.Id);
            Assert.False(p.IsDefault);
            Assert.Equal("Live", p.Name);
            Assert.Equal(2, p.Buttons.Count);
            Assert.Null(p.Buttons[0].IconBase64);
            Assert.Equal("videocam", p.Buttons[0].IconName);
            Assert.Equal(2, p.Buttons[0].ColumnSpan);
            Assert.Equal("Game", p.Buttons[0].Action!.Parameters["scene"]);
            Assert.NotEqual("b1", p.Buttons[0].Id);
        }

        [Theory]
        [InlineData("")]
        [InlineData("isso nao e base64!")]
        [InlineData("aGVsbG8")]
        public void TextoInvalido_Erro(string data) =>
            Assert.Throws<InvalidDataException>(() => ProfileShare.Decode(data));
    }
}
