using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Enumerate : ContentPage
    {
        private List<BusinessCatData> enums;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 2000;

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        public Enumerate()
        {
            try
            {
                InitializeComponent();
                InitializeSSL();
                LoadBusinessCategory();
                TrackUserActivity();
            }
            catch (Exception ex)
            {
                HandleCriticalError("Initialization Error", ex);
            }
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private void InitializeSSL()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;
                ServicePointManager.DefaultConnectionLimit = 10;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SSL Initialization Error: {ex.Message}");
            }
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None) return true;
            System.Diagnostics.Debug.WriteLine($"Certificate error: {sslPolicyErrors}");
            return true;
        }

        private async void LoadBusinessCategory()
        {
            SessionManager.Instance.UpdateActivity();
            int attempt = 0;

            while (attempt < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    attempt++;
                    string url = "https://yobe.osoftpay.net/api/taskpayers/GetBizCat";

                    using (var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                        SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
                    })
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                        var response = await client.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();

                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                var items = JsonConvert.DeserializeObject<List<BusinessCatData>>(json);

                                if (items != null && items.Any())
                                {
                                    Device.BeginInvokeOnMainThread(() =>
                                    {
                                        picker.ItemDisplayBinding = new Binding("businessName");
                                        picker.ItemsSource = items;
                                        enums = items;
                                    });
                                    return;
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"API returned status: {response.StatusCode}");
                        }
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Network error (Attempt {attempt}): {httpEx.Message}");
                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(RETRY_DELAY_MS);
                        continue;
                    }
                }
                catch (TaskCanceledException timeoutEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Request timeout (Attempt {attempt}): {timeoutEx.Message}");
                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(RETRY_DELAY_MS);
                        continue;
                    }
                }
                catch (JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON parsing error: {jsonEx.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected error loading categories: {ex.Message}");
                    break;
                }
            }

            Device.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Network Issue",
                    "Unable to load business categories. Please check your internet connection and try again.", "OK");
            });
        }

        private (bool isValid, string errorMessage) ValidateForm()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(BusinessName?.Text))
                    return (false, "Business Name is required");

                if (picker.SelectedIndex < 0)
                    return (false, "Please select a Business Category");

                if (string.IsNullOrWhiteSpace(Description?.Text))
                    return (false, "Business Description is required");

                if (string.IsNullOrWhiteSpace(BusinessOwner?.Text))
                    return (false, "Business Owner name is required");

                if (string.IsNullOrWhiteSpace(OwnerPhone?.Text))
                    return (false, "Owner Phone Number is required");

                if (OwnerPhone.Text.Length < 10)
                    return (false, "Please enter a valid phone number");

                if (string.IsNullOrWhiteSpace(BusinessAddress?.Text))
                    return (false, "Business Address is required");

                if (picker2.SelectedIndex < 0)
                    return (false, "Please select an LGA");

                if (string.IsNullOrWhiteSpace(Location?.Text))
                    return (false, "Location/Landmark is required");

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Validation error: {ex.Message}");
                return (false, "Error validating form. Please try again.");
            }
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            IDisposable loadingDialog = null;
            SessionManager.Instance.UpdateActivity();

            try
            {
                var validation = ValidateForm();
                if (!validation.isValid)
                {
                    await DisplayAlert("Required Field Missing", validation.errorMessage, "OK");
                    return;
                }

                loadingDialog = UserDialogs.Instance.Loading("Registering business...", null, null, true, MaskType.Black);
                await Task.Delay(500);

                string selectedCategory = GetSelectedCategory();
                if (string.IsNullOrEmpty(selectedCategory))
                {
                    loadingDialog?.Dispose();
                    await DisplayAlert("Error", "Please select a valid business category", "OK");
                    return;
                }

                var registrationData = new BusinessRegistrationObject
                {
                    BusinessName = BusinessName.Text?.Trim(),
                    BizCategory = selectedCategory,
                    BusinessOwner = BusinessOwner.Text?.Trim(),
                    Description = Description.Text?.Trim(),
                    Location = Location.Text?.Trim(),
                    LGA = picker2.SelectedItem?.ToString(),
                    TIN = TIN.Text?.Trim() ?? "",
                    PhoneNumber = OwnerPhone.Text?.Trim(),
                    Email = EmailAddress.Text?.Trim() ?? "",
                    Address = BusinessAddress.Text?.Trim(),
                    RecordedBy = MainPage.ValidUserMail ?? "Unknown"
                };

                var response = await SubmitRegistration(registrationData);

                loadingDialog?.Dispose();

                if (response != null && response.Status == "00")
                {
                    await HandleSuccessfulRegistration(response);
                }
                else
                {
                    string errorMsg = response?.Message ?? "Registration failed. Please try again.";
                    await DisplayAlert("Registration Failed", errorMsg, "OK");
                }
            }
            catch (Exception ex)
            {
                loadingDialog?.Dispose();
                await HandleError("Registration Error", ex);
            }
        }

        private string GetSelectedCategory()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (picker.SelectedIndex < 0 || enums == null || !enums.Any())
                    return null;

                int selectedIndex = picker.SelectedIndex;
                if (selectedIndex < enums.Count)
                    return enums[selectedIndex].businessName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting category: {ex.Message}");
            }
            return null;
        }

        private async Task<BusinessRegistrationResponseObject> SubmitRegistration(BusinessRegistrationObject data)
        {
            SessionManager.Instance.UpdateActivity();
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    attempt++;
                    string url = "https://yobe.osoftpay.net/api/TaskPayers/NewBusiness/017836098/Register";

                    using (var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                        SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11
                    })
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(45);

                        var formData = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>("BusinessName",  data.BusinessName ?? ""),
                            new KeyValuePair<string, string>("BizCategory",   data.BizCategory ?? ""),
                            new KeyValuePair<string, string>("BusinessOwner", data.BusinessOwner ?? ""),
                            new KeyValuePair<string, string>("LGA",           data.LGA ?? ""),
                            new KeyValuePair<string, string>("Location",      data.Location ?? ""),
                            new KeyValuePair<string, string>("Description",   data.Description ?? ""),
                            new KeyValuePair<string, string>("TIN",           data.TIN ?? ""),
                            new KeyValuePair<string, string>("PhoneNumber",   data.PhoneNumber ?? ""),
                            new KeyValuePair<string, string>("Email",         data.Email ?? ""),
                            new KeyValuePair<string, string>("Address",       data.Address ?? ""),
                            new KeyValuePair<string, string>("AreaOffice",    data.LGA ?? ""),
                            new KeyValuePair<string, string>("RecordedBy",    data.RecordedBy ?? "")
                        };

                        var content = new FormUrlEncodedContent(formData);
                        var response = await client.PostAsync(url, content);

                        if (response.IsSuccessStatusCode)
                        {
                            var resultString = await response.Content.ReadAsStringAsync();
                            return JsonConvert.DeserializeObject<BusinessRegistrationResponseObject>(resultString);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"API error: {response.StatusCode}");
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Network error on attempt {attempt}: {ex.Message}");
                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(RETRY_DELAY_MS);
                        continue;
                    }
                }
                catch (TaskCanceledException ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Timeout on attempt {attempt}: {ex.Message}");
                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        await Task.Delay(RETRY_DELAY_MS);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected error: {ex.Message}");
                    throw;
                }
            }

            throw new Exception($"Failed after {MAX_RETRY_ATTEMPTS} attempts. " +
                                (lastException?.Message ?? "Unknown error"));
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  SUCCESS HANDLER
        // ══════════════════════════════════════════════════════════════════════════

        private async Task HandleSuccessfulRegistration(BusinessRegistrationResponseObject response)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                string message = $"✅ Registration Successful!\n\nBusiness ID: {response.PayerId}\nBusiness: {response.BusinessName}";
                string action = await DisplayActionSheet(message, null, "Close", "Print Receipt");

                if (action == "Print Receipt")
                {
                    var receipt = BuildReceiptData(response);
                    await CallPrinterAsync(receipt);
                }

                ClearForm();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in success handler: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  RECEIPT DATA BUILDER — logo shown, NO barcode (BarcodeLabel = null)
        // ══════════════════════════════════════════════════════════════════════════

        private ReceiptData BuildReceiptData(BusinessRegistrationResponseObject response)
        {
            var items = new List<ReceiptItem>
            {
                new ReceiptItem
                {
                    Description = "Business Name",
                    Amount      = 0m,
                    SubText     = response.BusinessName ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "Payer ID",
                    Amount      = 0m,
                    SubText     = response.PayerId ?? "N/A"
                }
            };

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: +234-810-046-6363",
                ReceiptNumber = response.PayerId ?? "N/A",
                AgentName = MainPage.Name ?? "N/A",
                CollectionPoint = string.Empty,
                SuperAgent = MainPage.Super_Agent ?? string.Empty,
                PrintDate = DateTime.Now,
                Items = items,
                FooterLine1 = App.ThankYouMessage2 ?? "Thank You!",
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = null   // ← no QR barcode for enumeration receipts
            };
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  PRINTER — NEW SDK
        // ══════════════════════════════════════════════════════════════════════════

        private async Task CallPrinterAsync(ReceiptData receipt)
        {
            try
            {
                bool btGranted = await BluetoothPermissionHelper.RequestAsync();
                if (!btGranted)
                {
                    await DisplayAlert("Bluetooth Permission",
                        "Bluetooth permission denied. Grant 'Nearby devices' permission in App Settings to print.", "OK");
                    return;
                }

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    await _printer.PrintReceiptAsync(
                        receipt,
                        logoAssetName: "Logo.png",
                        watermarkText: "YIRS",
                        cancellationToken: cts.Token);
                }
            }
            catch (PrinterException pex)
            {
                await Device.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert("Printer Error", pex.Message, "OK"));
                System.Diagnostics.Debug.WriteLine($"[Home.Enumerate] PrinterException: {pex}");
            }
            catch (OperationCanceledException)
            {
                await Device.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert("Print Timeout",
                        "Print timed out. Check that the printer is powered on and within range.", "OK"));
            }
            catch (Exception ex)
            {
                await Device.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert("Printer Error",
                        "An error occurred while printing. Please try again.", "OK"));
                System.Diagnostics.Debug.WriteLine($"[Home.Enumerate] Print error: {ex}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  FORM CLEAR + ERROR HANDLING
        // ══════════════════════════════════════════════════════════════════════════

        private void ClearForm()
        {
            SessionManager.Instance.UpdateActivity();
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    BusinessName.Text = string.Empty;
                    RegistrationNumber.Text = string.Empty;
                    picker.SelectedIndex = -1;
                    Description.Text = string.Empty;
                    BusinessOwner.Text = string.Empty;
                    TIN.Text = string.Empty;
                    OwnerPhone.Text = string.Empty;
                    EmailAddress.Text = string.Empty;
                    BusinessAddress.Text = string.Empty;
                    picker2.SelectedIndex = -1;
                    Location.Text = string.Empty;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing form: {ex.Message}");
            }
        }

        private async Task HandleError(string title, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"{title}: {ex.Message}");
                string userMessage = "An error occurred. Please try again.";

                if (ex is HttpRequestException || ex is TaskCanceledException)
                    userMessage = "Network error. Please check your internet connection and try again.";
                else if (ex is JsonException)
                    userMessage = "Invalid data received. Please contact support.";

                await DisplayAlert(title, userMessage, "OK");
            }
            catch (Exception displayEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying error: {displayEx.Message}");
            }
        }

        private void HandleCriticalError(string title, Exception ex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL - {title}: {ex.Message}");
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert(title, "A critical error occurred. Please restart the application.", "OK"));
            }
            catch (Exception displayEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error displaying critical error: {displayEx.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            try { base.OnDisappearing(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDisappearing: {ex.Message}");
            }
        }
    }

    #region Data Models

    public class BusinessCatData
    {
        public string businessName { get; set; }
        public string categoryId { get; set; }
    }

    public class BusinessRegistrationObject
    {
        public string BusinessName { get; set; }
        public string BizCategory { get; set; }
        public string BusinessOwner { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public string LGA { get; set; }
        public string TIN { get; set; }
        public string PhoneNumber { get; set; }
        public string AreaOffice { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string RecordedBy { get; set; }
    }

    public class BusinessRegistrationResponseObject
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public string PayerId { get; set; }
        public string BusinessName { get; set; }
    }

    #endregion
}