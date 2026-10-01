using System;
using Avalonia.Input;

namespace RecordVideoAudio.GMTPC.Services;

public static class KeyBindingHelper
{
    public static (int vkCode, string displayName) FromAvaloniaKey(Key key)
    {
        return key switch
        {
            // Number Row (D-pad / Hàng phím số trên)
            Key.D0 => (0x30, "D0"),
            Key.D1 => (0x31, "D1"),
            Key.D2 => (0x32, "D2"),
            Key.D3 => (0x33, "D3"),
            Key.D4 => (0x34, "D4"),
            Key.D5 => (0x35, "D5"),
            Key.D6 => (0x36, "D6"),
            Key.D7 => (0x37, "D7"),
            Key.D8 => (0x38, "D8"),
            Key.D9 => (0x39, "D9"),

            // Numpad (Bàn phím số phụ)
            Key.NumPad0 => (0x60, "NumPad0"),
            Key.NumPad1 => (0x61, "NumPad1"),
            Key.NumPad2 => (0x62, "NumPad2"),
            Key.NumPad3 => (0x63, "NumPad3"),
            Key.NumPad4 => (0x64, "NumPad4"),
            Key.NumPad5 => (0x65, "NumPad5"),
            Key.NumPad6 => (0x66, "NumPad6"),
            Key.NumPad7 => (0x67, "NumPad7"),
            Key.NumPad8 => (0x68, "NumPad8"),
            Key.NumPad9 => (0x69, "NumPad9"),
            Key.Multiply => (0x6A, "NumPad*"),
            Key.Add => (0x6B, "NumPad+"),
            Key.Subtract => (0x6D, "NumPad-"),
            Key.Decimal => (0x6E, "NumPad."),
            Key.Divide => (0x6F, "NumPad/"),

            // Letters A-Z
            Key.A => (0x41, "A"),
            Key.B => (0x42, "B"),
            Key.C => (0x43, "C"),
            Key.D => (0x44, "D"),
            Key.E => (0x45, "E"),
            Key.F => (0x46, "F"),
            Key.G => (0x47, "G"),
            Key.H => (0x48, "H"),
            Key.I => (0x49, "I"),
            Key.J => (0x4A, "J"),
            Key.K => (0x4B, "K"),
            Key.L => (0x4C, "L"),
            Key.M => (0x4D, "M"),
            Key.N => (0x4E, "N"),
            Key.O => (0x4F, "O"),
            Key.P => (0x50, "P"),
            Key.Q => (0x51, "Q"),
            Key.R => (0x52, "R"),
            Key.S => (0x53, "S"),
            Key.T => (0x54, "T"),
            Key.U => (0x55, "U"),
            Key.V => (0x56, "V"),
            Key.W => (0x57, "W"),
            Key.X => (0x58, "X"),
            Key.Y => (0x59, "Y"),
            Key.Z => (0x5A, "Z"),

            // Function Keys F1-F12
            Key.F1 => (0x70, "F1"),
            Key.F2 => (0x71, "F2"),
            Key.F3 => (0x72, "F3"),
            Key.F4 => (0x73, "F4"),
            Key.F5 => (0x74, "F5"),
            Key.F6 => (0x75, "F6"),
            Key.F7 => (0x76, "F7"),
            Key.F8 => (0x77, "F8"),
            Key.F9 => (0x78, "F9"),
            Key.F10 => (0x79, "F10"),
            Key.F11 => (0x7A, "F11"),
            Key.F12 => (0x7B, "F12"),

            // OEM & Punctuation Keys
            Key.OemSemicolon => (0xBA, ";"),
            Key.OemPlus => (0xBB, "="),
            Key.OemComma => (0xBC, ","),
            Key.OemMinus => (0xBD, "-"),
            Key.OemPeriod => (0xBE, "."),
            Key.OemQuestion => (0xBF, "/"),
            Key.OemTilde => (0xC0, "`"),
            Key.OemOpenBrackets => (0xDB, "["),
            Key.OemPipe => (0xDC, "\\"),
            Key.OemCloseBrackets => (0xDD, "]"),
            Key.OemQuotes => (0xDE, "'"),

            // Special & Navigation Keys
            Key.Space => (0x20, "Space"),
            Key.Tab => (0x09, "Tab"),
            Key.Return => (0x0D, "Enter"),
            Key.Back => (0x08, "Backspace"),
            Key.Escape => (0x1B, "Escape"),
            Key.Insert => (0x2D, "Insert"),
            Key.Delete => (0x2E, "Delete"),
            Key.Home => (0x24, "Home"),
            Key.End => (0x23, "End"),
            Key.PageUp => (0x21, "PageUp"),
            Key.PageDown => (0x22, "PageDown"),
            Key.Up => (0x26, "Up"),
            Key.Down => (0x28, "Down"),
            Key.Left => (0x25, "Left"),
            Key.Right => (0x27, "Right"),

            _ => (0, string.Empty)
        };
    }

    public static (int vkCode, string displayName) ParseKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (0, string.Empty);

        string clean = input.Trim().ToUpperInvariant();

        // 1. D-pad (D0 - D9)
        if (clean.StartsWith("D") && clean.Length == 2 && char.IsDigit(clean[1]))
        {
            int digit = clean[1] - '0';
            return (0x30 + digit, $"D{digit}");
        }

        // Single digit -> map to D0-D9
        if (clean.Length == 1 && char.IsDigit(clean[0]))
        {
            int digit = clean[0] - '0';
            return (0x30 + digit, $"D{digit}");
        }

        // 2. Numpad (NUMPAD0 - NUMPAD9, NP0 - NP9)
        if (clean.StartsWith("NUMPAD") && clean.Length >= 7)
        {
            string sub = clean.Substring(6);
            if (sub.Length == 1 && char.IsDigit(sub[0]))
            {
                int digit = sub[0] - '0';
                return (0x60 + digit, $"NumPad{digit}");
            }
            if (sub == "*" || sub == "MULTIPLY") return (0x6A, "NumPad*");
            if (sub == "+" || sub == "ADD") return (0x6B, "NumPad+");
            if (sub == "-" || sub == "SUBTRACT") return (0x6D, "NumPad-");
            if (sub == "." || sub == "DECIMAL") return (0x6E, "NumPad.");
            if (sub == "/" || sub == "DIVIDE") return (0x6F, "NumPad/");
        }

        if (clean.StartsWith("NP") && clean.Length == 3 && char.IsDigit(clean[2]))
        {
            int digit = clean[2] - '0';
            return (0x60 + digit, $"NumPad{digit}");
        }

        // 3. Letters A - Z
        if (clean.Length == 1 && clean[0] >= 'A' && clean[0] <= 'Z')
        {
            return (clean[0], clean);
        }

        // 4. Function keys F1 - F12
        if (clean.StartsWith("F") && int.TryParse(clean.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
        {
            return (0x70 + (fNum - 1), $"F{fNum}");
        }

        // 5. Punctuation
        return clean switch
        {
            ";" or "SEMICOLON" => (0xBA, ";"),
            "=" or "EQUAL" or "EQUALS" or "PLUS" => (0xBB, "="),
            "," or "COMMA" => (0xBC, ","),
            "-" or "MINUS" => (0xBD, "-"),
            "." or "PERIOD" or "DOT" => (0xBE, "."),
            "/" or "SLASH" => (0xBF, "/"),
            "`" or "TILDE" or "GRAVE" => (0xC0, "`"),
            "[" or "OPENBRACKET" => (0xDB, "["),
            "\\" or "BACKSLASH" => (0xDC, "\\"),
            "]" or "CLOSEBRACKET" => (0xDD, "]"),
            "'" or "QUOTE" or "APOSTROPHE" => (0xDE, "'"),
            "SPACE" or " " => (0x20, "Space"),
            "TAB" => (0x09, "Tab"),
            "ENTER" or "RETURN" => (0x0D, "Enter"),
            "ESC" or "ESCAPE" => (0x1B, "Escape"),
            "BACKSPACE" or "BACK" => (0x08, "Backspace"),
            "INSERT" or "INS" => (0x2D, "Insert"),
            "DELETE" or "DEL" => (0x2E, "Delete"),
            "HOME" => (0x24, "Home"),
            "END" => (0x23, "End"),
            "PAGEUP" or "PGUP" => (0x21, "PageUp"),
            "PAGEDOWN" or "PGDN" => (0x22, "PageDown"),
            "UP" => (0x26, "Up"),
            "DOWN" => (0x28, "Down"),
            "LEFT" => (0x25, "Left"),
            "RIGHT" => (0x27, "Right"),
            _ => (0, string.Empty)
        };
    }
}
