using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MTGSimulator.Services;

namespace MTGSimulator.Views
{
    public partial class TokenCreationWindow : Window
    {
        private readonly CardImageService _cardImageService;
        private readonly BulkDataService? _bulkDataService;
        private readonly System.Threading.CancellationTokenSource _searchCancellationTokenSource;
        private System.Threading.CancellationTokenSource? _currentSearchCancellation;
        private List<CardSearchResult> _searchResults = new List<CardSearchResult>();
        private CardSearchResult? _selectedResult;

        public CardSearchResult? SelectedCard { get; private set; }
        public bool PersistsInGraveyard { get; private set; }

        public TokenCreationWindow(CardImageService cardImageService, BulkDataService? bulkDataService = null)
        {
            InitializeComponent();
            _cardImageService = cardImageService;
            _bulkDataService = bulkDataService;
            _searchCancellationTokenSource = new System.Threading.CancellationTokenSource();
            SearchTextBox.Focus();
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = SearchTextBox.Text.Trim();
            
            // Cancel previous search
            _currentSearchCancellation?.Cancel();
            _currentSearchCancellation?.Dispose();
            _currentSearchCancellation = new System.Threading.CancellationTokenSource();
            var cancellationToken = _currentSearchCancellation.Token;
            
            if (string.IsNullOrEmpty(searchText))
            {
                ResultsListBox.ItemsSource = null;
                ClearSelection();
                return;
            }

            try
            {
                // Debounce search - wait 500ms after user stops typing
                await Task.Delay(500, cancellationToken);
                
                if (cancellationToken.IsCancellationRequested)
                    return;

                // Use bulk data service if available, otherwise fall back to CardImageService
                List<CardSearchResult> results;
                if (_bulkDataService != null && _bulkDataService.HasBulkData)
                {
                    results = await _bulkDataService.SearchCardsAsync(searchText, 30);
                }
                else
                {
                    results = await _cardImageService.SearchCardsAsync(searchText, 30);
                }
                _searchResults = results;
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Double-check cancellation before updating UI
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            ResultsListBox.ItemsSource = results;
                            if (results.Count > 0)
                            {
                                ResultsListBox.SelectedIndex = 0;
                            }
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Search was cancelled, ignore silently
                // This also catches TaskCanceledException since it inherits from OperationCanceledException
            }
            catch (Exception ex)
            {
                // Only show error if it's not a cancellation
                if (!(ex is OperationCanceledException))
                {
                    System.Diagnostics.Debug.WriteLine($"Error searching cards: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error searching cards: {ex.Message}", "Search Error", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            }
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ResultsListBox.Items.Count > 0)
            {
                ResultsListBox.Focus();
                if (ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
                {
                    ResultsListBox.SelectedIndex++;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up && ResultsListBox.Items.Count > 0)
            {
                ResultsListBox.Focus();
                if (ResultsListBox.SelectedIndex > 0)
                {
                    ResultsListBox.SelectedIndex--;
                }
                e.Handled = true;
            }
        }

        private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsListBox.SelectedItem is CardSearchResult result)
            {
                _selectedResult = result;
                UpdateSelectedCardInfo(result);
            }
            else
            {
                ClearSelection();
            }
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedResult != null)
            {
                CreateToken();
            }
        }

        private void UpdateSelectedCardInfo(CardSearchResult result)
        {
            SelectedCardName.Text = result.Name;
            SelectedCardType.Text = $"{result.ManaCost} - {result.Type}";
            SelectedCardText.Text = result.OracleText;
        }

        private void ClearSelection()
        {
            SelectedCardName.Text = "";
            SelectedCardType.Text = "";
            SelectedCardText.Text = "";
            _selectedResult = null;
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            CreateToken();
        }

        private void CreateToken()
        {
            if (_selectedResult == null)
            {
                MessageBox.Show("Please select a card to create a token.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedCard = _selectedResult;
            PersistsInGraveyard = PersistInGraveyardCheckBox.IsChecked ?? false;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _currentSearchCancellation?.Cancel();
            _currentSearchCancellation?.Dispose();
            _searchCancellationTokenSource.Cancel();
            _searchCancellationTokenSource.Dispose();
            base.OnClosed(e);
        }
    }
}

