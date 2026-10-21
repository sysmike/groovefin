namespace Groovefin.Core.Shortcuts;

/// <summary>
/// A key with its modifiers, e.g. Ctrl+Right. <see cref="Key"/> is a Windows virtual key name as
/// <c>VirtualKey.ToString()</c> gives it ("Right", "K", "F11", "Number1"); its case does not matter.
/// </summary>
public readonly record struct KeyGesture(string Key, bool Control = false, bool Alt = false, bool Shift = false)
{
    /// <summary>Gets a value indicating whether neither Ctrl nor Alt is held (Shift alone still types).</summary>
    public bool IsBare => !Control && !Alt;

    public bool Equals(KeyGesture other) =>
        Control == other.Control && Alt == other.Alt && Shift == other.Shift
        && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(Control, Alt, Shift, StringComparer.OrdinalIgnoreCase.GetHashCode(Key ?? string.Empty));

    /// <summary>The form kept in the settings file, the same in every language, e.g. "Ctrl+Shift+K".</summary>
    public override string ToString() =>
        string.Concat(Control ? "Ctrl+" : string.Empty, Alt ? "Alt+" : string.Empty, Shift ? "Shift+" : string.Empty, Key);

    /// <summary>Reads the form <see cref="ToString"/> writes.</summary>
    public static bool TryParse(string? text, out KeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+');
        bool control = false, alt = false, shift = false;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier)
            {
                case "Ctrl":
                    control = true;
                    break;
                case "Alt":
                    alt = true;
                    break;
                case "Shift":
                    shift = true;
                    break;
                default:
                    return false;
            }
        }

        if (parts[^1].Length == 0)
        {
            return false;
        }

        gesture = new KeyGesture(parts[^1], control, alt, shift);
        return true;
    }
}
