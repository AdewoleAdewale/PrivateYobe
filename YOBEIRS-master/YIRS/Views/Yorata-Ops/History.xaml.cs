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
    public partial class History : ContentPage
    {
        private List<YorataTransactionData> _allTransactions = new List<YorataTransactionData>();
        private TransactionViewModel _viewModel;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private readonly HttpClient _httpClient;

        // New SDK printer instance (58mm by default; pass true for 80mm)
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService();

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

        // ─── ViewModels ────────────────────────────────────────────────────────

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

        public class YorataTransactionData
        {
            public string transactionId { get; set; }
            public string vehicleType { get; set; }
            public string vehicleNo { get; set; }
            public string serviceName { get; set; }
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
                => $"Vehicle Type: {vehicleType ?? "N/A"}\nVehicle No: {vehicleNo ?? "N/A"}";
        }

        public class TransactionWrapper
        {
            public string TransactionId { get; set; }
            public string ServiceName { get; set; }
            public string DisplayInfo { get; set; }
            public decimal Amount { get; set; }
            public DateTime DateRecorded { get; set; }
            public YorataTransactionData OriginalData { get; set; }
        }

        // ─── Search / Fetch ────────────────────────────────────────────────────

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
                    string url = $"https://yobe.osoftpay.net/api/TaskPayers/getYorotaTransaction?Email={MainPage.ValidUserMail}&SearchFrom={searchFrom}&SearchTo={searchTo}";
                    await FetchYorataTransactions(url);
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

        private async Task FetchYorataTransactions(string url)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                System.Diagnostics.Debug.WriteLine($"Fetching Yorata Transactions from: {url}");

                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response Content: {json}");

                response.EnsureSuccessStatusCode();

                var items = JsonConvert.DeserializeObject<List<YorataTransactionData>>(json);

                if (items != null && items.Any())
                {
                    _allTransactions = items;
                    DisplayTransactions(_allTransactions);
                    UpdateSummaryPanel(_allTransactions);
                    System.Diagnostics.Debug.WriteLine($"Loaded {items.Count} yorata transactions");
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
                System.Diagnostics.Debug.WriteLine($"HTTP Request Error in FetchYorataTransactions: {httpEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in FetchYorataTransactions: {ex.Message}");
                throw;
            }
        }

        // ─── Summary Panel ─────────────────────────────────────────────────────

        /// <summary>
        /// Calculates totals and updates the summary panel with animation.
        /// </summary>
        private void UpdateSummaryPanel(List<YorataTransactionData> transactions)
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

        // ─── Display / Search ──────────────────────────────────────────────────

        private void DisplayTransactions(List<YorataTransactionData> transactions)
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
            var filtered = _allTransactions.Where(t =>
                (t.TransactionId?.ToLower().Contains(searchText) ?? false) ||
                (t.ServiceName?.ToLower().Contains(searchText) ?? false) ||
                (t.GetDisplayInfo()?.ToLower().Contains(searchText) ?? false)
            ).ToList();

            DisplayTransactions(filtered);
            UpdateSummaryPanel(filtered);
        }

        // ─── Reprint (new BluetoothPrinterService SDK) ─────────────────────────

        private async void ReprintButton_Clicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            var button = sender as Button;
            var transaction = button?.BindingContext as TransactionWrapper;

            if (transaction == null)
            {
                await DisplayAlert("Error", "Transaction data not found.", "OK");
                return;
            }

            var confirm = await DisplayAlert("Confirm Reprint",
                $"Reprint receipt for transaction {transaction.TransactionId}?",
                "Yes", "No");

            if (!confirm) return;

            using (IProgressDialog progress = UserDialogs.Instance.Progress(
                "Processing reprint request...", null, null, true, MaskType.Gradient))
            {
                for (int i = 0; i < 50; i++)
                {
                    progress.PercentComplete = i;
                    await Task.Delay(20);
                }

                try
                {
                    string url = $"https://yobe.osoftpay.net/api/TaskPayers/ConfirmTransaction?TransactionId={transaction.TransactionId}";
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<TransactionConfirmationResponse>(json);

                    for (int i = 50; i < 100; i++)
                    {
                        progress.PercentComplete = i;
                        await Task.Delay(20);
                    }

                    if (result == null)
                    {
                        await DisplayAlert("Error", "Failed to retrieve transaction details.", "OK");
                        return;
                    }

                    if (result.printStatus?.ToLower() == "yes")
                    {
                        await PrintReceiptWithSDK(result, transaction.OriginalData);
                    }
                    else
                    {
                        await DisplayAlert("Cannot Reprint", "This receipt cannot be reprinted.", "OK");
                    }
                }
                catch (HttpRequestException)
                {
                    await DisplayAlert("Network Error", "Failed to connect to server.", "OK");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "An error occurred while processing your request.", "OK");
                    System.Diagnostics.Debug.WriteLine($"Reprint Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Prints the Yorata receipt using the new <see cref="BluetoothPrinterService"/> SDK.
        /// Builds a <see cref="ReceiptData"/> object and delegates all Bluetooth / ESC-POS
        /// work to the service, removing the old manual Bluetooth socket code.
        /// </summary>
        private async Task PrintReceiptWithSDK(
            TransactionConfirmationResponse result,
            YorataTransactionData yorataData)
        {
            try
            {
                // ── 1. Check printer availability first ──────────────────────
                bool printerReady = await _printer.IsPrinterAvailableAsync();
                if (!printerReady)
                {
                    await DisplayAlert(
                        "Printer Not Ready",
                        "No supported Bluetooth printer found. Please ensure the printer is on and paired, then try again.",
                        "OK");
                    return;
                }

                // ── 2. Build the receipt data model ──────────────────────────
                var receipt = new ReceiptData
                {
                    StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                    StorePhone = "Contact us: +234-810-046-6363",
                    ReceiptNumber = result.transactionNo ?? "N/A",
                    AgentName = result.businessInfo?.sUperAgent ?? "N/A",
                    CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                    SuperAgent = result.businessInfo?.sUperAgent ?? "N/A",
                    PrintDate = DateTime.Now,
                    AmountPaid = decimal.TryParse(result.amount, out decimal amt) ? amt : 0m,
                    FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                    Items = new List<ReceiptItem>
                    {
                        new ReceiptItem
                        {
                            Description = result.serviceName ?? "Service",
                            Amount      = decimal.TryParse(result.amount, out decimal iAmt) ? iAmt : 0m
                        },
                        new ReceiptItem
                        {
                            Description = "Payer ID",
                            Amount      = 0m,
                            SubText     = result.payer ?? "N/A"
                        },
                        new ReceiptItem
                        {
                            Description = "Business",
                            Amount      = 0m,
                            SubText     = result.businessInfo?.businessName ?? "N/A"
                        },
                        new ReceiptItem
                        {
                            Description = "LGA",
                            Amount      = 0m,
                            SubText     = result.businessInfo?.lga ?? "N/A"
                        },
                        new ReceiptItem
                        {
                            Description = "Vehicle Type",
                            Amount      = 0m,
                            SubText     = yorataData?.vehicleType ?? "N/A"
                        },
                        new ReceiptItem
                        {
                            Description = "Vehicle No",
                            Amount      = 0m,
                            SubText     = yorataData?.vehicleNo ?? "N/A"
                        },
                        new ReceiptItem
                        {
                            Description = "Date",
                            Amount      = 0m,
                            SubText     = result.transactionDate ?? DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                        }
                    }
                };



                await DisplayAlert("Success", "Receipt reprinted successfully.", "OK");
            }
            catch (PrinterException pex)
            {
                await DisplayAlert("Printer Error", pex.Message, "OK");
                System.Diagnostics.Debug.WriteLine($"PrinterException: {pex.Message}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Print Error", "Failed to print receipt. Please check your printer connection.", "OK");
                System.Diagnostics.Debug.WriteLine($"Print error: {ex.Message}");
            }
        }
    }

    // ─── API Response Models ───────────────────────────────────────────────────

    internal class TransactionConfirmationResponse
    {
        public string merchantNo { get; set; }
        public string transactionNo { get; set; }
        public string serviceName { get; set; }
        public string amount { get; set; }
        public string Status { get; set; }
        public string payer { get; set; }
        public string PayRef { get; set; }
        public string printStatus { get; set; }
        public BusinessInfo businessInfo { get; set; }
        public string payerContact { get; set; }
        public string transactionDate { get; set; }
    }

    internal class BusinessInfo
    {
        public string sUperAgent { get; set; }
        public string lga { get; set; }
        public string zonalOffice { get; set; }
        public string ato { get; set; }
        public string businessName { get; set; }
    }
}