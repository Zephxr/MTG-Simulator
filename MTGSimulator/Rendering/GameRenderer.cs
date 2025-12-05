using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MTGSimulator.Game;

namespace MTGSimulator.Rendering
{
    public class GameRenderer
    {
        private readonly Canvas _canvas;
        private double CardWidth = 120;
        private double CardHeight = 168;
        private const double CardSpacing = 10;
        public const double ZoneWidth = 140;
        public const double ZoneHeight = 200;
        public const double ZoneSpacing = 20;
        private BitmapImage? _cardBackImage;

        public GameRenderer(Canvas canvas)
        {
            _canvas = canvas;
            LoadCardBackImage();
        }

        private void LoadCardBackImage()
        {
            try
            {
                // Load from the executable directory
                var exePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var imagePath = System.IO.Path.Combine(exePath ?? "", "Assets", "card_back.png");
                
                if (File.Exists(imagePath))
                {
                    _cardBackImage = new BitmapImage();
                    _cardBackImage.BeginInit();
                    _cardBackImage.UriSource = new Uri(imagePath, UriKind.Absolute);
                    _cardBackImage.CacheOption = BitmapCacheOption.OnLoad;
                    _cardBackImage.EndInit();
                    _cardBackImage.Freeze();
                }
            }
            catch
            {
                // If loading fails, _cardBackImage will remain null and we'll use a placeholder
            }
        }

        public void UpdateCardSize(double width, double height)
        {
            CardWidth = width;
            CardHeight = height;
        }

        public void Render(GameState gameState, Rect? selectionBox = null)
        {
            _canvas.Children.Clear();

            double canvasWidth = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : 1280;
            double canvasHeight = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : 720;

            // Draw background
            var background = new Rectangle
            {
                Width = canvasWidth,
                Height = canvasHeight,
                Fill = new SolidColorBrush(Color.FromRgb(15, 15, 15))
            };
            _canvas.Children.Add(background);

            // Draw zones on the left side
            double leftMargin = 20;
            double topMargin = 20;
            
            // Deck zone (with count on top)
            var topDeckCard = gameState.GetTopCard("deck");
            DrawZone("Deck", gameState.DeckCount, leftMargin, topMargin, gameState.CardWidth, gameState.CardHeight, !gameState.AlwaysRevealTopOfLibrary, true, topDeckCard);
            
            // Graveyard zone (with count on top)
            double graveyardY = topMargin + ZoneHeight + ZoneSpacing;
            var topGraveyardCard = gameState.GetTopCard("graveyard");
            DrawZone("Graveyard", gameState.GraveyardCount, leftMargin, graveyardY, gameState.CardWidth, gameState.CardHeight, false, true, topGraveyardCard);
            
            // Exile zone (with count on top)
            double exileY = graveyardY + ZoneHeight + ZoneSpacing;
            var topExileCard = gameState.GetTopCard("exile");
            DrawZone("Exile", gameState.ExileCount, leftMargin, exileY, gameState.CardWidth, gameState.CardHeight, false, true, topExileCard);

            // Draw visual separator between zones and battlefield
            double separatorX = leftMargin + ZoneWidth + 20;
            var separator = new Rectangle
            {
                Width = 3,
                Height = canvasHeight,
                Fill = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Stroke = Brushes.DarkGray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(separator, separatorX);
            Canvas.SetTop(separator, 0);
            _canvas.Children.Add(separator);

            // Draw all cards at their stored positions
            // Draw most recently moved card last so it appears on top
            var cardsToDraw = gameState.Battlefield.ToList();
            var mostRecentlyMoved = gameState.MostRecentlyMovedCard;
            
            // Draw all cards except the most recently moved one
            foreach (var card in cardsToDraw)
            {
                if (card != mostRecentlyMoved)
            {
                DrawCard(card, card.X, card.Y, gameState.SelectedCards.Contains(card), 
                        gameState.CardWidth, gameState.CardHeight);
                }
            }
            
            // Draw most recently moved card last (on top)
            if (mostRecentlyMoved != null && cardsToDraw.Contains(mostRecentlyMoved))
            {
                DrawCard(mostRecentlyMoved, mostRecentlyMoved.X, mostRecentlyMoved.Y, 
                    gameState.SelectedCards.Contains(mostRecentlyMoved), 
                    gameState.CardWidth, gameState.CardHeight);
            }

            // Draw selection box if active
            if (selectionBox.HasValue)
            {
                var box = selectionBox.Value;
                // Draw even if very small (minimum 1 pixel) so user can see selection starting
                double minWidth = Math.Max(1, box.Width);
                double minHeight = Math.Max(1, box.Height);
                
                var selectionRect = new Rectangle
                {
                    Width = minWidth,
                    Height = minHeight,
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection(new double[] { 4, 4 }),
                    Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 255))
                };
                Canvas.SetLeft(selectionRect, box.X);
                Canvas.SetTop(selectionRect, box.Y);
                _canvas.Children.Add(selectionRect);
            }
        }

        private void DrawZone(string zoneName, int cardCount, double x, double y, double cardWidth, double cardHeight, bool showCardBack, bool showCountOnTop = false, Card? previewCard = null)
        {
            // Zone background
            var zoneRect = new Rectangle
            {
                Width = ZoneWidth,
                Height = ZoneHeight,
                Fill = new SolidColorBrush(Color.FromArgb(50, 50, 50, 50)),
                Stroke = Brushes.Gray,
                StrokeThickness = 2,
                RadiusX = 5,
                RadiusY = 5
            };
            Canvas.SetLeft(zoneRect, x);
            Canvas.SetTop(zoneRect, y);
            _canvas.Children.Add(zoneRect);

            // Card count is now always shown on top of the card (handled in preview card section)

            // Draw card back image for deck - fill the entire zone
            if (showCardBack && cardCount > 0)
            {
                var cardBackRect = new Rectangle
                {
                    Width = ZoneWidth,
                    Height = ZoneHeight,
                    Stroke = Brushes.Gold,
                    StrokeThickness = 2,
                    RadiusX = 5,
                    RadiusY = 5
                };

                if (_cardBackImage != null)
                {
                    cardBackRect.Fill = new ImageBrush(_cardBackImage)
                    {
                        Stretch = Stretch.UniformToFill
                    };
                }
                else
                {
                    // Fallback if image not loaded - create a card back pattern
                    var gradient = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1)
                    };
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 60), 0));
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(50, 50, 100), 1));
                    cardBackRect.Fill = gradient;
                    
                    // Add a simple pattern to indicate it's a card back
                    var patternText = new System.Windows.Controls.TextBlock
                    {
                        Text = "MTG",
                        Foreground = Brushes.Gold,
                        FontSize = 24,
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(patternText, x + ZoneWidth / 2 - 25);
                    Canvas.SetTop(patternText, y + ZoneHeight / 2 - 12);
                    _canvas.Children.Add(patternText);
                }

                Canvas.SetLeft(cardBackRect, x);
                Canvas.SetTop(cardBackRect, y);
                _canvas.Children.Add(cardBackRect);

                // Draw card count on top of card back image (for deck)
                if (showCountOnTop)
                {
                    var countTextTop = new System.Windows.Controls.TextBlock
                    {
                        Text = cardCount.ToString(),
                        Foreground = Brushes.White,
                        FontSize = 32,
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                        Padding = new Thickness(8, 4, 8, 4),
                        TextAlignment = TextAlignment.Center
                    };
                    // Center the text on the card back
                    countTextTop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double textWidth = countTextTop.DesiredSize.Width;
                    double textHeight = countTextTop.DesiredSize.Height;
                    Canvas.SetLeft(countTextTop, x + (ZoneWidth - textWidth) / 2);
                    Canvas.SetTop(countTextTop, y + (ZoneHeight - textHeight) / 2);
                    _canvas.Children.Add(countTextTop);
                }
            }
            
            // Draw preview card for zones - fit perfectly within zone outline
            if (previewCard != null && cardCount > 0)
            {
                // Make the card fill the ENTIRE zone - use absolute maximum dimensions
                // The card should extend to the very edges of the zone
                double availableWidth, availableHeight, cardY;
                
                // All zones now use the same layout: card fills zone, label and count overlay on top
                // Card extends from top to bottom, label is drawn on top of it
                availableWidth = ZoneWidth; // Full zone width
                availableHeight = ZoneHeight; // Full zone height - card fills everything
                cardY = y; // Start at top of zone
                
                // Maintain card aspect ratio (height/width = 168/120 = 1.4)
                double cardAspectRatio = cardHeight / cardWidth;
                
                // Calculate maximum card size - use the LARGER of the two constraints
                // This ensures the card fills as much space as possible
                // Try fitting to width first
                double previewWidthFromWidth = availableWidth;
                double previewHeightFromWidth = previewWidthFromWidth * cardAspectRatio;
                
                // Try fitting to height
                double previewHeightFromHeight = availableHeight;
                double previewWidthFromHeight = previewHeightFromHeight / cardAspectRatio;
                
                // Choose whichever gives us a LARGER card (even if it slightly exceeds one dimension)
                // We'll clip it to fit, but this maximizes the card size
                double previewWidth, previewHeight;
                if (previewWidthFromWidth * previewHeightFromWidth >= previewWidthFromHeight * previewHeightFromHeight)
                {
                    // Width-constrained gives larger area - use it, but clip height if needed
                    previewWidth = previewWidthFromWidth;
                    previewHeight = Math.Min(previewHeightFromWidth, availableHeight);
                }
                else
                {
                    // Height-constrained gives larger area - use it, but clip width if needed
                    previewWidth = Math.Min(previewWidthFromHeight, availableWidth);
                    previewHeight = previewHeightFromHeight;
                }
                
                // Center the card horizontally - it should fill the full width
                double cardX = x + (availableWidth - previewWidth) / 2;
                
                // Create a container for the preview card
                var previewContainer = new Canvas();
                
                // Try to load card image
                BitmapImage? cardImage = null;
                if (!string.IsNullOrEmpty(previewCard.ImagePath) && File.Exists(previewCard.ImagePath))
                {
                    try
                    {
                        cardImage = new BitmapImage();
                        cardImage.BeginInit();
                        cardImage.UriSource = new Uri(previewCard.ImagePath, UriKind.Absolute);
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
                    Width = previewWidth,
                    Height = previewHeight,
                    Fill = cardImage != null 
                        ? new ImageBrush(cardImage) 
                        { 
                            Stretch = Stretch.UniformToFill
                        }
                        : new SolidColorBrush(GetColorForManaCost(previewCard.ManaCost)),
                    Stroke = Brushes.Gold,
                    StrokeThickness = 2,
                    RadiusX = 5,
                    RadiusY = 5
                };
                if (cardImage != null)
                {
                    RenderOptions.SetBitmapScalingMode(cardRect, BitmapScalingMode.HighQuality);
                }
                previewContainer.Children.Add(cardRect);

                // Only show text overlay if no image
                if (cardImage == null)
                {
                // Card name
                var nameText = new System.Windows.Controls.TextBlock
                {
                    Text = previewCard.Name,
                    Foreground = Brushes.White,
                    FontSize = Math.Max(8, previewWidth / 10),
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = previewWidth - 10
                };
                Canvas.SetLeft(nameText, 5);
                Canvas.SetTop(nameText, 5);
                previewContainer.Children.Add(nameText);

                // Card type
                var typeText = new System.Windows.Controls.TextBlock
                {
                    Text = previewCard.Type,
                    Foreground = Brushes.LightGray,
                    FontSize = Math.Max(6, previewWidth / 15),
                    MaxWidth = previewWidth - 10
                };
                Canvas.SetLeft(typeText, 5);
                Canvas.SetTop(typeText, previewHeight - 25);
                previewContainer.Children.Add(typeText);
                }
                
                Canvas.SetLeft(previewContainer, cardX);
                Canvas.SetTop(previewContainer, cardY);
                _canvas.Children.Add(previewContainer);
                
                // Draw card count on top of preview card (for all zones now)
                if (showCountOnTop)
                {
                    // Size the count text proportionally to the card size
                    double fontSize = Math.Max(12, Math.Min(32, previewWidth / 4));
                    var countTextTop = new System.Windows.Controls.TextBlock
                    {
                        Text = cardCount.ToString(),
                        Foreground = Brushes.White,
                        FontSize = fontSize,
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                        Padding = new Thickness(Math.Max(4, previewWidth / 20), Math.Max(2, previewHeight / 50), Math.Max(4, previewWidth / 20), Math.Max(2, previewHeight / 50)),
                        TextAlignment = TextAlignment.Center
                    };
                    // Center the text on the preview card
                    countTextTop.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double textWidth = countTextTop.DesiredSize.Width;
                    double textHeight = countTextTop.DesiredSize.Height;
                    Canvas.SetLeft(countTextTop, cardX + (previewWidth - textWidth) / 2);
                    Canvas.SetTop(countTextTop, cardY + (previewHeight - textHeight) / 2);
                    _canvas.Children.Add(countTextTop);
                }
            }
            
            // Zone label - all caps, at top of zone within border (drawn last so it appears on top)
            string zoneLabel = zoneName.ToUpper() switch
            {
                "DECK" => "LIBRARY",
                _ => zoneName.ToUpper()
            };
            
            var labelText = new System.Windows.Controls.TextBlock
            {
                Text = zoneLabel,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)) // Semi-transparent background for visibility
            };
            Canvas.SetLeft(labelText, x + 10);
            Canvas.SetTop(labelText, y + 10);
            _canvas.Children.Add(labelText);
        }

        private void DrawCard(Card card, double x, double y, bool isSelected = false, double cardWidth = 120, double cardHeight = 168)
        {
            // Create a container for the card to apply rotation
            var cardContainer = new Canvas();
            
            // Calculate rotation angle
            double rotationAngle = card.IsTapped ? 90 : 0;
            
            // Card's stored position (x, y) is top-left
            // Calculate the center point of the card
            double centerX = x + cardWidth / 2;
            double centerY = y + cardHeight / 2;
            
            // Position the card so its center is at (centerX, centerY)
            // The card's top-left will be at (centerX - cardWidth/2, centerY - cardHeight/2)
            double cardX = centerX - cardWidth / 2;
            double cardY = centerY - cardHeight / 2;
            
            // Apply rotation transform around the center of the card
            // The rotation center is relative to the card's top-left, so it's at (cardWidth/2, cardHeight/2)
            var transform = new RotateTransform(rotationAngle, cardWidth / 2, cardHeight / 2);
            cardContainer.RenderTransform = transform;
            
            Canvas.SetLeft(cardContainer, cardX);
            Canvas.SetTop(cardContainer, cardY);
            
            // Card background - try to load image first
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
                    // If image loading fails, fall back to colored background
                    cardImage = null;
                }
            }
            
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
                Stroke = isSelected ? Brushes.Cyan : null,
                StrokeThickness = isSelected ? 3 : 0,
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
            var nameText = new System.Windows.Controls.TextBlock
            {
                Text = card.Name,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = cardWidth - 10
            };
            Canvas.SetLeft(nameText, 5);
            Canvas.SetTop(nameText, 5);
            cardContainer.Children.Add(nameText);

            // Card type
            var typeText = new System.Windows.Controls.TextBlock
            {
                Text = card.Type,
                Foreground = Brushes.LightGray,
                FontSize = 10,
                MaxWidth = cardWidth - 10
            };
            Canvas.SetLeft(typeText, 5);
            Canvas.SetTop(typeText, cardHeight - 30);
            cardContainer.Children.Add(typeText);
            }
            else
            {
                // Show card name as overlay on image (smaller, semi-transparent)
                var nameOverlay = new System.Windows.Controls.TextBlock
                {
                    Text = card.Name,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = cardWidth - 10,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0))
                };
                Canvas.SetLeft(nameOverlay, 5);
                Canvas.SetTop(nameOverlay, 5);
                cardContainer.Children.Add(nameOverlay);
            }
            
            // Draw power/toughness for creatures at the bottom
            DrawPowerToughness(card, cardContainer, cardWidth, cardHeight);
            
            // Draw counters on the card
            DrawCounters(card, cardContainer, cardWidth, cardHeight);
            
            // Draw exiled cards indicator
            DrawExiledCardsIndicator(card, cardContainer, cardWidth, cardHeight);
            
            // Draw annotations
            DrawAnnotations(card, cardContainer, cardWidth, cardHeight);
            
            _canvas.Children.Add(cardContainer);
        }
        
        private void DrawExiledCardsIndicator(Card card, Canvas cardContainer, double cardWidth, double cardHeight)
        {
            if (card.ExiledCards == null || card.ExiledCards.Count == 0) return;
            
            // Position indicator in bottom-left corner, similar to counters
            double margin = 6;
            double indicatorSize = Math.Max(18, Math.Min(24, cardWidth * 0.15));
            double labelHeight = 12;
            double fontSize = Math.Max(9, indicatorSize * 0.5);
            
            // Create container for label and badge
            var indicatorContainer = new Canvas();
            
            // Create "Exiled with" label above badge
            var label = new System.Windows.Controls.TextBlock
            {
                Text = "Exiled with",
                Foreground = Brushes.White,
                FontSize = Math.Max(8, indicatorSize * 0.35),
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Padding = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            
            // Measure label to center it
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelWidth = label.DesiredSize.Width;
            double labelX = (indicatorSize - labelWidth) / 2;
            Canvas.SetLeft(label, labelX);
            Canvas.SetTop(label, 0);
            indicatorContainer.Children.Add(label);
            
            // Create badge for exiled cards count
            var exiledBadge = new Border
            {
                Width = indicatorSize,
                Height = indicatorSize,
                Background = new SolidColorBrush(Color.FromArgb(220, 100, 50, 150)), // Purple-ish color for exile
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(indicatorSize / 2)
            };
            
            // Exiled cards count text
            var countText = new System.Windows.Controls.TextBlock
            {
                Text = card.ExiledCards.Count.ToString(),
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            
            exiledBadge.Child = countText;
            
            // Position badge below label
            Canvas.SetLeft(exiledBadge, 0);
            Canvas.SetTop(exiledBadge, labelHeight + 2);
            indicatorContainer.Children.Add(exiledBadge);
            
            // Position container in bottom-left corner
            double totalHeight = labelHeight + 2 + indicatorSize;
            double xPos = margin;
            double yPos = cardHeight - totalHeight - margin;
            
            Canvas.SetLeft(indicatorContainer, xPos);
            Canvas.SetTop(indicatorContainer, yPos);
            
            cardContainer.Children.Add(indicatorContainer);
        }

        private void DrawAnnotations(Card card, Canvas cardContainer, double cardWidth, double cardHeight)
        {
            if (string.IsNullOrWhiteSpace(card.Annotations)) return;

            // Position annotations below the card name (around 25-30 pixels from top)
            double topMargin = 25;
            double sideMargin = 5;
            double fontSize = Math.Max(10, Math.Min(14, cardWidth * 0.1));
            
            var annotationText = new System.Windows.Controls.TextBlock
            {
                Text = card.Annotations,
                Foreground = Brushes.Yellow,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
                Padding = new Thickness(6, 3, 6, 3),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = cardWidth - (sideMargin * 2)
            };
            
            // Measure to center horizontally
            annotationText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = annotationText.DesiredSize.Width;
            double xPos = (cardWidth - textWidth) / 2;
            
            Canvas.SetLeft(annotationText, xPos);
            Canvas.SetTop(annotationText, topMargin);
            
            cardContainer.Children.Add(annotationText);
        }

        private void DrawPowerToughness(Card card, Canvas cardContainer, double cardWidth, double cardHeight)
        {
            // Only show power/toughness for creatures
            // Check if it's a creature by looking at the type
            bool isCreature = !string.IsNullOrEmpty(card.Type) && 
                             card.Type.Contains("Creature", StringComparison.OrdinalIgnoreCase);

            // Also show if it's a token (tokens are often creatures)
            bool isToken = card.IsToken || (!string.IsNullOrEmpty(card.Type) && 
                             card.Type.Contains("Token", StringComparison.OrdinalIgnoreCase));

            // Only show if it's a creature or token, and has power/toughness values
            if (!isCreature && !isToken)
                return;

            // Must have at least one value to display
            if (string.IsNullOrEmpty(card.Power) && string.IsNullOrEmpty(card.Toughness))
                return;

            // Build display text (e.g., "2/3" or "*/*" or "2/*")
            string powerText = card.Power ?? "*";
            string toughnessText = card.Toughness ?? "*";
            string displayText = $"{powerText}/{toughnessText}";

            // Position at bottom center of card
            double fontSize = Math.Max(10, Math.Min(14, cardWidth * 0.12));
            double margin = 5;
            
            var ptText = new System.Windows.Controls.TextBlock
            {
                Text = displayText,
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
                Padding = new Thickness(6, 3, 6, 3),
                TextAlignment = TextAlignment.Center
            };

            // Measure to center horizontally
            ptText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = ptText.DesiredSize.Width;
            double xPos = (cardWidth - textWidth) / 2;
            double yPos = cardHeight - ptText.DesiredSize.Height - margin;

            Canvas.SetLeft(ptText, xPos);
            Canvas.SetTop(ptText, yPos);

            cardContainer.Children.Add(ptText);
        }

        private void DrawCounters(Card card, Canvas cardContainer, double cardWidth, double cardHeight)
        {
            if (!card.HasCounters()) return;

            // Position counters in bottom-right corner, stacked vertically
            // Move counters up to make room for power/toughness at bottom
            double counterSpacing = 30; // Space between counters (increased to fit label)
            double counterSize = Math.Max(18, Math.Min(24, cardWidth * 0.15)); // Scale with card size, min 18, max 24
            double margin = 6; // Margin from edges
            double labelHeight = 12; // Height for the label above counter
            double powerToughnessHeight = 0; // Space reserved for power/toughness at bottom

            // Check if power/toughness will be displayed
            bool hasPT = (!string.IsNullOrEmpty(card.Power) || !string.IsNullOrEmpty(card.Toughness));
            if (hasPT)
            {
                // Reserve space for power/toughness (approximate height + margin)
                powerToughnessHeight = 25; // Approximate height of power/toughness text block
            }

            // Draw counters in reverse order so most important ones are at bottom
            var counters = card.Counters.Where(c => c.Value > 0).OrderByDescending(c => GetCounterPriority(c.Key)).ToList();
            
            for (int i = 0; i < counters.Count; i++)
            {
                var counter = counters[i];
                
                // Create container for label and badge
                var counterContainer = new Canvas();
                
                // Create label above counter
                string labelText = GetCounterLabel(counter.Key);
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = labelText,
                    Foreground = Brushes.White,
                    FontSize = Math.Max(8, counterSize * 0.35),
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    Padding = new Thickness(3, 1, 3, 1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                
                // Measure label to center it
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelWidth = label.DesiredSize.Width;
                double labelX = (counterSize - labelWidth) / 2;
                Canvas.SetLeft(label, labelX);
                Canvas.SetTop(label, 0);
                counterContainer.Children.Add(label);
                
                // Create counter badge
                var counterBadge = new Border
                {
                    Width = counterSize,
                    Height = counterSize,
                    Background = GetCounterColor(counter.Key),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(counterSize / 2)
                };

                // Counter text - always show the number, never show symbol for loyalty
                string displayText = counter.Value.ToString();
                var counterText = new System.Windows.Controls.TextBlock
                {
                    Text = displayText,
                    Foreground = Brushes.White,
                    FontSize = Math.Max(9, counterSize * 0.5),
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                
                counterBadge.Child = counterText;
                
                // Position badge below label
                Canvas.SetLeft(counterBadge, 0);
                Canvas.SetTop(counterBadge, labelHeight + 2);
                counterContainer.Children.Add(counterBadge);
                
                // Position container in bottom-right corner, stacked from bottom up
                // Move up to make room for power/toughness
                double totalHeight = labelHeight + 2 + counterSize;
                double xPos = cardWidth - counterSize - margin;
                double yPos = cardHeight - totalHeight - margin - powerToughnessHeight - (i * counterSpacing);
                
                Canvas.SetLeft(counterContainer, xPos);
                Canvas.SetTop(counterContainer, yPos);
                
                cardContainer.Children.Add(counterContainer);
            }
        }
        
        private string GetCounterLabel(string counterType)
        {
            return counterType.ToLower() switch
            {
                "+1/+1" => "+1/+1",
                "-1/-1" => "-1/-1",
                "loyalty" => "Loyalty",
                "+1/+0" => "+1/+0",
                "+0/+1" => "+0/+1",
                "-1/-0" => "-1/-0",
                "-0/-1" => "-0/-1",
                "poison" => "Poison",
                "experience" => "Exp",
                "other" => "Other",
                _ => counterType // Use the counter type as-is for unknown types
            };
        }

        private int GetCounterPriority(string counterType)
        {
            // Priority order: higher number = drawn lower (more visible)
            return counterType.ToLower() switch
            {
                "+1/+1" => 100,
                "-1/-1" => 90,
                "loyalty" => 80,
                "+1/+0" => 70,
                "+0/+1" => 60,
                "-1/-0" => 50,
                "-0/-1" => 40,
                "poison" => 30,
                "experience" => 20,
                "other" => 15,
                _ => 10
            };
        }

        private Brush GetCounterColor(string counterType)
        {
            return counterType.ToLower() switch
            {
                "+1/+1" => new SolidColorBrush(Color.FromRgb(50, 150, 50)), // Green
                "-1/-1" => new SolidColorBrush(Color.FromRgb(200, 50, 50)), // Red
                "loyalty" => new SolidColorBrush(Color.FromRgb(100, 50, 200)), // Purple
                "+1/+0" => new SolidColorBrush(Color.FromRgb(50, 150, 50)), // Green
                "+0/+1" => new SolidColorBrush(Color.FromRgb(50, 150, 50)), // Green
                "-1/-0" => new SolidColorBrush(Color.FromRgb(200, 50, 50)), // Red
                "-0/-1" => new SolidColorBrush(Color.FromRgb(200, 50, 50)), // Red
                "poison" => new SolidColorBrush(Color.FromRgb(150, 0, 150)), // Purple
                "experience" => new SolidColorBrush(Color.FromRgb(200, 150, 0)), // Gold
                "other" => new SolidColorBrush(Color.FromRgb(100, 100, 150)), // Blue-gray for other
                _ => new SolidColorBrush(Color.FromRgb(100, 100, 100)) // Gray for unknown
            };
        }

        private string GetCounterSymbol(string counterType)
        {
            return counterType.ToLower() switch
            {
                "+1/+1" => "+",
                "-1/-1" => "-",
                "+1/+0" => "+",
                "+0/+1" => "+",
                "-1/-0" => "-",
                "-0/-1" => "-",
                "loyalty" => "L",
                "poison" => "P",
                "experience" => "E",
                _ => "•"
            };
        }

        private Color GetColorForManaCost(string manaCost)
        {
            // Simple color mapping for mana costs
            return manaCost switch
            {
                "R" => Color.FromRgb(200, 50, 50),    // Red
                "G" => Color.FromRgb(50, 150, 50),     // Green
                "U" => Color.FromRgb(50, 100, 200),    // Blue
                "B" => Color.FromRgb(100, 50, 100),    // Black
                "W" => Color.FromRgb(220, 220, 180),   // White
                _ => Color.FromRgb(100, 100, 100)      // Default gray
            };
        }

    }
}

