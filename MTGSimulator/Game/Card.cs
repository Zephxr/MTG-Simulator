using System;
using System.Collections.Generic;
using System.Linq;
using MTGSimulator.Services;

namespace MTGSimulator.Game
{
    public class Card
    {
        public string Name { get; set; }
        public string ManaCost { get; set; }
        public string Type { get; set; }
        public string Text { get; set; }
        public bool IsTapped { get; set; }
        public string? ImagePath { get; set; }
        public string? ScryfallId { get; set; }
        public string? SetCode { get; set; }
        public string? CollectorNumber { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public DateTime LastClickTime { get; set; } = DateTime.MinValue;
        public Dictionary<string, int> Counters { get; private set; }
        public Card? AttachedTo { get; set; }
        public List<Card> AttachedCards { get; private set; }
        public List<Card> ExiledCards { get; private set; } // Cards exiled under this card (private zone)
        public bool IsToken { get; set; }
        public bool PersistsInGraveyard { get; set; }
        public string Annotations { get; set; }
        public string? Power { get; set; } // Power (for creatures)
        public string? Toughness { get; set; } // Toughness (for creatures)

        public Card(string name, string manaCost = "", string type = "", string text = "")
        {
            Name = name;
            ManaCost = manaCost;
            Type = type;
            Text = text;
            IsTapped = false;
            ImagePath = null;
            ScryfallId = null;
            SetCode = null;
            CollectorNumber = null;
            X = 0;
            Y = 0;
            Counters = new Dictionary<string, int>();
            AttachedTo = null;
            AttachedCards = new List<Card>();
            ExiledCards = new List<Card>();
            IsToken = false;
            PersistsInGraveyard = false;
            Annotations = "";
            Power = null;
            Toughness = null;
        }

        public void OnClicked(GameLogger? logger)
        {
            DateTime clickTime = DateTime.Now;
            LastClickTime = clickTime;
        }

        public void AddCounter(string counterType, int amount = 1)
        {
            if (Counters.ContainsKey(counterType))
            {
                Counters[counterType] += amount;
            }
            else
            {
                Counters[counterType] = amount;
            }
            
            // Remove if count reaches zero or below
            if (Counters[counterType] <= 0)
            {
                Counters.Remove(counterType);
            }
        }

        public void RemoveCounter(string counterType, int amount = 1)
        {
            AddCounter(counterType, -amount);
        }

        public void SetCounter(string counterType, int amount)
        {
            if (amount <= 0)
            {
                Counters.Remove(counterType);
            }
            else
            {
                Counters[counterType] = amount;
            }
        }

        public int GetCounter(string counterType)
        {
            return Counters.ContainsKey(counterType) ? Counters[counterType] : 0;
        }

        public bool HasCounters()
        {
            return Counters.Count > 0 && Counters.Values.Any(v => v > 0);
        }

        public void AttachTo(Card targetCard)
        {
            // Detach from current parent if attached
            if (AttachedTo != null)
            {
                AttachedTo.AttachedCards.Remove(this);
            }
            
            // Attach to new target
            AttachedTo = targetCard;
            if (!targetCard.AttachedCards.Contains(this))
            {
                targetCard.AttachedCards.Add(this);
            }
        }

        public void Detach()
        {
            if (AttachedTo != null)
            {
                AttachedTo.AttachedCards.Remove(this);
                AttachedTo = null;
            }
        }

        public void DetachAll()
        {
            // Detach all cards attached to this card
            var attachedCardsCopy = AttachedCards.ToList();
            foreach (var attachedCard in attachedCardsCopy)
            {
                attachedCard.Detach();
            }
        }

        public void ExileCardUnder(Card cardToExile)
        {
            // Remove card from its current location and add to this card's private zone
            if (!ExiledCards.Contains(cardToExile))
            {
                ExiledCards.Add(cardToExile);
            }
        }

        public List<Card> ReleaseExiledCards()
        {
            // Return all exiled cards to exile zone
            var exiledCardsCopy = ExiledCards.ToList();
            ExiledCards.Clear();
            return exiledCardsCopy;
        }

        public void RemoveExiledCard(Card card)
        {
            ExiledCards.Remove(card);
        }

        public Card Clone()
        {
            var cloned = new Card(Name, ManaCost, Type, Text)
            {
                IsTapped = IsTapped,
                ImagePath = ImagePath,
                ScryfallId = ScryfallId,
                SetCode = SetCode,
                CollectorNumber = CollectorNumber,
                X = X,
                Y = Y,
                LastClickTime = DateTime.MinValue // Reset click time for cloned card
            };
            
            // Clone counters
            foreach (var counter in Counters)
            {
                cloned.Counters[counter.Key] = counter.Value;
            }
            
            // Clone annotations
            cloned.Annotations = Annotations;
            
            // Clone power/toughness
            cloned.Power = Power;
            cloned.Toughness = Toughness;
            
            // Note: Attachments and exiled cards are not cloned - cloned cards start unattached and with no exiled cards
            
            return cloned;
        }
    }
}

