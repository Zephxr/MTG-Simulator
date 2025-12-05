using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MTGSimulator.Game
{
    public class DeckImporter
    {
        public class DeckEntry
        {
            public int Quantity { get; set; }
            public string CardName { get; set; }
            public string? SetCode { get; set; }
            public string? CollectorNumber { get; set; }

            public DeckEntry(int quantity, string cardName, string? setCode = null, string? collectorNumber = null)
            {
                Quantity = quantity;
                CardName = cardName;
                SetCode = setCode;
                CollectorNumber = collectorNumber;
            }
        }

        public static List<DeckEntry> ImportDeckFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Deck file not found: {filePath}");
            }

            var entries = new List<DeckEntry>();
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//") || trimmedLine.StartsWith("#"))
                {
                    continue;
                }

                // Try to parse the line
                var entry = ParseDeckLine(trimmedLine);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static DeckEntry? ParseDeckLine(string line)
        {
            // Common formats:
            // "4x Lightning Bolt"
            // "4 Lightning Bolt"
            // "4 Lightning Bolt (M21) 123"
            // "4 Lightning Bolt [M21]"
            // "SB: 4 Lightning Bolt" (sideboard - we'll include it for now)

            line = line.Trim();
            
            // Remove sideboard prefix if present
            if (line.StartsWith("SB:", StringComparison.OrdinalIgnoreCase))
            {
                line = line.Substring(3).Trim();
            }

            // Pattern 1: "4x Card Name" or "4 Card Name"
            var pattern1 = @"^(\d+)\s*x?\s+(.+)$";
            var match1 = Regex.Match(line, pattern1);
            if (match1.Success)
            {
                int quantity = int.Parse(match1.Groups[1].Value);
                string cardName = match1.Groups[2].Value.Trim();
                
                // Check for set code in parentheses or brackets
                var setPattern = @"\(([A-Z0-9]+)\)\s*(\d+)?";
                var setMatch = Regex.Match(cardName, setPattern);
                if (setMatch.Success)
                {
                    string setCode = setMatch.Groups[1].Value;
                    string? collectorNumber = setMatch.Groups[2].Success ? setMatch.Groups[2].Value : null;
                    cardName = Regex.Replace(cardName, setPattern, "").Trim();
                    return new DeckEntry(quantity, cardName, setCode, collectorNumber);
                }
                
                var bracketPattern = @"\[([A-Z0-9]+)\]";
                var bracketMatch = Regex.Match(cardName, bracketPattern);
                if (bracketMatch.Success)
                {
                    string setCode = bracketMatch.Groups[1].Value;
                    cardName = Regex.Replace(cardName, bracketPattern, "").Trim();
                    return new DeckEntry(quantity, cardName, setCode);
                }
                
                return new DeckEntry(quantity, cardName);
            }

            // Pattern 2: Just card name (assume quantity 1)
            if (!string.IsNullOrWhiteSpace(line) && !char.IsDigit(line[0]))
            {
                return new DeckEntry(1, line);
            }

            return null;
        }

        public static List<Card> CreateCardsFromDeck(List<DeckEntry> deckEntries)
        {
            var cards = new List<Card>();
            
            foreach (var entry in deckEntries)
            {
                for (int i = 0; i < entry.Quantity; i++)
                {
                    var card = new Card(entry.CardName)
                    {
                        SetCode = entry.SetCode,
                        CollectorNumber = entry.CollectorNumber
                    };
                    cards.Add(card);
                }
            }

            return cards;
        }
    }
}

