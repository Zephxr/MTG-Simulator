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
using System.Windows.Threading;
using Microsoft.Win32;
using System.Threading.Tasks;
using MTGSimulator.Game;
using MTGSimulator.Rendering;
using MTGSimulator.Settings;
using MTGSimulator.Views;
using MTGSimulator.Services;
using MTGSimulator.Handlers;

namespace MTGSimulator
{
    public partial class MainWindow : Window
    {
        // Expose GameCanvas for hand window to access (GameCanvas is defined in XAML)
        public Canvas MainGameCanvas => GameCanvas;
        
        private GameState? _gameState;
        private GameRenderer? _renderer;
        private AppSettings _settings;
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        
        private Card? _draggedCard = null;
        private Point _dragOffset = new Point(0, 0);
        private bool _isDragging = false;
        private bool _isSelecting = false;
        private Point _selectionStart = new Point(0, 0);
        private Rect? _selectionBox = null;
        private Point _lastClickPosition = new Point(0, 0);
        private Dictionary<Card, Point> _selectedCardsInitialPositions = new Dictionary<Card, Point>();
        private CardImageService? _cardImageService;
        private Services.BulkDataService? _bulkDataService;
        private DateTime _lastImageLoadCheck = DateTime.MinValue;
        private HashSet<Card> _cardsLoadingImages = new HashSet<Card>();
        private Views.HandViewerWindow? _handWindow;
        private Views.CardInfoWindow? _cardInfoWindow;
        private Card? _lastHoveredCard = null;
        private Services.GameLogger? _gameLogger;
        private bool _isAttachMode = false;
        private List<Card> _cardsToAttach = new List<Card>();
        private string? _originalStatusText = null;
        
        private MouseButtonState _lastLeftButtonState = MouseButtonState.Released;
        private MouseButtonState _lastRightButtonState = MouseButtonState.Released;
        private Point _lastMousePosition = new Point(0, 0);
        private DateTime _lastManualClickTime = DateTime.MinValue;
        private DateTime _lastEventClickTime = DateTime.MinValue;
        private const double ManualClickCooldownMs = 50;
        private const double EventClickCooldownMs = 200;
        
        private Card? _lastClickedCard = null;
        private DateTime _lastClickTime = DateTime.MinValue;
        private const double DoubleClickTimeMs = 500;
        
        private bool _isContextMenuOpen = false;
        private int _renderCallCount = 0;
        private DateTime _lastRenderLogTime = DateTime.MinValue;

        // Handlers
        private InputHandler? _inputHandler;
        private DragDropHandler? _dragDropHandler;
        private CardContextMenuBuilder? _contextMenuBuilder;

        public MainWindow(BulkDataService? bulkDataService = null)
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            ApplySettings();
            InitializeGame(bulkDataService);
            StartRenderLoop();
            SetupMouseHandlers();
        }

        private void ApplySettings()
        {
            if (_settings.StartMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void SetupMouseHandlers()
        {
            GameCanvas.MouseDown += GameCanvas_MouseDown;
            GameCanvas.MouseMove += GameCanvas_MouseMove;
            GameCanvas.MouseUp += GameCanvas_MouseUp;
            GameCanvas.MouseRightButtonDown += GameCanvas_MouseRightButtonDown;
            GameCanvas.MouseLeave += GameCanvas_MouseLeave;  // Handle mouse leaving window
            this.KeyDown += MainWindow_KeyDown;
            this.Focusable = true;
        }
        
        private void GameCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            // Release mouse capture if mouse leaves the window while dragging/selecting
            if (GameCanvas.IsMouseCaptured)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    _draggedCard = null;
                    _selectedCardsInitialPositions.Clear();
                }
                if (_isSelecting)
                {
                    _isSelecting = false;
                    _selectionBox = null;
                }
                GameCanvas.ReleaseMouseCapture();
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            HandleKeyDown(e);
        }
        
        public void HandleKeyDown(KeyEventArgs e)
        {
            if (_inputHandler == null) return;

            // Handle Escape key to cancel attach mode
            if (e.Key == Key.Escape && _isAttachMode)
            {
                CancelAttachMode();
                e.Handled = true;
                return;
            }

            _inputHandler.HandleKeyDown(e, CancelAttachMode);
            
            // Update hand window if needed
            if (e.Handled && KeybindHelper.MatchesKeybind(_settings.DrawCardKey, e))
            {
                _handWindow?.UpdateHand();
            }
        }

        private void LogGameAction(string message)
        {
            _gameLogger?.Log(message);
        }

        public void LogAction(string message)
        {
            LogGameAction(message);
        }


        private void InitializeGame(BulkDataService? bulkDataService)
        {
            _gameState = new GameState();
            _renderer = new GameRenderer(GameCanvas);
            _cardImageService = new CardImageService(_settings.MaxCachedImages);
            
            _bulkDataService = bulkDataService ?? new Services.BulkDataService(_settings.BulkDataDirectory);
            if (!string.IsNullOrEmpty(_settings.BulkDataDirectory) && _bulkDataService != null)
            {
                _bulkDataService.SetBulkDataDirectory(_settings.BulkDataDirectory);
            }
            
            _gameLogger = new Services.GameLogger();
            _gameLogger.Log("Game initialized");
            
            _gameState.Initialize();
            InitializeHandlers();
            
            StatusText.Text = "Game initialized";
            Loaded += MainWindow_Loaded;
        }

        private async Task CheckBulkDataOnStartup()
        {
            if (_bulkDataService == null) return;

            bool hasData = await _bulkDataService.CheckAndLoadBulkData();
            if (!hasData)
            {
                var result = MessageBox.Show(
                    "Card database is not downloaded.\n\n" +
                    "Would you like to download it now? (~500MB)\n\n" +
                    "You can also download it later from Settings > Bulk Data tab.",
                    "Card Database",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadBulkDataWithProgress();
                }
            }
            // Bulk data loaded successfully (no logging needed)
        }

        private async Task DownloadBulkDataWithProgress()
        {
            if (_bulkDataService == null) return;

            var progressWindow = new Window
            {
                Title = "Downloading Bulk Data",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var progressGrid = new Grid();
            progressGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var progressBar = new ProgressBar
            {
                Height = 30,
                Margin = new Thickness(20)
            };
            Grid.SetRow(progressBar, 0);
            progressGrid.Children.Add(progressBar);

            var statusLabel = new TextBlock
            {
                Text = "Starting download...",
                Margin = new Thickness(20, 0, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(statusLabel, 1);
            progressGrid.Children.Add(statusLabel);

            progressWindow.Content = progressGrid;
            progressWindow.Show();

            try
            {
                bool success = await _bulkDataService.DownloadBulkDataAsync(
                    new Progress<(long bytesReceived, long totalBytes, string status)>(p =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (p.totalBytes > 0)
                            {
                                progressBar.Maximum = p.totalBytes;
                                progressBar.Value = p.bytesReceived;
                            }
                            statusLabel.Text = p.status;
                        });
                    }));

                progressWindow.Close();

                if (success)
                {
                    MessageBox.Show("Card database downloaded successfully!", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to download card database. You can try again from Settings > Bulk Data tab.", 
                        "Download Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                progressWindow.Close();
                MessageBox.Show($"Error downloading card database: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeHandlers()
        {
            if (_gameState == null) return;

            _inputHandler = new InputHandler(
                _gameState,
                _settings,
                _gameLogger,
                LogGameAction,
                UpdateCardInfo,
                GetZoneAtPosition,
                (p) => _gameState.GetCardAt(p.X, p.Y),
                ToggleHand,
                ToggleCardInfo);

            _dragDropHandler = new DragDropHandler(
                _gameState,
                GameCanvas,
                LogGameAction,
                GetZoneAtPosition);

            _contextMenuBuilder = new CardContextMenuBuilder(
                _gameState,
                _cardImageService,
                _handWindow,
                this,
                LogGameAction,
                ShowInputDialog);
        }

        // Public methods for handlers to call
        public void EnterAttachMode(List<Card> cardsToAttach)
        {
            _isAttachMode = true;
            _cardsToAttach = cardsToAttach;
            _originalStatusText = StatusText.Text;
            StatusText.Text = $"Attach Mode: Click on a card to attach {cardsToAttach.Count} card(s) to it. Press Escape to cancel.";
            if (_inputHandler != null)
            {
                _inputHandler.IsAttachMode = true;
                _inputHandler.CardsToAttach.Clear();
                _inputHandler.CardsToAttach.AddRange(cardsToAttach);
            }
        }

        public void CancelAttachMode()
        {
            _isAttachMode = false;
            _cardsToAttach.Clear();
            if (_originalStatusText != null)
            {
                StatusText.Text = _originalStatusText;
                _originalStatusText = null;
            }
            if (_inputHandler != null)
            {
                _inputHandler.IsAttachMode = false;
                _inputHandler.CardsToAttach.Clear();
            }
        }

        public void SetStatusText(string text)
        {
            StatusText.Text = text;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Create hand window and card info window after main window is fully loaded
            try
            {
                if (_gameState != null)
                {
                    _handWindow = new Views.HandViewerWindow(_gameState, this);
                    _handWindow.Hide(); // Start hidden
                    
                    _cardInfoWindow = new Views.CardInfoWindow();
                    _cardInfoWindow.Owner = this;
                    // Position card info window on the right side of the screen
                    var screen = System.Windows.SystemParameters.WorkArea;
                    _cardInfoWindow.Left = screen.Width - _cardInfoWindow.Width - 50;
                    _cardInfoWindow.Top = 100;
                    // Set up game logger
                    if (_gameLogger != null)
                    {
                        _cardInfoWindow.SetGameLogger(_gameLogger);
                    }
                    _cardInfoWindow.Hide(); // Start hidden
                }
                
                // Bulk data should already be loaded from menu, but check if it wasn't
                if (_bulkDataService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        await Dispatcher.InvokeAsync(async () => await CheckBulkDataOnStartup());
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating hand window: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                // Continue without hand window if there's an error
            }
        }

        private void StartRenderLoop()
        {
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_gameState == null || _renderer == null) return;

            _renderCallCount++;
            CheckMouseStateManually();

            // Lazy load card images
            var currentTime = DateTime.Now;
            if (_cardImageService != null && (currentTime - _lastImageLoadCheck).TotalSeconds >= 2.0)
            {
                _lastImageLoadCheck = currentTime;
                
                var cardsNeedingImages = new List<Card>();
                
                // Check battlefield cards (prioritize visible cards)
                foreach (var card in _gameState.Battlefield)
                {
                    if (string.IsNullOrEmpty(card.ImagePath) && 
                        !_cardsLoadingImages.Contains(card) &&
                        string.IsNullOrEmpty(card.ScryfallId))
                    {
                        cardsNeedingImages.Add(card);
                    }
                }
                
                // Check hand cards (high priority - visible in hand window)
                foreach (var card in _gameState.Hand)
                {
                    if (string.IsNullOrEmpty(card.ImagePath) && 
                        !_cardsLoadingImages.Contains(card) &&
                        string.IsNullOrEmpty(card.ScryfallId))
                    {
                        cardsNeedingImages.Insert(0, card); // High priority
                    }
                }
                
                // Check graveyard/exile for preview cards (high priority)
                var topGraveyard = _gameState.GetTopCard("graveyard");
                if (topGraveyard != null && 
                    string.IsNullOrEmpty(topGraveyard.ImagePath) && 
                    !_cardsLoadingImages.Contains(topGraveyard) &&
                    string.IsNullOrEmpty(topGraveyard.ScryfallId))
                {
                    cardsNeedingImages.Insert(0, topGraveyard);
                }
                
                var topExile = _gameState.GetTopCard("exile");
                if (topExile != null && 
                    string.IsNullOrEmpty(topExile.ImagePath) && 
                    !_cardsLoadingImages.Contains(topExile) &&
                    string.IsNullOrEmpty(topExile.ScryfallId))
                {
                    cardsNeedingImages.Insert(0, topExile);
                }
                
                // Download images for up to 2 cards at a time (to avoid blocking)
                if (cardsNeedingImages.Count > 0)
                {
                    var cardsToDownload = cardsNeedingImages.Take(2).ToList();
                    _ = Task.Run(async () =>
                    {
                        foreach (var card in cardsToDownload)
                        {
                            _cardsLoadingImages.Add(card);
                            try
                            {
                                var imagePath = await _cardImageService.DownloadCardImageAsync(card);
                                if (imagePath != null)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        card.ImagePath = imagePath;
                                        _cardsLoadingImages.Remove(card);
                                        
                                        // Refresh hand window if this card is in hand
                                        if (_handWindow != null && _handWindow.IsVisible && _gameState.Hand.Contains(card))
                                        {
                                            _handWindow.ForceRefresh();
                                        }
                                    });
                                }
                                else
                                {
                                    _cardsLoadingImages.Remove(card);
                                }
                            }
                            catch
                            {
                                _cardsLoadingImages.Remove(card);
                            }
                        }
                    });
                }
            }

            // Skip rendering if context menu is open to prevent flickering
            // The context menu is a popup that doesn't need the canvas to be redrawn
            if (!_isContextMenuOpen)
            {
                // Render the game with selection box
                _renderer.Render(_gameState, _selectionBox);
                
                // Re-add drag preview if it exists (renderer clears canvas, so we need to restore it)
                if (_dragPreview != null && !GameCanvas.Children.Contains(_dragPreview))
                {
                    GameCanvas.Children.Add(_dragPreview);
                }
            }

            // Update counters
            DeckCountText.Text = $"Deck: {_gameState.DeckCount}";
            HandCountText.Text = $"Hand: {_gameState.HandCount}";
            
            if (_handWindow != null && _handWindow.IsVisible)
            {
                _handWindow.UpdateHand();
            }

            // Update FPS counter
            _frameCount++;
            var fpsTime = DateTime.Now;
            if ((fpsTime - _lastFpsUpdate).TotalSeconds >= 1.0)
            {
                FpsText.Text = $"FPS: {_frameCount} | Selected: {_gameState.SelectedCards.Count}";
                _frameCount = 0;
                _lastFpsUpdate = fpsTime;
            }
        }

        private void ClearDragSelectState()
        {
            _isDragging = false;
            _isSelecting = false;
            _selectionBox = null;
            _draggedCard = null;
            if (GameCanvas.IsMouseCaptured)
            {
                GameCanvas.ReleaseMouseCapture();
            }
        }

        private void HandleDoubleClick(Card card)
        {
            if (_gameState == null) return;
            
            if (_gameState.SelectedCards.Count > 1)
            {
                int tappedCount = 0;
                int untappedCount = 0;
                foreach (var selectedCard in _gameState.SelectedCards)
                {
                    bool wasTapped = selectedCard.IsTapped;
                    selectedCard.IsTapped = !selectedCard.IsTapped;
                    if (selectedCard.IsTapped && !wasTapped) tappedCount++;
                    if (!selectedCard.IsTapped && wasTapped) untappedCount++;
                }
                if (tappedCount > 0) LogGameAction($"Tapped {tappedCount} card(s)");
                if (untappedCount > 0) LogGameAction($"Untapped {untappedCount} card(s)");
            }
            else
            {
                card.IsTapped = !card.IsTapped;
                LogGameAction(card.IsTapped ? $"Tapped {card.Name}" : $"Untapped {card.Name}");
            }
            card.OnClicked(_gameLogger);
        }

        private void CheckMouseStateManually()
        {
            if (_gameState == null || _isDragging || _isSelecting || _isContextMenuOpen || !GameCanvas.IsMouseOver) return;
            
            var currentLeftState = Mouse.LeftButton;
            var currentRightState = Mouse.RightButton;
            var mousePosition = Mouse.GetPosition(GameCanvas);
            
            if (currentLeftState == MouseButtonState.Pressed && _lastLeftButtonState == MouseButtonState.Released)
            {
                var timeSinceLastClick = (DateTime.Now - _lastManualClickTime).TotalMilliseconds;
                var timeSinceEventClick = (DateTime.Now - _lastEventClickTime).TotalMilliseconds;
                
                bool mouseOverCanvas = mousePosition.X >= 0 && mousePosition.Y >= 0 && 
                    mousePosition.X <= GameCanvas.ActualWidth && 
                    mousePosition.Y <= GameCanvas.ActualHeight;
                
                if (!mouseOverCanvas) return;
                
                if (timeSinceEventClick >= 50 && timeSinceLastClick > ManualClickCooldownMs)
                {
                    _lastManualClickTime = DateTime.Now;
                    HandleManualClick(mousePosition, false);
                }
            }
            
            if (currentRightState == MouseButtonState.Pressed && _lastRightButtonState == MouseButtonState.Released)
            {
                var timeSinceLastClick = (DateTime.Now - _lastManualClickTime).TotalMilliseconds;
                var timeSinceEventClick = (DateTime.Now - _lastEventClickTime).TotalMilliseconds;
                
                if (timeSinceLastClick > ManualClickCooldownMs && timeSinceEventClick > EventClickCooldownMs)
                {
                    if (mousePosition.X >= 0 && mousePosition.Y >= 0 && 
                        mousePosition.X <= GameCanvas.ActualWidth && 
                        mousePosition.Y <= GameCanvas.ActualHeight &&
                        !GameCanvas.IsMouseCaptured)
                    {
                        _lastManualClickTime = DateTime.Now;
                        HandleManualClick(mousePosition, true);
                    }
                }
            }
            
            _lastLeftButtonState = currentLeftState;
            _lastRightButtonState = currentRightState;
            _lastMousePosition = mousePosition;
        }

        private void HandleManualClick(Point position, bool isRightClick)
        {
            if (_gameState == null || _isAttachMode || _isContextMenuOpen) return;
            
            var card = _gameState.GetCardAt(position.X, position.Y);
            
            if (!isRightClick && card != null && _lastClickedCard == card && _lastClickTime != DateTime.MinValue)
            {
                var timeSinceLastClick = (DateTime.Now - _lastClickTime).TotalMilliseconds;
                if (timeSinceLastClick <= DoubleClickTimeMs)
                {
                    ClearDragSelectState();
                    HandleDoubleClick(card);
                    _lastClickedCard = null;
                    _lastClickTime = DateTime.MinValue;
                    return;
                }
            }
            
            if (isRightClick)
            {
                // Handle right-click manually
                if (card != null)
                {
                    ShowCardContextMenu(card, position);
                }
                else
                {
                    ShowEmptySpaceContextMenu(position);
                }
            }
            else
            {
                if (!_isDragging && !_isSelecting)
                {
                    if (card == null)
                    {
                        string? clickedZone = GetZoneAtPosition(position);
                        if (clickedZone == "deck" && _gameState.Deck.Count > 0)
                        {
                            var topCard = _gameState.Deck[0];
                            double leftMargin = 20;
                            double topMargin = 20;
                            double zoneX = leftMargin;
                            double zoneWidth = Rendering.GameRenderer.ZoneWidth;
                            double zoneHeight = Rendering.GameRenderer.ZoneHeight;
                            double cardWidth = _gameState.CardWidth;
                            double cardHeight = _gameState.CardHeight;
                            
                            // Card preview is centered in the zone
                            double cardX = zoneX + (zoneWidth - cardWidth) / 2;
                            double cardY = topMargin + (zoneHeight - cardHeight) / 2;
                            
                            if (position.X >= cardX && position.X <= cardX + cardWidth &&
                                position.Y >= cardY && position.Y <= cardY + cardHeight)
                            {
                                // Start dragging the top card from library
                                _draggedCard = topCard;
                                double cardCenterX = cardX + cardWidth / 2;
                                double cardCenterY = cardY + cardHeight / 2;
                                _dragOffset = new Point(position.X - cardCenterX, position.Y - cardCenterY);
                                _isDragging = true;
                                GameCanvas.CaptureMouse();
                                var screenPosition = this.PointToScreen(position);
                                ShowDragPreview(null, screenPosition, true);
                                return;
                            }
                        }
                    }
                    
                    if (card != null)
                    {
                        // Select the card
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
                        
                        _draggedCard = card;
                        double cardCenterX = card.X + _gameState.CardWidth / 2;
                        double cardCenterY = card.Y + _gameState.CardHeight / 2;
                        _dragOffset = new Point(position.X - cardCenterX, position.Y - cardCenterY);
                        
                        _selectedCardsInitialPositions.Clear();
                        foreach (var selectedCard in _gameState.SelectedCards)
                        {
                            _selectedCardsInitialPositions[selectedCard] = new Point(selectedCard.X, selectedCard.Y);
                        }
                        
                        _isDragging = true;
                        GameCanvas.CaptureMouse();
                        _lastClickedCard = card;
                        _lastClickTime = DateTime.Now;
                    }
                    else
                    {
                        if (Keyboard.Modifiers != ModifierKeys.Control)
                        {
                            _gameState.SelectedCards.Clear();
                        }
                        _isSelecting = true;
                        _selectionStart = position;
                        _selectionBox = new Rect(position, position);
                        GameCanvas.CaptureMouse();
                        _lastClickedCard = null;
                        _lastClickTime = DateTime.MinValue;
                    }
                }
            }
        }

        private void NewGame_Click(object sender, RoutedEventArgs e)
        {
            if (_gameState != null)
            {
                _gameState.CardWidth = _settings.CardWidth;
                _gameState.CardHeight = _settings.CardHeight;
                _gameState.Initialize();
            }
            
            _cardImageService?.ClearDeckFolder();
            
            StatusText.Text = "New game started";
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new Settings.SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() == true)
            {
                _settings = settingsWindow.Settings;
                ApplySettings();
                // Notify renderer about card size changes
                if (_renderer != null)
                {
                    _renderer.UpdateCardSize(_settings.CardWidth, _settings.CardHeight);
                }
                // Update game state card sizes
                if (_gameState != null)
                {
                    _gameState.CardWidth = _settings.CardWidth;
                    _gameState.CardHeight = _settings.CardHeight;
                }
                // Update card image service cache limit
                if (_cardImageService != null)
                {
                    _cardImageService.SetMaxCachedImages(_settings.MaxCachedImages);
                }
                // Update bulk data service directory
                if (_bulkDataService != null && !string.IsNullOrEmpty(_settings.BulkDataDirectory))
                {
                    _bulkDataService.SetBulkDataDirectory(_settings.BulkDataDirectory);
                }
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "MTG Simulator by Zephyr\n\nVersion 0.1.0",
                "About",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void ImportDeck_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Deck Files (*.txt;*.dec)|*.txt;*.dec|All Files (*.*)|*.*",
                Title = "Import Deck"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "Importing deck...";
                    
                    // Import deck from file
                    var deckEntries = DeckImporter.ImportDeckFromFile(openFileDialog.FileName);
                    var cards = DeckImporter.CreateCardsFromDeck(deckEntries);
                    
                    if (cards.Count == 0)
                    {
                        MessageBox.Show("No cards found in deck file.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusText.Text = "Deck import failed - no cards found";
                        return;
                    }

                    // Automatically download images in background
                    if (_cardImageService != null)
        {
                        // Set deck folder based on file name
                        string deckName = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                        _cardImageService.SetDeckFolder(deckName);

                        StatusText.Text = $"Importing deck and downloading images... ({cards.Count} cards)";
                        
                        // Show progress dialog
                        var progressWindow = new Window
                        {
                            Title = "Importing Deck",
                            Width = 400,
                            Height = 150,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Owner = this
                        };

                        var progressGrid = new Grid();
                        progressGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        var progressBar = new ProgressBar
                        {
                            Maximum = cards.Count,
                            Height = 30,
                            Margin = new Thickness(20)
                        };
                        Grid.SetRow(progressBar, 0);
                        progressGrid.Children.Add(progressBar);

                        var statusLabel = new TextBlock
                        {
                            Text = "Starting download...",
                            Margin = new Thickness(20, 0, 20, 20),
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        Grid.SetRow(statusLabel, 1);
                        progressGrid.Children.Add(statusLabel);

                        progressWindow.Content = progressGrid;
                        progressWindow.Show();

                        // Download images with verification
                        var downloadTask = _cardImageService.DownloadCardImagesWithVerificationAsync(
                            cards, 
                            new Progress<(int current, int total, string cardName)>(p =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    progressBar.Value = p.current;
                                    statusLabel.Text = $"Downloading: {p.cardName} ({p.current}/{p.total})";
                                });
                            }));

                        // Replace deck immediately (don't wait for images)
                        if (_gameState != null)
                        {
                            _gameState.LoadDeck(cards);
                            LogGameAction($"Imported deck: {cards.Count} cards");
                            // Update hand window
                            _handWindow?.UpdateHand();
                        }

                        // Wait for images to finish downloading and verify
                        var result = await downloadTask;

                        progressWindow.Close();
            
                        // Force refresh hand window to show newly loaded images
                        _handWindow?.ForceRefresh();

                        // Show results
                        if (result.failed > 0)
                        {
                            string failedList = string.Join("\n", result.failedCards.Take(10));
                            if (result.failedCards.Count > 10)
                            {
                                failedList += $"\n... and {result.failedCards.Count - 10} more";
                            }
                            
                            MessageBox.Show(
                                $"Deck imported: {result.downloaded} images downloaded, {result.failed} failed.\n\n" +
                                $"Failed cards:\n{failedList}",
                                "Import Complete",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            StatusText.Text = $"Deck imported: {cards.Count} cards ({result.downloaded} images, {result.failed} failed)";
                        }
                        else
                        {
                            StatusText.Text = $"Deck imported: {cards.Count} cards (all images downloaded)";
                        }
                    }
                    else
                    {
                        // Load deck even if image service isn't available
                        if (_gameState != null)
                        {
                            _gameState.LoadDeck(cards);
                            StatusText.Text = $"Deck imported: {cards.Count} cards";
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error importing deck:\n{ex.Message}",
                        "Import Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusText.Text = "Deck import failed";
                }
            }
        }

        public string? GetZoneAtPosition(Point position)
        {
            if (_gameState == null) return null;
            
            double leftMargin = 20;
            double topMargin = 20;
            double zoneX = leftMargin;
            double zoneWidth = Rendering.GameRenderer.ZoneWidth;
            double zoneHeight = Rendering.GameRenderer.ZoneHeight;
            double zoneSpacing = Rendering.GameRenderer.ZoneSpacing;
            
            double deckY = topMargin;
            double graveyardY = deckY + zoneHeight + zoneSpacing;
            double exileY = graveyardY + zoneHeight + zoneSpacing;
            
            if (position.X >= zoneX && position.X <= zoneX + zoneWidth)
            {
                if (position.Y >= deckY && position.Y <= deckY + zoneHeight)
                {
                    return "deck";
                }
                else if (position.Y >= graveyardY && position.Y <= graveyardY + zoneHeight)
                {
                    return "graveyard";
                }
                else if (position.Y >= exileY && position.Y <= exileY + zoneHeight)
                {
                    return "exile";
                }
            }
            
            return null;
        }

        private Rectangle? _dragPreview = null;
        
        public void ShowDragPreview(Card? card, Point screenPosition, bool showCardBack = false)
        {
            // Convert screen position to canvas position
            Point canvasPosition;
            try
            {
                canvasPosition = GameCanvas.PointFromScreen(screenPosition);
            }
            catch
            {
                // If conversion fails, use a default position
                canvasPosition = new Point(400, 300);
            }
            
            // Get card dimensions (used throughout the method)
            double cardWidth = _gameState?.CardWidth ?? 120;
            double cardHeight = _gameState?.CardHeight ?? 168;
            
            // Update existing preview or create new one
            if (_dragPreview != null)
            {
                // Just update position of existing preview (centered on cursor)
                Canvas.SetLeft(_dragPreview, canvasPosition.X - cardWidth / 2);
                Canvas.SetTop(_dragPreview, canvasPosition.Y - cardHeight / 2);
                
                // Make sure it's in the canvas children (renderer might have cleared it)
                if (!GameCanvas.Children.Contains(_dragPreview))
                {
                    GameCanvas.Children.Add(_dragPreview);
                }
                return;
            }
            
            // Create preview rectangle
            if (showCardBack || card == null)
            {
                // Show card back (generic card shape)
                _dragPreview = new Rectangle
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    Fill = new SolidColorBrush(Color.FromRgb(50, 50, 100)),  // Card back color
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 3,
                    StrokeDashArray = new DoubleCollection { 5, 5 },  // Dashed outline
                    RadiusX = 5,
                    RadiusY = 5,
                    Opacity = 0.9,
                    IsHitTestVisible = false  // Don't interfere with mouse events
                };
            }
            else
            {
                // Show card outline (for revealed cards)
                _dragPreview = new Rectangle
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    Fill = Brushes.Transparent,  // No fill, just outline
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 3,
                    StrokeDashArray = new DoubleCollection { 5, 5 },  // Dashed outline
                    RadiusX = 5,
                    RadiusY = 5,
                    Opacity = 0.9,
                    IsHitTestVisible = false  // Don't interfere with mouse events
                };
            }
            
            // Position preview at mouse position (centered on cursor)
            Canvas.SetLeft(_dragPreview, canvasPosition.X - cardWidth / 2);
            Canvas.SetTop(_dragPreview, canvasPosition.Y - cardHeight / 2);
            
            // Add to canvas (on top)
            GameCanvas.Children.Add(_dragPreview);
        }
        
        public void HideDragPreview()
        {
            if (_dragPreview != null)
            {
                GameCanvas.Children.Remove(_dragPreview);
                _dragPreview = null;
            }
        }
        
        private Color GetColorForManaCost(string manaCost)
        {
            if (string.IsNullOrEmpty(manaCost))
                return Color.FromRgb(100, 100, 100);

            manaCost = manaCost.ToUpper();
            
            if (manaCost.Contains("R") || manaCost.Contains("{R}"))
                return Color.FromRgb(200, 50, 50);
            if (manaCost.Contains("G") || manaCost.Contains("{G}"))
                return Color.FromRgb(50, 150, 50);
            if (manaCost.Contains("U") || manaCost.Contains("{U}"))
                return Color.FromRgb(50, 100, 200);
            if (manaCost.Contains("B") || manaCost.Contains("{B}"))
                return Color.FromRgb(100, 50, 100);
            if (manaCost.Contains("W") || manaCost.Contains("{W}"))
                return Color.FromRgb(250, 250, 200);
            if (manaCost.Contains("X") || manaCost.Contains("{X}"))
                return Color.FromRgb(150, 150, 150);

            return Color.FromRgb(100, 100, 100);
        }

        private void GameCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_gameState == null) return;
            
            // Mark that event system handled this click
            _lastEventClickTime = DateTime.Now;

            var position = e.GetPosition(GameCanvas);
            string? clickedZone = GetZoneAtPosition(position);
            
            if (clickedZone == "deck")
            {
                e.Handled = true;
                
                var contextMenu = new ContextMenu
                {
                    Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
                };
                
                // View Library
                var viewLibraryItem = new MenuItem
                {
                    Header = "View Library",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                viewLibraryItem.Click += (s, args) =>
                {
                    var viewer = new ZoneViewerWindow(_gameState, "Library", _gameState.Deck, this);
                    viewer.ShowDialog();
                };
                contextMenu.Items.Add(viewLibraryItem);
                
                // View Top Card of Library
                var viewTopCardItem = new MenuItem
                {
                    Header = "View Top Card of Library",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                viewTopCardItem.Click += (s, args) =>
                {
                    if (_gameState.Deck.Count > 0)
                    {
                        var topCard = _gameState.Deck[0];
                        var viewer = new ZoneViewerWindow(_gameState, "Top Card", new List<Card> { topCard }, this);
                        viewer.ShowDialog();
                    }
                };
                contextMenu.Items.Add(viewTopCardItem);
                
                // View Top X Cards of Library
                var viewTopXCardsItem = new MenuItem
                {
                    Header = "View Top X Cards of Library",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                viewTopXCardsItem.Click += (s, args) =>
                {
                    string? input = ShowInputDialog("View Top Cards", "How many cards from the top?", "5");
                    if (input != null && int.TryParse(input, out int count) && count > 0)
                    {
                        var topCards = _gameState.Deck.Take(count).ToList();
                        if (topCards.Count > 0)
                        {
                            var viewer = new ZoneViewerWindow(_gameState, $"Top {topCards.Count} Cards", topCards, this);
                            viewer.ShowDialog();
                        }
                    }
                };
                contextMenu.Items.Add(viewTopXCardsItem);
                
                // View Bottom X Cards of Library
                var viewBottomXCardsItem = new MenuItem
                {
                    Header = "View Bottom X Cards of Library",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                viewBottomXCardsItem.Click += (s, args) =>
                {
                    string? input = ShowInputDialog("View Bottom Cards", "How many cards from the bottom?", "5");
                    if (input != null && int.TryParse(input, out int count) && count > 0)
                    {
                        var bottomCards = _gameState.Deck.Skip(Math.Max(0, _gameState.Deck.Count - count)).ToList();
                        if (bottomCards.Count > 0)
                        {
                            var viewer = new ZoneViewerWindow(_gameState, $"Bottom {bottomCards.Count} Cards", bottomCards, this);
                            viewer.ShowDialog();
                        }
                    }
                };
                contextMenu.Items.Add(viewBottomXCardsItem);
                
                contextMenu.Items.Add(new Separator());
                
                // Always Reveal Top of Library (toggle)
                var revealTopItem = new MenuItem
                {
                    Header = _gameState.AlwaysRevealTopOfLibrary ? "Hide Top of Library" : "Always Reveal Top of Library",
                    Foreground = System.Windows.Media.Brushes.Black,
                    IsCheckable = true,
                    IsChecked = _gameState.AlwaysRevealTopOfLibrary
                };
                revealTopItem.Click += (s, args) =>
                {
                    _gameState.AlwaysRevealTopOfLibrary = !_gameState.AlwaysRevealTopOfLibrary;
                    revealTopItem.Header = _gameState.AlwaysRevealTopOfLibrary ? "Hide Top of Library" : "Always Reveal Top of Library";
                    revealTopItem.IsChecked = _gameState.AlwaysRevealTopOfLibrary;
                    LogGameAction(_gameState.AlwaysRevealTopOfLibrary ? "Revealing top of library" : "Hiding top of library");
                    // Update card info if top card is now revealed
                    if (_gameState.AlwaysRevealTopOfLibrary && _cardInfoWindow != null && _cardInfoWindow.IsVisible)
                    {
                        var topCard = _gameState.GetTopCard("deck");
                        if (topCard != null && _lastHoveredCard == null)
                        {
                            _lastHoveredCard = topCard;
                            UpdateCardInfo(topCard);
                        }
                    }
                };
                contextMenu.Items.Add(revealTopItem);
                
                contextMenu.Items.Add(new Separator());
                
                // Shuffle Library
                var shuffleItem = new MenuItem
                {
                    Header = "Shuffle Library",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                shuffleItem.Click += (s, args) =>
                {
                    _gameState.ShuffleDeck();
                    LogGameAction("Shuffled library");
                };
                contextMenu.Items.Add(shuffleItem);
                
                contextMenu.Items.Add(new Separator());
                
                // Draw Card
                var drawCardItem = new MenuItem
                {
                    Header = "Draw Card",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                drawCardItem.Click += (s, args) =>
                {
                    int drawn = _gameState.DrawCards(1);
                    if (drawn > 0)
                    {
                        LogGameAction($"Drew {drawn} card(s)");
                    }
                    else
                    {
                        LogGameAction("Couldn't draw any more cards (library is empty)");
                    }
                    _handWindow?.UpdateHand();
                };
                contextMenu.Items.Add(drawCardItem);
                
                // Draw Cards...
                var drawCardsItem = new MenuItem
                {
                    Header = "Draw Cards...",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                drawCardsItem.Click += (s, args) =>
                {
                    string? input = ShowInputDialog("Draw Cards", "How many cards to draw?", "1");
                    if (input != null && int.TryParse(input, out int count) && count > 0)
                    {
                        int drawn = _gameState.DrawCards(count);
                        if (drawn == count)
                        {
                            LogGameAction($"Drew {drawn} card(s)");
                        }
                        else if (drawn > 0)
                        {
                            LogGameAction($"Drew {drawn} card(s), couldn't draw {count - drawn} due to an empty library");
                        }
                        else
                        {
                            LogGameAction("Couldn't draw any cards (library is empty)");
                        }
                        _handWindow?.UpdateHand();
                    }
                };
                contextMenu.Items.Add(drawCardsItem);
                
                contextMenu.Items.Add(new Separator());
                
                // Mill Top Card
                var millTopCardItem = new MenuItem
                {
                    Header = "Mill Top Card",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                millTopCardItem.Click += (s, args) =>
                {
                    _gameState.MillTopCard();
                    LogGameAction("Milled top card");
                };
                contextMenu.Items.Add(millTopCardItem);
                
                // Mill Top Cards...
                var millTopCardsItem = new MenuItem
                {
                    Header = "Mill Top Cards...",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                millTopCardsItem.Click += (s, args) =>
                {
                    string? input = ShowInputDialog("Mill Cards", "How many cards to mill?", "1");
                    if (input != null && int.TryParse(input, out int count) && count > 0)
                    {
                        _gameState.MillTopCards(count);
                        LogGameAction($"Milled {count} card(s)");
                    }
                };
                contextMenu.Items.Add(millTopCardsItem);
                
                // Put Selected on Top / Bottom (only show if cards are selected)
                if (_gameState.SelectedCards.Count > 0)
                {
                    contextMenu.Items.Add(new Separator());
                    
                    var putOnTopItem = new MenuItem
                    {
                        Header = "Put Selected on Top",
                        Foreground = System.Windows.Media.Brushes.Black
                    };
                    putOnTopItem.Click += (s, args) =>
                    {
                        // Put selected cards on top in reverse order (so first selected is on top)
                        var selectedList = _gameState.SelectedCards.ToList();
                        foreach (var card in selectedList)
                        {
                            _gameState.PutCardOnTop(card);
                        }
                        LogGameAction($"Put {selectedList.Count} card(s) on top of library");
                    };
                    contextMenu.Items.Add(putOnTopItem);
                    
                    var putOnBottomItem = new MenuItem
                    {
                        Header = "Put Selected on Bottom",
                        Foreground = System.Windows.Media.Brushes.Black
                    };
                    putOnBottomItem.Click += (s, args) =>
                    {
                        // Put selected cards on bottom in order (so first selected is on bottom)
                        var selectedList = _gameState.SelectedCards.ToList();
                        foreach (var card in selectedList)
                        {
                            _gameState.PutCardOnBottom(card);
                        }
                        LogGameAction($"Put {selectedList.Count} card(s) on bottom of library");
                    };
                    contextMenu.Items.Add(putOnBottomItem);
                }
                
                _isContextMenuOpen = true;
                contextMenu.Closed += (s, args) =>
                {
                    _isContextMenuOpen = false;
                };
                contextMenu.IsOpen = true;
            }
            else
            {
                // Check if right-clicking on a card
                var card = _gameState.GetCardAt(position.X, position.Y);
                
                if (card != null)
                {
                    e.Handled = true;
                    ShowCardContextMenu(card, position);
                }
                else
                {
                    // Right-clicked on empty space - show token creation menu
                    e.Handled = true;
                    ShowEmptySpaceContextMenu(position);
                }
            }
        }

        private void ShowEmptySpaceContextMenu(Point position)
        {
            if (_gameState == null || _cardImageService == null) return;

            var contextMenu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };
            
            _isContextMenuOpen = true;
            contextMenu.Closed += (s, args) =>
            {
                _isContextMenuOpen = false;
            };

            // Create Token
            var createTokenItem = new MenuItem
            {
                Header = "Create Token",
                Foreground = System.Windows.Media.Brushes.Black
            };
            createTokenItem.Click += async (s, args) =>
            {
                var tokenWindow = new Views.TokenCreationWindow(_cardImageService, _bulkDataService)
                {
                    Owner = this
                };

                if (tokenWindow.ShowDialog() == true && tokenWindow.SelectedCard != null)
                {
                    await CreateTokenAtPosition(tokenWindow.SelectedCard, position, tokenWindow.PersistsInGraveyard);
                }
            };
            contextMenu.Items.Add(createTokenItem);

            contextMenu.IsOpen = true;
        }

        private async Task CreateTokenAtPosition(Services.CardSearchResult cardResult, Point position, bool persistsInGraveyard)
        {
            if (_gameState == null || _cardImageService == null) return;

            try
            {
                // Create the token card
                var token = new Card(cardResult.Name)
                {
                    ManaCost = cardResult.ManaCost,
                    Type = cardResult.Type,
                    Text = cardResult.OracleText,
                    ScryfallId = cardResult.ScryfallId,
                    IsToken = true,
                    PersistsInGraveyard = persistsInGraveyard,
                    X = position.X - _gameState.CardWidth / 2,
                    Y = position.Y - _gameState.CardHeight / 2,
                    IsTapped = false,
                    Power = cardResult.Power,
                    Toughness = cardResult.Toughness
                };

                // Download image for the token
                string? imagePath = await _cardImageService.DownloadCardImageAsync(token);
                if (imagePath != null)
                {
                    token.ImagePath = imagePath;
                }

                // Add to battlefield
                _gameState.Battlefield.Add(token);
                _gameState.SetMostRecentlyMovedCard(token);

                string persistText = persistsInGraveyard ? " (persists in graveyard)" : "";
                LogGameAction($"Created token: {token.Name}{persistText}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating token: {ex.Message}");
                MessageBox.Show($"Error creating token: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ShowCardContextMenu(Card card, Point position)
        {
            if (_contextMenuBuilder == null) return;

            var contextMenu = await _contextMenuBuilder.BuildContextMenu(card).ConfigureAwait(false);
            
            Dispatcher.Invoke(() =>
            {
                _isContextMenuOpen = true;
                contextMenu.IsOpen = true;
                contextMenu.Closed += (s, args) =>
                {
                    _isContextMenuOpen = false;
                };
            });
        }

        // Legacy method for backward compatibility - extracts the old context menu code
        private async void ShowCardContextMenu_OLD(Card card, Point position)
        {
            if (_gameState == null) return;

            var contextMenu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            // Tap/Untap
            var tapItem = new MenuItem
            {
                Header = card.IsTapped ? "Untap" : "Tap",
                Foreground = System.Windows.Media.Brushes.Black
            };
            tapItem.Click += (s, args) =>
            {
                if (_gameState.SelectedCards.Count > 0)
                {
                    // Tap/untap all selected cards
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
                    if (tappedCount > 0) LogGameAction($"Tapped {tappedCount} card(s)");
                    if (untappedCount > 0) LogGameAction($"Untapped {untappedCount} card(s)");
                }
                else
                {
                    // Tap/untap just this card
                    bool wasTapped = card.IsTapped;
                    card.IsTapped = !card.IsTapped;
                    LogGameAction(card.IsTapped ? $"Tapped {card.Name}" : $"Untapped {card.Name}");
                }
            };
            contextMenu.Items.Add(tapItem);

            contextMenu.Items.Add(new Separator());

            // Move to zones
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
                    LogGameAction($"Moved {selectedList.Count} card(s) to graveyard");
                }
                else
                {
                    _gameState.MoveCardToZone(card, "graveyard");
                    LogGameAction($"Moved {card.Name} to graveyard");
                }
            };
            contextMenu.Items.Add(moveToGraveyardItem);

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
                    LogGameAction($"Moved {selectedList.Count} card(s) to exile");
                }
                else
                {
                    _gameState.MoveCardToZone(card, "exile");
                    LogGameAction($"Moved {card.Name} to exile");
                }
            };
            contextMenu.Items.Add(moveToExileItem);

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
                        // Handle token persistence - tokens that don't persist are removed from game
                        if (selectedCard.IsToken && !selectedCard.PersistsInGraveyard)
                        {
                            selectedCard.DetachAll();
                            selectedCard.Detach();
                            _gameState.Battlefield.Remove(selectedCard);
                            _gameState.SelectedCards.Remove(selectedCard);
                            if (_gameState.MostRecentlyMovedCard == selectedCard)
                            {
                                _gameState.SetMostRecentlyMovedCard(null);
                            }
                            // Token is removed from game, not moved to hand
                            continue;
                        }

                        // Detach all cards attached to this card
                        selectedCard.DetachAll();
                        // Detach this card from its parent if attached
                        selectedCard.Detach();
                        
                        _gameState.Battlefield.Remove(selectedCard);
                        _gameState.SelectedCards.Remove(selectedCard);
                        if (_gameState.MostRecentlyMovedCard == selectedCard)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }
                        _gameState.Hand.Insert(0, selectedCard);
                    }
                    LogGameAction($"Moved {selectedList.Count} card(s) to hand");
                }
                else
                {
                    // Handle token persistence - tokens that don't persist are removed from game
                    if (card.IsToken && !card.PersistsInGraveyard)
                    {
                        card.DetachAll();
                        card.Detach();
                        _gameState.Battlefield.Remove(card);
                        _gameState.SelectedCards.Remove(card);
                        if (_gameState.MostRecentlyMovedCard == card)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }
                        LogGameAction($"{card.Name} (token) was removed from the game");
                        _handWindow?.UpdateHand();
                        return;
                    }

                    // Detach all cards attached to this card
                    card.DetachAll();
                    // Detach this card from its parent if attached
                    card.Detach();
                    
                    _gameState.Battlefield.Remove(card);
                    _gameState.SelectedCards.Remove(card);
                    if (_gameState.MostRecentlyMovedCard == card)
                    {
                        _gameState.SetMostRecentlyMovedCard(null);
                    }
                    _gameState.Hand.Insert(0, card);
                    LogGameAction($"Moved {card.Name} to hand");
                }
                _handWindow?.UpdateHand();
            };
            contextMenu.Items.Add(moveToHandItem);

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
                        // Handle token persistence - tokens that don't persist are removed from game
                        if (selectedCard.IsToken && !selectedCard.PersistsInGraveyard)
                        {
                            selectedCard.DetachAll();
                            selectedCard.Detach();
                            _gameState.Battlefield.Remove(selectedCard);
                            _gameState.SelectedCards.Remove(selectedCard);
                            if (_gameState.MostRecentlyMovedCard == selectedCard)
                            {
                                _gameState.SetMostRecentlyMovedCard(null);
                            }
                            // Token is removed from game, not shuffled
                            continue;
                        }

                        // Detach all cards attached to this card
                        selectedCard.DetachAll();
                        // Detach this card from its parent if attached
                        selectedCard.Detach();
                        
                        _gameState.Battlefield.Remove(selectedCard);
                        _gameState.SelectedCards.Remove(selectedCard);
                        if (_gameState.MostRecentlyMovedCard == selectedCard)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }
                        // Add to random position in deck
                        int randomIndex = new Random().Next(_gameState.Deck.Count + 1);
                        _gameState.Deck.Insert(randomIndex, selectedCard);
                    }
                    LogGameAction($"Shuffled {cardsToShuffle.Count} card(s) into library");
                }
                else
                {
                    // Handle token persistence - tokens that don't persist are removed from game
                    if (card.IsToken && !card.PersistsInGraveyard)
                    {
                        card.DetachAll();
                        card.Detach();
                        _gameState.Battlefield.Remove(card);
                        _gameState.SelectedCards.Remove(card);
                        if (_gameState.MostRecentlyMovedCard == card)
                        {
                            _gameState.SetMostRecentlyMovedCard(null);
                        }
                        LogGameAction($"{card.Name} (token) was removed from the game");
                        return;
                    }

                    // Detach all cards attached to this card
                    card.DetachAll();
                    // Detach this card from its parent if attached
                    card.Detach();
                    
                    _gameState.Battlefield.Remove(card);
                    _gameState.SelectedCards.Remove(card);
                    if (_gameState.MostRecentlyMovedCard == card)
                    {
                        _gameState.SetMostRecentlyMovedCard(null);
                    }
                    // Add to random position in deck
                    int randomIndex = new Random().Next(_gameState.Deck.Count + 1);
                    _gameState.Deck.Insert(randomIndex, card);
                    LogGameAction($"Shuffled {card.Name} into library");
                }
            };
            contextMenu.Items.Add(shuffleIntoLibraryItem);

            contextMenu.Items.Add(new Separator());

            // Counters submenu
            var countersMenu = new MenuItem
            {
                Header = "Counters",
                Foreground = System.Windows.Media.Brushes.Black
            };
            
            // Add +1/+1 counter
            var addPlusOneItem = new MenuItem
            {
                Header = "Add +1/+1",
                Foreground = System.Windows.Media.Brushes.Black
            };
            addPlusOneItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.AddCounter("+1/+1", 1);
                }
                LogGameAction($"Added +1/+1 counter to {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(addPlusOneItem);
            
            // Remove +1/+1 counter
            var removePlusOneItem = new MenuItem
            {
                Header = "Remove +1/+1",
                Foreground = System.Windows.Media.Brushes.Black
            };
            removePlusOneItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.RemoveCounter("+1/+1", 1);
                }
                LogGameAction($"Removed +1/+1 counter from {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(removePlusOneItem);
            
            // Set +1/+1 counter
            var setPlusOneItem = new MenuItem
            {
                Header = "Set +1/+1",
                Foreground = System.Windows.Media.Brushes.Black
            };
            setPlusOneItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                var currentValue = targetCards.FirstOrDefault()?.GetCounter("+1/+1") ?? 0;
                string? input = ShowInputDialog("Set +1/+1 Counters", $"Enter the number of +1/+1 counters to set:", currentValue.ToString());
                if (input != null && int.TryParse(input, out int value))
                {
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.SetCounter("+1/+1", value);
                    }
                    LogGameAction($"Set +1/+1 counters to {value} on {targetCards.Count} card(s)");
                }
            };
            countersMenu.Items.Add(setPlusOneItem);
            
            countersMenu.Items.Add(new Separator());
            
            // Add -1/-1 counter
            var addMinusOneItem = new MenuItem
            {
                Header = "Add -1/-1",
                Foreground = System.Windows.Media.Brushes.Black
            };
            addMinusOneItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.AddCounter("-1/-1", 1);
                }
                LogGameAction($"Added -1/-1 counter to {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(addMinusOneItem);
            
            // Remove -1/-1 counter
            var removeMinusOneItem = new MenuItem
            {
                Header = "Remove -1/-1",
                Foreground = System.Windows.Media.Brushes.Black
            };
            removeMinusOneItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.RemoveCounter("-1/-1", 1);
                }
                LogGameAction($"Removed -1/-1 counter from {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(removeMinusOneItem);
            
            // Set -1/-1 counter
            var setMinusOneItem = new MenuItem
            {
                Header = "Set -1/-1",
                Foreground = System.Windows.Media.Brushes.Black
            };
            setMinusOneItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                var currentValue = targetCards.FirstOrDefault()?.GetCounter("-1/-1") ?? 0;
                string? input = ShowInputDialog("Set -1/-1 Counters", $"Enter the number of -1/-1 counters to set:", currentValue.ToString());
                if (input != null && int.TryParse(input, out int value))
                {
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.SetCounter("-1/-1", value);
                    }
                    LogGameAction($"Set -1/-1 counters to {value} on {targetCards.Count} card(s)");
                }
            };
            countersMenu.Items.Add(setMinusOneItem);
            
            countersMenu.Items.Add(new Separator());
            
            // Add loyalty counter
            var addLoyaltyItem = new MenuItem
            {
                Header = "Add Loyalty",
                Foreground = System.Windows.Media.Brushes.Black
            };
            addLoyaltyItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.AddCounter("loyalty", 1);
                }
                LogGameAction($"Added loyalty counter to {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(addLoyaltyItem);
            
            // Remove loyalty counter
            var removeLoyaltyItem = new MenuItem
            {
                Header = "Remove Loyalty",
                Foreground = System.Windows.Media.Brushes.Black
            };
            removeLoyaltyItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.RemoveCounter("loyalty", 1);
                }
                LogGameAction($"Removed loyalty counter from {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(removeLoyaltyItem);
            
            // Set loyalty counter
            var setLoyaltyItem = new MenuItem
            {
                Header = "Set Loyalty",
                Foreground = System.Windows.Media.Brushes.Black
            };
            setLoyaltyItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                var currentValue = targetCards.FirstOrDefault()?.GetCounter("loyalty") ?? 0;
                string? input = ShowInputDialog("Set Loyalty Counters", $"Enter the number of loyalty counters to set:", currentValue.ToString());
                if (input != null && int.TryParse(input, out int value))
                {
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.SetCounter("loyalty", value);
                    }
                    LogGameAction($"Set loyalty counters to {value} on {targetCards.Count} card(s)");
                }
            };
            countersMenu.Items.Add(setLoyaltyItem);
            
            countersMenu.Items.Add(new Separator());
            
            // Add other counter
            var addOtherItem = new MenuItem
            {
                Header = "Add Other",
                Foreground = System.Windows.Media.Brushes.Black
            };
            addOtherItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.AddCounter("other", 1);
                }
                LogGameAction($"Added other counter to {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(addOtherItem);
            
            // Remove other counter
            var removeOtherItem = new MenuItem
            {
                Header = "Remove Other",
                Foreground = System.Windows.Media.Brushes.Black
            };
            removeOtherItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                foreach (var targetCard in targetCards)
                {
                    targetCard.RemoveCounter("other", 1);
                }
                LogGameAction($"Removed other counter from {targetCards.Count} card(s)");
            };
            countersMenu.Items.Add(removeOtherItem);
            
            // Set other counter
            var setOtherItem = new MenuItem
            {
                Header = "Set Other",
                Foreground = System.Windows.Media.Brushes.Black
            };
            setOtherItem.Click += (s, args) =>
            {
                var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                var currentValue = targetCards.FirstOrDefault()?.GetCounter("other") ?? 0;
                string? input = ShowInputDialog("Set Other Counters", $"Enter the number of other counters to set:", currentValue.ToString());
                if (input != null && int.TryParse(input, out int value))
                {
                    foreach (var targetCard in targetCards)
                    {
                        targetCard.SetCounter("other", value);
                    }
                    LogGameAction($"Set other counters to {value} on {targetCards.Count} card(s)");
                }
            };
            countersMenu.Items.Add(setOtherItem);
            
            contextMenu.Items.Add(countersMenu);
            
            contextMenu.Items.Add(new Separator());

            // Attachment menu items
            if (card.AttachedTo == null)
            {
                // Attach to... option
                var attachToItem = new MenuItem
                {
                    Header = "Attach to",
                    Foreground = System.Windows.Media.Brushes.Black
                };
                attachToItem.Click += (s, args) =>
                {
                    var targetCards = _gameState.SelectedCards.Count > 0 ? _gameState.SelectedCards.ToList() : new List<Card> { card };
                    // Filter out cards that are already attached or are in the target list
                    var validTargets = targetCards.Where(c => c.AttachedTo == null).ToList();
                    if (validTargets.Count == 0)
                    {
                        StatusText.Text = "Cannot attach: Selected card(s) are already attached to another card.";
                        return;
                    }
                    
                    // Enter attach mode
                    _isAttachMode = true;
                    _cardsToAttach = validTargets;
                    // Store original status and update to attach mode message
                    _originalStatusText = StatusText.Text;
                    StatusText.Text = $"Attach Mode: Click on a card to attach {validTargets.Count} card(s) to it. Press Escape to cancel.";
                };
                contextMenu.Items.Add(attachToItem);
            }
            else
            {
                // Detach option
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
                    LogGameAction($"Detached {targetCards.Count} card(s)");
                };
                contextMenu.Items.Add(detachItem);
            }
            
            // Detach all option (if card has attached cards)
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
                    LogGameAction($"Detached all {attachedCount} card(s) from {card.Name}");
                };
                contextMenu.Items.Add(detachAllItem);
            }
            
            contextMenu.Items.Add(new Separator());

            // Clone card
            var cloneCardItem = new MenuItem
            {
                Header = "Clone Card",
                Foreground = System.Windows.Media.Brushes.Black
            };
            cloneCardItem.Click += (s, args) =>
            {
                var clonedCard = card.Clone();
                clonedCard.X = card.X + 20; // Offset slightly
                clonedCard.Y = card.Y + 20;
                clonedCard.IsTapped = false;
                _gameState.Battlefield.Add(clonedCard);
                _gameState.SetMostRecentlyMovedCard(clonedCard);
                LogGameAction($"Cloned {card.Name}");
            };
            contextMenu.Items.Add(cloneCardItem);

            // Add associated cards menu (back faces, tokens, etc.)
            if (_cardImageService != null)
            {
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
                                Tag = associatedCard // Store the associated card data
                            };

                            associatedItem.Click += async (s, args) =>
                            {
                                await AddAssociatedCardToBattlefield(associatedCard, card);
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

            contextMenu.IsOpen = true;
        }

        public async Task AddAssociatedCardToBattlefield(Services.AssociatedCard associatedCard, Card sourceCard)
        {
            if (_gameState == null || _cardImageService == null) return;

            try
            {
                // Create a new card for the associated card
                var newCard = new Card(associatedCard.Name)
                {
                    ScryfallId = associatedCard.ScryfallId,
                    X = sourceCard.X + 20, // Offset slightly from source card
                    Y = sourceCard.Y + 20,
                    IsTapped = false
                };

                // Download image for the associated card
                string? imagePath = await _cardImageService.DownloadCardImageAsync(newCard);
                if (imagePath != null)
                {
                    newCard.ImagePath = imagePath;
                }

                // Add to battlefield
                _gameState.Battlefield.Add(newCard);
                _gameState.SetMostRecentlyMovedCard(newCard);

                string cardType = associatedCard.Type switch
                {
                    "back_face" => "back face",
                    "token" => "token",
                    _ => "associated card"
                };

                LogGameAction($"Added {cardType} {associatedCard.Name} to battlefield");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding associated card: {ex.Message}");
            }
        }

        private void GameCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_gameState == null || e.ChangedButton != MouseButton.Left) return;
            
            var position = e.GetPosition(GameCanvas);
            var card = _gameState.GetCardAt(position.X, position.Y);
            
            _lastEventClickTime = DateTime.Now;

            if (GameCanvas.IsMouseCaptured && (!_isDragging && !_isSelecting || _isDragging || _isSelecting))
            {
                ClearDragSelectState();
            }
            else if (_isDragging || _isSelecting)
            {
                _isDragging = false;
                _isSelecting = false;
                _selectionBox = null;
                _draggedCard = null;
            }
            if (card == null && e.ClickCount == 1)
            {
                string? clickedZone = GetZoneAtPosition(position);
                if (clickedZone == "deck" && _gameState.Deck.Count > 0)
                {
                    var topCard = _gameState.Deck[0];
                    
                    double leftMargin = 20;
                    double topMargin = 20;
                    double zoneX = leftMargin;
                    double zoneWidth = Rendering.GameRenderer.ZoneWidth;
                    double zoneHeight = Rendering.GameRenderer.ZoneHeight;
                    double cardWidth = _gameState.CardWidth;
                    double cardHeight = _gameState.CardHeight;
                    
                    double cardX = zoneX + (zoneWidth - cardWidth) / 2;
                    double cardY = topMargin + (zoneHeight - cardHeight) / 2;
                    
                    if (position.X >= cardX && position.X <= cardX + cardWidth &&
                        position.Y >= cardY && position.Y <= cardY + cardHeight)
                    {
                        _draggedCard = topCard;
                        double cardCenterX = cardX + cardWidth / 2;
                        double cardCenterY = cardY + cardHeight / 2;
                        _dragOffset = new Point(position.X - cardCenterX, position.Y - cardCenterY);
                        _isDragging = true;
                        GameCanvas.CaptureMouse();
                        ShowDragPreview(null, this.PointToScreen(position), true);
                        return;
                    }
                }
            }
            
            bool isDoubleClick = false;
            if (card != null && _lastClickedCard == card && _lastClickTime != DateTime.MinValue)
            {
                var timeSinceLastClick = (DateTime.Now - _lastClickTime).TotalMilliseconds;
                if (timeSinceLastClick <= DoubleClickTimeMs)
                {
                    isDoubleClick = true;
                }
            }
            
            if (e.ClickCount == 2 || isDoubleClick)
            {
                string? clickedZone = GetZoneAtPosition(position);
                
                if (clickedZone == "graveyard" || clickedZone == "exile")
                {
                    if (clickedZone == "graveyard")
                    {
                        var viewer = new ZoneViewerWindow(_gameState, "Graveyard", _gameState.Graveyard, this);
                        viewer.ShowDialog();
                    }
                    else if (clickedZone == "exile")
                    {
                        var viewer = new ZoneViewerWindow(_gameState, "Exile", _gameState.Exile, this);
                        viewer.ShowDialog();
                    }
                    _lastClickedCard = null;
                    _lastClickTime = DateTime.MinValue;
                    return;
                }
                
                if (card != null)
                {
                    if (_isDragging || _isSelecting)
                    {
                        ClearDragSelectState();
                    }
                    
                    HandleDoubleClick(card);
                    _lastClickedCard = null;
                    _lastClickTime = DateTime.MinValue;
                    return;
                }
                
                if (_isDragging || _isSelecting)
                {
                    ClearDragSelectState();
                }
                _lastClickedCard = null;
                _lastClickTime = DateTime.MinValue;
                return;
            }
            if (_isAttachMode && e.ClickCount == 1)
            {
                if (card != null && _cardsToAttach.Count > 0)
                {
                    // Don't allow attaching to a card that's already attached or is in the list of cards to attach
                    if (!_cardsToAttach.Contains(card) && card.AttachedTo == null)
                    {
                        foreach (var cardToAttach in _cardsToAttach)
                        {
                            cardToAttach.AttachTo(card);
                        }
                        LogGameAction($"Attached {_cardsToAttach.Count} card(s) to {card.Name}");
                        // Restore original status
                        if (_originalStatusText != null)
                        {
                            StatusText.Text = _originalStatusText;
                            _originalStatusText = null;
                        }
                    }
                    else if (_cardsToAttach.Contains(card))
                    {
                        StatusText.Text = "Cannot attach a card to itself.";
                    }
                    else if (card.AttachedTo != null)
                    {
                        StatusText.Text = "Target card is already attached to another card.";
                    }
                }
                else if (card == null)
                {
                    // Clicked on empty space - cancel attach mode
                    // Restore original status
                    if (_originalStatusText != null)
                    {
                        StatusText.Text = _originalStatusText;
                        _originalStatusText = null;
                    }
                }
                else
                {
                    // No cards to attach - exit attach mode
                    // Restore original status
                    if (_originalStatusText != null)
                    {
                        StatusText.Text = _originalStatusText;
                        _originalStatusText = null;
                    }
                }
                
                // Exit attach mode after handling
                _isAttachMode = false;
                _cardsToAttach.Clear();
                return;
            }

            if (card != null)
            {
                card.OnClicked(_gameLogger);

                // If clicking on a card, check if it's already selected
                if (!_gameState.SelectedCards.Contains(card) && Keyboard.Modifiers != ModifierKeys.Control)
                {
                    // Clear selection if not holding Ctrl
                    _gameState.SelectedCards.Clear();
                }

                // Toggle selection with Ctrl, or select if not selected
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

                // Single click - start dragging
                _draggedCard = card;
                // Calculate offset from the card's center (since rotation is around center)
                double cardCenterX = card.X + _gameState.CardWidth / 2;
                double cardCenterY = card.Y + _gameState.CardHeight / 2;
                _dragOffset = new Point(position.X - cardCenterX, position.Y - cardCenterY);
                
                // Store initial positions of all selected cards
                _selectedCardsInitialPositions.Clear();
                foreach (var selectedCard in _gameState.SelectedCards)
                {
                    _selectedCardsInitialPositions[selectedCard] = new Point(selectedCard.X, selectedCard.Y);
                }
                
                _isDragging = true;
                GameCanvas.CaptureMouse();
                _lastClickPosition = position;
                _lastClickedCard = card;
                _lastClickTime = DateTime.Now;
            }
            else
            {
                // Clicked on empty space - start selection box
                if (Keyboard.Modifiers != ModifierKeys.Control)
                {
                    _gameState.SelectedCards.Clear();
                }
                _isSelecting = true;
                _selectionStart = position;
                _selectionBox = new Rect(position, position);
                GameCanvas.CaptureMouse();
                
                // Reset double-click tracking for empty space clicks
                _lastClickedCard = null;
                _lastClickTime = DateTime.MinValue;
            }
        }

        private void GameCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(GameCanvas);
            
            // Track hovered card for card info window
            if (!_isDragging && _gameState != null)
            {
                var hoveredCard = _gameState.GetCardAt(position.X, position.Y);
                
                // If not hovering over a card, check if we're hovering over the deck zone with reveal enabled
                if (hoveredCard == null && _gameState.AlwaysRevealTopOfLibrary)
                {
                    string? hoveredZone = GetZoneAtPosition(position);
                    if (hoveredZone == "deck")
                    {
                        var topDeckCard = _gameState.GetTopCard("deck");
                        if (topDeckCard != null)
                        {
                            hoveredCard = topDeckCard;
                        }
                    }
                }
                
                // Only update _lastHoveredCard when we actually have a card
                if (hoveredCard != null && hoveredCard != _lastHoveredCard)
                {
                    _lastHoveredCard = hoveredCard;
                    UpdateCardInfo(hoveredCard);
                }
                // If no card is hovered, keep showing the last hovered card
                else if (hoveredCard == null && _lastHoveredCard != null)
                {
                    // Don't update _lastHoveredCard, just ensure the card info shows it
                    UpdateCardInfo(_lastHoveredCard);
                }
            }
            
            // Update drag preview if dragging from hand (hand window will set this)
            if (_dragPreview != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var screenPosition = this.PointToScreen(position);
                // The preview position will be updated by HandViewerWindow, but we can also update it here
                // for smoother tracking when mouse is over main window
            }
            
            // Update drag preview for top card of library (show card back)
            if (_isDragging && _draggedCard != null && _gameState != null && 
                _gameState.Deck.Contains(_draggedCard) && _gameState.Deck.Count > 0 && _draggedCard == _gameState.Deck[0])
            {
                var screenPosition = this.PointToScreen(position);
                ShowDragPreview(null, screenPosition, true); // Show card back, don't reveal
            }

            if (_isDragging && _draggedCard != null && _gameState != null && _renderer != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // Calculate new center position for the dragged card
                double newCenterX = position.X - _dragOffset.X;
                double newCenterY = position.Y - _dragOffset.Y;
                
                // Calculate the offset from initial center position
                if (_selectedCardsInitialPositions.ContainsKey(_draggedCard))
                {
                    double initialCenterX = _selectedCardsInitialPositions[_draggedCard].X + _gameState.CardWidth / 2;
                    double initialCenterY = _selectedCardsInitialPositions[_draggedCard].Y + _gameState.CardHeight / 2;
                    double offsetX = newCenterX - initialCenterX;
                    double offsetY = newCenterY - initialCenterY;
                    
                    // Apply the same offset to all selected cards
                    foreach (var selectedCard in _gameState.SelectedCards)
                    {
                        if (_selectedCardsInitialPositions.ContainsKey(selectedCard))
                        {
                            double cardInitialCenterX = _selectedCardsInitialPositions[selectedCard].X + _gameState.CardWidth / 2;
                            double cardInitialCenterY = _selectedCardsInitialPositions[selectedCard].Y + _gameState.CardHeight / 2;
                            
                            double cardNewCenterX = cardInitialCenterX + offsetX;
                            double cardNewCenterY = cardInitialCenterY + offsetY;
                            
                            // Convert center back to top-left position
                            double cardNewX = cardNewCenterX - _gameState.CardWidth / 2;
                            double cardNewY = cardNewCenterY - _gameState.CardHeight / 2;
                            
                            // Calculate zone boundaries
                            double leftMargin = 20;
                            double zoneX = leftMargin;
                            double zoneWidth = Rendering.GameRenderer.ZoneWidth;
                            double separatorX = zoneX + zoneWidth + 20;
                            
                            // Calculate card bounds (accounting for rotation)
                            double cardWidth = selectedCard.IsTapped ? _gameState.CardHeight : _gameState.CardWidth;
                            double cardHeight = selectedCard.IsTapped ? _gameState.CardWidth : _gameState.CardHeight;
                            double cardLeft = cardNewCenterX - cardWidth / 2;
                            double cardRight = cardNewCenterX + cardWidth / 2;
                            double cardTop = cardNewCenterY - cardHeight / 2;
                            double cardBottom = cardNewCenterY + cardHeight / 2;
                            
                            // Check if card is in a zone
                            double topMargin = 20;
                            double zoneHeight = Rendering.GameRenderer.ZoneHeight;
                            double zoneSpacing = Rendering.GameRenderer.ZoneSpacing;
                            double deckY = topMargin;
                            double graveyardY = deckY + zoneHeight + zoneSpacing;
                            double exileY = graveyardY + zoneHeight + zoneSpacing;
                            
                            bool inZone = false;
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
                                // If any part of the card is in the left area (before separator), move it to battlefield
                                if (cardLeft < separatorX)
                                {
                                    cardNewCenterX = separatorX + cardWidth / 2 + 5; // Move to right of separator with small gap
                                }
                                // If card overlaps the separator line, move it fully to the right
                                else if (cardLeft < separatorX + 5) // Small buffer to prevent overlap
                                {
                                    cardNewCenterX = separatorX + cardWidth / 2 + 5;
                                }
                            }
                            
                            // Calculate bounds - must be in battlefield area (right of separator) or in a zone
                            // If not in a zone, ensure card is fully to the right of separator
                            if (!inZone)
                            {
                                double minX = separatorX + cardWidth / 2 + 5; // Right of separator with gap
                                double maxX = GameCanvas.ActualWidth - cardWidth / 2;
                                cardNewCenterX = Math.Max(minX, Math.Min(cardNewCenterX, maxX));
                            }
                            else
                            {
                                // If in zone, allow movement within zone bounds
                                double minX = zoneX + cardWidth / 2;
                                double maxX = zoneX + zoneWidth - cardWidth / 2;
                                cardNewCenterX = Math.Max(minX, Math.Min(cardNewCenterX, maxX));
                            }
                            
                            // Vertical bounds
                            double minY = cardHeight / 2;
                            double maxY = GameCanvas.ActualHeight - cardHeight / 2;
                            cardNewCenterY = Math.Max(minY, Math.Min(cardNewCenterY, maxY));
                            
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
                            
                            // Track most recently moved card (for z-order)
                            _gameState.SetMostRecentlyMovedCard(selectedCard);
                        }
                    }
                }
            }
            else if (_isSelecting)
            {
                // Update selection box (check mouse button state, but also allow if mouse is captured)
                if (e.LeftButton == MouseButtonState.Pressed || GameCanvas.IsMouseCaptured)
                {
                    // Update selection box
                    _selectionBox = new Rect(
                        Math.Min(_selectionStart.X, position.X),
                        Math.Min(_selectionStart.Y, position.Y),
                        Math.Abs(position.X - _selectionStart.X),
                        Math.Abs(position.Y - _selectionStart.Y));

                    // Update selected cards based on selection box
                    if (_gameState != null)
                    {
                        var rect = _selectionBox.Value;
                        var cardsInBox = _gameState.GetCardsInRect(
                            rect.X, rect.Y,
                            rect.X + rect.Width, rect.Y + rect.Height);

                        if (Keyboard.Modifiers == ModifierKeys.Control)
                        {
                            // Add to selection
                            foreach (var card in cardsInBox)
                            {
                                _gameState.SelectedCards.Add(card);
                            }
                        }
                        else
                        {
                            // Replace selection
                            _gameState.SelectedCards.Clear();
                            foreach (var card in cardsInBox)
                            {
                                _gameState.SelectedCards.Add(card);
                            }
                        }
                    }
                }
            }
        }

        private void GameCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Only handle left button release
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }
            
            if (_isDragging && _gameState != null)
            {
                var position = e.GetPosition(GameCanvas);
                var screenPosition = this.PointToScreen(position);
                
                // Handle dragging top card of library
                if (_draggedCard != null && _gameState.Deck.Contains(_draggedCard) && _gameState.Deck.Count > 0 && _draggedCard == _gameState.Deck[0])
                {
                    // Check if card was dropped over the hand window
                    bool topCardDroppedOnHand = false;
                    if (_handWindow != null && _handWindow.IsVisible)
                    {
                        var handWindowRect = new Rect(
                            _handWindow.Left,
                            _handWindow.Top,
                            _handWindow.Width,
                            _handWindow.Height);
                        
                        if (handWindowRect.Contains(screenPosition))
                        {
                            topCardDroppedOnHand = true;
                        }
                    }
                    
                    if (topCardDroppedOnHand)
                    {
                        // Move top card to hand
                        _gameState.Deck.RemoveAt(0);
                        _gameState.Hand.Insert(0, _draggedCard);
                        LogGameAction($"Drew {_draggedCard.Name}");
                        _handWindow?.UpdateHand();
                    }
                    else
                    {
                        // Check if dropped in a zone
                        string? targetZone = GetZoneAtPosition(position);
                        
                        if (targetZone != null && targetZone != "deck")
                        {
                            // Move to zone
                            _gameState.Deck.RemoveAt(0);
                            _gameState.MoveCardToZone(_draggedCard, targetZone);
                            LogGameAction($"Moved {_draggedCard.Name} to {targetZone}");
                        }
                        else
                        {
                            // Move to battlefield
                            double separatorX = 20 + Rendering.GameRenderer.ZoneWidth + 20;
                            if (position.X < separatorX + 50)
                            {
                                position.X = separatorX + 50;
                            }
                            
                            _gameState.Deck.RemoveAt(0);
                            _draggedCard.X = position.X - _gameState.CardWidth / 2;
                            _draggedCard.Y = position.Y - _gameState.CardHeight / 2;
                            _draggedCard.IsTapped = false;
                            _gameState.Battlefield.Add(_draggedCard);
                            _gameState.SetMostRecentlyMovedCard(_draggedCard);
                            LogGameAction($"Played {_draggedCard.Name}");
                        }
                    }
                    
                    HideDragPreview();
                    _isDragging = false;
                    _draggedCard = null;
                    GameCanvas.ReleaseMouseCapture();
                    return;
                }
                
                // Check if card was dropped over the hand window
                bool droppedOnHand = false;
                if (_handWindow != null && _handWindow.IsVisible)
                {
                    var handWindowRect = new Rect(
                        _handWindow.Left,
                        _handWindow.Top,
                        _handWindow.Width,
                        _handWindow.Height);
                    
                    if (handWindowRect.Contains(screenPosition))
                    {
                        droppedOnHand = true;
                    }
                }
                
                if (droppedOnHand)
                {
                    // Move selected cards to hand
                    var movedCards = new List<Card>();
                    foreach (var card in _gameState.SelectedCards.ToList())
                    {
                        if (_gameState.Battlefield.Contains(card))
                        {
                            // Handle token persistence - tokens that don't persist are removed from game
                            if (card.IsToken && !card.PersistsInGraveyard)
                            {
                                card.DetachAll();
                                card.Detach();
                                _gameState.Battlefield.Remove(card);
                                _gameState.SelectedCards.Remove(card);
                                if (_gameState.MostRecentlyMovedCard == card)
                                {
                                    _gameState.SetMostRecentlyMovedCard(null);
                                }
                                // Token is removed from game, not moved to hand
                                continue;
                            }

                            // Detach all cards attached to this card
                            card.DetachAll();
                            // Detach this card from its parent if attached
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
                        LogGameAction(movedCards.Count == 1 
                            ? $"Moved {movedCards[0].Name} to hand" 
                            : $"Moved {movedCards.Count} card(s) to hand");
                    }
                    _handWindow?.UpdateHand();
                }
                else
                {
                // Check if card was dropped in a zone
                double leftMargin = 20;
                double topMargin = 20;
                double zoneX = leftMargin;
                double zoneWidth = Rendering.GameRenderer.ZoneWidth;
                double zoneHeight = Rendering.GameRenderer.ZoneHeight;
                double zoneSpacing = Rendering.GameRenderer.ZoneSpacing;
                
                double deckY = topMargin;
                double graveyardY = deckY + zoneHeight + zoneSpacing;
                double exileY = graveyardY + zoneHeight + zoneSpacing;
                
                // Check which zone the card center is in
                foreach (var card in _gameState.SelectedCards.ToList())
                {
                    double cardCenterX = card.X + _gameState.CardWidth / 2;
                    double cardCenterY = card.Y + _gameState.CardHeight / 2;
                    
                    if (cardCenterX >= zoneX && cardCenterX <= zoneX + zoneWidth)
                    {
                        if (cardCenterY >= deckY && cardCenterY <= deckY + zoneHeight)
                        {
                            // Dropped in deck (always goes on top)
                            _gameState.MoveCardToZone(card, "deck");
                            LogGameAction($"Put {card.Name} on top of library");
                        }
                        else if (cardCenterY >= graveyardY && cardCenterY <= graveyardY + zoneHeight)
                        {
                            // Dropped in graveyard
                            _gameState.MoveCardToZone(card, "graveyard");
                            LogGameAction($"Moved {card.Name} to graveyard");
                        }
                        else if (cardCenterY >= exileY && cardCenterY <= exileY + zoneHeight)
                        {
                            // Dropped in exile
                            _gameState.MoveCardToZone(card, "exile");
                            LogGameAction($"Moved {card.Name} to exile");
                        }
                        }
                    }
                }
                
                _isDragging = false;
                _draggedCard = null;
                _selectedCardsInitialPositions.Clear();
                GameCanvas.ReleaseMouseCapture();
            }
            else if (_isSelecting)
            {
                _isSelecting = false;
                _selectionBox = null;
                GameCanvas.ReleaseMouseCapture();
            }
            else if (GameCanvas.IsMouseCaptured)
            {
                // Safety: release capture if it's still active but we're not dragging or selecting
                GameCanvas.ReleaseMouseCapture();
                _isDragging = false;
                _isSelecting = false;
                _selectionBox = null;
                _draggedCard = null;
            }
            
            // Final safety check: ensure state is consistent
            if (!GameCanvas.IsMouseCaptured && (_isDragging || _isSelecting))
            {
                _isDragging = false;
                _isSelecting = false;
                _selectionBox = null;
                _draggedCard = null;
            }
        }

        private void ToggleHand_Click(object sender, RoutedEventArgs e)
        {
            ToggleHand();
        }

        private void ToggleHand()
        {
            if (_handWindow == null) return;
            
            if (_handWindow.IsVisible)
            {
                _handWindow.Hide();
                if (ToggleHandMenuItem != null)
                {
                    ToggleHandMenuItem.Header = "Show Hand";
                }
            }
            else
            {
                _handWindow.Show();
                _handWindow.UpdateHand();
                if (ToggleHandMenuItem != null)
                {
                    ToggleHandMenuItem.Header = "Hide Hand";
                }
            }
        }

        private void ToggleCardInfo_Click(object sender, RoutedEventArgs e)
        {
            ToggleCardInfo();
        }

        private void ToggleCardInfo()
        {
            if (_cardInfoWindow == null) return;
            
            if (_cardInfoWindow.IsVisible)
            {
                _cardInfoWindow.Hide();
                if (ToggleCardInfoMenuItem != null)
                {
                    ToggleCardInfoMenuItem.Header = "Show Card Info";
                }
            }
            else
            {
                _cardInfoWindow.Show();
                // Update with last hovered card if available, or top of library if revealed
                Card? cardToShow = _lastHoveredCard;
                if (cardToShow == null && _gameState != null && _gameState.AlwaysRevealTopOfLibrary)
                {
                    cardToShow = _gameState.GetTopCard("deck");
                }
                if (cardToShow != null)
                {
                    UpdateCardInfo(cardToShow);
                }
                if (ToggleCardInfoMenuItem != null)
                {
                    ToggleCardInfoMenuItem.Header = "Hide Card Info";
                }
            }
        }

        public void UpdateCardInfoOnHover(Card? card)
        {
            // Only update _lastHoveredCard when we actually have a card
            if (card != null && card != _lastHoveredCard)
            {
                _lastHoveredCard = card;
                UpdateCardInfo(card);
            }
            // If no card is hovered, keep showing the last hovered card
            else if (card == null && _lastHoveredCard != null)
            {
                // Don't update _lastHoveredCard, just ensure the card info shows it
                UpdateCardInfo(_lastHoveredCard);
            }
        }

        private async void UpdateCardInfo(Card? card)
        {
            if (_cardInfoWindow == null || !_cardInfoWindow.IsVisible) return;
            
            // If card is null, always show the last hovered card
            if (card == null)
            {
                // If we have a last hovered card, keep showing it
                if (_lastHoveredCard != null)
                {
                    card = _lastHoveredCard;
                }
                // Otherwise, check if we should show the top of library
                else if (_gameState != null && _gameState.AlwaysRevealTopOfLibrary)
                {
                    var topCard = _gameState.GetTopCard("deck");
                    if (topCard != null)
                    {
                        card = topCard;
                    }
                }
                
                // Only clear if we truly have nothing to show
                if (card == null)
                {
                    _cardInfoWindow.UpdateCard(null);
                    return;
                }
            }
            
            // At this point, card should never be null due to early return above
            if (card == null) return;

            // Update _lastHoveredCard when we have a valid card
            _lastHoveredCard = card;

            // Update immediately with available data
            _cardInfoWindow.UpdateCard(card);

            // Fetch full card data from Scryfall if we have the service and need oracle text
            if (_cardImageService != null && (string.IsNullOrEmpty(card.Text) || card.Text == card.Name))
            {
                try
                {
                    var cardData = await _cardImageService.GetCardDataAsync(card);
                    if (cardData != null)
                    {
                        // Update card with oracle text
                        card.Text = cardData.OracleText;
                        if (string.IsNullOrEmpty(card.ManaCost) && !string.IsNullOrEmpty(cardData.ManaCost))
                        {
                            card.ManaCost = cardData.ManaCost;
                        }
                        if (string.IsNullOrEmpty(card.Type) && !string.IsNullOrEmpty(cardData.Type))
                        {
                            card.Type = cardData.Type;
                        }
                        
                        // Update the window with new data
                        _cardInfoWindow.UpdateCard(card);
                    }
                }
                catch
                {
                    // If fetching fails, just use what we have
                }
            }
        }

        private string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            var inputWindow = new Window
            {
                Title = title,
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var stackPanel = new StackPanel
            {
                Margin = new Thickness(15)
            };

            var promptText = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(promptText);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 15)
            };
            textBox.SelectAll();
            textBox.Focus();
            stackPanel.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { inputWindow.DialogResult = true; inputWindow.Close(); };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { inputWindow.DialogResult = false; inputWindow.Close(); };
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonPanel);
            inputWindow.Content = stackPanel;

            // Handle Enter key
            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    inputWindow.DialogResult = true;
                    inputWindow.Close();
                }
            };

            if (inputWindow.ShowDialog() == true)
            {
                return textBox.Text;
            }
            return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Force close hand window if it exists
            if (_handWindow != null)
            {
                _handWindow.IsForceClosing = true;
                _handWindow.Close();
            }
            
            // Force close card info window if it exists
            if (_cardInfoWindow != null)
            {
                _cardInfoWindow.IsForceClosing = true;
                _cardInfoWindow.Close();
            }
            
            _cardImageService?.Dispose();
            _bulkDataService?.Dispose();
            
            // Explicitly shutdown application
            Application.Current.Shutdown();
            
            base.OnClosed(e);
        }
    }
}

