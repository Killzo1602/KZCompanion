using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Builder;

namespace DroidDeck.Lib
{
    public static class L
    {
        public const string Source = "en";
        public const string Fallback = "en";

        public static IReadOnlyList<string> Languages { get; } = new[] { Source };

        public static string Language
        {
            get => Source;
            set { }
        }

        public static bool English
        {
            get => true;
            set { }
        }

        public static bool Translating => false;

        public static string T(string text) => text;

        public static string T(string text, params (string key, object? value)[] args) => Fill(text, args);

        public static string Ui(string text) => text;

        public static string Ui(string text, params (string key, object? value)[] args) => Fill(text, args);

        public static string? TOrNull(string? text) => text;

        private static string Fill(string text, (string key, object? value)[] args)
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            foreach (var (key, value) in args)
                text = text.Replace("{" + key + "}", value switch
                {
                    null => "",
                    IFormattable f => f.ToString(null, culture),
                    _ => value.ToString() ?? "",
                });
            return text;
        }

        public sealed class Text
        {
            private readonly string _text;
            private readonly (string key, object? value)[] _args;

            public Text(string text, params (string key, object? value)[] args)
            {
                _text = text;
                _args = args;
            }

            public override string ToString() => Fill(_text, _args);

            public string In(string language) => ToString();

            public string Plain => ToString();
        }

        public static string Message(Exception ex) => ex is LocalizedException le ? le.Text.ToString() : ex.Message;

        public static string LanguageOf(string? acceptLanguage) => Source;

        public static bool IsEnglish(string? acceptLanguage) => true;

        public static IApplicationBuilder UseLanguage(this IApplicationBuilder app) => app;
    }

    public class LocalizedException : InvalidOperationException
    {
        public L.Text Text { get; }

        public LocalizedException(L.Text text) : base(text.Plain) => Text = text;

        public LocalizedException(string text, params (string key, object? value)[] args) : this(new L.Text(text, args)) { }
    }
}
