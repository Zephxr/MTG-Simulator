using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using MTGSimulator.Game;
using MTGSimulator.Services;
using MTGSimulator.Views;

namespace MTGSimulator.Handlers
{
    public class CardContextMenuBuilder
    {
        private readonly GameState _gameState;
        private readonly CardImageService? _cardImageService;
        private readonly HandViewerWindow? _handWindow;
        private readonly MainWindow _mainWindow;
        private readonly Action<string> _logGameAction;
        private readonly Func<string, string, string, string?> _showInputDialog;

        public CardContextMenuBuilder(
            GameState gameState,
            CardImageService? cardImageService,
            HandViewerWindow? handWindow,
            MainWindow mainWindow,
            Action<string> logGameAction,
            Func<string, string, string, string?> showInputDialog)
        {
            _gameState = gameState;
            _cardImageService = cardImageService;
            _handWindow = handWindow;
            _mainWindow = mainWindow;
            _logGameAction = logGameAction;
            _showInputDialog = showInputDialog;
        }

        public async Task<ContextMenu> BuildContextMenu(Card card)
        {
            var contextMenu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            // Tap/Untap
            AddTapMenuItem(contextMenu, card);

            contextMenu.Items.Add(new Separator());

            // Move to zones
            AddMoveToZoneMenuItems(contextMenu, card);

            contextMenu.Items.Add(new Separator());

            // Counters submenu
            AddCountersMenu(contextMenu, card);

            contextMenu.Items.Add(new Separator());

            // Annotations menu items
            AddAnnotationsMenuItems(contextMenu, card);

            // Power/Toughness menu items
            AddPowerToughnessMenuItems(contextMenu, card);

            contextMenu.Items.Add(new Separator());

            // Attachment menu items
            AddAttachmentMenuItems(contextMenu, card);

            contextMenu.Items.Add(new Separator());

            // Private zone (exile under card) menu items
            AddPrivateZoneMenuItems(contextMenu, card);

            contextMenu.Items.Add(new Separator());

            // Clone card
            AddCloneMenuItem(contextMenu, card);

            // Associated cards menu
            await AddAssociatedCardsMenu(contextMenu, card);

            return contextMenu;
        }

        private void AddTapMenuItem(ContextMenu contextMenu, Card card)
        {
            var tapItem = new MenuItem
            {
                Header = card.IsTapped ? "Untap" : "Tap",
                Foreground = System.Windows.Media.Brushes.Black
            };
            tapItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    var selectedList = _gameState.SelectedCards.ToList();
                    int tappedCount = 0;
                    int untappedCount = 0;
                    foreach (var selectedCard in selectedList)
                    {
                        bool wasTapped = selectedCard.IsTapped;
                        selectedCard.IsTapped = !selectedCard.IsTapped;
                        if (selectedCard.IsTapped && !wasTapped) tappedCount++;
                        if (!selectedCard.IsTapped && wasTapped) untappedCount++;
                    }
                    if (tappedCount > 0) _logGameAction($"Tapped {tappedCount} card(s)");
                    if (untappedCount > 0) _logGameAction($"Untapped {untappedCount} card(s)");
                }
                else
                {
                    card.IsTapped = !card.IsTapped;
                    _logGameAction(card.IsTapped ? $"Tapped {card.Name}" : $"Untapped {card.Name}");
                }
            };
            contextMenu.Items.Add(tapItem);
        }

        private void AddMoveToZoneMenuItems(ContextMenu contextMenu, Card card)
        {
            // Move to Graveyard
            var moveToGraveyardItem = new MenuItem
            {
                Header = "Move to Graveyard",
                Foreground = System.Windows.Media.Brushes.Black
            };
            moveToGraveyardItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    var selectedList = _gameState.SelectedCards.ToList();
                    foreach (var selectedCard in selectedList)
                    {
                        _gameState.MoveCardToZone(selectedCard, "graveyard");
                    }
                    _logGameAction($"Moved {selectedList.Count} card(s) to graveyard");
                }
                else
                {
                    _gameState.MoveCardToZone(card, "graveyard");
                    _logGameAction($"Moved {card.Name} to graveyard");
                }
            };
            contextMenu.Items.Add(moveToGraveyardItem);

            // Move to Exile
            var moveToExileItem = new MenuItem
            {
                Header = "Move to Exile",
                Foreground = System.Windows.Media.Brushes.Black
            };
            moveToExileItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    var selectedList = _gameState.SelectedCards.ToList();
                    foreach (var selectedCard in selectedList)
                    {
                        _gameState.MoveCardToZone(selectedCard, "exile");
                    }
                    _logGameAction($"Moved {selectedList.Count} card(s) to exile");
                }
                else
                {
                    _gameState.MoveCardToZone(card, "exile");
                    _logGameAction($"Moved {card.Name} to exile");
                }
            };
            contextMenu.Items.Add(moveToExileItem);

            // Move to Hand
            var moveToHandItem = new MenuItem
            {
                Header = "Move to Hand",
                Foreground = System.Windows.Media.Brushes.Black
            };
            moveToHandItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    var selectedList = _gameState.SelectedCards.ToList();
                    foreach (var selectedCard in selectedList)
                    {
                        selectedCard.DetachAll();
                        selectedCard.Detach();
                        _gameState.Battlefield.Remove(selectedCard);
                        _gameState.SelectedCards.Remove(selectedCard);
                        if (_gameState.MostRecentlyMovedCard == selectedCard)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }
                        _gameState.Hand.Insert(0, selectedCard);
                    }
                    _logGameAction($"Moved {selectedList.Count} card(s) to hand");
                }
                else
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
                    _logGameAction($"Moved {card.Name} to hand");
                }
                _handWindow?.UpdateHand();
            };
            contextMenu.Items.Add(moveToHandItem);

            // Move to Top of Library
            var moveToTopOfLibraryItem = new MenuItem
            {
                Header = "Move to Top of Library",
                Foreground = System.Windows.Media.Brushes.Black
            };
            moveToTopOfLibraryItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    var selectedList = _gameState.SelectedCards.ToList();
                    foreach (var selectedCard in selectedList)
                    {
                        _gameState.PutCardOnTop(selectedCard);
                    }
                    _logGameAction($"Put {selectedList.Count} card(s) on top of library");
                }
                else
                {
                    _gameState.PutCardOnTop(card);
                    _logGameAction($"Put {card.Name} on top of library");
                }
            };
            contextMenu.Items.Add(moveToTopOfLibraryItem);

            // Put X Cards from Top of Library
            var putXCardsFromTopItem = new MenuItem
            {
                Header = "Put X Cards from Top of Library",
                Foreground = System.Windows.Media.Brushes.Black
            };
            putXCardsFromTopItem.Click += (s, args) =>
            {
                string? input = _showInputDialog("Put Card from Top", "How many cards from the top? (1 = top, 2 = second, etc.)", "1");
                if (input != null && int.TryParse(input, out int position) && position > 0)
                {
                    if (_gameState.SelectedCards.Count > 0)
                    {
                        var selectedList = _gameState.SelectedCards.ToList();
                        foreach (var selectedCard in selectedList)
                        {
                            // Skip if card is already in the library
                            if (_gameState.Deck.Contains(selectedCard))
                                continue;

                            // Release any cards exiled under this card
                            if (selectedCard.ExiledCards.Count > 0)
                            {
                                var releasedCards = selectedCard.ReleaseExiledCards();
                                foreach (var releasedCard in releasedCards)
                                {
                                    _gameState.Exile.Insert(0, releasedCard);
                                }
                            }

                            // Remove from current location
                            selectedCard.DetachAll();
                            selectedCard.Detach();
                            _gameState.Battlefield.Remove(selectedCard);
                            _gameState.Hand.Remove(selectedCard);
                            _gameState.Graveyard.Remove(selectedCard);
                            _gameState.Exile.Remove(selectedCard);
                            _gameState.SelectedCards.Remove(selectedCard);

                            // Insert at position (position-1 because 1 = top = index 0)
                            int insertIndex = Math.Min(position - 1, _gameState.Deck.Count);
                            _gameState.Deck.Insert(insertIndex, selectedCard);

                            if (_gameState.MostRecentlyMovedCard == selectedCard)
                            {
                                _gameState.SetMostRecentlyMovedCard(null);
                            }
                        }
                        _logGameAction($"Put {selectedList.Count} card(s) {position} card(s) from top of library");
                    }
                    else
                    {
                        // Skip if card is already in the library
                        if (_gameState.Deck.Contains(card))
                        {
                            _logGameAction($"{card.Name} is already in the library");
                            return;
                        }

                        // Release any cards exiled under this card
                        if (card.ExiledCards.Count > 0)
                        {
                            var releasedCards = card.ReleaseExiledCards();
                            foreach (var releasedCard in releasedCards)
                            {
                                _gameState.Exile.Insert(0, releasedCard);
                            }
                        }

                        // Remove from current location
                        card.DetachAll();
                        card.Detach();
                        _gameState.Battlefield.Remove(card);
                        _gameState.Hand.Remove(card);
                        _gameState.Graveyard.Remove(card);
                        _gameState.Exile.Remove(card);
                        _gameState.SelectedCards.Remove(card);

                        // Insert at position (position-1 because 1 = top = index 0)
                        int insertIndex = Math.Min(position - 1, _gameState.Deck.Count);
                        _gameState.Deck.Insert(insertIndex, card);

                        if (_gameState.MostRecentlyMovedCard == card)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }

                        _logGameAction($"Put {card.Name} {position} card(s) from top of library");
                    }
                }
            };
            contextMenu.Items.Add(putXCardsFromTopItem);

            // Shuffle into Library
            var shuffleIntoLibraryItem = new MenuItem
            {
                Header = "Shuffle into Library",
                Foreground = System.Windows.Media.Brushes.Black
            };
            shuffleIntoLibraryItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    var cardsToShuffle = _gameState.SelectedCards.ToList();
                    foreach (var selectedCard in cardsToShuffle)
                    {
                        selectedCard.DetachAll();
                        selectedCard.Detach();
                        _gameState.Battlefield.Remove(selectedCard);
                        _gameState.SelectedCards.Remove(selectedCard);
                        if (_gameState.MostRecentlyMovedCard == selectedCard)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }
                        int randomIndex = new Random().Next(_gameState.Deck.Count + 1);
                        _gameState.Deck.Insert(randomIndex, selectedCard);
                    }
                    _logGameAction($"Shuffled {cardsToShuffle.Count} card(s) into library");
                }
                else
                {
                    card.DetachAll();
                    card.Detach();
                    _gameState.Battlefield.Remove(card);
                    _gameState.SelectedCards.Remove(card);
                    if (_gameState.MostRecentlyMovedCard == card)
                    {
                        _gameState.SetMostRecentlyMovedCard(null);
                    }
                    int randomIndex = new Random().Next(_gameState.Deck.Count + 1);
                    _gameState.Deck.Insert(randomIndex, card);
                    _logGameAction($"Shuffled {card.Name} into library");
                }
            };
            contextMenu.Items.Add(shuffleIntoLibraryItem);
        }

        private void AddCountersMenu(ContextMenu contextMenu, Card card)
        {
            var countersMenu = new MenuItem
            {
                Header = "Counters",
                Foreground = System.Windows.Media.Brushes.Black
            };

            // +1/+1 counters
            AddCounterMenuItems(countersMenu, card, "+1/+1", "Add +1/+1", "Remove +1/+1", "Set +1/+1");
            countersMenu.Items.Add(new Separator());

            // -1/-1 counters
            AddCounterMenuItems(countersMenu, card, "-1/-1", "Add -1/-1", "Remove -1/-1", "Set -1/-1");
            countersMenu.Items.Add(new Separator());

            // Loyalty counters
            AddCounterMenuItems(countersMenu, card, "loyalty", "Add Loyalty", "Remove Loyalty", "Set Loyalty");
            countersMenu.Items.Add(new Separator());

            // Other counters
            AddCounterMenuItems(countersMenu, card, "other", "Add Other", "Remove Other", "Set Other");

            contextMenu.Items.Add(countersMenu);
        }

        private void AddCounterMenuItems(MenuItem parentMenu, Card card, string counterType, string addHeader, string removeHeader, string setHeader)
        {
            var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };

            // Add counter
            var addItem = new MenuItem
            {
                Header = addHeader,
                Foreground = System.Windows.Media.Brushes.Black
            };
            addItem.Click += (s, args) =>
            {
                foreach (var targetCard in targetCards)
                {
                    targetCard.AddCounter(counterType, 1);
                }
                _logGameAction($"Added {counterType} counter to {targetCards.Count} card(s)");
            };
            parentMenu.Items.Add(addItem);

            // Remove counter
            var removeItem = new MenuItem
            {
                Header = removeHeader,
                Foreground = System.Windows.Media.Brushes.Black
            };
            removeItem.Click += (s, args) =>
            {
                foreach (var targetCard in targetCards)
                {
                    targetCard.RemoveCounter(counterType, 1);
                }
                _logGameAction($"Removed {counterType} counter from {targetCards.Count} card(s)");
            };
            parentMenu.Items.Add(removeItem);

            // Set counter
            var setItem = new MenuItem
            {
                Header = setHeader,
                Foreground = System.Windows.Media.Brushes.Black
            };
            setItem.Click += (s, args) =>
            {
                var currentValue = targetCards.FirstOrDefault()?.GetCounter(counterType) ?? 0;
                string? input = _showInputDialog($"Set {counterType} Counters", $"Enter the number of {counterType} counters to set:", currentValue.ToString());
                if (input != null && int.TryParse(input, out int value))
                {
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.SetCounter(counterType, value);
                    }
                    _logGameAction($"Set {counterType} counters to {value} on {targetCards.Count} card(s)");
                }
            };
            parentMenu.Items.Add(setItem);
        }

        private void AddAnnotationsMenuItems(ContextMenu contextMenu, Card card)
        {
            var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };

            // Edit Annotation
            var editAnnotationsItem = new MenuItem
            {
                Header = "Edit annotation",
                Foreground = System.Windows.Media.Brushes.Black
            };
            editAnnotationsItem.Click += (s, args) =>
            {
                string? input = _showInputDialog("Annotations", "Enter annotation text:", card.Annotations ?? "");
                if (input != null)
                {
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.Annotations = input;
                    }
                    _logGameAction($"Set annotations on {targetCards.Count} card(s)");
                }
            };
            contextMenu.Items.Add(editAnnotationsItem);
        }

        private void AddPowerToughnessMenuItems(ContextMenu contextMenu, Card card)
        {
            var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };

            // Set Power/Toughness
            var setPTItem = new MenuItem
            {
                Header = "Set Power/Toughness",
                Foreground = System.Windows.Media.Brushes.Black
            };
            setPTItem.Click += (s, args) =>
            {
                // Build current value string
                string currentPower = card.Power ?? "";
                string currentToughness = card.Toughness ?? "";
                string currentValue = string.IsNullOrEmpty(currentPower) && string.IsNullOrEmpty(currentToughness) 
                    ? "" 
                    : $"{currentPower}/{currentToughness}";
                
                string? input = _showInputDialog("Power/Toughness", "Enter power/toughness (e.g., \"2/3\" or \"*/3\" or \"2/*\"):", currentValue);
                if (input != null)
                {
                    // Parse the input
                    string[] parts = input.Split('/');
                    string? power = null;
                    string? toughness = null;
                    
                    if (parts.Length == 2)
                    {
                        power = string.IsNullOrWhiteSpace(parts[0]) ? null : parts[0].Trim();
                        toughness = string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1].Trim();
                        
                        // If both are empty or "*", clear them
                        if ((string.IsNullOrEmpty(power) || power == "*") && 
                            (string.IsNullOrEmpty(toughness) || toughness == "*"))
                        {
                            power = null;
                            toughness = null;
                        }
                        else
                        {
                            // Use "*" for empty values
                            if (string.IsNullOrEmpty(power) || power == "*")
                                power = "*";
                            if (string.IsNullOrEmpty(toughness) || toughness == "*")
                                toughness = "*";
                        }
                    }
                    else if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        // Single value - treat as power only
                        power = parts[0].Trim();
                        toughness = null;
                    }
                    
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.Power = power;
                        targetCard.Toughness = toughness;
                    }
                    
                    string ptDisplay = power != null && toughness != null ? $"{power}/{toughness}" : 
                                      power != null ? power : 
                                      toughness != null ? $"/{toughness}" : "cleared";
                    _logGameAction($"Set power/toughness to {ptDisplay} on {targetCards.Count} card(s)");
                }
            };
            contextMenu.Items.Add(setPTItem);
        }

        private void AddAttachmentMenuItems(ContextMenu contextMenu, Card card)
        {
            if (card.AttachedTo == null)
            {
                var attachToItem = new MenuItem
                {
                    Header = "Attach to",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                attachToItem.Click += (s, args) =>
                {
                    var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                    var validTargets = targetCards.Where(c => c.AttachedTo == null).ToList();
                    if (validTargets.Count == 0)
                    {
                        _mainWindow.SetStatusText("Cannot attach: Selected card(s) are already attached to another card.");
                        return;
                    }
                    _mainWindow.EnterAttachMode(validTargets);
                };
                contextMenu.Items.Add(attachToItem);
            }
            else
            {
                var detachItem = new MenuItem
                {
                    Header = "Detach",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                detachItem.Click += (s, args) =>
                {
                    var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                    foreach (var targetCard in targetCards)
                    {
                        if (targetCard.AttachedTo != null)
                        {
                            targetCard.Detach();
                        }
                    }
                    _logGameAction($"Detached {targetCards.Count} card(s)");
                };
                contextMenu.Items.Add(detachItem);
            }

            if (card.AttachedCards.Count > 0)
            {
                var detachAllItem = new MenuItem
                {
                    Header = $"Detach All ({card.AttachedCards.Count})",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                detachAllItem.Click += (s, args) =>
                {
                    var attachedCount = card.AttachedCards.Count;
                    card.DetachAll();
                    _logGameAction($"Detached all {attachedCount} card(s) from {card.Name}");
                };
                contextMenu.Items.Add(detachAllItem);
            }
        }

        private void AddPrivateZoneMenuItems(ContextMenu contextMenu, Card card)
        {
            // "Exile Under" option - allows exiling selected cards under this card
            var exileUnderItem = new MenuItem
            {
                Header = "Exile Under This Card",
                Foreground = System.Windows.Media.Brushes.Black
            };
            exileUnderItem.Click += (s, args) =>
            {
                var cardsToExile = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                var validCards = cardsToExile.Where(c => c != card && !card.ExiledCards.Contains(c)).ToList();
                
                if (validCards.Count == 0)
                {
                    _mainWindow.SetStatusText("No valid cards to exile under this card.");
                    return;
                }

                foreach (var cardToExile in validCards)
                {
                    // Remove from current location
                    _gameState.Battlefield.Remove(cardToExile);
                    _gameState.Hand.Remove(cardToExile);
                    _gameState.Graveyard.Remove(cardToExile);
                    _gameState.Exile.Remove(cardToExile);
                    _gameState.Deck.Remove(cardToExile);
                    _gameState.SelectedCards.Remove(cardToExile);
                    
                    // Add to this card's private zone
                    card.ExileCardUnder(cardToExile);
                }
                
                _logGameAction($"Exiled {validCards.Count} card(s) under {card.Name}");
            };
            contextMenu.Items.Add(exileUnderItem);

            // Show exiled cards count and option to release them
            if (card.ExiledCards.Count > 0)
            {
                contextMenu.Items.Add(new Separator());
                
                var viewExiledItem = new MenuItem
                {
                    Header = $"View Exiled Cards ({card.ExiledCards.Count})",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                viewExiledItem.Click += (s, args) =>
                {
                    var viewer = new ZoneViewerWindow(_gameState, $"Exiled under {card.Name}", card.ExiledCards, _mainWindow);
                    viewer.ShowDialog();
                };
                contextMenu.Items.Add(viewExiledItem);

                var releaseExiledItem = new MenuItem
                {
                    Header = $"Release All Exiled Cards ({card.ExiledCards.Count})",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                releaseExiledItem.Click += (s, args) =>
                {
                    var releasedCards = card.ReleaseExiledCards();
                    foreach (var releasedCard in releasedCards)
                    {
                        // Move to exile zone
                        _gameState.Exile.Insert(0, releasedCard);
                    }
                    _logGameAction($"Released {releasedCards.Count} card(s) from {card.Name} to exile");
                };
                contextMenu.Items.Add(releaseExiledItem);
            }
        }

        private void AddCloneMenuItem(ContextMenu contextMenu, Card card)
        {
            var cloneCardItem = new MenuItem
            {
                Header = "Clone Card",
                Foreground = System.Windows.Media.Brushes.Black
            };
            cloneCardItem.Click += (s, args) =>
            {
                var clonedCard = card.Clone();
                clonedCard.X = card.X + 20;
                clonedCard.Y = card.Y + 20;
                clonedCard.IsTapped = false;
                _gameState.Battlefield.Add(clonedCard);
                _gameState.SetMostRecentlyMovedCard(clonedCard);
                _logGameAction($"Cloned {card.Name}");
            };
            contextMenu.Items.Add(cloneCardItem);
        }

        private async Task AddAssociatedCardsMenu(ContextMenu contextMenu, Card card)
        {
            if (_cardImageService == null) return;

            try
            {
                var cardData = await _cardImageService.GetCardDataAsync(card);
                if (cardData != null && cardData.AssociatedCards.Count > 0)
                {
                    contextMenu.Items.Add(new Separator());

                    var associatedMenu = new MenuItem
                    {
                        Header = "Associated Cards",
                        Foreground = System.Windows.Media.Brushes.Black
                    };

                    foreach (var associatedCard in cardData.AssociatedCards)
                    {
                        string menuText = associatedCard.Type switch
                        {
                            "back_face" => $"Back: {associatedCard.Name}",
                            "token" => $"Token: {associatedCard.Name}",
                            _ => associatedCard.Name
                        };

                        var associatedItem = new MenuItem
                        {
                            Header = menuText,
                            Foreground = System.Windows.Media.Brushes.Black,
                            Tag = associatedCard
                        };

                        associatedItem.Click += async (s, args) =>
                        {
                            await _mainWindow.AddAssociatedCardToBattlefield(associatedCard, card);
                        };

                        associatedMenu.Items.Add(associatedItem);
                    }

                    contextMenu.Items.Add(associatedMenu);
                }
            }
            catch
            {
                // If fetching fails, just continue without associated cards menu
            }
        }
    }
}

