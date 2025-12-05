using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MTGSimulator.Game;
using MTGSimulator.Rendering;

namespace MTGSimulator.Handlers
{
    public class DragDropHandler
    {
        private readonly GameState _gameState;
        private readonly Canvas _gameCanvas;
        private readonly Action<string> _logGameAction;
        private readonly Func<Point, string?> _getZoneAtPosition;

        public DragDropHandler(
            GameState gameState,
            Canvas gameCanvas,
            Action<string> logGameAction,
            Func<Point, string?> getZoneAtPosition)
        {
            _gameState = gameState;
            _gameCanvas = gameCanvas;
            _logGameAction = logGameAction;
            _getZoneAtPosition = getZoneAtPosition;
        }

        public void UpdateDragPosition(
            Point position,
            Card draggedCard,
            Point dragOffset,
            Dictionary<Card, Point> selectedCardsInitialPositions)
        {
            if (_gameState == null) return;

            double newCenterX = position.X - dragOffset.X;
            double newCenterY = position.Y - dragOffset.Y;

            if (!selectedCardsInitialPositions.ContainsKey(draggedCard)) return;

            double initialCenterX = selectedCardsInitialPositions[draggedCard].X + _gameState.CardWidth / 2;
            double initialCenterY = selectedCardsInitialPositions[draggedCard].Y + _gameState.CardHeight / 2;
            double offsetX = newCenterX - initialCenterX;
            double offsetY = newCenterY - initialCenterY;

            foreach (var selectedCard in _gameState.SelectedCards)
            {
                if (selectedCardsInitialPositions.ContainsKey(selectedCard))
                {
                    double cardInitialCenterX = selectedCardsInitialPositions[selectedCard].X + _gameState.CardWidth / 2;
                    double cardInitialCenterY = selectedCardsInitialPositions[selectedCard].Y + _gameState.CardHeight / 2;

                    double cardNewCenterX = cardInitialCenterX + offsetX;
                    double cardNewCenterY = cardInitialCenterY + offsetY;

                    // Constrain position
                    ConstrainCardPosition(
                        ref cardNewCenterX,
                        ref cardNewCenterY,
                        selectedCard,
                        out bool inZone);

                    // Convert back to top-left
                    double oldX = selectedCard.X;
                    double oldY = selectedCard.Y;
                    selectedCard.X = cardNewCenterX - _gameState.CardWidth / 2;
                    selectedCard.Y = cardNewCenterY - _gameState.CardHeight / 2;

                    // Move attached cards with the parent
                    if (selectedCard.AttachedCards.Count > 0)
                    {
                        double deltaX = selectedCard.X - oldX;
                        double deltaY = selectedCard.Y - oldY;
                        foreach (var attachedCard in selectedCard.AttachedCards)
                        {
                            attachedCard.X += deltaX;
                            attachedCard.Y += deltaY;
                        }
                    }

                    _gameState.SetMostRecentlyMovedCard(selectedCard);
                }
            }
        }

        private void ConstrainCardPosition(
            ref double cardNewCenterX,
            ref double cardNewCenterY,
            Card card,
            out bool inZone)
        {
            inZone = false;

            double leftMargin = 20;
            double topMargin = 20;
            double zoneX = leftMargin;
            double zoneWidth = GameRenderer.ZoneWidth;
            double zoneHeight = GameRenderer.ZoneHeight;
            double zoneSpacing = GameRenderer.ZoneSpacing;
            double separatorX = zoneX + zoneWidth + 20;

            double cardWidth = card.IsTapped ? _gameState.CardHeight : _gameState.CardWidth;
            double cardHeight = card.IsTapped ? _gameState.CardWidth : _gameState.CardHeight;
            double cardLeft = cardNewCenterX - cardWidth / 2;
            double cardRight = cardNewCenterX + cardWidth / 2;
            double cardTop = cardNewCenterY - cardHeight / 2;
            double cardBottom = cardNewCenterY + cardHeight / 2;

            double deckY = topMargin;
            double graveyardY = deckY + zoneHeight + zoneSpacing;
            double exileY = graveyardY + zoneHeight + zoneSpacing;

            // Check if card is in a zone
            if (cardNewCenterX >= zoneX && cardNewCenterX <= zoneX + zoneWidth)
            {
                if ((cardNewCenterY >= deckY && cardNewCenterY <= deckY + zoneHeight) ||
                    (cardNewCenterY >= graveyardY && cardNewCenterY <= graveyardY + zoneHeight) ||
                    (cardNewCenterY >= exileY && cardNewCenterY <= exileY + zoneHeight))
                {
                    inZone = true;
                }
            }

            // If card overlaps the separator line or is in left area but not in a zone, move it
            if (!inZone)
            {
                if (cardLeft < separatorX)
                {
                    cardNewCenterX = separatorX + cardWidth / 2 + 5;
                }
                else if (cardLeft < separatorX + 5)
                {
                    cardNewCenterX = separatorX + cardWidth / 2 + 5;
                }
            }

            // Calculate bounds
            if (!inZone)
            {
                double minX = separatorX + cardWidth / 2 + 5;
                double maxX = _gameCanvas.ActualWidth - cardWidth / 2;
                cardNewCenterX = Math.Max(minX, Math.Min(cardNewCenterX, maxX));
            }
            else
            {
                double minX = zoneX + cardWidth / 2;
                double maxX = zoneX + zoneWidth - cardWidth / 2;
                cardNewCenterX = Math.Max(minX, Math.Min(cardNewCenterX, maxX));
            }

            // Vertical bounds
            double minY = cardHeight / 2;
            double maxY = _gameCanvas.ActualHeight - cardHeight / 2;
            cardNewCenterY = Math.Max(minY, Math.Min(cardNewCenterY, maxY));
        }

        public void HandleDrop(
            Point position,
            Point screenPosition,
            Rect? handWindowRect,
            Action updateHandWindow)
        {
            if (_gameState == null) return;

            // Check if dropped on hand window
            bool droppedOnHand = false;
            if (handWindowRect.HasValue)
            {
                droppedOnHand = handWindowRect.Value.Contains(screenPosition);
            }

            if (droppedOnHand)
            {
                MoveCardsToHand(updateHandWindow);
            }
            else
            {
                MoveCardsToZones(position);
            }
        }

        private void MoveCardsToHand(Action updateHandWindow)
        {
            var movedCards = new List<Card>();
            foreach (var card in _gameState.SelectedCards.ToList())
            {
                if (_gameState.Battlefield.Contains(card))
                {
                    card.DetachAll();
                    card.Detach();
                    _gameState.Battlefield.Remove(card);
                    _gameState.SelectedCards.Remove(card);
                    if (_gameState.MostRecentlyMovedCard == card)
                    {
                        _gameState.SetMostRecentlyMovedCard(null);
                    }
                    _gameState.Hand.Insert(0, card);
                    movedCards.Add(card);
                }
            }
            if (movedCards.Count > 0)
            {
                _logGameAction(movedCards.Count == 1
                    ? $"Moved {movedCards[0].Name} to hand"
                    : $"Moved {movedCards.Count} card(s) to hand");
            }
            updateHandWindow();
        }

        private void MoveCardsToZones(Point position)
        {
            double leftMargin = 20;
            double topMargin = 20;
            double zoneX = leftMargin;
            double zoneWidth = GameRenderer.ZoneWidth;
            double zoneHeight = GameRenderer.ZoneHeight;
            double zoneSpacing = GameRenderer.ZoneSpacing;

            double deckY = topMargin;
            double graveyardY = deckY + zoneHeight + zoneSpacing;
            double exileY = graveyardY + zoneHeight + zoneSpacing;

            foreach (var card in _gameState.SelectedCards.ToList())
            {
                double cardCenterX = card.X + _gameState.CardWidth / 2;
                double cardCenterY = card.Y + _gameState.CardHeight / 2;

                if (cardCenterX >= zoneX && cardCenterX <= zoneX + zoneWidth)
                {
                    if (cardCenterY >= deckY && cardCenterY <= deckY + zoneHeight)
                    {
                        _gameState.MoveCardToZone(card, "deck");
                        _logGameAction($"Put {card.Name} on top of library");
                    }
                    else if (cardCenterY >= graveyardY && cardCenterY <= graveyardY + zoneHeight)
                    {
                        _gameState.MoveCardToZone(card, "graveyard");
                        _logGameAction($"Moved {card.Name} to graveyard");
                    }
                    else if (cardCenterY >= exileY && cardCenterY <= exileY + zoneHeight)
                    {
                        _gameState.MoveCardToZone(card, "exile");
                        _logGameAction($"Moved {card.Name} to exile");
                    }
                }
            }
        }
    }
}

