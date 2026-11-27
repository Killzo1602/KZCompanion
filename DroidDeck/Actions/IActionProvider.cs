using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DroidDeck.Models;

namespace DroidDeck.Actions
{
    public interface IActionProvider
    {
        string Type { get; }

        IReadOnlyCollection<string> Aliases => Array.Empty<string>();

        ActionSchema Describe();

        Task ExecuteAsync(ActionContext context);
    }

    public interface IStatefulActionProvider : IActionProvider
    {
        string StateEvent { get; }

        object GetState();
    }

    public class ActionContext
    {
        public ActionContext(DeckAction action, string? buttonId, Func<DeckAction, string?, Task> execute)
        {
            Action = action;
            ButtonId = buttonId;
            _execute = execute;
        }

        private readonly Func<DeckAction, string?, Task> _execute;

        public DeckAction Action { get; }

        public string? ButtonId { get; }

        public IDictionary<string, string> Parameters => Action.Parameters;

        public string Get(string key, string fallback = "") =>
            Parameters.TryGetValue(key, out var v) && v != null ? v : fallback;

        public string Operation(string fallback) =>
            (Get("operation") is { Length: > 0 } op ? op : fallback).Trim().ToLowerInvariant();

        public int Int(string key, int fallback) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public double Number(string key, double fallback) =>
            double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public Task ExecuteAsync(DeckAction action) => _execute(action, ButtonId);
    }
}
