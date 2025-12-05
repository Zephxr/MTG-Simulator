using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using MTGSimulator.Game;

namespace MTGSimulator.Views
{
    public partial class DeckViewerWindow : Window
    {
        private List<DeckCardInfo> _deckCards = new List<DeckCardInfo>();

        public DeckViewerWindow()
        {
            InitializeComponent();
        }

        private void LoadDeckButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Deck Files (*.txt;*.dec)|*.txt;*.dec|All Files (*.*)|*.*",
                Title = "Load Deck"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var deckEntries = DeckImporter.ImportDeckFromFile(openFileDialog.FileName);
                    
                    _deckCards.Clear();
                    var cardGroups = deckEntries.GroupBy(e => e.CardName);
                    
                    foreach (var group in cardGroups.OrderBy(g => g.Key))
                    {
                        var firstEntry = group.First();
                        int totalQuantity = group.Sum(e => e.Quantity);
                        _deckCards.Add(new DeckCardInfo
                        {
                            Quantity = totalQuantity,
                            Name = firstEntry.CardName,
                            Type = "",
                            ManaCost = ""
                        });
                    }
                    
                    DeckDataGrid.ItemsSource = _deckCards;
                    DeckNameText.Text = System.IO.Path.GetFileName(openFileDialog.FileName);
                    CardCountText.Text = $"{deckEntries.Sum(e => e.Quantity)} cards ({_deckCards.Count} unique)";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error loading deck: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private class DeckCardInfo
        {
            public int Quantity { get; set; }
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string ManaCost { get; set; } = "";
        }
    }
}

