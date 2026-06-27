using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Yorata_Ops
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class GenerateInvoice : ContentPage
    {
        // Static properties to receive data from ServiceList
        public static string SelectedServiceName { get; set; }
        public static string SelectedServiceDescription { get; set; }
        public static decimal SelectedServiceAmount { get; set; }
        public static string RevenueHead { get; set; }

        private bool isProcessing = false;
        private SemaphoreSlim processingLock = new SemaphoreSlim(1, 1);
        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 1000;

        // Store generated invoice details
        private InvoiceResponse currentInvoice;

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        public GenerateInvoice()
        {
            try
            {
                InitializeComponent();
                InitializePage();
                TrackUserActivity();
            }
            catch (Exception ex)
            {
                HandleCriticalException(ex, "Failed to initialize invoice generation page");
            }
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
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(SelectedServiceName))
                    throw new InvalidOperationException("Service information is missing");

                if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Authentication Required",
                            "Your session has expired. Please log in again.", "OK");
                        await Navigation.PopToRootAsync();
                    });
                    return;
                }

                ServiceNameLabel.Text = SelectedServiceName ?? "Unknown Service";
                ServiceAmountLabel.Text = $"₦{SelectedServiceAmount:N2}";
                AmountDisplayLabel.Text = $"{SelectedServiceAmount:N2}";

                if (successSheet != null)
                    successSheet.IsOpen = false;

                LogDebug("Page initialized successfully");
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to initialize page");
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Initialization Error",
                        "Failed to load service details. Please try again.", "OK");
                    await Navigation.PopAsync();
                });
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SessionManager.Instance.UpdateActivity();
        }

        private async void GenerateInvoice_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (isProcessing)
            {
                ShowError("Invoice generation is already in progress. Please wait.");
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

                using (UserDialogs.Instance.Loading("Generating Invoice...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(500);
                    await GenerateInvoiceAsync();
                }
            }
            catch (TaskCanceledException)
            {
                await DisplayAlert("Timeout",
                    "The request took too long. Please check your connection and try again.", "OK");
            }
            catch (HttpRequestException ex)
            {
                await HandleInvoiceException(ex, "Network error occurred");
            }
            catch (Exception ex)
            {
                await HandleInvoiceException(ex, "An unexpected error occurred");
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

                if (string.IsNullOrWhiteSpace(FullNameEntry?.Text))
                {
                    ShowError("Please enter customer's full name");
                    FullNameEntry?.Focus();
                    return false;
                }

                if (FullNameEntry.Text.Trim().Length < 3)
                {
                    ShowError("Full name must be at least 3 characters");
                    FullNameEntry?.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(PhoneNumberEntry?.Text))
                {
                    ShowError("Please enter customer's phone number");
                    PhoneNumberEntry?.Focus();
                    return false;
                }

                string phone = PhoneNumberEntry.Text.Trim();
                if (phone.Length != 11 || !phone.All(char.IsDigit))
                {
                    ShowError("Phone number must be exactly 11 digits");
                    PhoneNumberEntry?.Focus();
                    return false;
                }

                if (!phone.StartsWith("0"))
                {
                    ShowError("Phone number must start with 0");
                    PhoneNumberEntry?.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(AddressEntry?.Text))
                {
                    ShowError("Please enter customer's address");
                    AddressEntry?.Focus();
                    return false;
                }

                if (AddressEntry.Text.Trim().Length < 3)
                {
                    ShowError("Address must be at least 3 characters");
                    AddressEntry?.Focus();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError(ex, "Validation failed");
                return false;
            }
        }

        private async Task GenerateInvoiceAsync()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                var response = await GenerateInvoiceWithRetryAsync(
                    FullNameEntry.Text.Trim(),
                    PhoneNumberEntry.Text.Trim(),
                    AddressEntry.Text.Trim(),
                    SelectedServiceAmount);

                if (response != null && response.status == "00")
                    await HandleSuccessfulInvoice(response);
                else
                    await HandleFailedInvoice(response?.message ?? "Invoice generation failed");
            }
            catch (Exception ex)
            {
                throw new Exception($"Invoice generation failed: {ex.Message}", ex);
            }
        }

        private async Task<InvoiceResponse> GenerateInvoiceWithRetryAsync(
            string fullName, string phoneNo, string address, decimal amount)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    attempt++;
                    LogDebug($"Invoice API attempt {attempt} of {MAX_RETRY_ATTEMPTS}");
                    return await CallInvoiceApiAsync(fullName, phoneNo, address, amount);
                }
                catch (TaskCanceledException ex) when (attempt < MAX_RETRY_ATTEMPTS)
                {
                    lastException = ex;
                    LogError(ex, $"Request timeout on attempt {attempt}");
                    await Task.Delay(RETRY_DELAY_MS * attempt);
                }
                catch (HttpRequestException ex) when (attempt < MAX_RETRY_ATTEMPTS)
                {
                    lastException = ex;
                    LogError(ex, $"Network error on attempt {attempt}");
                    await Task.Delay(RETRY_DELAY_MS * attempt);
                }
                catch (WebException ex) when (attempt < MAX_RETRY_ATTEMPTS)
                {
                    lastException = ex;
                    LogError(ex, $"Web exception on attempt {attempt}");
                    await Task.Delay(RETRY_DELAY_MS * attempt);
                }
                catch (UnauthorizedAccessException) { throw; }
                catch (InvalidOperationException ex)
                {
                    LogError(ex, "Invalid operation - no retry");
                    throw;
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Unexpected error on attempt {attempt}");
                    lastException = ex;
                    if (attempt >= MAX_RETRY_ATTEMPTS) throw;
                    await Task.Delay(RETRY_DELAY_MS * attempt);
                }
            }

            throw lastException ?? new Exception("Failed to generate invoice after multiple attempts");
        }

        private async Task<InvoiceResponse> CallInvoiceApiAsync(
            string fullName, string phoneNo, string address, decimal amount)
        {
            string url = "https://yobe.osoftpay.net/api/KekeTransactions/GenerateInvoice";
            LogDebug($"Calling Invoice API: {url}");

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    if (cert != null)
                        LogDebug($"Certificate - Subject: {cert.Subject}, Issuer: {cert.Issuer}");
                    return true;
                }
            };

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS);

                try
                {
                    var formData = new Dictionary<string, string>
                    {
                        { "ServiceName", SelectedServiceName ?? "" },
                        { "RevHead",     RevenueHead ?? MainPage.CollectionPoint ?? "Unknown" },
                        { "FullName",    fullName },
                        { "PhoneNo",     phoneNo },
                        { "Address",     address },
                        { "Amount",      amount.ToString("F2") },
                        { "AgentEmail",  MainPage.ValidUserMail ?? "" }
                    };

                    LogDebug($"Request: ServiceName={formData["ServiceName"]}, " +
                             $"RevHead={formData["RevHead"]}, FullName={formData["FullName"]}, " +
                             $"PhoneNo={formData["PhoneNo"]}, Amount={formData["Amount"]}");

                    var content = new FormUrlEncodedContent(formData);
                    var response = await client.PostAsync(url, content);

                    LogDebug($"Response Status: {response.StatusCode}");

                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                        throw new UnauthorizedAccessException("Session expired or unauthorized access");

                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        var errContent = await response.Content.ReadAsStringAsync();
                        throw new InvalidOperationException($"Invalid request: {errContent}");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errContent = await response.Content.ReadAsStringAsync();
                        throw new HttpRequestException(
                            $"Server returned error: {response.StatusCode} - {response.ReasonPhrase}");
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    LogDebug($"API Response: {json}");

                    if (string.IsNullOrWhiteSpace(json))
                        throw new InvalidOperationException("Server returned empty response");

                    InvoiceResponse invoiceResponse;
                    try
                    {
                        invoiceResponse = JsonConvert.DeserializeObject<InvoiceResponse>(json);
                    }
                    catch (JsonException jsonEx)
                    {
                        LogError(jsonEx, $"JSON parsing failed. Response: {json}");
                        throw new InvalidOperationException("Failed to parse server response", jsonEx);
                    }

                    if (invoiceResponse == null)
                        throw new InvalidOperationException("Failed to parse invoice response");

                    if (string.IsNullOrWhiteSpace(invoiceResponse.status))
                        throw new InvalidOperationException("Invalid response format: missing status");

                    LogDebug($"Invoice Response – Status: {invoiceResponse.status}, " +
                             $"Message: {invoiceResponse.message ?? "N/A"}, " +
                             $"Invoice Number: {invoiceResponse.invoice_number ?? "N/A"}");

                    return invoiceResponse;
                }
                catch (TaskCanceledException ex)
                {
                    LogError(ex, "Request cancelled or timed out");
                    throw new TaskCanceledException(
                        "The request timed out. Please check your internet connection.", ex);
                }
                catch (HttpRequestException ex) { LogError(ex, "HTTP request failed"); throw; }
                catch (WebException ex)
                {
                    LogError(ex, "Web exception occurred");
                    throw new HttpRequestException("Network error occurred.", ex);
                }
                catch (UnauthorizedAccessException) { throw; }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex)
                {
                    LogError(ex, "Unexpected error in API call");
                    throw new Exception("An unexpected error occurred while generating the invoice", ex);
                }
            }
        }

        private async Task HandleSuccessfulInvoice(InvoiceResponse response)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                currentInvoice = response;

                Device.BeginInvokeOnMainThread(() =>
                {
                    if (successSheet != null)
                    {
                        successSheet.BindingContext = new InvoiceDisplayModel
                        {
                            InvoiceNumber = response.invoice_number ?? "N/A",
                            CustomerName = response.name ?? "N/A",
                            CustomerPhone = response.phoneNo ?? "N/A",
                            CustomerAddress = response.address ?? "N/A",
                            Service = response.service ?? "N/A",
                            Amount = $"₦{response.amount:N2}"
                        };

                        successSheet.IsOpen = true;
                    }

                    ClearFormFields();
                });

                LogDebug($"Invoice generated successfully: {response.invoice_number}");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error handling successful invoice");
                await DisplayAlert("Warning",
                    "Invoice was generated but there was an error displaying it.", "OK");
            }
        }

        private async Task HandleFailedInvoice(string message)
        {
            try
            {
                string displayMessage = string.IsNullOrWhiteSpace(message)
                    ? "Invoice generation failed. Please try again."
                    : message;

                LogError(null, $"Invoice generation failed: {displayMessage}");
                await Device.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert("Invoice Generation Failed", displayMessage, "OK"));
            }
            catch (Exception ex)
            {
                LogError(ex, "Error handling failed invoice");
            }
        }

        private async Task HandleInvoiceException(Exception ex, string userMessage = "An error occurred")
        {
            LogError(ex, "Invoice exception");

            await Device.InvokeOnMainThreadAsync(async () =>
            {
                string message;

                if (ex is TaskCanceledException || ex is TimeoutException)
                    message = "The request timed out. Please check your internet connection and try again.";
                else if (ex is HttpRequestException)
                    message = "Network error occurred. Please check your internet connection and try again.";
                else if (ex is WebException)
                    message = "Unable to connect to the server. Please check your internet connection.";
                else if (ex is UnauthorizedAccessException)
                {
                    message = "Your session has expired. Please log in again.";
                    await Task.Delay(2000);
                    await Navigation.PopToRootAsync();
                    return;
                }
                else if (ex is InvalidOperationException)
                    message = ex.Message;
                else
                    message = $"{userMessage}. Please try again or contact support if the issue persists.";

                await DisplayAlert("Error", message, "OK");
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MODAL ACTION HANDLERS
        // ══════════════════════════════════════════════════════════════════════

        private async void PrintInvoice_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (currentInvoice == null)
            {
                ShowError("No invoice data available to print");
                return;
            }

            try
            {
                using (UserDialogs.Instance.Loading("Preparing to print...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(500);
                    var receipt = BuildReceiptData(currentInvoice);
                    await CallPrinterAsync(receipt);
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Print failed");
                await DisplayAlert("Print Error", "Failed to print invoice. Please try again.", "OK");
            }
        }

        private async void ShareInvoice_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (currentInvoice == null)
            {
                ShowError("No invoice data available to share");
                return;
            }

            try
            {
                string shareText =
                    $"🧾 INVOICE GENERATED\n\n" +
                    $"Invoice No: {currentInvoice.invoice_number}\n" +
                    $"Customer: {currentInvoice.name}\n" +
                    $"Phone: {currentInvoice.phoneNo}\n" +
                    $"Address: {currentInvoice.address}\n" +
                    $"Service: {currentInvoice.service}\n" +
                    $"Amount: ₦{currentInvoice.amount:N2}\n" +
                    $"Revenue Head: {currentInvoice.revHead}\n\n" +
                    $"Generated by: {MainPage.Name}\n" +
                    $"Date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n\n" +
                    $"Powered by YIRS – Yobe State Internal Revenue Service";

                await Share.RequestAsync(new ShareTextRequest
                {
                    Text = shareText,
                    Title = "Share Invoice"
                });

                LogDebug("Invoice shared successfully");
            }
            catch (Exception ex)
            {
                LogError(ex, "Share failed");
                await DisplayAlert("Share Error", "Failed to share invoice. Please try again.", "OK");
            }
        }

        private async void CopyDetails_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();

            if (currentInvoice == null)
            {
                ShowError("No invoice data available to copy");
                return;
            }

            try
            {
                string copyText =
                    $"Invoice No: {currentInvoice.invoice_number}\n" +
                    $"Customer: {currentInvoice.name}\n" +
                    $"Phone: {currentInvoice.phoneNo}\n" +
                    $"Address: {currentInvoice.address}\n" +
                    $"Service: {currentInvoice.service}\n" +
                    $"Amount: ₦{currentInvoice.amount:N2}\n" +
                    $"Revenue Head: {currentInvoice.revHead}\n" +
                    $"Date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

                await Clipboard.SetTextAsync(copyText);

                UserDialogs.Instance.Toast(new ToastConfig("Invoice details copied to clipboard!")
                {
                    Duration = TimeSpan.FromSeconds(3),
                    BackgroundColor = System.Drawing.Color.FromArgb(46, 125, 50),
                    MessageTextColor = System.Drawing.Color.White,
                    Position = ToastPosition.Bottom
                });

                LogDebug("Invoice details copied to clipboard");
            }
            catch (Exception ex)
            {
                LogError(ex, "Copy failed");
                ShowError("Failed to copy invoice details");
            }
        }

        private void CloseSuccessSheet_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            try
            {
                if (successSheet != null) successSheet.IsOpen = false;
                currentInvoice = null;
            }
            catch (Exception ex) { LogError(ex, "Error closing success sheet"); }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  RECEIPT DATA BUILDER — logo (via SDK) + QR barcode
        // ══════════════════════════════════════════════════════════════════════

        private ReceiptData BuildReceiptData(InvoiceResponse invoice)
        {
            // QR code encodes a payment URL that the payer/verifier can scan
            string verifyUrl =
                $"https://yobe.osoftpay.net/singlecollections/verify" +
                $"?InvoiceNo={Uri.EscapeDataString(invoice.invoice_number ?? string.Empty)}";

            var items = new List<ReceiptItem>
            {
                // Customer block (amount = 0 → printed as label:value rows)
                new ReceiptItem { Description = "Customer",  Amount = 0m, SubText = invoice.name    ?? "N/A" },
                new ReceiptItem { Description = "Phone",     Amount = 0m, SubText = invoice.phoneNo  ?? "N/A" },
                new ReceiptItem { Description = "Address",   Amount = 0m, SubText = invoice.address  ?? "N/A" },
                new ReceiptItem { Description = "Rev. Head", Amount = 0m, SubText = invoice.revHead  ?? "N/A" },

                // Service line with the payable amount
                new ReceiptItem
                {
                    Description = invoice.service ?? "Service",
                    Amount      = invoice.amount,
                    SubText     = null
                }
            };

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: +234-810-046-6363",
                ReceiptNumber = invoice.invoice_number ?? "N/A",
                AgentName = MainPage.Name ?? "N/A",
                PrintDate = DateTime.Now,
                Items = items,
                FooterLine1 = "Use this invoice number for payment",
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
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
                    UserDialogs.Instance.Toast(
                        "Bluetooth permission denied. " +
                        "Grant 'Nearby devices' permission in App Settings to print.",
                        TimeSpan.FromSeconds(8));
                    return;
                }

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    await _printer.PrintReceiptAsync(
                        receipt,
                        logoAssetName: "Logo.png",   // printed above header via SDK
                        watermarkText: "YIRS",
                        cancellationToken: cts.Token);
                }

                UserDialogs.Instance.Toast(new ToastConfig("Invoice printed successfully!")
                {
                    Duration = TimeSpan.FromSeconds(3),
                    BackgroundColor = System.Drawing.Color.FromArgb(46, 125, 50),
                    MessageTextColor = System.Drawing.Color.White
                });
            }
            catch (PrinterException pex)
            {
                LogError(pex, "PrinterException in GenerateInvoice");
                UserDialogs.Instance.Toast(
                    $"Print failed: {pex.Message}",
                    TimeSpan.FromSeconds(8));
            }
            catch (OperationCanceledException)
            {
                UserDialogs.Instance.Toast(
                    "Print timed out. Check that the printer is powered on.",
                    TimeSpan.FromSeconds(6));
            }
            catch (Exception ex)
            {
                LogError(ex, "Print error in GenerateInvoice");
                UserDialogs.Instance.Toast(
                    "Printer not connected. Your invoice is saved and can be shared.",
                    TimeSpan.FromSeconds(6));
            }
        }

        // ── Helper methods ────────────────────────────────────────────────────

        private void SetProcessingState(bool processing)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                GeneratingIndicator.IsRunning = processing;
                GeneratingIndicator.IsVisible = processing;
                GenerateButtonLabel.IsVisible = !processing;
                FullNameEntry.IsEnabled = !processing;
                PhoneNumberEntry.IsEnabled = !processing;
                AddressEntry.IsEnabled = !processing;
            });
        }

        private void ClearFormFields()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                FullNameEntry.Text = string.Empty;
                PhoneNumberEntry.Text = string.Empty;
                AddressEntry.Text = string.Empty;
            });
        }

        private void ShowError(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

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

        private void HandleCriticalException(Exception ex, string userMessage)
        {
            LogError(ex, $"CRITICAL: {userMessage}");
            Device.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Critical Error",
                    $"{userMessage}. Please restart the application.", "OK");
                await Navigation.PopAsync();
            });
        }

        private void LogError(Exception ex, string message)
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

        private void LogDebug(string message)
            => System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DEBUG: {message}");

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            processingLock?.Dispose();
        }
    }

    // ── Response models ───────────────────────────────────────────────────────

    public class InvoiceResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public string invoice_number { get; set; }
        public string service { get; set; }
        public decimal amount { get; set; }
        public string name { get; set; }
        public string phoneNo { get; set; }
        public string address { get; set; }
        public string revHead { get; set; }
    }

    public class InvoiceDisplayModel
    {
        public string InvoiceNumber { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPhone { get; set; }
        public string CustomerAddress { get; set; }
        public string Service { get; set; }
        public string Amount { get; set; }
    }
}