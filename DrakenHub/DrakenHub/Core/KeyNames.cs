namespace DrakenHub.Core;

internal static class KeyNames
{
    private static readonly Dictionary<Keys, string> Special = new()
    {
        [Keys.None] = "Нет",
        [Keys.Insert] = "Insert",
        [Keys.End] = "End",
        [Keys.Home] = "Home",
        [Keys.Escape] = "Escape",
        [Keys.Space] = "Space",
        [Keys.LMenu] = "Left Alt",
        [Keys.RMenu] = "Right Alt",
        [Keys.LControlKey] = "Left Ctrl",
        [Keys.RControlKey] = "Right Ctrl",
        [Keys.LShiftKey] = "Left Shift",
        [Keys.RShiftKey] = "Right Shift",
        [Keys.OemMinus] = "OemMinus",
        [Keys.Oemplus] = "OemPlus",
        [Keys.Oemcomma] = "OemComma",
        [Keys.OemPeriod] = "OemPeriod",
        [Keys.OemQuestion] = "OemQuestion",
        [Keys.Oemtilde] = "OemTilde",
        [Keys.OemOpenBrackets] = "OemOpenBrackets",
        [Keys.OemCloseBrackets] = "OemCloseBrackets",
        [Keys.OemPipe] = "OemPipe",
        [Keys.OemQuotes] = "OemQuotes",
        [Keys.OemSemicolon] = "OemSemicolon",
        [Keys.NumPad0] = "NumPad0",
        [Keys.NumPad1] = "NumPad1",
        [Keys.NumPad2] = "NumPad2",
        [Keys.NumPad3] = "NumPad3",
        [Keys.NumPad4] = "NumPad4",
        [Keys.NumPad5] = "NumPad5",
        [Keys.NumPad6] = "NumPad6",
        [Keys.NumPad7] = "NumPad7",
        [Keys.NumPad8] = "NumPad8",
        [Keys.NumPad9] = "NumPad9",
        [Keys.Multiply] = "NumPad*",
        [Keys.Add] = "NumPad+",
        [Keys.Subtract] = "NumPad-",
        [Keys.Divide] = "NumPad/",
        [Keys.Decimal] = "NumPad.",
    };

    public static string Format(Keys key)
    {
        if (key == Keys.None) return "Нет";
        if (Special.TryGetValue(key, out var name)) return name;
        if (key is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (key is >= Keys.A and <= Keys.Z) return key.ToString();
        if (key is >= Keys.F1 and <= Keys.F24) return key.ToString();
        return key.ToString();
    }

    public static Keys Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "Нет" or "None" or "No")
            return Keys.None;

        foreach (var pair in Special)
        {
            if (string.Equals(pair.Value, text, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }

        if (text.Length == 1 && char.IsDigit(text[0]))
            return Keys.D0 + (text[0] - '0');

        if (text.Length == 1 && char.IsLetter(text[0]))
            return (Keys)char.ToUpperInvariant(text[0]);

        return Enum.TryParse<Keys>(text, true, out var key) ? key : Keys.None;
    }

    public static string Display(string? stored) => Format(Parse(stored));
}
