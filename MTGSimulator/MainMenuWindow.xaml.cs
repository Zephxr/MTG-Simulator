using System;
using System.Windows;
using MTGSimulator.Settings;
using MTGSimulator.Services;
using MTGSimulator.Views;

namespace MTGSimulator
{
    public partial class MainMenuWindow : Window
    {
        private AppSettings _settings;
        private BulkDataService? _bulkDataService;

        public MainMenuWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
        }

        private async void SinglePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            SinglePlayerButton.IsEnabled = false;
            LoadingPanel.Visibility = Visibility.Visible;

            try
            {
                _bulkDataService = new BulkDataService(_settings.BulkDataDirectory);
                if (!string.IsNullOrEmpty(_settings.BulkDataDirectory))
                {
                    _bulkDataService.SetBulkDataDirectory(_settings.BulkDataDirectory);
                }

                await _bulkDataService.CheckAndLoadBulkData();

                var mainWindow = new MainWindow(_bulkDataService);
                mainWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading game: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                SinglePlayerButton.IsEnabled = true;
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void DeckViewerButton_Click(object sender, RoutedEventArgs e)
        {
            var deckViewer = new Views.DeckViewerWindow();
            deckViewer.ShowDialog();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() == true)
            {
                _settings = settingsWindow.Settings;
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}

