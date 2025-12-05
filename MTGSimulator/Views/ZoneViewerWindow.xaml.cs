using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MTGSimulator.Game;

namespace MTGSimulator.Views
{
    public partial class ZoneViewerWindow : Window
    {
        private GameState _gameState;
        private string _zoneName;
        private List<Card> _zoneCards;
        private const double CardWidth = 120;
        private const double CardHeight = 168;
        private const double CardSpacing = 10;
        private const double CardsPerRow = 5;
        private Card? _draggedCard = null;
        private Point _dragOffset = new Point(0, 0);
        private bool _isDragging = false;
        private MainWindow? _mainWindow;

        public ZoneViewerWindow(GameState gameState, string zoneName, List<Card> cards, MainWindow? mainWindow = null)
        {
            InitializeComponent();
            _gameState = gameState;
            _zoneName = zoneName;
            _zoneCards = cards;
            _mainWindow = mainWindow;

            ZoneTitle.Text = $"{zoneName} ({cards.Count} cards)";
            CardCountText.Text = $"{cards.Count} cards";
            
            Loaded += ZoneViewerWindow_Loaded;
            KeyDown += ZoneViewerWindow_KeyDown;
            Focusable = true;
            
            if (mainWindow != null)
            {
                this.Owner = mainWindow;
            }
        }

        private void ZoneViewerWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void ZoneViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RenderCards();
        }

        private void RenderCards()
        {
            CardsCanvas.Children.Clear();
            
            double startX = CardSpacing;
            double startY = CardSpacing;
            double currentX = startX;
            double currentY = startY;
            int cardsInRow = 0;

            foreach (var card in _zoneCards)
            {
                DrawCard(card, currentX, currentY);
                
                cardsInRow++;
                if (cardsInRow >= CardsPerRow)
                {
                    cardsInRow = 0;
                    currentX = startX;
                    currentY += CardHeight + CardSpacing;
                }
                else
                {
                    currentX += CardWidth + CardSpacing;
                }
            }

            // Set canvas size
            CardsCanvas.Width = CardsPerRow * (CardWidth + CardSpacing) + CardSpacing;
            CardsCanvas.Height = Math.Max(CardHeight + CardSpacing * 2, 
                ((_zoneCards.Count + CardsPerRow - 1) / CardsPerRow) * (CardHeight + CardSpacing) + CardSpacing);
        }

        private void DrawCard(Card card, double x, double y)
        {
            var cardContainer = new Canvas();
            
            // Try to load card image
            BitmapImage? cardImage = null;
            if (!string.IsNullOrEmpty(card.ImagePath) && File.Exists(card.ImagePath))
            {
                try
                {
                    cardImage = new BitmapImage();
                    cardImage.BeginInit();
                    cardImage.UriSource = new Uri(card.ImagePath, UriKind.Absolute);
                    cardImage.CacheOption = BitmapCacheOption.OnLoad;
                    cardImage.EndInit();
                    cardImage.Freeze();
                }
                catch
                {
                    cardImage = null;
                }
            }
            
            // Card background
            var cardRect = new Rectangle
            {
                Width = CardWidth,
                Height = CardHeight,
                Fill = cardImage != null 
                    ? new ImageBrush(cardImage) 
                    { 
                        Stretch = Stretch.UniformToFill
                    }
                    : new SolidColorBrush(GetColorForManaCost(card.ManaCost)),
                Stroke = Brushes.Gold,
                StrokeThickness = 2,
                RadiusX = 5,
                RadiusY = 5
            };
            if (cardImage != null)
            {
                RenderOptions.SetBitmapScalingMode(cardRect, BitmapScalingMode.HighQuality);
            }
            cardContainer.Children.Add(cardRect);

            // Only show text overlay if no image (or if image failed to load)
            if (cardImage == null)
            {
                // Card name
                var nameText = new TextBlock
                {
                    Text = card.Name,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = CardWidth - 10
                };
                Canvas.SetLeft(nameText, 5);
                Canvas.SetTop(nameText, 5);
                cardContainer.Children.Add(nameText);

                // Card type
                var typeText = new TextBlock
                {
                    Text = card.Type,
                    Foreground = Brushes.LightGray,
                    FontSize = 10,
                    MaxWidth = CardWidth - 10
                };
                Canvas.SetLeft(typeText, 5);
                Canvas.SetTop(typeText, CardHeight - 30);
                cardContainer.Children.Add(typeText);
            }
            else
            {
                // Show card name as overlay on image (smaller, semi-transparent)
                var nameOverlay = new TextBlock
                {
                    Text = card.Name,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = CardWidth - 10,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0))
                };
                Canvas.SetLeft(nameOverlay, 5);
                Canvas.SetTop(nameOverlay, 5);
                cardContainer.Children.Add(nameOverlay);
            }

            Canvas.SetLeft(cardContainer, x);
            Canvas.SetTop(cardContainer, y);
            
            // Store card reference and original position for drag detection
            cardContainer.Tag = new CardInfo { Card = card, OriginalX = x, OriginalY = y };
            
            CardsCanvas.Children.Add(cardContainer);
        }
        
        private class CardInfo
        {
            public Card Card { get; set; } = null!;
            public double OriginalX { get; set; }
            public double OriginalY { get; set; }
        }

        private Color GetColorForManaCost(string manaCost)
        {
            // Simple color mapping for mana costs
            if (string.IsNullOrEmpty(manaCost))
                return Color.FromRgb(100, 100, 100);

            manaCost = manaCost.ToUpper();
            
            if (manaCost.Contains("R") || manaCost.Contains("{R}"))
                return Color.FromRgb(200, 50, 50); // Red
            if (manaCost.Contains("G") || manaCost.Contains("{G}"))
                return Color.FromRgb(50, 150, 50); // Green
            if (manaCost.Contains("U") || manaCost.Contains("{U}"))
                return Color.FromRgb(50, 100, 200); // Blue
            if (manaCost.Contains("B") || manaCost.Contains("{B}"))
                return Color.FromRgb(100, 50, 100); // Black
            if (manaCost.Contains("W") || manaCost.Contains("{W}"))
                return Color.FromRgb(250, 250, 200); // White
            if (manaCost.Contains("X") || manaCost.Contains("{X}"))
                return Color.FromRgb(150, 150, 150); // Colorless

            return Color.FromRgb(100, 100, 100); // Default gray
        }

        private void CardsCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var position = e.GetPosition(CardsCanvas);
            
            // Find which card was clicked
            foreach (var child in CardsCanvas.Children)
            {
                if (child is Canvas cardContainer && cardContainer.Tag is CardInfo cardInfo)
                {
                    double cardX = Canvas.GetLeft(cardContainer);
                    double cardY = Canvas.GetTop(cardContainer);
                    
                    if (position.X >= cardX && position.X <= cardX + CardWidth &&
                        position.Y >= cardY && position.Y <= cardY + CardHeight)
                    {
                        _draggedCard = cardInfo.Card;
                        _dragOffset = new Point(position.X - cardX, position.Y - cardY);
                        _isDragging = true;
                        CardsCanvas.CaptureMouse();
                        
                        // Show preview immediately when starting to drag
                        if (_mainWindow != null && _draggedCard != null)
                        {
                            var mousePosition = e.GetPosition(this);
                            var screenPosition = this.PointToScreen(mousePosition);
                            _mainWindow.ShowDragPreview(_draggedCard, screenPosition);
                        }
                        break;
                    }
                }
            }
        }
        
        private void CardsCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(CardsCanvas);
            
            if (_isDragging && _draggedCard != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // Get actual mouse cursor position on screen (not relative to canvas)
                var mousePosition = e.GetPosition(this);
                var screenPosition = this.PointToScreen(mousePosition);
                
                // Find the card container and update its position
                foreach (var child in CardsCanvas.Children)
                {
                    if (child is Canvas cardContainer && cardContainer.Tag is CardInfo cardInfo && cardInfo.Card == _draggedCard)
                    {
                        double newX = position.X - _dragOffset.X;
                        double newY = position.Y - _dragOffset.Y;
                        
                        Canvas.SetLeft(cardContainer, newX);
                        Canvas.SetTop(cardContainer, newY);
                        break;
                    }
                }
                
                // Always update drag preview position when dragging
                if (_mainWindow != null && _draggedCard != null)
                {
                    _mainWindow.ShowDragPreview(_draggedCard, screenPosition);
                }
            }
        }
        
        private void CardsCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && _draggedCard != null)
            {
                var position = e.GetPosition(CardsCanvas);
                // Get the actual mouse position on screen (not relative to canvas)
                var mouseScreenPosition = this.PointToScreen(e.GetPosition(this));
                
                // Hide drag preview
                _mainWindow?.HideDragPreview();
                
                // Check if card was dragged outside the zone window (to main window)
                bool draggedToMainWindow = position.Y < -30 || 
                                          position.X < -30 || 
                                          position.X > CardsCanvas.ActualWidth + 30 ||
                                          (mouseScreenPosition.Y < this.Top - 50);
                
                if (draggedToMainWindow && _mainWindow != null)
                {
                    // Move card from zone
                    if (_zoneCards.Contains(_draggedCard))
                    {
                        _zoneCards.Remove(_draggedCard);
                        
                        // Remove from the zone in game state
                        switch (_zoneName.ToLower())
                        {
                            case "graveyard":
                                _gameState.Graveyard.Remove(_draggedCard);
                                break;
                            case "exile":
                                _gameState.Exile.Remove(_draggedCard);
                                break;
                            case "library":
                            case "deck":
                                _gameState.Deck.Remove(_draggedCard);
                                break;
                        }
                        
                        try
                        {
                            // Convert screen position to main canvas position
                            var mainCanvasPoint = _mainWindow.MainGameCanvas.PointFromScreen(mouseScreenPosition);
                            
                            // Check if dropped on a zone
                            string? targetZone = _mainWindow.GetZoneAtPosition(mainCanvasPoint);
                            
                            if (targetZone == "hand")
                            {
                                // Move to hand
                                _gameState.Hand.Add(_draggedCard);
                                _mainWindow?.LogAction($"Moved {_draggedCard.Name} to hand");
                            }
                            else if (targetZone != null)
                            {
                                // Move to zone
                                _gameState.MoveCardToZone(_draggedCard, targetZone);
                                if (targetZone == "deck")
                                {
                                    _mainWindow?.LogAction($"Put {_draggedCard.Name} on top of library");
                                }
                                else
                                {
                                    _mainWindow?.LogAction($"Moved {_draggedCard.Name} to {targetZone}");
                                }
                            }
                            else
                            {
                                // Move to battlefield
                                // Ensure position is within battlefield area (right of separator)
                                double separatorX = 20 + 140 + 20; // leftMargin + ZoneWidth + spacing
                                if (mainCanvasPoint.X < separatorX + 50)
                                {
                                    mainCanvasPoint.X = separatorX + 50;
                                }
                                
                                // Position card so its center is at the mouse position
                                _draggedCard.X = mainCanvasPoint.X - CardWidth / 2;
                                _draggedCard.Y = mainCanvasPoint.Y - CardHeight / 2;
                                _draggedCard.IsTapped = false;
                                _gameState.Battlefield.Add(_draggedCard);
                                _gameState.SetMostRecentlyMovedCard(_draggedCard);
                                _mainWindow?.LogAction($"Played {_draggedCard.Name}");
                            }
                        }
                        catch
                        {
                            // Fallback: move to battlefield at default position
                            _draggedCard.X = 400;
                            _draggedCard.Y = 300;
                            _draggedCard.IsTapped = false;
                            _gameState.Battlefield.Add(_draggedCard);
                            _gameState.SetMostRecentlyMovedCard(_draggedCard);
                        }
                        
                        // Update the display
                        RenderCards();
                        CardCountText.Text = $"{_zoneCards.Count} cards";
                        ZoneTitle.Text = $"{_zoneName} ({_zoneCards.Count} cards)";
                    }
                }
                else
                {
                    // Card was dropped back in zone - reset position
                    foreach (var child in CardsCanvas.Children)
                    {
                        if (child is Canvas cardContainer && cardContainer.Tag is CardInfo cardInfo && cardInfo.Card == _draggedCard)
                        {
                            Canvas.SetLeft(cardContainer, cardInfo.OriginalX);
                            Canvas.SetTop(cardContainer, cardInfo.OriginalY);
                            break;
                        }
                    }
                }
                
                _isDragging = false;
                _draggedCard = null;
                CardsCanvas.ReleaseMouseCapture();
            }
        }
    }
}

