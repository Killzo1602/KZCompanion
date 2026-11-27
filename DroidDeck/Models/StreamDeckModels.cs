using System.Collections.Generic;

namespace DroidDeck.Models
{
    public class DeckProfile
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Profile";
        public int Rows { get; set; } = 3;
        public int Columns { get; set; } = 4;
        public bool IsDefault { get; set; } = false;
        public List<DeckButton> Buttons { get; set; } = new List<DeckButton>();

        public List<string>? AutoSwitchApps { get; set; }
    }

    public class DeckButton
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public int Row { get; set; }
        public int Column { get; set; }
        public string? Label { get; set; }
        public string? IconBase64 { get; set; }
        public string? IconName { get; set; }
        public string? BackgroundColor { get; set; }
        public string? ActiveColor { get; set; }
        public string? DynamicType { get; set; }
        public DeckAction? Action { get; set; }

        public DeckAction? LongPressAction { get; set; }

        public DeckAction? DoubleTapAction { get; set; }

        public bool Toggle { get; set; }

        public DeckAction? ActiveAction { get; set; }

        public string? ActiveLabel { get; set; }
        public string? ActiveIconName { get; set; }
        public string? ActiveIconBase64 { get; set; }

        public Dictionary<string, string>? DynamicParameters { get; set; }

        public int ColumnSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;

        public DeckAction? ActionFor(string? gesture) => (gesture ?? "").ToLowerInvariant() switch
        {
            "longpress" => LongPressAction,
            "doubletap" => DoubleTapAction,
            "active" => ActiveAction,
            _ => Action,
        };

        public static readonly string[] Gestures = { "tap", "longPress", "doubleTap" };

        public static readonly string[] ActionSlots = { "tap", "longPress", "doubleTap", "active" };
    }

    public class DeckAction
    {
        public string Type { get; set; } = "none";
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class MultiStep
    {
        public string? Type { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public int DelayMs { get; set; }

        public StepCondition? If { get; set; }
    }

    public class StepCondition
    {
        public string? Check { get; set; }
        public string? Value { get; set; }
        public bool Negate { get; set; }
    }

    public class DeviceLayout
    {
        public int Rows { get; set; } = 3;
        public int Columns { get; set; } = 5;
    }
}
