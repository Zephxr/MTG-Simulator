using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MTGSimulator.Game;
using MTGSimulator.Settings;

namespace MTGSimulator.Views
{
    public partial class HandViewerWindow : Window
    {
        private GameState _gameState;
        private List<Card> _handCards;
        private int _lastHandCount = -1;
        private const double BaseCardWidth = 120;
        private const double BaseCardHeight = 168;
        private const double CardAspectRatio = 168.0 / 120.0; // height/width
        private const double CardSpacing = 10;
        private const double MinCardWidth = 80; // Minimum card width to ensure readability
        private double _currentCardWidth = BaseCardWidth;
        private double _currentCardHeight = BaseCardHeight;
        private Card? _draggedCard = null;
        private Point _dragOffset = new Point(0, 0);
        private bool _isDragging = false;
        private MainWindow? _mainWindow;

        public HandViewerWindow(GameState gameState, MainWindow? mainWindow = null)
        {
            InitializeComponent();
            _gameState = gameState;
            _handCards = gameState.Hand;
            _mainWindow = mainWindow;

            UpdateTitle();
            
            Loaded += HandViewerWindow_Loaded;
            Closing += HandViewerWindow_Closing;
            SizeChanged += HandViewerWindow_SizeChanged;
            
            // Set owner so it closes when main window closes
            if (mainWindow != null)
            {
                this.Owner = mainWindow;
            }
            
            // Forward key events to main window so keybinds work
            this.KeyDown += HandViewerWindow_KeyDown;
            this.Focusable = true;
        }
        
        private void HandViewerWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Forward key events to main window if it exists
            // But don't forward if it's the ShowHand key to prevent double-toggling
            if (_mainWindow != null)
            {
                // Check if this is the ShowHand key - if so, only forward if window is not visible
                // This prevents the window from immediately closing when H is pressed while it's open
                var settings = AppSettings.Load();
                if (KeybindHelper.MatchesKeybind(settings.ShowHandKey, e))
                {
                    // Only toggle if window is not visible (to prevent immediate close)
                    if (!this.IsVisible)
                    {
                        _mainWindow.HandleKeyDown(e);
                    }
                    e.Handled = true;
                }
                else
                {
                    _mainWindow.HandleKeyDown(e);
                }
            }
        }
        
        public bool IsForceClosing { get; set; } = false;

        private void HandViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RenderCards();
            
            // Position window at bottom of screen, centered horizontally
            var screen = System.Windows.SystemParameters.WorkArea;
            this.Left = (screen.Width - this.Width) / 2;
            this.Top = screen.Height - this.Height - 50;
        }

        private void HandViewerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-render cards when window size changes to adjust card sizes
            // Use a small delay to ensure layout has completed
            if (e.WidthChanged || e.HeightChanged)
            {
                Dispatcher.BeginInvoke(new Action(() => RenderCards()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void HandViewerWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // If force closing (from main window), allow it
            if (IsForceClosing)
            {
                return;
            }
            
            // Otherwise, prevent closing - just hide instead
            e.Cancel = true;
            this.Hide();
        }

        public void UpdateHand()
        {
            _handCards = _gameState.Hand;
            
            // Re-render if hand count changed OR if we need to refresh (e.g., images loaded)
            bool shouldRender = _handCards.Count != _lastHandCount;
            
            if (shouldRender)
            {
                _lastHandCount = _handCards.Count;
                UpdateTitle();
                RenderCards();
            }
        }
        
        public void ForceRefresh()
        {
            // Force a re-render to update images
            UpdateTitle();
            RenderCards();
        }

        private void UpdateTitle()
        {
            HandTitle.Text = "Your Hand";
            CardCountText.Text = $" ({_handCards.Count} cards)";
        }

        private void RenderCards()
        {
            CardsCanvas.Children.Clear();
            
            if (_handCards.Count == 0)
            {
                return;
            }

            // Get available dimensions from the canvas (which fills the grid row)
            // Use RenderSize if ActualWidth/Height aren't available yet
            double availableWidth = CardsCanvas.ActualWidth > 0 ? CardsCanvas.ActualWidth : CardsCanvas.RenderSize.Width;
            double availableHeight = CardsCanvas.ActualHeight > 0 ? CardsCanvas.ActualHeight : CardsCanvas.RenderSize.Height;
            
            // If still not measured, try to get from parent or window
            if (availableWidth <= 0 || availableHeight <= 0)
            {
                // Try to get from the grid row
                var parent = CardsCanvas.Parent as FrameworkElement;
                if (parent != null)
                {
                    if (availableWidth <= 0) availableWidth = parent.ActualWidth > 0 ? parent.ActualWidth : parent.RenderSize.Width;
                    if (availableHeight <= 0) availableHeight = parent.ActualHeight > 0 ? parent.ActualHeight : parent.RenderSize.Height;
                }
                
                // Fallback to window size minus header
                if (availableWidth <= 0) availableWidth = Math.Max(100, this.ActualWidth - 20);
                if (availableHeight <= 0) availableHeight = Math.Max(100, this.ActualHeight - 50); // Account for header
            }
            
            // Ensure we have valid dimensions
            if (availableWidth <= 0 || availableHeight <= 0)
            {
                // Schedule a re-render after layout
                Dispatcher.BeginInvoke(new Action(() => RenderCards()), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            
            // Ensure dimensions are reasonable (at least enough for minimum card)
            availableWidth = Math.Max(availableWidth, MinCardWidth + 2 * CardSpacing);
            availableHeight = Math.Max(availableHeight, MinCardWidth * CardAspectRatio + 2 * CardSpacing);

            // Find the best layout that fits ALL cards within available space
            // Strategy: Try different numbers of cards per row, calculate card size needed to fit all cards,
            // and choose the layout with the largest cards that fits all cards
            
            int bestCardsPerRow = 1;
            int bestNumRows = 1;
            double bestCardWidth = 0;
            double bestCardHeight = 0;

            // Try different numbers of cards per row (from 1 to total cards)
            // We'll find the layout that fits all cards with the largest possible card size
            for (int cardsPerRow = 1; cardsPerRow <= _handCards.Count; cardsPerRow++)
            {
                // Calculate how many rows we need for ALL cards
                int numRows = (int)Math.Ceiling((double)_handCards.Count / cardsPerRow);
                
                // Calculate the maximum card width that fits in the available width
                // Formula: availableWidth = 2*margin + cardsPerRow*cardWidth + (cardsPerRow-1)*spacing
                // Solving for cardWidth: cardWidth = (availableWidth - 2*CardSpacing - (cardsPerRow-1)*CardSpacing) / cardsPerRow
                double maxCardWidthFromWidth = (availableWidth - 2 * CardSpacing - (cardsPerRow - 1) * CardSpacing) / cardsPerRow;
                
                // Calculate the maximum card height that fits in the available height
                // Formula: availableHeight = 2*margin + numRows*cardHeight + (numRows-1)*spacing
                // Solving for cardHeight: cardHeight = (availableHeight - 2*CardSpacing - (numRows-1)*CardSpacing) / numRows
                double maxCardHeightFromHeight = (availableHeight - 2 * CardSpacing - (numRows - 1) * CardSpacing) / numRows;
                
                // Convert height constraint to width constraint (maintaining aspect ratio)
                double maxCardWidthFromHeight = maxCardHeightFromHeight / CardAspectRatio;
                
                // Use the smaller constraint to ensure it fits both dimensions
                double cardWidth = Math.Min(maxCardWidthFromWidth, maxCardWidthFromHeight);
                
                // Skip if card would be too small (but we'll allow it if it's the only way to fit all cards)
                if (cardWidth <= 0)
                {
                    continue;
                }
                
                double cardHeight = cardWidth * CardAspectRatio;
                
                // Verify this layout actually fits all cards by checking total space needed
                double totalWidthNeeded = 2 * CardSpacing + cardsPerRow * cardWidth + (cardsPerRow - 1) * CardSpacing;
                double totalHeightNeeded = 2 * CardSpacing + numRows * cardHeight + (numRows - 1) * CardSpacing;
                
                // Allow small tolerance for floating point rounding errors
                const double tolerance = 1.0;
                bool fitsWidth = totalWidthNeeded <= availableWidth + tolerance;
                bool fitsHeight = totalHeightNeeded <= availableHeight + tolerance;
                
                if (fitsWidth && fitsHeight)
                {
                    // This layout fits all cards! Choose the one with largest cards
                    if (bestCardWidth == 0 || cardWidth > bestCardWidth)
                    {
                        bestCardsPerRow = cardsPerRow;
                        bestNumRows = numRows;
                        bestCardWidth = cardWidth;
                        bestCardHeight = cardHeight;
                    }
                }
            }

            // If we didn't find a layout that fits (should be rare), force a layout that fits all cards
            // by scaling down as needed
            if (bestCardWidth == 0 || bestCardWidth < 1)
            {
                // Calculate a layout that will definitely fit all cards
                // Start with a reasonable number of cards per row
                bestCardsPerRow = Math.Max(1, Math.Min(_handCards.Count, (int)Math.Floor((availableWidth - 2 * CardSpacing) / (MinCardWidth + CardSpacing))));
                if (bestCardsPerRow == 0) bestCardsPerRow = 1;
                
                // Calculate rows needed for all cards
                bestNumRows = (int)Math.Ceiling((double)_handCards.Count / bestCardsPerRow);
                
                // Calculate card size that fits both width and height constraints
                double maxCardWidthFromWidth = (availableWidth - 2 * CardSpacing - (bestCardsPerRow - 1) * CardSpacing) / bestCardsPerRow;
                double maxCardHeightFromHeight = (availableHeight - 2 * CardSpacing - (bestNumRows - 1) * CardSpacing) / bestNumRows;
                double maxCardWidthFromHeight = maxCardHeightFromHeight / CardAspectRatio;
                
                // Use the smaller constraint
                bestCardWidth = Math.Min(maxCardWidthFromWidth, maxCardWidthFromHeight);
                bestCardHeight = bestCardWidth * CardAspectRatio;
                
                // Ensure we have valid dimensions
                if (bestCardWidth <= 0 || bestCardHeight <= 0)
                {
                    // Last resort: use very small cards
                    bestCardWidth = Math.Max(10, Math.Min(availableWidth / bestCardsPerRow, availableHeight / bestNumRows / CardAspectRatio));
                    bestCardHeight = bestCardWidth * CardAspectRatio;
                }
            }

            _currentCardWidth = bestCardWidth;
            _currentCardHeight = bestCardHeight;
            
            // Render all cards in rows
            double startX = CardSpacing;
            double startY = CardSpacing;
            double currentX = startX;
            double currentY = startY;
            int cardIndex = 0;

            foreach (var card in _handCards)
            {
                // Check if this card position would be visible (within bounds)
                // Check both X and Y bounds
                if (currentX + bestCardWidth <= availableWidth && 
                    currentY + bestCardHeight <= availableHeight &&
                    currentX >= 0 && currentY >= 0)
                {
                    DrawCard(card, currentX, currentY, bestCardWidth, bestCardHeight);
                }
                else
                {
                    // Card would overflow, stop rendering
                    break;
                }
                
                cardIndex++;
                currentX += bestCardWidth + CardSpacing;
                
                // Move to next row if we've filled this row
                if (cardIndex % bestCardsPerRow == 0)
                {
                    currentX = startX;
                    currentY += bestCardHeight + CardSpacing;
                    
                    // Stop if next row would exceed available height
                    if (currentY + bestCardHeight > availableHeight)
                    {
                        break;
                    }
                }
            }

            // Canvas automatically sizes to fill the grid, so we don't need to set width/height
        }

        private void DrawCard(Card card, double x, double y, double cardWidth, double cardHeight)
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
            
            // Scale font sizes based on card size, but ensure minimum readability
            double fontSizeScale = cardWidth / BaseCardWidth;
            double nameFontSize = Math.Max(10, 12 * fontSizeScale);
            double typeFontSize = Math.Max(8, 10 * fontSizeScale);
            double padding = Math.Max(4, 5 * fontSizeScale);
            
            // Card background
            var cardRect = new Rectangle
            {
                Width = cardWidth,
                Height = cardHeight,
                Fill = cardImage != null 
                    ? new ImageBrush(cardImage) 
                    { 
                        Stretch = Stretch.UniformToFill
                    }
                    : new SolidColorBrush(GetColorForManaCost(card.ManaCost)),
                Stroke = Brushes.Gold,
                StrokeThickness = Math.Max(1, 2 * fontSizeScale),
                RadiusX = Math.Max(2, 5 * fontSizeScale),
                RadiusY = Math.Max(2, 5 * fontSizeScale)
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
                    FontSize = nameFontSize,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = cardWidth - padding * 2
                };
                Canvas.SetLeft(nameText, padding);
                Canvas.SetTop(nameText, padding);
                cardContainer.Children.Add(nameText);

                // Card type
                var typeText = new TextBlock
                {
                    Text = card.Type,
                    Foreground = Brushes.LightGray,
                    FontSize = typeFontSize,
                    MaxWidth = cardWidth - padding * 2
                };
                Canvas.SetLeft(typeText, padding);
                Canvas.SetTop(typeText, cardHeight - typeFontSize * 2 - padding);
                cardContainer.Children.Add(typeText);
            }
            else
            {
                // Show card name as overlay on image (smaller, semi-transparent)
                var nameOverlay = new TextBlock
                {
                    Text = card.Name,
                    Foreground = Brushes.White,
                    FontSize = typeFontSize,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = cardWidth - padding * 2,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0))
                };
                Canvas.SetLeft(nameOverlay, padding);
                Canvas.SetTop(nameOverlay, padding);
                cardContainer.Children.Add(nameOverlay);
            }

            Canvas.SetLeft(cardContainer, x);
            Canvas.SetTop(cardContainer, y);
            
            // Store card reference and original position for drag detection
            cardContainer.Tag = new CardInfo { Card = card, OriginalX = x, OriginalY = y };
            
            CardsCanvas.Children.Add(cardContainer);
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
                    
                    if (position.X >= cardX && position.X <= cardX + _currentCardWidth &&
                        position.Y >= cardY && position.Y <= cardY + _currentCardHeight)
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
                    // Always show preview when dragging, regardless of position
                    _mainWindow.ShowDragPreview(_draggedCard, screenPosition);
                }
            }
            else
            {
                // Track hovered card for card info window
                Card? hoveredCard = null;
                foreach (var child in CardsCanvas.Children)
                {
                    if (child is Canvas cardContainer && cardContainer.Tag is CardInfo cardInfo)
                    {
                        double cardX = Canvas.GetLeft(cardContainer);
                        double cardY = Canvas.GetTop(cardContainer);
                        
                        if (position.X >= cardX && position.X <= cardX + _currentCardWidth &&
                            position.Y >= cardY && position.Y <= cardY + _currentCardHeight)
                        {
                            hoveredCard = cardInfo.Card;
                            break;
                        }
                    }
                }
                
                // Update card info window if main window has it
                if (_mainWindow != null && hoveredCard != null)
                {
                    _mainWindow.UpdateCardInfoOnHover(hoveredCard);
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
                
                // Check if card was dragged outside the hand window (to main window)
                // Allow dragging up (negative Y) or outside the window bounds
                bool draggedToMainWindow = position.Y < -30 || 
                                          position.X < -30 || 
                                          position.X > CardsCanvas.ActualWidth + 30 ||
                                          (mouseScreenPosition.Y < this.Top - 50);
                
                if (draggedToMainWindow && _mainWindow != null)
                {
                    // Move card from hand
                    if (_handCards.Contains(_draggedCard))
                    {
                        _handCards.Remove(_draggedCard);
                        
                        try
                        {
                            // Convert screen position to main canvas position
                            var mainCanvasPoint = _mainWindow.MainGameCanvas.PointFromScreen(mouseScreenPosition);
                            
                            // Check if dropped on a zone
                            string? targetZone = _mainWindow.GetZoneAtPosition(mainCanvasPoint);
                            
                            if (targetZone != null)
                            {
                                // Move to zone
                                _gameState.MoveCardToZone(_draggedCard, targetZone);
                                if (targetZone == "deck")
                                {
                                    _mainWindow?.LogAction($"Put {_draggedCard.Name} on top of library");
                                }
                                else
                                {
                                    _mainWindow?.LogAction($"Played {_draggedCard.Name} to {targetZone}");
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
                                _draggedCard.X = mainCanvasPoint.X - _currentCardWidth / 2;
                                _draggedCard.Y = mainCanvasPoint.Y - _currentCardHeight / 2;
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
                        
                        UpdateHand();
                    }
                }
                else
                {
                    // Card was dropped back in hand - reset position
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

        private class CardInfo
        {
            public Card Card { get; set; } = null!;
            public double OriginalX { get; set; }
            public double OriginalY { get; set; }
        }
    }
}

