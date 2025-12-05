using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGSimulator.Game
{
    public class GameState
    {
        public List<Card> Battlefield { get; private set; }
        public List<Card> Deck { get; private set; }
        public List<Card> Hand { get; private set; }
        public List<Card> Graveyard { get; private set; }
        public List<Card> Exile { get; private set; }
        public HashSet<Card> SelectedCards { get; private set; }
        public Card? MostRecentlyMovedCard { get; private set; }
        public bool AlwaysRevealTopOfLibrary { get; set; } = false;

        public void SetMostRecentlyMovedCard(Card? card)
        {
            MostRecentlyMovedCard = card;
        }
        public double CardWidth { get; set; } = 120;
        public double CardHeight { get; set; } = 168;
        private const double CardSpacing = 10;

        public GameState()
        {
            Battlefield = new List<Card>();
            Deck = new List<Card>();
            Hand = new List<Card>();
            Graveyard = new List<Card>();
            Exile = new List<Card>();
            SelectedCards = new HashSet<Card>();
            MostRecentlyMovedCard = null;
        }

        public void Initialize()
        {
            // Reset game state - start with empty deck
            Battlefield.Clear();
            Deck.Clear();
            Hand.Clear();
            Graveyard.Clear();
            Exile.Clear();
            SelectedCards.Clear();
            MostRecentlyMovedCard = null;
        }

        public void LoadDeck(List<Card> cards)
        {
            // Reset game state
            Battlefield.Clear();
            Deck.Clear();
            Hand.Clear();
            Graveyard.Clear();
            Exile.Clear();
            SelectedCards.Clear();

            // Shuffle deck (simple shuffle)
            var shuffled = cards.OrderBy(x => Guid.NewGuid()).ToList();
            Deck.AddRange(shuffled);

            // Don't draw initial hand - player must manually draw cards
        }

        public int DrawCards(int count)
        {
            int drawn = 0;
            while (Deck.Count > 0 && drawn < count)
            {
                var card = Deck[0];
                Deck.RemoveAt(0);
                Hand.Add(card);
                drawn++;
            }
            return drawn;
        }

        public void ShuffleDeck()
        {
            var shuffled = Deck.OrderBy(x => Guid.NewGuid()).ToList();
            Deck.Clear();
            Deck.AddRange(shuffled);
        }

        public void MillTopCard()
        {
            if (Deck.Count > 0)
            {
                var card = Deck[0];
                Deck.RemoveAt(0);
                Graveyard.Insert(0, card);
            }
        }

        public void MillTopCards(int count)
        {
            int milled = 0;
            while (Deck.Count > 0 && milled < count)
            {
                var card = Deck[0];
                Deck.RemoveAt(0);
                Graveyard.Insert(0, card);
                milled++;
            }
        }

        public void PutCardOnTop(Card card)
        {
            // Release any cards exiled under this card (they return to exile zone)
            if (card.ExiledCards.Count > 0)
            {
                var releasedCards = card.ReleaseExiledCards();
                foreach (var releasedCard in releasedCards)
                {
                    Exile.Insert(0, releasedCard);
                }
            }
            
            // Remove from current location
            Battlefield.Remove(card);
            Hand.Remove(card);
            Graveyard.Remove(card);
            Exile.Remove(card);
            SelectedCards.Remove(card);
            
            // Add to top of deck
            Deck.Insert(0, card);
            
            if (MostRecentlyMovedCard == card)
            {
                SetMostRecentlyMovedCard(null);
            }
        }

        public void PutCardOnBottom(Card card)
        {
            // Release any cards exiled under this card (they return to exile zone)
            if (card.ExiledCards.Count > 0)
            {
                var releasedCards = card.ReleaseExiledCards();
                foreach (var releasedCard in releasedCards)
                {
                    Exile.Insert(0, releasedCard);
                }
            }
            
            // Remove from current location
            Battlefield.Remove(card);
            Hand.Remove(card);
            Graveyard.Remove(card);
            Exile.Remove(card);
            SelectedCards.Remove(card);
            
            // Add to bottom of deck
            Deck.Add(card);
            
            if (MostRecentlyMovedCard == card)
            {
                SetMostRecentlyMovedCard(null);
            }
        }

        public int DeckCount => Deck.Count;
        public int HandCount => Hand.Count;
        public int GraveyardCount => Graveyard.Count;
        public int ExileCount => Exile.Count;

        public List<Card> GetCardsInRect(double x1, double y1, double x2, double y2)
        {
            var result = new List<Card>();
            double minX = Math.Min(x1, x2);
            double maxX = Math.Max(x1, x2);
            double minY = Math.Min(y1, y2);
            double maxY = Math.Max(y1, y2);

            foreach (var card in Battlefield)
            {
                // Card's stored position (X, Y) is top-left
                // Calculate center
                double centerX = card.X + CardWidth / 2;
                double centerY = card.Y + CardHeight / 2;
                
                // Calculate bounding box - when rotated, use the larger dimension for both
                double boundingSize = Math.Max(CardWidth, CardHeight);
                double cardX = centerX - boundingSize / 2;
                double cardY = centerY - boundingSize / 2;

                // Check if card's bounding box overlaps with selection rectangle
                if (cardX < maxX && cardX + boundingSize > minX &&
                    cardY < maxY && cardY + boundingSize > minY)
                {
                    result.Add(card);
                }
            }
            return result;
        }

        public void TapAllSelected()
        {
            foreach (var card in SelectedCards)
            {
                card.IsTapped = !card.IsTapped;
            }
        }

        public void MoveCardToZone(Card card, string zoneName)
        {
            // Release any cards exiled under this card (they return to exile zone)
            if (card.ExiledCards.Count > 0)
            {
                var releasedCards = card.ReleaseExiledCards();
                foreach (var releasedCard in releasedCards)
                {
                    // Move to exile zone
                    Exile.Insert(0, releasedCard);
                }
            }
            
            // Detach all cards attached to this card
            card.DetachAll();
            // Detach this card from its parent if attached
            card.Detach();
            
            // Remove from any zone
            Battlefield.Remove(card);
            Hand.Remove(card);
            SelectedCards.Remove(card);
            
            // Clear most recently moved card since it's leaving the battlefield
            if (MostRecentlyMovedCard == card)
            {
                SetMostRecentlyMovedCard(null);
            }

            // Handle token persistence
            if (card.IsToken && zoneName.ToLower() == "graveyard" && !card.PersistsInGraveyard)
            {
                // Token doesn't persist - it's removed from the game instead
                return;
            }

            // Add to appropriate zone at the top (index 0)
            switch (zoneName.ToLower())
            {
                case "deck":
                    Deck.Insert(0, card);
                    break;
                case "graveyard":
                    Graveyard.Insert(0, card);
                    break;
                case "exile":
                    Exile.Insert(0, card);
                    break;
            }
        }

        public Card? GetTopCard(string zoneName)
        {
            return zoneName.ToLower() switch
            {
                "deck" => (AlwaysRevealTopOfLibrary && Deck.Count > 0) ? Deck[0] : null,
                "graveyard" => Graveyard.Count > 0 ? Graveyard[0] : null,
                "exile" => Exile.Count > 0 ? Exile[0] : null,
                _ => null
            };
        }

        public Card? GetCardAt(double x, double y)
        {
            // Iterate in reverse order to check topmost cards first
            // (Cards are rendered in order, so later cards are on top)
            // Also check MostRecentlyMovedCard first if it exists, as it's always on top
            if (MostRecentlyMovedCard != null && Battlefield.Contains(MostRecentlyMovedCard))
            {
                var card = MostRecentlyMovedCard;
                if (IsPointInCard(card, x, y))
                {
                    return card;
                }
            }
            
            // Check remaining cards in reverse order (top to bottom)
            for (int i = Battlefield.Count - 1; i >= 0; i--)
            {
                var card = Battlefield[i];
                // Skip if we already checked it (MostRecentlyMovedCard)
                if (card == MostRecentlyMovedCard)
                {
                    continue;
                }
                
                if (IsPointInCard(card, x, y))
                {
                    return card;
                }
            }
            
            return null;
        }
        
        private bool IsPointInCard(Card card, double x, double y)
        {
            // Card's stored position (X, Y) is top-left of the untapped card
            // When rendered, cards are positioned with their center at (X + CardWidth/2, Y + CardHeight/2)
            // and rotated around that center if tapped
            
            double centerX = card.X + CardWidth / 2;
            double centerY = card.Y + CardHeight / 2;
            
            if (card.IsTapped)
            {
                // Card is rotated 90 degrees around its center
                // Rotate the point back to check if it's in the original card bounds
                double dx = x - centerX;
                double dy = y - centerY;
                
                // Rotate point back by -90 degrees (clockwise)
                double rotatedX = dy;
                double rotatedY = -dx;
                
                // Check if rotated point is within original card bounds
                return rotatedX >= -CardWidth / 2 && rotatedX <= CardWidth / 2 &&
                       rotatedY >= -CardHeight / 2 && rotatedY <= CardHeight / 2;
            }
            else
            {
                // Card is not rotated - use simple rectangle check
                return x >= card.X && x <= card.X + CardWidth &&
                       y >= card.Y && y <= card.Y + CardHeight;
            }
        }
    }
}

