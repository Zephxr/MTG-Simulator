using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using MTGSimulator.Services;

namespace MTGSimulator.Settings
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }
        private AppSettings _originalSettings;
        private BulkDataService? _bulkDataService;

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            Settings = new AppSettings
            {
                StartMaximized = currentSettings.StartMaximized,
                CardWidth = currentSettings.CardWidth,
                CardHeight = currentSettings.CardHeight,
                DrawCardKey = currentSettings.DrawCardKey,
                TapCardKey = currentSettings.TapCardKey,
                ShowHandKey = currentSettings.ShowHandKey,
                ShowCardInfoKey = currentSettings.ShowCardInfoKey,
                ShuffleKey = currentSettings.ShuffleKey,
                AddPlusOnePlusOneKey = currentSettings.AddPlusOnePlusOneKey,
                RemovePlusOnePlusOneKey = currentSettings.RemovePlusOnePlusOneKey,
                AddOtherCounterKey = currentSettings.AddOtherCounterKey,
                RemoveOtherCounterKey = currentSettings.RemoveOtherCounterKey,
                AddLoyaltyKey = currentSettings.AddLoyaltyKey,
                RemoveLoyaltyKey = currentSettings.RemoveLoyaltyKey,
                ShowFpsCounter = currentSettings.ShowFpsCounter,
                ShowSelectionCount = currentSettings.ShowSelectionCount,
                MaxCachedImages = currentSettings.MaxCachedImages,
                BulkDataDirectory = currentSettings.BulkDataDirectory
            };
            _originalSettings = currentSettings;

            // Initialize bulk data service
            _bulkDataService = new BulkDataService(Settings.BulkDataDirectory);
            if (!string.IsNullOrEmpty(Settings.BulkDataDirectory))
            {
                _bulkDataService.SetBulkDataDirectory(Settings.BulkDataDirectory);
            }

            LoadSettings();
            SetupEventHandlers();
            LoadBulkDataStatus();
        }

        private void LoadSettings()
        {
            StartMaximizedCheckBox.IsChecked = Settings.StartMaximized;
            ShowFpsCounterCheckBox.IsChecked = Settings.ShowFpsCounter;
            ShowSelectionCountCheckBox.IsChecked = Settings.ShowSelectionCount;

            CardWidthSlider.Value = Settings.CardWidth;
            CardHeightSlider.Value = Settings.CardHeight;
            CardWidthValue.Text = Settings.CardWidth.ToString("0");
            CardHeightValue.Text = Settings.CardHeight.ToString("0");

            DrawCardKeyTextBox.Text = Settings.DrawCardKey;
            TapCardKeyTextBox.Text = Settings.TapCardKey;
            ShowHandKeyTextBox.Text = Settings.ShowHandKey;
            ShowCardInfoKeyTextBox.Text = Settings.ShowCardInfoKey;
            ShuffleKeyTextBox.Text = Settings.ShuffleKey;
            AddPlusOnePlusOneKeyTextBox.Text = Settings.AddPlusOnePlusOneKey;
            RemovePlusOnePlusOneKeyTextBox.Text = Settings.RemovePlusOnePlusOneKey;
            AddOtherCounterKeyTextBox.Text = Settings.AddOtherCounterKey;
            RemoveOtherCounterKeyTextBox.Text = Settings.RemoveOtherCounterKey;
            AddLoyaltyKeyTextBox.Text = Settings.AddLoyaltyKey;
            RemoveLoyaltyKeyTextBox.Text = Settings.RemoveLoyaltyKey;

            MaxCachedImagesSlider.Value = Settings.MaxCachedImages;
            MaxCachedImagesValue.Text = Settings.MaxCachedImages.ToString();

            BulkDataDirectoryTextBox.Text = Settings.BulkDataDirectory ?? "Default location";
        }

        private async void LoadBulkDataStatus()
        {
            if (_bulkDataService == null) return;

            bool hasData = await _bulkDataService.CheckAndLoadBulkData();
            var lastUpdate = _bulkDataService.GetLastUpdateTime();
            
            Dispatcher.Invoke(() =>
            {
                if (hasData && _bulkDataService.HasBulkData)
                {
                    BulkDataStatusText.Text = $"Status: Loaded ({_bulkDataService.CardCount:N0} cards)";
                    UpdateBulkDataButton.IsEnabled = true;
                    if (lastUpdate.HasValue)
                    {
                        BulkDataInfoText.Text = $"Last updated: {lastUpdate.Value:yyyy-MM-dd HH:mm:ss}";
                    }
                    else
                    {
                        BulkDataInfoText.Text = "Card database is loaded and ready to use.";
                    }
                }
                else
                {
                    BulkDataStatusText.Text = "Status: Not downloaded";
                    UpdateBulkDataButton.IsEnabled = true;
                    BulkDataInfoText.Text = "Card database not found. Click 'Update to Latest' to download.";
                }
            });
        }

        private void BrowseBulkDataDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select directory for bulk data storage";
                if (!string.IsNullOrEmpty(Settings.BulkDataDirectory) && Directory.Exists(Settings.BulkDataDirectory))
                {
                    dialog.SelectedPath = Settings.BulkDataDirectory;
                }
                
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    Settings.BulkDataDirectory = dialog.SelectedPath;
                    BulkDataDirectoryTextBox.Text = Settings.BulkDataDirectory;
                    if (_bulkDataService != null)
                    {
                        _bulkDataService.SetBulkDataDirectory(Settings.BulkDataDirectory);
                        LoadBulkDataStatus();
                    }
                }
            }
        }

        private async void UpdateBulkDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_bulkDataService == null) return;

            UpdateBulkDataButton.IsEnabled = false;
            BulkDataStatusText.Text = "Status: Downloading...";
            BulkDataInfoText.Text = "";

            try
            {
                var progressWindow = new Window
                {
                    Title = "Downloading Bulk Data",
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var progressGrid = new System.Windows.Controls.Grid();
                progressGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                progressGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                var progressBar = new System.Windows.Controls.ProgressBar
                {
                    Height = 30,
                    Margin = new System.Windows.Thickness(20)
                };
                System.Windows.Controls.Grid.SetRow(progressBar, 0);
                progressGrid.Children.Add(progressBar);

                var statusLabel = new System.Windows.Controls.TextBlock
                {
                    Text = "Starting download...",
                    Margin = new System.Windows.Thickness(20, 0, 20, 20),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                System.Windows.Controls.Grid.SetRow(statusLabel, 1);
                progressGrid.Children.Add(statusLabel);

                progressWindow.Content = progressGrid;
                progressWindow.Show();

                var downloadTask = _bulkDataService.DownloadBulkDataAsync(
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

                bool success = await downloadTask;
                progressWindow.Close();

                if (success)
                {
                    BulkDataStatusText.Text = $"Status: Downloaded and loaded";
                    var lastUpdate = _bulkDataService.GetLastUpdateTime();
                    if (lastUpdate.HasValue)
                    {
                        BulkDataInfoText.Text = $"Last updated: {lastUpdate.Value:yyyy-MM-dd HH:mm:ss}";
                    }
                    System.Windows.MessageBox.Show("Card database downloaded successfully!", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    BulkDataStatusText.Text = "Status: Download failed";
                    BulkDataInfoText.Text = "Failed to download card database. Please check your internet connection and try again.";
                    System.Windows.MessageBox.Show("Failed to download card database. Please check your internet connection.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                BulkDataStatusText.Text = "Status: Error";
                BulkDataInfoText.Text = $"Error: {ex.Message}";
                System.Windows.MessageBox.Show($"Error downloading card database: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateBulkDataButton.IsEnabled = true;
            }
        }

        private void SetupEventHandlers()
        {
            CardWidthSlider.ValueChanged += (s, e) =>
            {
                Settings.CardWidth = CardWidthSlider.Value;
                CardWidthValue.Text = Settings.CardWidth.ToString("0");
            };

            CardHeightSlider.ValueChanged += (s, e) =>
            {
                Settings.CardHeight = CardHeightSlider.Value;
                CardHeightValue.Text = Settings.CardHeight.ToString("0");
            };

            MaxCachedImagesSlider.ValueChanged += (s, e) =>
            {
                Settings.MaxCachedImages = (int)MaxCachedImagesSlider.Value;
                MaxCachedImagesValue.Text = Settings.MaxCachedImages.ToString();
            };
        }

        private void KeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox == null) return;

            // Don't capture modifier keys alone
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            // Build modifier string
            var modifiers = new System.Collections.Generic.List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                modifiers.Add("Ctrl");
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                modifiers.Add("Alt");
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                modifiers.Add("Shift");
            }

            // Get the key name
            string keyName = e.Key.ToString();
            
            // Handle special cases
            if (e.Key == Key.Space)
            {
                keyName = "Space";
            }
            else if (e.Key == Key.OemPlus)
            {
                keyName = "Plus";
            }
            else if (e.Key == Key.OemMinus)
            {
                keyName = "Minus";
            }
            else if (keyName.StartsWith("D") && keyName.Length == 2 && char.IsDigit(keyName[1]))
            {
                // Keep digit keys as-is (D0, D1, etc.)
            }
            else if (keyName.Length > 1 && !keyName.StartsWith("Left") && !keyName.StartsWith("Right"))
            {
                // Skip modifier keys and arrow keys, but allow function keys
                if (!keyName.StartsWith("F") || keyName.Length > 2)
                {
                    // Allow arrow keys and other special keys
                    if (keyName != "Up" && keyName != "Down" && keyName != "Left" && keyName != "Right" &&
                        keyName != "Enter" && keyName != "Tab" && keyName != "Escape" &&
                        keyName != "Back" && keyName != "Delete" && keyName != "Insert" &&
                        keyName != "Home" && keyName != "End" && keyName != "PageUp" && keyName != "PageDown")
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Build the full keybind string
            string keybind = modifiers.Count > 0 
                ? string.Join("+", modifiers) + "+" + keyName 
                : keyName;

            textBox.Text = keybind;
            e.Handled = true;

            // Update settings based on which textbox
            if (textBox == DrawCardKeyTextBox)
            {
                Settings.DrawCardKey = keybind;
            }
            else if (textBox == TapCardKeyTextBox)
            {
                Settings.TapCardKey = keybind;
            }
            else if (textBox == ShowHandKeyTextBox)
            {
                Settings.ShowHandKey = keybind;
            }
            else if (textBox == ShowCardInfoKeyTextBox)
            {
                Settings.ShowCardInfoKey = keybind;
            }
            else if (textBox == ShuffleKeyTextBox)
            {
                Settings.ShuffleKey = keybind;
            }
            else if (textBox == AddPlusOnePlusOneKeyTextBox)
            {
                Settings.AddPlusOnePlusOneKey = keybind;
            }
            else if (textBox == RemovePlusOnePlusOneKeyTextBox)
            {
                Settings.RemovePlusOnePlusOneKey = keybind;
            }
            else if (textBox == AddOtherCounterKeyTextBox)
            {
                Settings.AddOtherCounterKey = keybind;
            }
            else if (textBox == RemoveOtherCounterKeyTextBox)
            {
                Settings.RemoveOtherCounterKey = keybind;
            }
            else if (textBox == AddLoyaltyKeyTextBox)
            {
                Settings.AddLoyaltyKey = keybind;
            }
            else if (textBox == RemoveLoyaltyKeyTextBox)
            {
                Settings.RemoveLoyaltyKey = keybind;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Update settings from UI
            Settings.StartMaximized = StartMaximizedCheckBox.IsChecked ?? true;
            Settings.ShowFpsCounter = ShowFpsCounterCheckBox.IsChecked ?? true;
            Settings.ShowSelectionCount = ShowSelectionCountCheckBox.IsChecked ?? true;

            Settings.CardWidth = CardWidthSlider.Value;
            Settings.CardHeight = CardHeightSlider.Value;

            Settings.DrawCardKey = DrawCardKeyTextBox.Text;
            Settings.TapCardKey = TapCardKeyTextBox.Text;
            Settings.ShowHandKey = ShowHandKeyTextBox.Text;
            Settings.ShowCardInfoKey = ShowCardInfoKeyTextBox.Text;
            Settings.ShuffleKey = ShuffleKeyTextBox.Text;
            Settings.AddPlusOnePlusOneKey = AddPlusOnePlusOneKeyTextBox.Text;
            Settings.RemovePlusOnePlusOneKey = RemovePlusOnePlusOneKeyTextBox.Text;
            Settings.AddOtherCounterKey = AddOtherCounterKeyTextBox.Text;
            Settings.RemoveOtherCounterKey = RemoveOtherCounterKeyTextBox.Text;
            Settings.AddLoyaltyKey = AddLoyaltyKeyTextBox.Text;
            Settings.RemoveLoyaltyKey = RemoveLoyaltyKeyTextBox.Text;

            Settings.MaxCachedImages = (int)MaxCachedImagesSlider.Value;
            Settings.BulkDataDirectory = BulkDataDirectoryTextBox.Text == "Default location" ? null : BulkDataDirectoryTextBox.Text;

            Settings.Save();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Restore original settings
            Settings = _originalSettings;
            DialogResult = false;
            Close();
        }
    }
}

