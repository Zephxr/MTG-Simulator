using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MTGSimulator.Game;

namespace MTGSimulator.Services
{
    public class BulkDataService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string _bulkDataDirectory;
        private const string ScryfallApiBase = "https://api.scryfall.com";
        private const string DefaultCardsFileName = "default-cards.json";
        private string? _cardsFilePath;
        private List<BulkCardData>? _cachedCards;
        private DateTime _lastUpdateCheck = DateTime.MinValue;

        public bool HasBulkData => _cachedCards != null && _cachedCards.Count > 0;
        public int CardCount => _cachedCards?.Count ?? 0;

        public BulkDataService(string? bulkDataDirectory = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGSimulator/1.0");
            
            if (string.IsNullOrEmpty(bulkDataDirectory))
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _bulkDataDirectory = Path.Combine(appDataPath, "MTGSimulator", "BulkData");
            }
            else
            {
                _bulkDataDirectory = bulkDataDirectory;
            }

            if (!Directory.Exists(_bulkDataDirectory))
            {
                Directory.CreateDirectory(_bulkDataDirectory);
            }

            _cardsFilePath = Path.Combine(_bulkDataDirectory, DefaultCardsFileName);
        }

        public void SetBulkDataDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _bulkDataDirectory = directory;
            _cardsFilePath = Path.Combine(_bulkDataDirectory, DefaultCardsFileName);
            _cachedCards = null; // Clear cache when directory changes
        }

        public async Task<bool> CheckAndLoadBulkData()
        {
            if (File.Exists(_cardsFilePath))
            {
                try
                {
                    await LoadBulkData();
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading bulk data: {ex.Message}");
                    return false;
                }
            }
            return false;
        }

        public async Task<BulkDataInfo?> GetBulkDataInfoAsync()
        {
            try
            {
                string url = $"{ScryfallApiBase}/bulk-data";
                var response = await _httpClient.GetStringAsync(url);
                var jsonDoc = JsonDocument.Parse(response);
                
                if (jsonDoc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var type) && type.GetString() == "default_cards")
                        {
                            var info = new BulkDataInfo
                            {
                                Uri = item.TryGetProperty("download_uri", out var uri) ? uri.GetString() : null,
                                UpdatedAt = item.TryGetProperty("updated_at", out var updated) ? updated.GetString() : null,
                                Size = item.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                                ContentType = item.TryGetProperty("content_type", out var contentType) ? contentType.GetString() : null
                            };
                            return info;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting bulk data info: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> DownloadBulkDataAsync(IProgress<(long bytesReceived, long totalBytes, string status)>? progress = null)
        {
            try
            {
                var info = await GetBulkDataInfoAsync();
                if (info == null || string.IsNullOrEmpty(info.Uri))
                {
                    return false;
                }

                progress?.Report((0, info.Size, "Downloading bulk data..."));

                // Download to temporary file first
                string tempFile = _cardsFilePath + ".tmp";
                
                using (var response = await _httpClient.GetAsync(info.Uri, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? info.Size;
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var httpStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            progress?.Report((totalBytesRead, totalBytes, $"Downloading... {totalBytesRead / 1024 / 1024} MB"));
                        }
                    }
                }

                // Replace old file with new one
                if (File.Exists(_cardsFilePath) && !string.IsNullOrEmpty(_cardsFilePath))
                {
                    File.Delete(_cardsFilePath);
                }
                if (!string.IsNullOrEmpty(_cardsFilePath))
                {
                    File.Move(tempFile, _cardsFilePath);
                }

                // Clear cache to force reload
                _cachedCards = null;
                
                progress?.Report((info.Size, info.Size, "Download complete! Loading data..."));
                await LoadBulkData();
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading bulk data: {ex.Message}");
                return false;
            }
        }

        private async Task LoadBulkData()
        {
            if (!File.Exists(_cardsFilePath))
            {
                _cachedCards = new List<BulkCardData>();
                return;
            }

            _cachedCards = new List<BulkCardData>();
            
            try
            {
                // Scryfall bulk data is a JSON array - parse it efficiently
                // The file is typically 500MB uncompressed, which should be manageable
                using (var fileStream = new FileStream(_cardsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var jsonDoc = await JsonDocument.ParseAsync(fileStream);
                    
                    if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        int count = 0;
                        foreach (var cardElement in jsonDoc.RootElement.EnumerateArray())
                        {
                            try
                            {
                                var cardData = ParseCardData(cardElement);
                                if (cardData != null)
                                {
                                    _cachedCards.Add(cardData);
                                    count++;
                                    
                                    if (count % 10000 == 0)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Loaded {count} cards...");
                                    }
                                }
                            }
                            catch
                            {
                                // Skip invalid cards
                                continue;
                            }
                        }
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine("Out of memory loading bulk data - file may be too large");
                _cachedCards = new List<BulkCardData>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading bulk data: {ex.Message}");
                _cachedCards = new List<BulkCardData>();
            }
            
            System.Diagnostics.Debug.WriteLine($"Loaded {_cachedCards.Count} cards from bulk data");
        }

        private BulkCardData? ParseCardData(JsonElement element)
        {
            try
            {
                var card = new BulkCardData
                {
                    Name = element.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    ScryfallId = element.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    ManaCost = element.TryGetProperty("mana_cost", out var manaCost) ? manaCost.GetString() ?? "" : "",
                    Type = element.TryGetProperty("type_line", out var type) ? type.GetString() ?? "" : "",
                    OracleText = element.TryGetProperty("oracle_text", out var oracleText) ? oracleText.GetString() ?? "" : "",
                    IsToken = element.TryGetProperty("layout", out var layout) && layout.GetString() == "token",
                    Power = element.TryGetProperty("power", out var power) ? power.GetString() : null,
                    Toughness = element.TryGetProperty("toughness", out var toughness) ? toughness.GetString() : null
                };

                // Only return if we have at least a name and ID
                if (!string.IsNullOrEmpty(card.Name) && !string.IsNullOrEmpty(card.ScryfallId))
                {
                    return card;
                }
            }
            catch
            {
                // Skip invalid cards
            }
            
            return null;
        }

        public List<CardSearchResult> SearchCards(string searchQuery, int maxResults = 30)
        {
            if (_cachedCards == null || _cachedCards.Count == 0)
            {
                return new List<CardSearchResult>();
            }

            var query = searchQuery.ToLowerInvariant();
            var results = new List<CardSearchResult>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Simple search - match name, type, or oracle text
            foreach (var card in _cachedCards)
            {
                if (results.Count >= maxResults)
                    break;

                // Skip if we've already seen this card name
                if (seenNames.Contains(card.Name))
                    continue;

                bool matches = 
                    card.Name.ToLowerInvariant().Contains(query) ||
                    card.Type.ToLowerInvariant().Contains(query) ||
                    card.OracleText.ToLowerInvariant().Contains(query);

                if (matches)
                {
                    seenNames.Add(card.Name);
                    results.Add(new CardSearchResult
                    {
                        Name = card.Name,
                        ScryfallId = card.ScryfallId,
                        ManaCost = card.ManaCost,
                        Type = card.Type,
                        OracleText = card.OracleText,
                        IsToken = card.IsToken,
                        Power = card.Power,
                        Toughness = card.Toughness
                    });
                }
            }

            // Sort by relevance (exact name matches first, then by name)
            results = results.OrderByDescending(r => r.Name.ToLowerInvariant().StartsWith(query))
                            .ThenBy(r => r.Name)
                            .ToList();

            return results;
        }

        public async Task<List<CardSearchResult>> SearchCardsAsync(string searchQuery, int maxResults = 30)
        {
            // If bulk data isn't loaded, try to load it
            if (_cachedCards == null)
            {
                await CheckAndLoadBulkData();
            }

            return SearchCards(searchQuery, maxResults);
        }

        public DateTime? GetLastUpdateTime()
        {
            if (File.Exists(_cardsFilePath))
            {
                return File.GetLastWriteTime(_cardsFilePath);
            }
            return null;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class BulkDataInfo
    {
        public string? Uri { get; set; }
        public string? UpdatedAt { get; set; }
        public long Size { get; set; }
        public string? ContentType { get; set; }
    }

    public class BulkCardData
    {
        public string Name { get; set; } = "";
        public string ScryfallId { get; set; } = "";
        public string ManaCost { get; set; } = "";
        public string Type { get; set; } = "";
        public string OracleText { get; set; } = "";
        public bool IsToken { get; set; }
        public string? Power { get; set; }
        public string? Toughness { get; set; }
    }
}

