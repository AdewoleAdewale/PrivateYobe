using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Yorata_Ops
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class VerifyInvoice : ContentPage
    {
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const string API_BASE_URL = "https://yobe.osoftpay.net/api/KekeTransactions/Verify";

        private TransactionResponse _currentTransaction;

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        public VerifyInvoice()
        {
            InitializeComponent();
            TrackUserActivity();
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            Content.GestureRecognizers.Add(tapGesture);
        }

        private async void OnVerifyClicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (string.IsNullOrWhiteSpace(InvoiceEntry.Text))
            {
                await DisplayAlert("Input Required", "Please enter an invoice number", "OK");
                return;
            }

            await VerifyTransaction(InvoiceEntry.Text.Trim());
        }

        private async Task VerifyTransaction(string invoiceNo)
        {
            try
            {
                SetLoadingState(true);
                ResultCard.IsVisible = false;

                var response = await FetchTransactionStatus(invoiceNo);

                if (response == null)
                {
                    await ShowErrorState("Unable to verify transaction. Please check your connection and try again.");
                    return;
                }

                _currentTransaction = response;
                await DisplayTransactionResult(response);
            }
            catch (HttpRequestException ex)
            {
                LogError(ex, "Network error during verification");
                await ShowErrorState("Network error. Please check your internet connection.");
            }
            catch (TaskCanceledException ex)
            {
                LogError(ex, "Request timeout");
                await ShowErrorState("Request timed out. Please try again.");
            }
            catch (Exception ex)
            {
                LogError(ex, "Verification failed");
                await ShowErrorState("An unexpected error occurred. Please try again.");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async Task<TransactionResponse> FetchTransactionStatus(string invoiceNo)
        {
            var url = $"{API_BASE_URL}?InvoiceNo={invoiceNo}";

            using (var httpClientHandler = new HttpClientHandler())
            {
                httpClientHandler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true;

                using (var client = new HttpClient(httpClientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var response = await client.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        LogError(new Exception($"API returned status code: {response.StatusCode}"),
                            "API request failed");
                        return null;
                    }

                    var json = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        LogError(new Exception("Empty response from API"), "Empty API response");
                        return null;
                    }

                    return JsonConvert.DeserializeObject<TransactionResponse>(json);
                }
            }
        }

        private async Task DisplayTransactionResult(TransactionResponse response)
        {
            ResultCard.IsVisible = true;

            InvoiceLabel.Text = response.Invoice ?? "N/A";
            AmountLabel.Text = $"₦{response.Amount:N2}";
            DateLabel.Text = DateTime.Now.ToString("MMM dd, yyyy HH:mm");

            switch (response.Status)
            {
                case "00":
                    SetSuccessState(response);
                    break;
                case "02":
                    SetPendingState(response);
                    break;
                default:
                    SetFailedState(response);
                    break;
            }

            await ResultCard.FadeTo(1, 300, Easing.CubicOut);
        }

        private void SetSuccessState(TransactionResponse response)
        {
            StatusHeader.BackgroundColor = Color.FromHex("#10B981");
            StatusIconFrame.BackgroundColor = Color.White;
            StatusIcon.Text = "✓";
            StatusIcon.TextColor = Color.FromHex("#10B981");
            StatusLabel.Text = "Transaction Successful";
            StatusLabel.TextColor = Color.White;
            StatusMessage.Text = response.Message ?? "Payment completed successfully";
            StatusMessage.TextColor = Color.White;

            PrintButton.IsVisible = true;
            RetryButton.IsVisible = false;
        }

        private void SetPendingState(TransactionResponse response)
        {
            StatusHeader.BackgroundColor = Color.FromHex("#F59E0B");
            StatusIconFrame.BackgroundColor = Color.White;
            StatusIcon.Text = "⏳";
            StatusIcon.TextColor = Color.FromHex("#F59E0B");
            StatusLabel.Text = "Transaction Pending";
            StatusLabel.TextColor = Color.White;
            StatusMessage.Text = response.Message ?? "Transaction is being processed";
            StatusMessage.TextColor = Color.White;

            PrintButton.IsVisible = false;
            RetryButton.IsVisible = true;
        }

        private void SetFailedState(TransactionResponse response)
        {
            StatusHeader.BackgroundColor = Color.FromHex("#EF4444");
            StatusIconFrame.BackgroundColor = Color.White;
            StatusIcon.Text = "✕";
            StatusIcon.TextColor = Color.FromHex("#EF4444");
            StatusLabel.Text = "Transaction Failed";
            StatusLabel.TextColor = Color.White;
            StatusMessage.Text = response.Message ?? "Transaction could not be completed";
            StatusMessage.TextColor = Color.White;

            PrintButton.IsVisible = false;
            RetryButton.IsVisible = true;
        }

        private async Task ShowErrorState(string message)
        {
            await DisplayAlert("Verification Error", message, "OK");
            SetLoadingState(false);
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingIndicator.IsVisible = isLoading;
            LoadingIndicator.IsRunning = isLoading;
            VerifyButton.IsEnabled = !isLoading;
            VerifyButton.Opacity = isLoading ? 0.6 : 1.0;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PRINT — new SDK with logo + QR barcode
        // ══════════════════════════════════════════════════════════════════════

        private async void OnPrintClicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (_currentTransaction == null || _currentTransaction.Status != "00")
            {
                await DisplayAlert("Print Error",
                    "Cannot print receipt for unsuccessful transactions", "OK");
                return;
            }

            try
            {
                var receipt = BuildReceiptData(_currentTransaction);
                await CallPrinterAsync(receipt);
            }
            catch (Exception ex)
            {
                LogError(ex, "Print failed");
                await DisplayAlert("Print Error",
                    "Failed to print receipt. Transaction was successful and is saved in history.", "OK");
            }
        }

        // ── Build ReceiptData (logo + QR barcode via BarcodeLabel) ──────────

        private ReceiptData BuildReceiptData(TransactionResponse transaction)
        {
            // Full URL that the QR code will open when scanned
            string verifyUrl =
                $"https://yobe.osoftpay.net/singlecollections/verify" +
                $"?TransactId={Uri.EscapeDataString(transaction.Invoice ?? string.Empty)}";

            var items = new System.Collections.Generic.List<ReceiptItem>
            {
                new ReceiptItem
                {
                    Description = "Invoice No.",
                    Amount      = 0m,
                    SubText     = transaction.Invoice ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "Status",
                    Amount      = 0m,
                    SubText     = "SUCCESSFUL"
                },
                new ReceiptItem
                {
                    Description = "Amount",
                    Amount      = transaction.Amount,
                    SubText     = null
                }
            };

            return new ReceiptData
            {
                // StoreName → printed as bold header; uses app config if available
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: +234-810-046-6363",
                ReceiptNumber = transaction.Invoice ?? "N/A",
                AgentName = MainPage.Name ?? "N/A",
                PrintDate = DateTime.Now,
                Items = items,
                FooterLine1 = "Thank you for using our service",
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",

                BarcodeLabel = verifyUrl
            };
        }

        // ── Delegate to BluetoothPrinterService SDK ──────────────────────────

        private async Task CallPrinterAsync(ReceiptData receipt)
        {
            try
            {
                bool btGranted = await BluetoothPermissionHelper.RequestAsync();
                if (!btGranted)
                {
                    await DisplayAlert("Bluetooth Permission",
                        "Bluetooth permission is required to print.\n\n" +
                        "On Android 12+: App Settings → Permissions → Nearby devices → Allow.\n" +
                        "On older Android: allow Location permission.",
                        "OK");
                    return;
                }

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    await _printer.PrintReceiptAsync(
                        receipt,
                        logoAssetName: "Logo.png",   // printed above the header
                        watermarkText: "YIRS",
                        cancellationToken: cts.Token);
                }

                await DisplayAlert("Success", "Receipt printed successfully.", "OK");
            }
            catch (PrinterException pex)
            {
                LogError(pex, "PrinterException in VerifyInvoice");
                await DisplayAlert("Printer Error", pex.Message, "OK");
            }
            catch (OperationCanceledException)
            {
                await DisplayAlert("Timeout",
                    "Printer did not respond in time. Ensure it is on and within range.", "OK");
            }
            catch (Exception ex)
            {
                LogError(ex, "Print error in VerifyInvoice");
                await DisplayAlert("Printer Error",
                    "Failed to print receipt. Transaction was successful and is saved in history.", "OK");
            }
        }

        private void LogError(Exception ex, string context)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {context}: {ex?.Message}");
            System.Diagnostics.Debug.WriteLine($"[STACK] {ex?.StackTrace}");
        }

        // ── Response model ────────────────────────────────────────────────────

        private class TransactionResponse
        {
            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("invoice")]
            public string Invoice { get; set; }

            [JsonProperty("amount")]
            public decimal Amount { get; set; }
        }
    }
}