using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MTGSimulator.Game;
using MTGSimulator.Services;

namespace MTGSimulator.Views
{
    public partial class CardInfoWindow : Window
    {
        private GameLogger? _gameLogger;
        private bool _isScrolledToBottom = true;

        public CardInfoWindow()
        {
            InitializeComponent();
            Closing += CardInfoWindow_Closing;
        }
        
        public bool IsForceClosing { get; set; } = false;

        public void SetGameLogger(GameLogger logger)
        {
            if (_gameLogger != null)
            {
                _gameLogger.LogAdded -= GameLogger_LogAdded;
            }
            
            _gameLogger = logger;
            _gameLogger.LogAdded += GameLogger_LogAdded;
            
            // Update with existing logs
            UpdateLogDisplay();
        }

        private void GameLogger_LogAdded(GameLogger.LogEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateLogDisplay();
            });
        }

        private void UpdateLogDisplay()
        {
            if (_gameLogger != null)
            {
                LogTextBox.Text = _gameLogger.GetLogText(100);
                // Auto-scroll to bottom only if user was already at the bottom
                if (_isScrolledToBottom)
                {
                    // Use Dispatcher to ensure UI is updated before scrolling
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogScrollViewer.ScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        private void LogScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            // Check if scroll viewer is at the bottom (within 5 pixels to account for rounding)
            var scrollViewer = sender as System.Windows.Controls.ScrollViewer;
            if (scrollViewer != null)
            {
                _isScrolledToBottom = scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 5;
            }
        }

        private void CardInfoWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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

        public void UpdateCard(Card? card)
        {
            if (card == null)
            {
                CardNameText.Text = "No card selected";
                CardImage.Source = null;
                ManaCostText.Text = "";
                TypeText.Text = "";
                OracleText.Text = "Hover over a card to see its details.";
                return;
            }

            CardNameText.Text = card.Name;
            ManaCostText.Text = !string.IsNullOrEmpty(card.ManaCost) ? $"Mana Cost: {card.ManaCost}" : "";
            TypeText.Text = !string.IsNullOrEmpty(card.Type) ? card.Type : "";
            
            // Format oracle text with line breaks for better readability
            string oracleText = !string.IsNullOrEmpty(card.Text) ? card.Text : "No oracle text available.";
            // Replace newlines and format
            oracleText = oracleText.Replace("\\n", "\n");
            
            // Add exiled cards information if any
            if (card.ExiledCards.Count > 0)
            {
                oracleText += $"\n\n--- Exiled under this card ({card.ExiledCards.Count}) ---\n";
                foreach (var exiledCard in card.ExiledCards)
                {
                    oracleText += $"{exiledCard.Name}\n";
                }
            }
            
            OracleText.Text = oracleText;

            // Load card image
            if (!string.IsNullOrEmpty(card.ImagePath) && File.Exists(card.ImagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(card.ImagePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    CardImage.Source = bitmap;
                }
                catch
                {
                    CardImage.Source = null;
                }
            }
            else
            {
                CardImage.Source = null;
            }
        }
    }
}

