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

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage
    {
        private List<ITransactionData> _allTransactions = new List<ITransactionData>();
        private TransactionViewModel _viewModel;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private readonly HttpClient _httpClient;

        public History()
        {
            InitializeComponent();
            _viewModel = new TransactionViewModel();
            TrackUserActivity();
            _httpClient = CreateHttpClientWithSSLHandler();
            BindingContext = _viewModel;
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private HttpClient CreateHttpClientWithSSLHandler()
        {
            try
            {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
                };

                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 |
                    SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
                return client;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating HTTP client: {ex.Message}");
                return new HttpClient { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) };
            }
        }

        // Interface for common transaction properties
        public interface ITransactionData
        {
            string TransactionId { get; }
            string ServiceName { get; }
            decimal Amount { get; }
            DateTime DateRecorded { get; }
            string GetDisplayInfo();
        }

        // ViewModel for data binding
        public class TransactionViewModel : INotifyPropertyChanged
        {
            private ObservableCollection<TransactionWrapper> _transactions;

            public ObservableCollection<TransactionWrapper> Transactions
            {
                get => _transactions;
                set
                {
                    _transactions = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Summary));
                }
            }

            public string Summary => $"Total Transactions: {Transactions?.Count ?? 0}";

            public TransactionViewModel()
            {
                Transactions = new ObservableCollection<TransactionWrapper>();
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Default transaction data
        class DefaultTransactionData : ITransactionData
        {
            public string transactionId { get; set; }
            public string businessName { get; set; }
            public string serviceName { get; set; }
            public string payerId { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }

            public string TransactionId => transactionId;
            public string ServiceName => serviceName ?? "N/A";
            public decimal Amount => amount;

            public DateTime DateRecorded
            {
                get
                {
                    if (DateTime.TryParse(dateRecorded, out DateTime result)) return result;
                    return DateTime.MinValue;
                }
            }

            public string GetDisplayInfo()
                => $"Business: {businessName ?? "N/A"}\nPayer ID: {payerId ?? "N/A"}";
        }

        // Wrapper class for ListView binding
        public class TransactionWrapper
        {
            public string TransactionId { get; set; }
            public string ServiceName { get; set; }
            public string DisplayInfo { get; set; }
            public decimal Amount { get; set; }
            public DateTime DateRecorded { get; set; }
            public ITransactionData OriginalData { get; set; }
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (startDatePicker.Date > endDatePicker.Date)
            {
                await DisplayAlert("Invalid Date Range", "Start date cannot be after end date.", "OK");
                return;
            }

            using (UserDialogs.Instance.Loading("Fetching transaction history...", null, null, true, MaskType.Black))
            {
                await Task.Delay(500);

                string searchFrom = startDatePicker.Date.ToString("MM-dd-yyyy");
                string searchTo = endDatePicker.Date.ToString("MM-dd-yyyy");

                try
                {
                    string url = $"https://yobe.osoftpay.net/api/TaskPayers/gettransaction?Email={MainPage.ValidUserMail}&SearchFrom={searchFrom}&SearchTo={searchTo}";
                    await FetchDefaultTransactions(url);
                }
                catch (HttpRequestException httpEx)
                {
                    await DisplayAlert("Network Error", "Failed to connect to server. Please check your internet connection.", "OK");
                    System.Diagnostics.Debug.WriteLine($"HTTP Error: {httpEx.Message}\n{httpEx.InnerException?.Message}");
                }
                catch (JsonException jsonEx)
                {
                    await DisplayAlert("Data Error", "Failed to process server response.", "OK");
                    System.Diagnostics.Debug.WriteLine($"JSON Error: {jsonEx.Message}");
                }
                catch (TaskCanceledException timeoutEx)
                {
                    await DisplayAlert("Timeout Error", "Request took too long. Please check your connection and try again.", "OK");
                    System.Diagnostics.Debug.WriteLine($"Timeout Error: {timeoutEx.Message}");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"An unexpected error occurred: {ex.Message}", "OK");
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
            }
        }

        private async Task FetchDefaultTransactions(string url)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Fetching Default Transactions from: {url}");

                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response Content: {json}");

                response.EnsureSuccessStatusCode();

                var items = JsonConvert.DeserializeObject<List<DefaultTransactionData>>(json);

                if (items != null && items.Any())
                {
                    _allTransactions = items.Cast<ITransactionData>().ToList();
                    DisplayTransactions(_allTransactions);
                    UpdateSummaryPanel(_allTransactions);
                    System.Diagnostics.Debug.WriteLine($"Loaded {items.Count} default transactions");
                }
                else
                {
                    await DisplayAlert("No Results", "No transactions found for the selected date range.", "OK");
                    _viewModel.Transactions.Clear();
                    HideSummaryPanel();
                }
            }
            catch (HttpRequestException httpEx)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP Request Error in FetchDefaultTransactions: {httpEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FetchDefaultTransactions: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates totals and updates the Summary Panel on the UI thread.
        /// </summary>
        private void UpdateSummaryPanel(List<ITransactionData> transactions)
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    int count = transactions?.Count ?? 0;
                    decimal total = transactions?.Sum(t => t.Amount) ?? 0m;
                    string dateFrom = startDatePicker.Date.ToString("MMM dd");
                    string dateTo = endDatePicker.Date.ToString("MMM dd, yyyy");

                    SummaryTotalCount.Text = count.ToString();
                    SummaryTotalAmount.Text = $"₦{total:N2}";
                    SummaryDateRange.Text = $"{dateFrom} – {dateTo}";

                    // Animate the panel in
                    SummaryPanel.IsVisible = true;
                    SummaryPanel.Opacity = 0;
                    SummaryPanel.TranslationY = -10;
                    await Task.WhenAll(
                        SummaryPanel.FadeTo(1, 350),
                        SummaryPanel.TranslateTo(0, 0, 350, Easing.CubicOut)
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateSummaryPanel error: {ex.Message}");
                }
            });
        }

        private void HideSummaryPanel()
        {
            Device.BeginInvokeOnMainThread(() => SummaryPanel.IsVisible = false);
        }

        private void DisplayTransactions(List<ITransactionData> transactions)
        {
            var wrapperList = transactions.Select(t => new TransactionWrapper
            {
                TransactionId = t.TransactionId ?? "N/A",
                ServiceName = t.ServiceName ?? "N/A",
                DisplayInfo = t.GetDisplayInfo(),
                Amount = t.Amount,
                DateRecorded = t.DateRecorded,
                OriginalData = t
            }).ToList();

            _viewModel.Transactions = new ObservableCollection<TransactionWrapper>(wrapperList);
            System.Diagnostics.Debug.WriteLine($"Displaying {wrapperList.Count} transactions");
        }

        private void SearchEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                DisplayTransactions(_allTransactions);
                UpdateSummaryPanel(_allTransactions);
                return;
            }

            var searchText = e.NewTextValue.ToLower();
            var filteredTransactions = _allTransactions.Where(t =>
                (t.TransactionId?.ToLower().Contains(searchText) ?? false) ||
                (t.ServiceName?.ToLower().Contains(searchText) ?? false) ||
                (t.GetDisplayInfo()?.ToLower().Contains(searchText) ?? false)
            ).ToList();

            DisplayTransactions(filteredTransactions);
            UpdateSummaryPanel(filteredTransactions);
        }
    }
}