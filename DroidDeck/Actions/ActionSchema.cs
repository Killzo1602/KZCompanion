using System.Collections.Generic;
using System.Linq;

namespace DroidDeck.Actions
{
    public class ActionSchema
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";

        public string Icon { get; set; } = "extension";

        public string Category { get; set; } = "Utilities";

        public string? Description { get; set; }

        public bool Privileged { get; set; }

        public bool Stateful { get; set; }

        public string? Editor { get; set; }

        public string? Settings { get; set; }

        public string? StateEvent { get; set; }

        public List<ActionField> Fields { get; set; } = new();
    }

    public static class FieldKind
    {
        public const string Text = "text";
        public const string Multiline = "multiline";
        public const string Number = "number";
        public const string Select = "select";
        public const string Toggle = "toggle";
    }

    public class ActionField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Kind { get; set; } = FieldKind.Text;
        public string? Default { get; set; }
        public string? Help { get; set; }
        public string? Hint { get; set; }
        public bool Required { get; set; }

        public List<FieldOption>? Options { get; set; }

        public string? OptionsSource { get; set; }

        public bool AllowCustom { get; set; }

        public double? Min { get; set; }
        public double? Max { get; set; }

        public string? Unit { get; set; }

        public Dictionary<string, string[]>? VisibleWhen { get; set; }

        public static ActionField Text(string key, string label, string? hint = null, string? help = null, bool required = false) =>
            new() { Key = key, Label = label, Kind = FieldKind.Text, Hint = hint, Help = help, Required = required };

        public static ActionField Multiline(string key, string label, string? hint = null, string? help = null) =>
            new() { Key = key, Label = label, Kind = FieldKind.Multiline, Hint = hint, Help = help };

        public static ActionField Number(string key, string label, double def, double? min = null, double? max = null, string? unit = null, string? help = null) =>
            new() { Key = key, Label = label, Kind = FieldKind.Number, Default = def.ToString(System.Globalization.CultureInfo.InvariantCulture), Min = min, Max = max, Unit = unit, Help = help };

        public static ActionField Toggle(string key, string label, bool def = false, string? help = null) =>
            new() { Key = key, Label = label, Kind = FieldKind.Toggle, Default = def ? "true" : "false", Help = help };

        public static ActionField Select(string key, string label, params (string value, string label)[] options) =>
            new()
            {
                Key = key,
                Label = label,
                Kind = FieldKind.Select,
                Default = options.Length > 0 ? options[0].value : null,
                Options = options.Select(o => new FieldOption(o.value, o.label)).ToList(),
            };

        public static ActionField Dynamic(string key, string label, string source, string? help = null, bool allowCustom = false) =>
            new() { Key = key, Label = label, Kind = FieldKind.Select, OptionsSource = source, Help = help, AllowCustom = allowCustom, Required = true };

        public ActionField When(string field, params string[] values)
        {
            VisibleWhen ??= new Dictionary<string, string[]>();
            VisibleWhen[field] = values;
            return this;
        }

        public ActionField WithDefault(string value)
        {
            Default = value;
            return this;
        }
    }

    public class FieldOption
    {
        public FieldOption() { }

        public FieldOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
