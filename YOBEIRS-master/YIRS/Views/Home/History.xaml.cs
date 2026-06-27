using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage
    {
        // Static properties
        public static string transactionNo { get; set; }
        public static string serviceName { get; set; }
        public static string Status { get; set; }
        public static string payer { get; set; }
        public static string SuperAgent { get; set; }
        public static string BusinessName { get; set; }
        public static string Businesslga { get; set; }
        public static string amount { get; set; }
        public static string payerContact { get; set; }
        public static string transactionDate { get; set; }

        private CancellationTokenSource _cancellationTokenSource;
        private bool _isLoading = false;

        class HistoryData
        {
            public string transactionId { get; set; }
            public string businessName { get; set; }
            public string serviceName { get; set; }
            public string payerId { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }
        }

        class HistoryDataHeaderFooter
        {
            public List<HistoryData> HD { get; set; }
            public string Intro => HD != null && HD.Any()
                ? $"You have performed {HD.Count} transaction{(HD.Count != 1 ? "s" : "")} within your search dates"
                : "No transactions found";
            public string Summary => HD != null && HD.Any()
                ? $"Total: {HD.Count} transaction{(HD.Count != 1 ? "s" : "")}"
                : "No transactions";
            public decimal Size => HD?.Count ?? 0;
        }

        public History()
        {
            InitializeComponent();
            InitializePage();
            TrackUserActivity();
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private void InitializePage()
        {
            try
            {
                endDatePicker.Date = DateTime.Now;
                startDatePicker.Date = DateTime.Now.AddMonths(-1);

                summarySection.IsVisible = false;
                emptyStateView.IsVisible = false;
                listView.IsVisible = false;
            }
            catch (Exception ex)
            {
                LogError("InitializePage", ex);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
            catch (Exception ex)
            {
                LogError("OnDisappearing", ex);
            }
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            if (_isLoading)
            {
                await ShowAlert("Please Wait", "A search is already in progress.");
                return;
            }
            await SearchTransactions();
        }

        private async Task SearchTransactions()
        {
            _isLoading = true;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            SessionManager.Instance.UpdateActivity();

            try
            {
                if (!ValidateDates()) return;

                ShowLoading(true);
                await Task.Delay(300);

                string searchStringFrom = startDatePicker.Date.ToString("MM/dd/yyyy");
                string searchStringTo = endDatePicker.Date.ToString("MM/dd/yyyy");

                if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    await ShowAlert("Error", "User email is not available. Please login again.");
                    return;
                }

                string url = $"https://yobe.osoftpay.net/api/TaskPayers/gettransaction?Email={Uri.EscapeDataString(MainPage.ValidUserMail)}&SearchFrom={searchStringFrom}&SearchTo={searchStringTo}";

                var transactions = await FetchTransactionsAsync(url, _cancellationTokenSource.Token);
                await UpdateUIWithResults(transactions);
            }
            catch (OperationCanceledException)
            {
                LogError("SearchTransactions", new Exception("Operation cancelled"));
            }
            catch (HttpRequestException httpEx)
            {
                await HandleHttpException(httpEx);
            }
            catch (JsonException jsonEx)
            {
                await ShowAlert("Data Error", "Failed to process server response. Please try again later.");
                LogError("SearchTransactions - JSON", jsonEx);
            }
            catch (Exception ex)
            {
                await ShowAlert("Error", "An unexpected error occurred. Please try again.");
                LogError("SearchTransactions", ex);
            }
            finally
            {
                ShowLoading(false);
                _isLoading = false;
            }
        }

        private bool ValidateDates()
        {
            SessionManager.Instance.UpdateActivity();
            try
            {
                if (startDatePicker.Date > endDatePicker.Date)
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await ShowAlert("Invalid Date Range", "Start date cannot be after end date."));
                    return false;
                }

                var dateSpan = endDatePicker.Date - startDatePicker.Date;
                if (dateSpan.TotalDays > 365)
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await ShowAlert("Date Range Too Large", "Please select a date range of one year or less."));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("ValidateDates", ex);
                return false;
            }
        }

        private async Task<List<HistoryData>> FetchTransactionsAsync(string url, CancellationToken cancellationToken)
        {
            using (var httpClientHandler = new HttpClientHandler())
            {
                SessionManager.Instance.UpdateActivity();
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using (var client = new HttpClient(httpClientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                    using (var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"Server returned status code: {response.StatusCode}");

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(json))
                            return new List<HistoryData>();

                        var items = JsonConvert.DeserializeObject<List<HistoryData>>(json);
                        return items ?? new List<HistoryData>();
                    }
                }
            }
        }

        private async Task UpdateUIWithResults(List<HistoryData> transactions)
        {
            await Device.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    if (transactions == null || !transactions.Any())
                    {
                        listView.IsVisible = false;
                        summarySection.IsVisible = false;
                        emptyStateView.IsVisible = true;
                        BindingContext = null;
                    }
                    else
                    {
                        emptyStateView.IsVisible = false;
                        listView.IsVisible = true;
                        summarySection.IsVisible = true;

                        var dataContext = new HistoryDataHeaderFooter { HD = transactions };
                        BindingContext = dataContext;

                        // Update the rich summary panel
                        UpdateTransactionSummaryPanel(transactions);
                    }
                }
                catch (Exception ex)
                {
                    LogError("UpdateUIWithResults", ex);
                    throw;
                }
            });
        }

        /// <summary>
        /// Populates the transaction summary panel with count, total amount and date range.
        /// </summary>
        private void UpdateTransactionSummaryPanel(List<HistoryData> transactions)
        {
            try
            {
                int count = transactions?.Count ?? 0;
                decimal totalAmount = transactions?.Sum(t => t.amount) ?? 0m;
                string dateFrom = startDatePicker.Date.ToString("MMM dd");
                string dateTo = endDatePicker.Date.ToString("MMM dd, yyyy");

                // summaryLabel is the simple badge label already in XAML
                summaryLabel.Text = $"{count} transaction{(count != 1 ? "s" : "")} found";

                // If the XAML has extended summary labels, populate them (see XAML update)
                if (summaryCountLabel != null)
                    summaryCountLabel.Text = count.ToString();
                if (summaryAmountLabel != null)
                    summaryAmountLabel.Text = $"₦{totalAmount:N2}";
                if (summaryRangeLabel != null)
                    summaryRangeLabel.Text = $"{dateFrom} – {dateTo}";
            }
            catch (Exception ex)
            {
                LogError("UpdateTransactionSummaryPanel", ex);
            }
        }

        private void ShowLoading(bool isLoading)
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    loadingOverlay.IsVisible = isLoading;
                    loadingOverlay.InputTransparent = !isLoading;
                });
            }
            catch (Exception ex)
            {
                LogError("ShowLoading", ex);
            }
        }

        private async Task HandleHttpException(HttpRequestException httpEx)
        {
            string message;

            if (httpEx.Message.Contains("Name or service not known") || httpEx.Message.Contains("No such host is known"))
                message = "Unable to connect to the server. Please check your internet connection.";
            else if (httpEx.Message.Contains("timeout") || httpEx.Message.Contains("timed out"))
                message = "Connection timed out. Please check your internet connection and try again.";
            else if (httpEx.Message.Contains("401"))
                message = "Authentication failed. Please login again.";
            else if (httpEx.Message.Contains("403"))
                message = "Access denied. You don't have permission to view this data.";
            else if (httpEx.Message.Contains("404"))
                message = "Service not found. Please contact support.";
            else if (httpEx.Message.Contains("500") || httpEx.Message.Contains("503"))
                message = "Server error. Please try again later.";
            else
                message = "Network error occurred. Please check your connection and try again.";

            await ShowAlert("Connection Error", message);
            LogError("HandleHttpException", httpEx);
        }

        private async Task ShowAlert(string title, string message)
        {
            try
            {
                await Device.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert(title, message, "OK"));
            }
            catch (Exception ex)
            {
                LogError("ShowAlert", ex);
            }
        }

        private void LogError(string method, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {method}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[STACK TRACE] {ex.StackTrace}");
                if (ex.InnerException != null)
                    System.Diagnostics.Debug.WriteLine($"[INNER EXCEPTION] {ex.InnerException.Message}");
            }
            catch { }
        }
    }
}