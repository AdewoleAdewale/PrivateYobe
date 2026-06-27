using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Renderers;
using YIRS.Services;

namespace YIRS.Views.Yorata_Ops
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Payment : ContentPage
    {
        private INotificationManager notificationManager;
        private int notificationNumber = 0;
        public StackLayout stackLayout;

        private bool _isVerifying = false;
        private bool _isPrinting = false;
        public bool DoesBusinessExist { get; set; }
        public string BusinessNames { get; set; }
        public string BusinessLGA { get; set; }
        private List<VCatData> VList = new List<VCatData>();
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private bool isProcessing = false;
        private bool isPageInitialized = false;
        private SemaphoreSlim processingLock = new SemaphoreSlim(1, 1);

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        // ── Holds last successful transaction for re-print / sheet binding ─────
        private SuccessReceiptModel _lastReceipt;

        public Payment()
        {
            try
            {
                InitializeComponent();
                InitializeNotificationManager();
                ConfigurePageBasedOnCategory();
                InitializeServiceDetails();
                TrackUserActivity();
                isPageInitialized = true;
            }
            catch (Exception ex)
            {
                HandleCriticalException(ex, "Failed to initialize payment page");
            }
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private void InitializeNotificationManager()
        {
            try
            {
                notificationManager = DependencyService.Get<INotificationManager>();
                if (notificationManager != null)
                    notificationManager.NotificationReceived += OnNotificationReceived;
                else
                    System.Diagnostics.Debug.WriteLine("Warning: Notification manager not available");
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to initialize notification manager");
            }
        }

        private void OnNotificationReceived(object sender, EventArgs eventArgs)
        {
            try
            {
                if (eventArgs is NotificationEventArgs evtData)
                    ShowError(evtData.Title ?? "Notification");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error handling notification");
            }
        }

        private void ConfigurePageBasedOnCategory()
        {
            try
            {
                bool isYorotaCategory = !string.IsNullOrEmpty(MainPage.Category) && MainPage.Category != "Default";

                if (VehicleCategoryStack != null)
                    VehicleCategoryStack.IsVisible = isYorotaCategory;

                if (stackIdname != null)
                    stackIdname.Text = isYorotaCategory ? "VEHICLE NUMBER" : "PAYER ID (Optional)";

                if (PayerId != null)
                    PayerId.Placeholder = isYorotaCategory ? "Enter Vehicle Number" : "Enter Payer ID";

                if (isYorotaCategory)
                    _ = LoadVehicleCategoriesAsync();
            }
            catch (Exception ex)
            {
                HandleException(ex, "Failed to configure page");
            }
        }

        private HttpClient CreateHttpClient()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };

                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
                };

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

                return client;
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to create HTTP client");
                return new HttpClient { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) };
            }
        }

        private void InitializeServiceDetails()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (ServiceName != null)
                    ServiceName.Text = ServiceList.ServiceName ?? "Service Name Not Available";

                if (ServiceDescription != null)
                    ServiceDescription.Text = ServiceList.ServiceDescription ?? "No description available";

                if (ServiceAmount != null)
                {
                    decimal serviceAmount = ServiceList.ServiceAmount;
                    if (serviceAmount > 0)
                    {
                        ServiceAmount.Text = serviceAmount.ToString("N0");
                        ServiceAmount.IsEnabled = false;
                    }
                    else
                    {
                        ServiceAmount.Text = "0";
                        ServiceAmount.IsEnabled = true;
                    }
                }

                if (sheetBehavior != null)
                    sheetBehavior.IsOpen = false;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Failed to initialize service details");
            }
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (isProcessing)
            {
                ShowError("A transaction is already in progress. Please wait.");
                return;
            }

            if (!await processingLock.WaitAsync(0))
            {
                ShowError("Please wait for the current operation to complete.");
                return;
            }

            try
            {
                if (!ValidateInputs()) return;

                isProcessing = true;
                SetProcessingState(true);

                using (UserDialogs.Instance.Loading("Connecting to Service, Please Wait...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(500);
                    await ProcessPayment();
                }
            }
            catch (TaskCanceledException)
            {
                await DisplayAlert("Timeout", "The request took too long. Please check your connection and try again.", "OK");
            }
            catch (HttpRequestException ex)
            {
                await HandlePaymentException(ex, "Network error occurred");
            }
            catch (Exception ex)
            {
                await HandlePaymentException(ex, "An unexpected error occurred");
            }
            finally
            {
                isProcessing = false;
                SetProcessingState(false);
                processingLock.Release();
            }
        }

        private bool ValidateInputs()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(PIN?.Text) || PIN.Text.Length != 4)
                {
                    ShowError("Please enter a valid 4-digit PIN");
                    PIN?.Focus();
                    return false;
                }

                if (!int.TryParse(PIN.Text, out _))
                {
                    ShowError("PIN must contain only numbers");
                    PIN?.Focus();
                    return false;
                }

                string amountText = ServiceAmount?.Text?.Replace(",", "").Replace("₦", "").Trim();
                if (string.IsNullOrWhiteSpace(amountText) ||
                    !decimal.TryParse(amountText, out decimal amount) || amount <= 0)
                {
                    ShowError("Please enter a valid amount greater than zero");
                    ServiceAmount?.Focus();
                    return false;
                }

                bool isYorotaCategory = !string.IsNullOrEmpty(MainPage.Category) && MainPage.Category != "Default";

                if (isYorotaCategory)
                {
                    if (string.IsNullOrWhiteSpace(PayerId?.Text))
                    {
                        ShowError("Vehicle Number is required for Yorota payment");
                        PayerId?.Focus();
                        return false;
                    }

                    if (VPicker?.SelectedItem == null)
                    {
                        ShowError("Please select a vehicle category");
                        return false;
                    }

                    var selectedCategory = VPicker.SelectedItem as VCatData;
                    if (selectedCategory == null || string.IsNullOrWhiteSpace(selectedCategory.vehicleName))
                    {
                        ShowError("Invalid vehicle category selected. Please try again.");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Validation failed");
                return false;
            }
        }

        private async Task ProcessPayment()
        {
            SessionManager.Instance.UpdateActivity();
            try
            {
                bool isYorotaCategory = !string.IsNullOrEmpty(MainPage.Category) && MainPage.Category != "Default";

                if (isYorotaCategory)
                    await ProcessYorotaPayment();
                else
                    await ProcessDefaultPayment();
            }
            catch (Exception ex)
            {
                throw new Exception("Payment processing failed", ex);
            }
        }

        private async Task ProcessDefaultPayment()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                string url = "https://yobe.osoftpay.net/api/SingleCollections/NewCollect";
                string cleanAmount = ServiceAmount?.Text?.Replace(",", "").Replace("₦", "").Trim() ?? "0";

                var nvc = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("ServiceName", ServiceName?.Text ?? ""),
                    new KeyValuePair<string, string>("Amount",      cleanAmount),
                    new KeyValuePair<string, string>("Email",       MainPage.ValidUserMail ?? ""),
                    new KeyValuePair<string, string>("Pin",         PIN?.Text ?? ""),
                    new KeyValuePair<string, string>("Payer",       PayerId?.Text ?? "")
                };

                var response = await SendPaymentRequest(url, nvc);

                if (response != null && response.RespondCode == "00")
                    await HandleSuccessfulPayment(response, false);
                else
                    await HandleFailedPayment(response?.Message ?? "Transaction failed. Please try again.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Default payment processing failed: {ex.Message}", ex);
            }
        }

        private async Task ProcessYorotaPayment()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                var selectedV = VPicker?.SelectedItem as VCatData;
                if (selectedV == null || string.IsNullOrWhiteSpace(selectedV.vehicleName))
                    throw new InvalidOperationException("No valid vehicle category selected");

                string url = "https://yobe.osoftpay.net/Api/SingleCollections/YorotaCollection";
                string cleanAmount = ServiceAmount?.Text?.Replace(",", "").Replace("₦", "").Trim() ?? "0";

                var nvc = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("ServiceName", ServiceName?.Text ?? ""),
                    new KeyValuePair<string, string>("Amount",      cleanAmount),
                    new KeyValuePair<string, string>("Email",       MainPage.ValidUserMail ?? ""),
                    new KeyValuePair<string, string>("Pin",         PIN?.Text ?? ""),
                    new KeyValuePair<string, string>("VehicleType", selectedV.vehicleName),
                    new KeyValuePair<string, string>("VehicleNo",   PayerId?.Text ?? "")
                };

                var response = await SendPaymentRequest(url, nvc);

                if (response != null && response.RespondCode == "00")
                    await HandleSuccessfulPayment(response, true, selectedV.vehicleName);
                else
                    await HandleFailedPayment(response?.Message ?? "Transaction failed");
            }
            catch (Exception ex)
            {
                throw new Exception($"Yorota payment processing failed: {ex.Message}", ex);
            }
        }

        private async Task<StateCollectionResponseObject> SendPaymentRequest(
            string url, List<KeyValuePair<string, string>> parameters)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be empty", nameof(url));

            if (parameters == null || parameters.Count == 0)
                throw new ArgumentException("Parameters cannot be empty", nameof(parameters));

            using (var client = CreateHttpClient())
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new FormUrlEncodedContent(parameters)
                    };

                    var res = await client.SendAsync(req).ConfigureAwait(false);
                    var resultString = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(resultString))
                        throw new Exception("Empty response from server");

                    var response = JsonConvert.DeserializeObject<StateCollectionResponseObject>(resultString);
                    if (response == null)
                        throw new Exception("Failed to parse server response");

                    return response;
                }
                catch (TaskCanceledException)
                {
                    throw new TimeoutException("Request timed out. Please check your connection.");
                }
                catch (HttpRequestException ex)
                {
                    throw new HttpRequestException($"Network error: {ex.Message}", ex);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SUCCESS HANDLER — sophisticated sheet popup + new SDK print
        // ══════════════════════════════════════════════════════════════════════

        private async Task HandleSuccessfulPayment(
            StateCollectionResponseObject response,
            bool isYorota,
            string vehicleType = null)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                string txnNo = response.TransactionNo ?? "N/A";
                string cleanAmount = ServiceAmount?.Text?.Replace(",", "").Replace("₦", "").Trim() ?? "0";
                decimal amount = decimal.TryParse(cleanAmount, out decimal a) ? a : 0m;

                // Build receipt data for both the popup and the printer
                var receipt = BuildReceiptData(cleanAmount, txnNo, PayerId?.Text, isYorota, vehicleType);
                _lastReceipt = new SuccessReceiptModel
                {
                    TransactionNo = txnNo,
                    AgentName = MainPage.Name ?? "N/A",
                    ServiceName = ServiceName?.Text ?? "N/A",
                    VehicleType = vehicleType ?? "N/A",
                    VehicleNo = PayerId?.Text ?? "N/A",
                    Amount = amount,
                    FormattedAmount = $"₦{amount:N2}",
                    DateFormatted = DateTime.Now.ToString("dd MMM yyyy  HH:mm"),
                    CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                    VerifyUrl = receipt.BarcodeLabel
                };

                // Show animated success sheet on main thread
                await Device.InvokeOnMainThreadAsync(() =>
                {
                    BindSuccessSheet(_lastReceipt);
                    OpenSuccessSheet();
                });

                // Send notification
                try
                {
                    notificationNumber++;
                    string identifier = isYorota
                        ? $"Vehicle: {PayerId?.Text}"
                        : (!string.IsNullOrEmpty(PayerId?.Text) ? $"Payer ID: {PayerId.Text}" : ServiceName?.Text);

                    notificationManager?.SendNotification(
                        $"YIRS – {identifier}",
                        $"Payment successful! Ref: {txnNo}. Check Payment History.",
                        DateTime.Now.AddSeconds(5));
                }
                catch (Exception notifEx)
                {
                    LogError(notifEx, "Failed to send notification");
                }

                // Print receipt using new SDK (fire-and-forget with toast on error)
                await CallPrinterAsync(receipt);
            }
            catch (Exception ex)
            {
                LogError(ex, "Error handling successful payment");
            }
        }

        /// <summary>
        /// Binds the success sheet BindingContext to the receipt model so
        /// the XAML labels/barcode control update automatically.
        /// </summary>
        private void BindSuccessSheet(SuccessReceiptModel model)
        {
            try
            {
                if (sheetBehavior != null)
                    sheetBehavior.BindingContext = model;
            }
            catch (Exception ex)
            {
                LogError(ex, "BindSuccessSheet error");
            }
        }

        /// <summary>
        /// Opens the bottom sheet with a subtle bounce animation.
        /// </summary>
        private void OpenSuccessSheet()
        {
            try
            {
                if (sheetBehavior != null)
                {
                    sheetBehavior.IsOpen = true;

                }
            }
            catch (Exception ex)
            {
                LogError(ex, "OpenSuccessSheet error");
            }
        }

        /// <summary>
        /// Bouncy scale animation on the success check-mark icon element (if present).
        /// The XAML element should be named SuccessIconFrame.
        /// </summary>


        // ══════════════════════════════════════════════════════════════════════
        //  RECEIPT DATA BUILDER — same structure as Kaduna Payment.cs
        // ══════════════════════════════════════════════════════════════════════

        private ReceiptData BuildReceiptData(
            string cleanAmount,
            string transactionNo,
            string payerOrVehicle,
            bool isYorota = true,
            string vehicleType = null)
        {
            decimal amt = decimal.TryParse(cleanAmount, out decimal a) ? a : 0m;

            // Verification URL encoded into the QR barcode
            string verifyUrl =
                $"https://yobe.osoftpay.net/singlecollections/verify" +
                $"?TransactId={Uri.EscapeDataString(transactionNo)}";

            var items = new List<ReceiptItem>();

            if (isYorota)
            {
                items.Add(new ReceiptItem
                {
                    Description = "Vehicle Type",
                    Amount = 0m,
                    SubText = vehicleType ?? "N/A"
                });
                items.Add(new ReceiptItem
                {
                    Description = "Vehicle No.",
                    Amount = 0m,
                    SubText = payerOrVehicle ?? "N/A"
                });
                items.Add(new ReceiptItem
                {
                    Description = ServiceName?.Text ?? "Service",
                    Amount = amt,
                    SubText = null
                });
            }
            else
            {
                items.Add(new ReceiptItem
                {
                    Description = ServiceName?.Text ?? "Service",
                    Amount = amt,
                    SubText = string.IsNullOrWhiteSpace(payerOrVehicle)
                                    ? null
                                    : $"Payer ID: {payerOrVehicle}"
                });

                if (!string.IsNullOrWhiteSpace(BusinessNames))
                    items.Add(new ReceiptItem
                    {
                        Description = "Business Name",
                        Amount = 0m,
                        SubText = BusinessNames
                    });

                if (!string.IsNullOrWhiteSpace(BusinessLGA))
                    items.Add(new ReceiptItem
                    {
                        Description = "LGA",
                        Amount = 0m,
                        SubText = BusinessLGA
                    });
            }

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: 09070701616,07017639494",
                ReceiptNumber = transactionNo,
                AgentName = MainPage.Name ?? "N/A",
                CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                SuperAgent = MainPage.Super_Agent ?? string.Empty,
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = amt,
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = verifyUrl   // → rendered as QR code on receipt
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PRINTER — NEW SDK (mirrors Kaduna Payment.cs CallPrinterAsync)
        // ══════════════════════════════════════════════════════════════════════

        private static Task<bool> RequestBluetoothConnectPermissionAsync_Shared()
               => BluetoothPermissionHelper.RequestAsync();

        private async Task<bool> CallPrinterAsync(ReceiptData receipt)   // ← return Task<bool>
        {
            if (_isPrinting)
            {
                System.Diagnostics.Debug.WriteLine("[Payment] Print already in progress – skipped.");
                return false;                                             // ← return false, not return
            }
            _isPrinting = true;

            try
            {
                if (!await RequestBluetoothConnectPermissionAsync_Shared())
                {
                    UserDialogs.Instance.Toast("Bluetooth permission denied.", TimeSpan.FromSeconds(6));
                    return false;                                         // ← return false
                }

                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.ChunkStarted:
                                ShowMessage(string.Format("Printing {0}…", p.ChunkName),
                                    "🖨️", Color.FromHex("#E3F2FD"), Color.Blue);
                                break;
                            case PrintProgressStatus.ChunkRetrying:
                                ShowMessage(string.Format("Reconnecting… retrying {0} (#{1})",
                                    p.ChunkName, p.AttemptNumber),
                                    "🔄", Color.FromHex("#FFF8E1"), Color.DarkOrange);
                                break;
                            case PrintProgressStatus.SessionCompleted:
                                ShowSuccessMessage("Receipt printed.");
                                break;
                            case PrintProgressStatus.ChunkFailed:
                                ShowErrorMessage(string.Format("Could not print {0}.", p.ChunkName));
                                break;
                        }
                    }));

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                    await App.PrintJobManager.DeleteJobAsync(job.JobId);
                    return true;                                          // ← success
                }
                catch (PrinterException pex)
                {
                    UserDialogs.Instance.Toast(string.Format("Print failed: {0}", pex.Message),
                        TimeSpan.FromSeconds(8));
                    return false;
                }
                catch (OperationCanceledException)
                {
                    UserDialogs.Instance.Toast("Print timed out. Will retry when printer is available.",
                        TimeSpan.FromSeconds(6));
                    return false;
                }
                catch (Exception ex)
                {
                    UserDialogs.Instance.Toast("Printer not connected. Transaction was successful.",
                        TimeSpan.FromSeconds(6));
                    System.Diagnostics.Debug.WriteLine(string.Format("[Payment] {0}", ex));
                    return false;
                }
                finally
                {
                    cts.Dispose();
                }
            }
            finally
            {
                _isPrinting = false;
            }
        }

        private void HandleException(Exception ex, string context)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("Error in {0}: {1}", context, ex.Message));
                System.Diagnostics.Debug.WriteLine(
                    string.Format("Stack trace: {0}", ex.StackTrace));

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        ShowErrorMessage("An unexpected error occurred. Please try again.");
                        await ShowToast("An unexpected error occurred. Please try again.", false);
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to show error message to user");
                    }
                });
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("Failed to handle exception properly");
            }
        }

        private async Task ShowToast(string message, bool isSuccess)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    string title = isSuccess ? "Success" : "Error";
                    await DisplayAlert(title, message, "OK");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("Error showing toast: {0}. Original message: {1}",
                        ex.Message, message));
            }
        }

        private void ShowErrorMessage(string message)
        {
            try { ShowMessage(message, "❌", Color.FromHex("#FFE6E6"), Color.Red); }
            catch (Exception ex) { HandleException(ex, "Error showing error message"); }
        }
        private void ShowSuccessMessage(string message)
        {
            try { ShowMessage(message, "✅", Color.FromHex("#E8F5E8"), Color.ForestGreen); }
            catch (Exception ex) { HandleException(ex, "Error showing success message"); }
        }

        private void ShowMessage(string message, string icon, Color backgroundColor, Color textColor)
        {
            try
            {
                MessageContainer.IsVisible = true;
                MessageFrame.BackgroundColor = backgroundColor;
                MessageIcon.Text = icon;
                MessageIcon.TextColor = textColor;
                MessageLabel.Text = message;
                MessageLabel.TextColor = textColor;
            }
            catch (Exception ex) { HandleException(ex, "Error showing message"); }
        }
        /// <summary>
        /// Re-print button handler on the success sheet.
        /// Bound in XAML: TapGestureRecognizer on a "PRINT" button inside the sheet.
        /// </summary>
        private async void OnReprintTapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (_lastReceipt == null)
            {
                ShowError("No receipt available to re-print.");
                return;
            }

            var receipt = BuildReceiptData(
                _lastReceipt.Amount.ToString("F2"),
                _lastReceipt.TransactionNo,
                _lastReceipt.VehicleNo,
                isYorota: !string.IsNullOrEmpty(MainPage.Category) && MainPage.Category != "Default",
                vehicleType: _lastReceipt.VehicleType);

            using (UserDialogs.Instance.Loading("Re-printing...", null, null, true, MaskType.Black))
            {
                await CallPrinterAsync(receipt);
            }
        }

        private async Task HandleFailedPayment(string message)
        {
            try
            {
                string displayMessage = string.IsNullOrWhiteSpace(message)
                    ? "Transaction failed. Please try again."
                    : message;

                await Device.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert("Transaction Failed", displayMessage, "OK"));
            }
            catch (Exception ex)
            {
                LogError(ex, "Error handling failed payment");
            }
        }

        private async Task HandlePaymentException(Exception ex, string userMessage = "An error occurred")
        {
            LogError(ex, "Payment exception");

            await Device.InvokeOnMainThreadAsync(async () =>
            {
                string message = ex is TimeoutException
                    ? "The request timed out. Please check your connection and try again."
                    : ex is HttpRequestException
                    ? "Network error occurred. Transaction might be successful. Please check Payment History to confirm."
                    : $"{userMessage}. Please try again or contact support if the issue persists.";

                await DisplayAlert("NOTIFICATION", message, "OKAY");
            });
        }

        private async Task LoadVehicleCategoriesAsync()
        {
            SessionManager.Instance.UpdateActivity();
            if (VPicker == null || VPickerLoader == null)
            {
                LogError(new InvalidOperationException("Picker controls not initialized"), "Cannot load categories");
                return;
            }

            Device.BeginInvokeOnMainThread(() =>
            {
                VPickerLoader.IsRunning = true;
                VPickerLoader.IsVisible = true;
                VPicker.IsEnabled = false;
            });

            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS);

                    var response = await client.GetAsync("https://yobe.osoftpay.net/Api/HulageVehicles/VehicleTypes");

                    if (response == null) throw new Exception("No response from server");
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"API returned status code: {response.StatusCode}");

                    var json = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(json)) throw new Exception("Empty response from server");

                    var categories = JsonConvert.DeserializeObject<List<VCatData>>(json);
                    if (categories == null) throw new Exception("Failed to parse vehicle categories");

                    VList = categories
                        .Where(v => v != null && !string.IsNullOrWhiteSpace(v.vehicleName))
                        .ToList();

                    if (VList.Count == 0) throw new Exception("No valid vehicle categories found");

                    Device.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            VPicker.ItemDisplayBinding = new Binding("vehicleName");
                            VPicker.ItemsSource = VList;
                            VPicker.IsEnabled = true;
                        }
                        catch (Exception ex) { LogError(ex, "Error binding picker data"); }
                    });
                }
            }
            catch (TaskCanceledException)
            {
                Device.BeginInvokeOnMainThread(() =>
                    ShowError("Request timed out while loading vehicle categories."));
            }
            catch (HttpRequestException ex)
            {
                LogError(ex, "Network error loading categories");
                Device.BeginInvokeOnMainThread(() =>
                    ShowError("Network error. Please check your internet connection."));
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to load vehicle categories");
                Device.BeginInvokeOnMainThread(() =>
                    ShowError("Failed to load vehicle categories. Please restart the app."));
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (VPickerLoader != null)
                    {
                        VPickerLoader.IsRunning = false;
                        VPickerLoader.IsVisible = false;
                    }
                });
            }
        }

        private void SetProcessingState(bool processing)
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (ProcessingIndicator != null)
                    {
                        ProcessingIndicator.IsRunning = processing;
                        ProcessingIndicator.IsVisible = processing;
                    }
                    if (PaymentButtonLabel != null) PaymentButtonLabel.IsVisible = !processing;
                    if (ServiceAmount != null) ServiceAmount.IsEnabled = !processing;
                    if (PayerId != null) PayerId.IsEnabled = !processing;
                    if (VPicker != null) VPicker.IsEnabled = !processing;
                    if (PIN != null) PIN.IsEnabled = !processing;
                });
            }
            catch (Exception ex)
            {
                LogError(ex, "Error setting processing state");
            }
        }


        private void HandleCriticalException(Exception ex, string userMessage)
        {
            LogError(ex, $"CRITICAL: {userMessage}");
            Device.BeginInvokeOnMainThread(async () =>
                await DisplayAlert("Critical Error", $"{userMessage}. Please restart the application.", "OK"));
        }

        private void LogError(Exception ex, string message)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}");
                if (ex != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                        System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }
            catch { }
        }

        private void ShowError(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.Toast(new ToastConfig(message)
                    {
                        Duration = TimeSpan.FromSeconds(4),
                        BackgroundColor = System.Drawing.Color.FromArgb(231, 76, 60),
                        MessageTextColor = System.Drawing.Color.White,
                        Position = ToastPosition.Bottom
                    });
                });
            }
            catch (Exception ex)
            {
                LogError(ex, "Error showing error toast");
                try
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await DisplayAlert("Error", message, "OK"));
                }
                catch { }
            }
        }

        private void sheetBehavior_ActionClicked(object sender, EventArgs e)
        {
            try
            {
                if (sheetBehavior != null) sheetBehavior.IsOpen = false;
                ClearFormFields();
            }
            catch (Exception ex) { LogError(ex, "Error closing sheet"); }
        }

        private void Button_Clicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            try
            {
                if (sheetBehavior != null) sheetBehavior.IsOpen = false;
                ClearFormFields();
            }
            catch (Exception ex) { LogError(ex, "Error in button click"); }
        }

        private void ClearFormFields()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (PayerId != null) PayerId.Text = string.Empty;
                    if (PIN != null) PIN.Text = string.Empty;
                    if (VPicker != null) VPicker.SelectedItem = null;
                    if (ServiceAmount?.IsEnabled == true) ServiceAmount.Text = string.Empty;
                });
            }
            catch (Exception ex) { LogError(ex, "Error clearing form fields"); }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (isPageInitialized) InitializeServiceDetails();
            }
            catch (Exception ex) { LogError(ex, "OnAppearing error"); }
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
                if (notificationManager != null)
                    notificationManager.NotificationReceived -= OnNotificationReceived;
                processingLock?.Dispose();
            }
            catch (Exception ex) { LogError(ex, "Error in OnDisappearing"); }
        }

        public class InverseBoolConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                => value is bool b && !b;

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => value is bool b && !b;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  SUCCESS RECEIPT MODEL — bound to the sophisticated success sheet in XAML
    // ══════════════════════════════════════════════════════════════════════════

    public class SuccessReceiptModel
    {
        public string TransactionNo { get; set; }
        public string AgentName { get; set; }
        public string ServiceName { get; set; }
        public string VehicleType { get; set; }
        public string VehicleNo { get; set; }
        public decimal Amount { get; set; }
        public string FormattedAmount { get; set; }
        public string DateFormatted { get; set; }
        public string CollectionPoint { get; set; }

        /// <summary>Full URL encoded in the QR code — scannable for payment verification.</summary>
        public string VerifyUrl { get; set; }
    }

    // ── Response / data models ───────────────────────────────────────────────

    internal class StateCollectionResponseObject
    {
        public string RespondCode { get; set; }
        public string Message { get; set; }
        public AddSinglecollect addSinglecollect { get; set; }
        public string PrintCode { get; set; }
        public string TransactionNo { get; set; }
    }

    internal class AddSinglecollect { public string TransactionNo { get; set; } }

    internal class StateCollectionObject
    {
        public string ProcessedBy { get; set; }
        public string Payer { get; set; }
        public string ServiceName { get; set; }
        public string AuthCode { get; set; }
        public string Amount { get; set; }
        public string Email { get; set; }
        public string Pin { get; set; }
        public string ServiceDescription { get; set; }
    }

    internal class BusinessQueryResponse
    {
        public string businessName { get; set; }
        public string payerId { get; set; }
        public string status { get; set; }
        public string message { get; set; }
        public string tin { get; set; }
        public string LGA { get; set; }
    }

    internal class VCatData
    {
        private string _vehicleName;

        public string vehicleName
        {
            get => _vehicleName ?? string.Empty;
            set => _vehicleName = value;
        }

        public override string ToString() => vehicleName ?? "Unknown Category";
    }
}