using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MTGSimulator.Game;

namespace MTGSimulator.Services
{
    public class CardImageService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _imageCacheDirectory;
        private string? _currentDeckFolder;
        private int _maxCachedImages;
        private const string ScryfallApiBase = "https://api.scryfall.com";

        public CardImageService(int maxCachedImages = 1000)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGSimulator/1.0");
            _maxCachedImages = maxCachedImages;
            
            // Create cache directory in user's AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _imageCacheDirectory = Path.Combine(appDataPath, "MTGSimulator", "CardImages");
            
            if (!Directory.Exists(_imageCacheDirectory))
            {
                Directory.CreateDirectory(_imageCacheDirectory);
            }
        }

        public void SetDeckFolder(string deckName)
        {
            // Create a folder for this deck
            var sanitizedDeckName = SanitizeFileName(deckName);
            _currentDeckFolder = Path.Combine(_imageCacheDirectory, sanitizedDeckName);
            
            if (!Directory.Exists(_currentDeckFolder))
            {
                Directory.CreateDirectory(_currentDeckFolder);
            }
        }

        public void ClearDeckFolder()
        {
            _currentDeckFolder = null;
        }

        public void SetMaxCachedImages(int maxCachedImages)
        {
            _maxCachedImages = maxCachedImages;
            EnforceCacheLimit();
        }

        public async Task<string?> DownloadCardImageAsync(Card card)
        {
            try
            {
                // First, get or search for ScryfallId
                string? scryfallId = card.ScryfallId;
                if (string.IsNullOrEmpty(scryfallId))
                {
                    scryfallId = await SearchCardOnScryfallAsync(card);
                    if (scryfallId == null)
                    {
                        return null;
                    }
                    card.ScryfallId = scryfallId;
                }

                // Now check if image already exists in cache (we need ScryfallId to check cache)
                string? cachedPath = GetCachedImagePath(card);
                if (cachedPath != null && File.Exists(cachedPath))
                {
                    // Image exists in cache, use it
                    card.ImagePath = cachedPath;
                    return cachedPath;
                }

                // Image not in cache, download it
                // Download image - use large version for better quality
                string imageUrl = $"https://api.scryfall.com/cards/{scryfallId}?format=image&version=large";
                byte[] imageData = await _httpClient.GetByteArrayAsync(imageUrl);

                // Save to cache (and deck folder if set)
                string fileName = SanitizeFileName($"{scryfallId}.jpg");
                string filePath = Path.Combine(_imageCacheDirectory, fileName);
                await File.WriteAllBytesAsync(filePath, imageData);

                // Also save to deck folder if one is set
                if (!string.IsNullOrEmpty(_currentDeckFolder))
                {
                    string deckFilePath = Path.Combine(_currentDeckFolder, fileName);
                    await File.WriteAllBytesAsync(deckFilePath, imageData);
                }

                // Enforce cache limit after saving
                EnforceCacheLimit();

                card.ImagePath = filePath;
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading image for {card.Name}: {ex.Message}");
                return null;
            }
        }

        public async Task DownloadCardImagesAsync(List<Card> cards, IProgress<(int current, int total, string cardName)>? progress = null)
        {
            int total = cards.Count;
            int completed = 0;
            
            // Use semaphore to limit concurrent downloads (Scryfall recommends max 10 requests/second)
            // We'll use 5 concurrent downloads to be safe
            using var semaphore = new SemaphoreSlim(5, 5);
            var tasks = new List<Task>();

            foreach (var card in cards)
            {
                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // DownloadCardImageAsync sets card.ImagePath internally
                        await DownloadCardImageAsync(card);
                        
                        Interlocked.Increment(ref completed);
                        progress?.Report((completed, total, card.Name));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        public async Task<(int downloaded, int failed, List<string> failedCards)> DownloadCardImagesWithVerificationAsync(
            List<Card> cards, 
            IProgress<(int current, int total, string cardName)>? progress = null)
        {
            // First pass: download all images
            await DownloadCardImagesAsync(cards, progress);

            // Second pass: verify all cards have images and retry failed ones
            var failedCards = new List<string>();
            var cardsToRetry = new List<Card>();

            foreach (var card in cards)
            {
                if (string.IsNullOrEmpty(card.ImagePath) || !File.Exists(card.ImagePath))
                {
                    failedCards.Add(card.Name);
                    cardsToRetry.Add(card);
                }
            }

            // Retry failed cards once
            if (cardsToRetry.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Retrying {cardsToRetry.Count} failed card downloads...");
                await DownloadCardImagesAsync(cardsToRetry, progress);
                
                // Check again after retry
                failedCards.Clear();
                foreach (var card in cardsToRetry)
                {
                    if (string.IsNullOrEmpty(card.ImagePath) || !File.Exists(card.ImagePath))
                    {
                        failedCards.Add(card.Name);
                    }
                }
            }

            int downloaded = cards.Count - failedCards.Count;
            return (downloaded, failedCards.Count, failedCards);
        }

        private async Task<string?> SearchCardOnScryfallAsync(Card card)
        {
            try
            {
                // Build search query
                string query = $"!\"{card.Name}\"";
                if (!string.IsNullOrEmpty(card.SetCode))
                {
                    query += $" set:{card.SetCode}";
                }
                if (!string.IsNullOrEmpty(card.CollectorNumber))
                {
                    query += $" number:{card.CollectorNumber}";
                }

                string searchUrl = $"{ScryfallApiBase}/cards/search?q={Uri.EscapeDataString(query)}";
                
                var response = await _httpClient.GetStringAsync(searchUrl);
                var jsonDoc = JsonDocument.Parse(response);
                
                if (jsonDoc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                {
                    var firstCard = data[0];
                    if (firstCard.TryGetProperty("id", out var id))
                    {
                        return id.GetString();
                    }
                }

                // If exact match failed, try fuzzy search
                string fuzzyQuery = Uri.EscapeDataString(card.Name);
                string fuzzyUrl = $"{ScryfallApiBase}/cards/named?fuzzy={fuzzyQuery}";
                
                var fuzzyResponse = await _httpClient.GetStringAsync(fuzzyUrl);
                var fuzzyDoc = JsonDocument.Parse(fuzzyResponse);
                
                if (fuzzyDoc.RootElement.TryGetProperty("id", out var fuzzyId))
                {
                    return fuzzyId.GetString();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching Scryfall for {card.Name}: {ex.Message}");
            }

            return null;
        }

        private string? GetCachedImagePath(Card card)
        {
            if (!string.IsNullOrEmpty(card.ScryfallId))
            {
                string fileName = SanitizeFileName($"{card.ScryfallId}.jpg");
                
                // Check deck folder first if it exists
                if (!string.IsNullOrEmpty(_currentDeckFolder))
                {
                    string deckFilePath = Path.Combine(_currentDeckFolder, fileName);
                    if (File.Exists(deckFilePath))
                    {
                        return deckFilePath;
                    }
                }
                
                // Fall back to main cache
                string filePath = Path.Combine(_imageCacheDirectory, fileName);
                return filePath;
            }
            return null;
        }

        private string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        private void EnforceCacheLimit()
        {
            try
            {
                var imageFiles = Directory.GetFiles(_imageCacheDirectory, "*.jpg")
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastWriteTime)
                    .ToList();

                if (imageFiles.Count > _maxCachedImages)
                {
                    int filesToDelete = imageFiles.Count - _maxCachedImages;
                    for (int i = 0; i < filesToDelete; i++)
                    {
                        try
                        {
                            imageFiles[i].Delete();
                        }
                        catch
                        {
                            // Ignore errors deleting individual files
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors in cache management
            }
        }

        public async Task<CardData?> GetCardDataAsync(Card card)
        {
            try
            {
                // Get Scryfall ID if we don't have it
                string? scryfallId = card.ScryfallId;
                if (string.IsNullOrEmpty(scryfallId))
                {
                    scryfallId = await SearchCardOnScryfallAsync(card);
                    if (scryfallId == null)
                    {
                        return null;
                    }
                    card.ScryfallId = scryfallId;
                }

                // Fetch full card data from Scryfall
                string cardUrl = $"{ScryfallApiBase}/cards/{scryfallId}";
                var response = await _httpClient.GetStringAsync(cardUrl);
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                var cardData = new CardData
                {
                    Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? card.Name : card.Name,
                    ManaCost = root.TryGetProperty("mana_cost", out var manaCost) ? manaCost.GetString() ?? card.ManaCost : card.ManaCost,
                    Type = root.TryGetProperty("type_line", out var type) ? type.GetString() ?? card.Type : card.Type,
                    OracleText = root.TryGetProperty("oracle_text", out var oracleText) ? oracleText.GetString() ?? card.Text : card.Text,
                    Power = root.TryGetProperty("power", out var power) ? power.GetString() : null,
                    Toughness = root.TryGetProperty("toughness", out var toughness) ? toughness.GetString() : null
                };
                
                // Also update the card directly if power/toughness are available
                if (cardData.Power != null)
                {
                    card.Power = cardData.Power;
                }
                if (cardData.Toughness != null)
                {
                    card.Toughness = cardData.Toughness;
                }

                // Get associated cards (back faces, tokens, etc.)
                var associatedCards = new List<AssociatedCard>();

                // Check for card_faces (double-faced cards)
                if (root.TryGetProperty("card_faces", out var cardFaces) && cardFaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (var face in cardFaces.EnumerateArray())
                    {
                        if (face.TryGetProperty("name", out var faceName) && face.TryGetProperty("id", out var faceId))
                        {
                            string faceNameStr = faceName.GetString() ?? "";
                            string faceIdStr = faceId.GetString() ?? "";
                            
                            // Only add if it's different from the main card name
                            if (faceNameStr != cardData.Name && !string.IsNullOrEmpty(faceIdStr))
                            {
                                associatedCards.Add(new AssociatedCard
                                {
                                    Name = faceNameStr,
                                    ScryfallId = faceIdStr,
                                    Type = "back_face",
                                    ImageUrl = face.TryGetProperty("image_uris", out var imageUris) && 
                                              imageUris.TryGetProperty("large", out var largeImg) 
                                              ? largeImg.GetString() : null
                                });
                            }
                        }
                    }
                }

                // Check for all_parts (tokens, emblems, etc.)
                if (root.TryGetProperty("all_parts", out var allParts) && allParts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in allParts.EnumerateArray())
                    {
                        if (part.TryGetProperty("name", out var partName) && 
                            part.TryGetProperty("id", out var partId) &&
                            part.TryGetProperty("component", out var component))
                        {
                            string partNameStr = partName.GetString() ?? "";
                            string partIdStr = partId.GetString() ?? "";
                            string componentStr = component.GetString() ?? "";
                            
                            // Only add tokens and related cards, not the card itself
                            if (partNameStr != cardData.Name && 
                                !string.IsNullOrEmpty(partIdStr) &&
                                (componentStr == "token" || componentStr == "combo_piece" || componentStr == "meld_result"))
                            {
                                associatedCards.Add(new AssociatedCard
                                {
                                    Name = partNameStr,
                                    ScryfallId = partIdStr,
                                    Type = componentStr == "token" ? "token" : componentStr
                                });
                            }
                        }
                    }
                }

                cardData.AssociatedCards = associatedCards;
                return cardData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching card data for {card.Name}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<CardSearchResult>> SearchCardsAsync(string searchQuery, int maxResults = 20)
        {
            var results = new List<CardSearchResult>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string searchUrl = $"{ScryfallApiBase}/cards/search?q={Uri.EscapeDataString(searchQuery)}&order=released&dir=desc";
                var response = await _httpClient.GetStringAsync(searchUrl);
                var jsonDoc = JsonDocument.Parse(response);
                
                if (jsonDoc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cardElement in data.EnumerateArray())
                    {
                        if (results.Count >= maxResults) break;
                        
                        var result = new CardSearchResult
                        {
                            Name = cardElement.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                            ScryfallId = cardElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            ManaCost = cardElement.TryGetProperty("mana_cost", out var manaCost) ? manaCost.GetString() ?? "" : "",
                            Type = cardElement.TryGetProperty("type_line", out var type) ? type.GetString() ?? "" : "",
                            OracleText = cardElement.TryGetProperty("oracle_text", out var oracleText) ? oracleText.GetString() ?? "" : "",
                            IsToken = cardElement.TryGetProperty("layout", out var layout) && layout.GetString() == "token",
                            Power = cardElement.TryGetProperty("power", out var power) ? power.GetString() : null,
                            Toughness = cardElement.TryGetProperty("toughness", out var toughness) ? toughness.GetString() : null
                        };
                        
                        // Skip if we've already seen this card name
                        if (!string.IsNullOrEmpty(result.Name) && !string.IsNullOrEmpty(result.ScryfallId) && !seenNames.Contains(result.Name))
                        {
                            seenNames.Add(result.Name);
                            results.Add(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching cards: {ex.Message}");
            }
            
            return results;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class CardSearchResult
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

    public class CardData
    {
        public string Name { get; set; } = "";
        public string ManaCost { get; set; } = "";
        public string Type { get; set; } = "";
        public string OracleText { get; set; } = "";
        public string? Power { get; set; }
        public string? Toughness { get; set; }
        public List<AssociatedCard> AssociatedCards { get; set; } = new List<AssociatedCard>();
    }

    public class AssociatedCard
    {
        public string Name { get; set; } = "";
        public string ScryfallId { get; set; } = "";
        public string Type { get; set; } = ""; // "back_face", "token", "meld_result", etc.
        public string? ImageUrl { get; set; }
    }
}

