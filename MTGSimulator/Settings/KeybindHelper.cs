using System;
using System.Linq;
using System.Windows.Input;

namespace MTGSimulator.Settings
{
    public static class KeybindHelper
    {
        public static bool MatchesKeybind(string keybind, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(keybind)) return false;

            // Parse the keybind string (e.g., "Ctrl+D", "Alt+Shift+T", "D")
            var parts = keybind.Split('+');
            
            // Get the main key (last part)
            string mainKeyStr = parts[parts.Length - 1];
            Key mainKey;
            
            // Convert string to Key
            if (mainKeyStr == "Space")
            {
                mainKey = Key.Space;
            }
            else if (mainKeyStr == "Plus")
            {
                mainKey = Key.OemPlus;
            }
            else if (mainKeyStr == "Minus")
            {
                mainKey = Key.OemMinus;
            }
            else if (!Enum.TryParse<Key>(mainKeyStr, out mainKey))
            {
                return false;
            }

            // Check if the main key matches
            if (e.Key != mainKey)
            {
                return false;
            }

            // Check modifiers
            var requiredModifiers = parts.Take(parts.Length - 1)
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();
            var currentModifiers = new System.Collections.Generic.List<string>();

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                currentModifiers.Add("Ctrl");
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                currentModifiers.Add("Alt");
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                currentModifiers.Add("Shift");
            }

            // Normalize both lists for comparison (case-insensitive)
            var requiredNormalized = requiredModifiers.Select(m => m.ToLowerInvariant()).OrderBy(m => m).ToList();
            var currentNormalized = currentModifiers.Select(m => m.ToLowerInvariant()).OrderBy(m => m).ToList();

            // Check if the exact set of modifiers matches (both count and content)
            if (requiredNormalized.Count != currentNormalized.Count)
            {
                return false;
            }

            // Check if all required modifiers are present and no extra ones
            for (int i = 0; i < requiredNormalized.Count; i++)
            {
                if (requiredNormalized[i] != currentNormalized[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}

