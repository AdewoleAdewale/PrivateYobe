using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Yorata_Ops
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class GHistory : ContentPage
    {
        private List<YorataTransaction> _allTransactions = new List<YorataTransaction>();
        private TransactionViewModel _viewModel;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private readonly HttpClient _httpClient;

        public GHistory()
        {
            try
            {
                InitializeComponent();
                _viewModel = new TransactionViewModel();
                _httpClient = CreateHttpClientWithSSLHandler();
                BindingContext = _viewModel;

                // Set default date range (last 7 days)
                endDatePicker.Date = DateTime.Now;
                startDatePicker.Date = DateTime.Now.AddDays(-7);

                TrackUserActivity();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GHistory constructor: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void TrackUserActivity()
        {
            try
            {
                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (s, e) =>
                {
                    try
                    {
                        SessionManager.Instance?.UpdateActivity();
                    }
                    catch { }
                };
                this.Content?.GestureRecognizers.Add(tapGesture);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error tracking activity: {ex.Message}");
            }
        }

        private HttpClient CreateHttpClientWithSSLHandler()
        {
            try
            {
                var handler = new HttpClientHandler();

                // Bypass SSL certificate validation for both debug and release modes
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    // Accept all certificates without validation
                    // NOTE: This is not recommended for production apps handling sensitive data
                    // Consider implementing proper certificate pinning for production
                    return true;
                };

                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
                };

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
                return client;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating HTTP client: {ex.Message}");
                // Return basic client as fallback
                return new HttpClient { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) };
            }
        }

        // ViewModel for data binding
        public class TransactionViewModel : INotifyPropertyChanged
        {
            private ObservableCollection<YorataTransaction> _transactions;
            private string _summary;
            private bool _hasTransactions;
            private bool _showEmptyState;

            public ObservableCollection<YorataTransaction> Transactions
            {
                get => _transactions ?? new ObservableCollection<YorataTransaction>();
                set
                {
                    _transactions = value;
                    OnPropertyChanged();
                    UpdateSummary();
                    UpdateVisibility();
                }
            }

            public bool HasTransactions
            {
                get => _hasTransactions;
                private set
                {
                    if (_hasTransactions != value)
                    {
                        _hasTransactions = value;
                        OnPropertyChanged();
                    }
                }
            }

            public bool ShowEmptyState
            {
                get => _showEmptyState;
                private set
                {
                    if (_showEmptyState != value)
                    {
                        _showEmptyState = value;
                        OnPropertyChanged();
                    }
                }
            }

            public string Summary
            {
                get => _summary ?? "No transactions found";
                private set
                {
                    if (_summary != value)
                    {
                        _summary = value;
                        OnPropertyChanged();
                    }
                }
            }

            private void UpdateSummary()
            {
                try
                {
                    if (_transactions == null || _transactions.Count == 0)
                    {
                        Summary = "No transactions found";
                        return;
                    }

                    decimal totalAmount = _transactions.Sum(t => t?.AmountValue ?? 0);
                    Summary = $"Total: {_transactions.Count} transaction(s) | Amount: ₦{totalAmount:N2}";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating summary: {ex.Message}");
                    Summary = "No transactions found";
                }
            }

            private void UpdateVisibility()
            {
                HasTransactions = _transactions != null && _transactions.Count > 0;
                ShowEmptyState = !HasTransactions;
            }

            public TransactionViewModel()
            {
                _transactions = new ObservableCollection<YorataTransaction>();
                _summary = "No transactions found";
                _hasTransactions = false;
                _showEmptyState = true;
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Yorata transaction data model matching the API response
        public class YorataTransaction
        {
            [JsonProperty("performedBy")]
            public string PerformedBy { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("processed")]
            public string Processed { get; set; }

            [JsonProperty("category")]
            public string Category { get; set; }

            [JsonProperty("serviceType")]
            public string ServiceType { get; set; }

            [JsonProperty("invoice")]
            public string Invoice { get; set; }

            [JsonProperty("amount")]
            public string Amount { get; set; }

            [JsonProperty("dateRecorded")]
            public string DateRecorded { get; set; }

            // Helper properties for UI binding with null safety
            public string StatusColor
            {
                get
                {
                    try
                    {
                        return Status?.ToLower() == "paid" ? "#4CAF50" : "#FF9800";
                    }
                    catch
                    {
                        return "#FF9800";
                    }
                }
            }

            public string ProcessedColor
            {
                get
                {
                    try
                    {
                        return Processed?.ToLower() == "yes" ? "#4CAF50" : "#F44336";
                    }
                    catch
                    {
                        return "#F44336";
                    }
                }
            }

            public string StatusIcon
            {
                get
                {
                    try
                    {
                        return Status?.ToLower() == "paid" ? "✓" : "⏳";
                    }
                    catch
                    {
                        return "⏳";
                    }
                }
            }

            public decimal AmountValue
            {
                get
                {
                    try
                    {
                        if (string.IsNullOrEmpty(Amount))
                            return 0;

                        string cleanAmount = Amount.Replace(",", "").Replace("₦", "").Trim();
                        if (decimal.TryParse(cleanAmount, out decimal result))
                            return result;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing amount: {ex.Message}");
                    }
                    return 0;
                }
            }

            public string FormattedAmount
            {
                get
                {
                    try
                    {
                        return $"₦{AmountValue:N2}";
                    }
                    catch
                    {
                        return "₦0.00";
                    }
                }
            }

            public string DisplayInfo
            {
                get
                {
                    try
                    {
                        var category = string.IsNullOrEmpty(Category) ? "N/A" : Category;
                        var serviceType = string.IsNullOrEmpty(ServiceType) ? "N/A" : ServiceType;
                        return $"{category} | {serviceType}";
                    }
                    catch
                    {
                        return "N/A | N/A";
                    }
                }
            }
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance?.UpdateActivity();

                if (startDatePicker.Date > endDatePicker.Date)
                {
                    await DisplayAlert("Invalid Date Range", "Start date cannot be after end date.", "OK");
                    return;
                }

                // Check if date range is too large (more than 90 days)
                if ((endDatePicker.Date - startDatePicker.Date).TotalDays > 90)
                {
                    var confirm = await DisplayAlert("Large Date Range",
                        "You've selected a date range larger than 90 days. This may take longer to load. Continue?",
                        "Yes", "No");

                    if (!confirm)
                        return;
                }

                using (UserDialogs.Instance.Loading("Fetching transaction history...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(300);

                    string searchFrom = startDatePicker.Date.ToString("yyyy-MM-dd");
                    string searchTo = endDatePicker.Date.ToString("yyyy-MM-dd");

                    try
                    {
                        string url = $"https://yobe.osoftpay.net/api/KekeTransactions/gethistory?Email={MainPage.ValidUserMail}&SearchFrom={searchFrom}&SearchTo={searchTo}";
                        await FetchYorataTransactions(url);
                    }
                    catch (HttpRequestException httpEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"HTTP Error: {httpEx.Message}\n{httpEx.InnerException?.Message}");
                        await DisplayAlert("Network Error",
                            "Failed to connect to server. Please check your internet connection and try again.",
                            "OK");
                    }
                    catch (JsonException jsonEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"JSON Error: {jsonEx.Message}");
                        await DisplayAlert("Data Error",
                            "Failed to process server response. The data format may be invalid.",
                            "OK");
                    }
                    catch (TaskCanceledException)
                    {
                        await DisplayAlert("Timeout Error",
                            "Request took too long. Please check your connection and try again.",
                            "OK");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                        await DisplayAlert("Error",
                            "An unexpected error occurred. Please try again later.",
                            "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Button_Clicked: {ex.Message}\n{ex.StackTrace}");
                await DisplayAlert("Error", "Failed to process request. Please try again.", "OK");
            }
        }

        private async Task FetchYorataTransactions(string url)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Fetching Yorata Transactions from: {url}");

                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response Content: {json.Substring(0, Math.Min(500, json.Length))}...");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await DisplayAlert("Authentication Error",
                            "Your session may have expired. Please log in again.",
                            "OK");
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        await DisplayAlert("Error",
                            "The service endpoint was not found. Please contact support.",
                            "OK");
                    }
                    else
                    {
                        await DisplayAlert("Server Error",
                            $"Server returned error: {response.StatusCode}. Please try again later.",
                            "OK");
                    }
                    return;
                }

                var items = JsonConvert.DeserializeObject<List<YorataTransaction>>(json);

                if (items != null && items.Any())
                {
                    _allTransactions = items;
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        DisplayTransactions(_allTransactions);
                    });
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded {items.Count} transactions");
                }
                else
                {
                    _allTransactions.Clear();
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        _viewModel.Transactions.Clear();
                    });
                    await DisplayAlert("No Results",
                        "No transactions found for the selected date range.",
                        "OK");
                }
            }
            catch (JsonException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FetchYorataTransactions: {ex.Message}");
                throw new Exception("Failed to fetch transaction data", ex);
            }
        }

        private void DisplayTransactions(List<YorataTransaction> transactions)
        {
            try
            {
                if (transactions == null)
                {
                    _viewModel.Transactions = new ObservableCollection<YorataTransaction>();
                    return;
                }

                // Sort by date (most recent first)
                var sortedTransactions = transactions
                    .Where(t => t != null)
                    .OrderByDescending(t => ParseDate(t.DateRecorded))
                    .ToList();

                _viewModel.Transactions = new ObservableCollection<YorataTransaction>(sortedTransactions);
                System.Diagnostics.Debug.WriteLine($"Displaying {sortedTransactions.Count} transactions");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying transactions: {ex.Message}");
                _viewModel.Transactions = new ObservableCollection<YorataTransaction>();
            }
        }

        private DateTime ParseDate(string dateStr)
        {
            try
            {
                if (string.IsNullOrEmpty(dateStr))
                    return DateTime.MinValue;

                // Parse format: "24/11/25 09:40 AM"
                if (DateTime.TryParseExact(dateStr, "dd/MM/yy hh:mm tt",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime result))
                {
                    return result;
                }

                // Try alternative format without time
                if (DateTime.TryParseExact(dateStr, "dd/MM/yy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime result2))
                {
                    return result2;
                }

                // Try generic parse as fallback
                if (DateTime.TryParse(dateStr, out DateTime result3))
                {
                    return result3;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing date '{dateStr}': {ex.Message}");
            }

            return DateTime.MinValue;
        }

        private void SearchEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                SessionManager.Instance?.UpdateActivity();

                if (string.IsNullOrWhiteSpace(e.NewTextValue))
                {
                    DisplayTransactions(_allTransactions);
                    return;
                }

                var searchText = e.NewTextValue.ToLower().Trim();
                var filteredTransactions = _allTransactions
                    .Where(t => t != null && (
                        (t.Invoice?.ToLower().Contains(searchText) ?? false) ||
                        (t.ServiceType?.ToLower().Contains(searchText) ?? false) ||
                        (t.Status?.ToLower().Contains(searchText) ?? false) ||
                        (t.Category?.ToLower().Contains(searchText) ?? false) ||
                        (t.Amount?.ToLower().Contains(searchText) ?? false)
                    ))
                    .ToList();

                DisplayTransactions(filteredTransactions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in search: {ex.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                _httpClient?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing HTTP client: {ex.Message}");
            }
        }
    }
}