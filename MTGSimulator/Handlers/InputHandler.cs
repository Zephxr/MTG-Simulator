using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MTGSimulator.Game;
using MTGSimulator.Settings;
using MTGSimulator.Services;

namespace MTGSimulator.Handlers
{
    public class InputHandler
    {
        private readonly GameState _gameState;
        private readonly AppSettings _settings;
        private readonly GameLogger? _gameLogger;
        private readonly Action<string> _logGameAction;
        private readonly Action<Card?> _updateCardInfo;
        private readonly Func<Point, string?> _getZoneAtPosition;
        private readonly Func<Point, Card?> _getCardAtPosition;
        private readonly Action _toggleHand;
        private readonly Action _toggleCardInfo;

        // Drag state
        public Card? DraggedCard { get; set; }
        public Point DragOffset { get; set; }
        public bool IsDragging { get; set; }
        public bool IsSelecting { get; set; }
        public Point SelectionStart { get; set; }
        public Rect? SelectionBox { get; set; }
        public Dictionary<Card, Point> SelectedCardsInitialPositions { get; } = new Dictionary<Card, Point>();

        // Attach mode state
        public bool IsAttachMode { get; set; }
        public List<Card> CardsToAttach { get; } = new List<Card>();

        public InputHandler(
            GameState gameState,
            AppSettings settings,
            GameLogger? gameLogger,
            Action<string> logGameAction,
            Action<Card?> updateCardInfo,
            Func<Point, string?> getZoneAtPosition,
            Func<Point, Card?> getCardAtPosition,
            Action toggleHand,
            Action toggleCardInfo)
        {
            _gameState = gameState;
            _settings = settings;
            _gameLogger = gameLogger;
            _logGameAction = logGameAction;
            _updateCardInfo = updateCardInfo;
            _getZoneAtPosition = getZoneAtPosition;
            _getCardAtPosition = getCardAtPosition;
            _toggleHand = toggleHand;
            _toggleCardInfo = toggleCardInfo;
        }

        public void HandleKeyDown(KeyEventArgs e, Action cancelAttachMode)
        {
            if (_gameState == null) return;

            // Handle Escape key to cancel attach mode
            if (e.Key == Key.Escape && IsAttachMode)
            {
                cancelAttachMode();
                e.Handled = true;
                return;
            }

            // Check keybinds from settings
            if (KeybindHelper.MatchesKeybind(_settings.DrawCardKey, e))
            {
                int drawn = _gameState.DrawCards(1);
                if (drawn > 0)
                {
                    _logGameAction($"Drew {drawn} card(s)");
                }
                else
                {
                    _logGameAction("Cannot draw from an empty library.");
                }
                e.Handled = true;
            }
            else if (KeybindHelper.MatchesKeybind(_settings.TapCardKey, e) && _gameState.SelectedCards.Count > 0)
            {
                int tappedCount = _gameState.SelectedCards.Count(c => !c.IsTapped);
                int untappedCount = _gameState.SelectedCards.Count(c => c.IsTapped);
                _gameState.TapAllSelected();
                if (tappedCount > 0)
                    _logGameAction($"Tapped {tappedCount} card(s)");
                if (untappedCount > 0)
                    _logGameAction($"Untapped {untappedCount} card(s)");
                e.Handled = true;
            }
            else if (KeybindHelper.MatchesKeybind(_settings.ShowHandKey, e))
            {
                e.Handled = true;
                _toggleHand();
            }
            else if (KeybindHelper.MatchesKeybind(_settings.ShowCardInfoKey, e))
            {
                e.Handled = true;
                _toggleCardInfo();
            }
            else if (KeybindHelper.MatchesKeybind(_settings.ShuffleKey, e))
            {
                if (_gameState.Deck.Count > 0)
                {
                    _gameState.ShuffleDeck();
                    _logGameAction("Shuffled library");
                }
                e.Handled = true;
            }
            // Counter keybinds - only work on selected cards
            else if (_gameState.SelectedCards.Count > 0)
            {
                if (KeybindHelper.MatchesKeybind(_settings.AddPlusOnePlusOneKey, e))
                {
                    foreach (var card in _gameState.SelectedCards)
                    {
                        card.AddCounter("+1/+1", 1);
                    }
                    _logGameAction($"Added +1/+1 counter to {_gameState.SelectedCards.Count} card(s)");
                    e.Handled = true;
                }
                else if (KeybindHelper.MatchesKeybind(_settings.RemovePlusOnePlusOneKey, e))
                {
                    foreach (var card in _gameState.SelectedCards)
                    {
                        card.RemoveCounter("+1/+1", 1);
                    }
                    _logGameAction($"Removed +1/+1 counter from {_gameState.SelectedCards.Count} card(s)");
                    e.Handled = true;
                }
                else if (KeybindHelper.MatchesKeybind(_settings.AddOtherCounterKey, e))
                {
                    foreach (var card in _gameState.SelectedCards)
                    {
                        card.AddCounter("other", 1);
                    }
                    _logGameAction($"Added other counter to {_gameState.SelectedCards.Count} card(s)");
                    e.Handled = true;
                }
                else if (KeybindHelper.MatchesKeybind(_settings.RemoveOtherCounterKey, e))
                {
                    foreach (var card in _gameState.SelectedCards)
                    {
                        card.RemoveCounter("other", 1);
                    }
                    _logGameAction($"Removed other counter from {_gameState.SelectedCards.Count} card(s)");
                    e.Handled = true;
                }
                else if (KeybindHelper.MatchesKeybind(_settings.AddLoyaltyKey, e))
                {
                    foreach (var card in _gameState.SelectedCards)
                    {
                        card.AddCounter("loyalty", 1);
                    }
                    _logGameAction($"Added loyalty counter to {_gameState.SelectedCards.Count} card(s)");
                    e.Handled = true;
                }
                else if (KeybindHelper.MatchesKeybind(_settings.RemoveLoyaltyKey, e))
                {
                    foreach (var card in _gameState.SelectedCards)
                    {
                        card.RemoveCounter("loyalty", 1);
                    }
                    _logGameAction($"Removed loyalty counter from {_gameState.SelectedCards.Count} card(s)");
                    e.Handled = true;
                }
            }
        }

        public bool HandleMouseDown(MouseButtonEventArgs e, Point position, Action<Card> onCardAttach, Action cancelAttachMode)
        {
            if (_gameState == null || e.ChangedButton != MouseButton.Left) return false;

            var card = _getCardAtPosition(position);

            // Handle double-click
            if (e.ClickCount == 2)
            {
                string? clickedZone = _getZoneAtPosition(position);
                if (clickedZone == "graveyard" || clickedZone == "exile")
                {
                    return true; // Handled by caller
                }

                if (card != null)
                {
                    card.IsTapped = !card.IsTapped;
                    _logGameAction(card.IsTapped ? $"Tapped {card.Name}" : $"Untapped {card.Name}");
                    card.OnClicked(_gameLogger);
                    return true;
                }
                return true;
            }

            // Handle attach mode
            if (IsAttachMode && e.ClickCount == 1)
            {
                if (card != null && CardsToAttach.Count > 0)
                {
                    if (!CardsToAttach.Contains(card) && card.AttachedTo == null)
                    {
                        onCardAttach(card);
                        cancelAttachMode();
                    }
                }
                else
                {
                    cancelAttachMode();
                }
                return true;
            }

            // Normal card selection and drag
            if (card != null)
            {
                card.OnClicked(_gameLogger);

                if (!_gameState.SelectedCards.Contains(card) && Keyboard.Modifiers != ModifierKeys.Control)
                {
                    _gameState.SelectedCards.Clear();
                }

                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    if (_gameState.SelectedCards.Contains(card))
                        _gameState.SelectedCards.Remove(card);
                    else
                        _gameState.SelectedCards.Add(card);
                }
                else
                {
                    _gameState.SelectedCards.Add(card);
                }

                // Start dragging
                DraggedCard = card;
                double cardCenterX = card.X + _gameState.CardWidth / 2;
                double cardCenterY = card.Y + _gameState.CardHeight / 2;
                DragOffset = new Point(position.X - cardCenterX, position.Y - cardCenterY);

                SelectedCardsInitialPositions.Clear();
                foreach (var selectedCard in _gameState.SelectedCards)
                {
                    SelectedCardsInitialPositions[selectedCard] = new Point(selectedCard.X, selectedCard.Y);
                }

                IsDragging = true;
                return true;
            }
            else
            {
                // Start selection box
                if (Keyboard.Modifiers != ModifierKeys.Control)
                {
                    _gameState.SelectedCards.Clear();
                }
                IsSelecting = true;
                SelectionStart = position;
                SelectionBox = new Rect(position, position);
                return true;
            }
        }

        public void HandleMouseMove(Point position, double cardWidth, double cardHeight)
        {
            if (!IsDragging && _gameState != null)
            {
                var hoveredCard = _getCardAtPosition(position);

                if (hoveredCard == null && _gameState.AlwaysRevealTopOfLibrary)
                {
                    string? hoveredZone = _getZoneAtPosition(position);
                    if (hoveredZone == "deck")
                    {
                        var topDeckCard = _gameState.GetTopCard("deck");
                        if (topDeckCard != null)
                        {
                            hoveredCard = topDeckCard;
                        }
                    }
                }

                _updateCardInfo(hoveredCard);
            }
        }

        public void HandleSelectionBox(Point position)
        {
            if (!IsSelecting || _gameState == null) return;

            SelectionBox = new Rect(
                Math.Min(SelectionStart.X, position.X),
                Math.Min(SelectionStart.Y, position.Y),
                Math.Abs(position.X - SelectionStart.X),
                Math.Abs(position.Y - SelectionStart.Y));

            var rect = SelectionBox.Value;
            var cardsInBox = _gameState.GetCardsInRect(
                rect.X, rect.Y,
                rect.X + rect.Width, rect.Y + rect.Height);

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                foreach (var card in cardsInBox)
                {
                    _gameState.SelectedCards.Add(card);
                }
            }
            else
            {
                _gameState.SelectedCards.Clear();
                foreach (var card in cardsInBox)
                {
                    _gameState.SelectedCards.Add(card);
                }
            }
        }
    }
}

