using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Enumerate : ContentPage
    {
        List<BusinessCatData> enums;
        private bool isProcessing = false;

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        public Enumerate()
        {
            InitializeComponent();
            LoadbusinessCategory();
            TrackUserActivity();
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private async void LoadbusinessCategory()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                string url = "https://yobe.osoftpay.net/api/taskpayers/GetBizCat";

                using (var httpClientHandler = new HttpClientHandler())
                {
                    httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                    using (HttpClient client = new HttpClient(httpClientHandler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);

                        using (HttpResponseMessage response = await client.GetAsync(url))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                using (HttpContent content = response.Content)
                                {
                                    var json = await content.ReadAsStringAsync();
                                    List<BusinessCatData> items = JsonConvert.DeserializeObject<List<BusinessCatData>>(json);

                                    if (items != null && items.Any())
                                    {
                                        picker.ItemDisplayBinding = new Binding("businessName");
                                        picker.ItemsSource = items.ToList();
                                        enums = items.ToList();
                                    }
                                }
                            }
                            else
                            {
                                await DisplayAlert("Error", "Failed to load business categories. Please check your connection and try again.", "OK");
                            }
                        }
                    }
                }
            }
            catch (Exception exe)
            {
                await DisplayAlert("Error", $"Unable to load business categories: {exe.Message}", "OK");
            }
        }

        private bool ValidateForm()
        {
            bool isValid = true;
            SessionManager.Instance.UpdateActivity();

            BusinessNameError.IsVisible = false;
            CategoryError.IsVisible = false;
            DescriptionError.IsVisible = false;
            OwnerError.IsVisible = false;
            PhoneError.IsVisible = false;
            EmailError.IsVisible = false;
            AddressError.IsVisible = false;
            LGAError.IsVisible = false;
            LocationError.IsVisible = false;

            if (string.IsNullOrWhiteSpace(BusinessName.Text))
            {
                BusinessNameError.Text = "Business name is required";
                BusinessNameError.IsVisible = true;
                isValid = false;
            }
            else if (BusinessName.Text.Trim().Length < 3)
            {
                BusinessNameError.Text = "Business name must be at least 3 characters";
                BusinessNameError.IsVisible = true;
                isValid = false;
            }

            if (picker.SelectedIndex == -1)
            {
                CategoryError.Text = "Please select a business category";
                CategoryError.IsVisible = true;
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(Description.Text))
            {
                DescriptionError.Text = "Business description is required";
                DescriptionError.IsVisible = true;
                isValid = false;
            }
            else if (Description.Text.Trim().Length < 10)
            {
                DescriptionError.Text = "Description must be at least 10 characters";
                DescriptionError.IsVisible = true;
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(BusinessOwner.Text))
            {
                OwnerError.Text = "Business owner name is required";
                OwnerError.IsVisible = true;
                isValid = false;
            }
            else if (BusinessOwner.Text.Trim().Length < 3)
            {
                OwnerError.Text = "Owner name must be at least 3 characters";
                OwnerError.IsVisible = true;
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(OwnerPhone.Text))
            {
                PhoneError.Text = "Phone number is required";
                PhoneError.IsVisible = true;
                isValid = false;
            }
            else if (OwnerPhone.Text.Length != 11)
            {
                PhoneError.Text = "Phone number must be exactly 11 digits";
                PhoneError.IsVisible = true;
                isValid = false;
            }
            else if (!Regex.IsMatch(OwnerPhone.Text, @"^[0-9]+$"))
            {
                PhoneError.Text = "Phone number must contain only digits";
                PhoneError.IsVisible = true;
                isValid = false;
            }

            if (!string.IsNullOrWhiteSpace(EmailAddress.Text))
            {
                if (!IsValidEmail(EmailAddress.Text))
                {
                    EmailError.Text = "Please enter a valid email address";
                    EmailError.IsVisible = true;
                    isValid = false;
                }
            }

            if (string.IsNullOrWhiteSpace(BusinessAddress.Text))
            {
                AddressError.Text = "Business address is required";
                AddressError.IsVisible = true;
                isValid = false;
            }
            else if (BusinessAddress.Text.Trim().Length < 20)
            {
                AddressError.Text = $"Address must be at least 20 characters (currently {BusinessAddress.Text.Trim().Length})";
                AddressError.IsVisible = true;
                isValid = false;
            }

            if (picker2.SelectedIndex == -1)
            {
                LGAError.Text = "Please select an LGA";
                LGAError.IsVisible = true;
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(Location.Text))
            {
                LocationError.Text = "Location is required";
                LocationError.IsVisible = true;
                isValid = false;
            }

            return isValid;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch { return false; }
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            if (isProcessing)
            {
                await DisplayAlert("Please Wait", "A registration is already in progress", "OK");
                return;
            }

            SessionManager.Instance.UpdateActivity();

            if (!ValidateForm())
            {
                await DisplayAlert("Validation Error", "Please correct the errors and try again", "OK");
                return;
            }

            isProcessing = true;

            try
            {
                using (UserDialogs.Instance.Loading("Registering business, please wait...", null, null, true))
                {
                    await Task.Delay(1000);

                    int indexSelected = picker.SelectedIndex;
                    string CatNameSelected = enums[indexSelected].businessName;

                    string url = "https://yobe.osoftpay.net/api/TaskPayers/NewBusiness/017836098/Register";

                    BusinessRegistrationObject businessRegistrationObject = new BusinessRegistrationObject()
                    {
                        BusinessName = BusinessName.Text.Trim(),
                        BizCategory = CatNameSelected,
                        BusinessOwner = BusinessOwner.Text.Trim(),
                        Description = Description.Text.Trim(),
                        Location = Location.Text.Trim(),
                        LGA = picker2.SelectedItem.ToString(),
                        TIN = TIN.Text?.Trim() ?? "",
                        PhoneNumber = OwnerPhone.Text.Trim(),
                        Email = EmailAddress.Text?.Trim() ?? "",
                        Address = BusinessAddress.Text.Trim(),
                        RecordedBy = MainPage.ValidUserMail,
                    };

                    using (var httpClientHandler = new HttpClientHandler())
                    {
                        httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                        using (HttpClient client = new HttpClient(httpClientHandler))
                        {
                            client.Timeout = TimeSpan.FromSeconds(60);

                            var nvc = new List<KeyValuePair<string, string>>
                            {
                                new KeyValuePair<string, string>("BusinessName",  businessRegistrationObject.BusinessName),
                                new KeyValuePair<string, string>("BizCategory",   businessRegistrationObject.BizCategory),
                                new KeyValuePair<string, string>("BusinessOwner", businessRegistrationObject.BusinessOwner),
                                new KeyValuePair<string, string>("LGA",           businessRegistrationObject.LGA),
                                new KeyValuePair<string, string>("Location",      businessRegistrationObject.Location),
                                new KeyValuePair<string, string>("Description",   businessRegistrationObject.Description),
                                new KeyValuePair<string, string>("TIN",           businessRegistrationObject.TIN),
                                new KeyValuePair<string, string>("PhoneNumber",   businessRegistrationObject.PhoneNumber),
                                new KeyValuePair<string, string>("Email",         businessRegistrationObject.Email),
                                new KeyValuePair<string, string>("AreaOffice",    businessRegistrationObject.LGA),
                                new KeyValuePair<string, string>("Address",       businessRegistrationObject.Address),
                                new KeyValuePair<string, string>("RecordedBy",    MainPage.ValidUserMail)
                            };

                            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(nvc) };
                            var res = await client.SendAsync(req);

                            if (!res.IsSuccessStatusCode)
                            {
                                await ShowErrorSheet($"Server returned error: {res.StatusCode}", "Network Error");
                                return;
                            }

                            var resultString = await res.Content.ReadAsStringAsync();

                            if (string.IsNullOrWhiteSpace(resultString))
                            {
                                await ShowErrorSheet("Server returned empty response", "Invalid Response");
                                return;
                            }

                            var BusinessRegistrationResponse = JsonConvert.DeserializeObject<BusinessRegistrationResponseObject>(resultString);

                            if (BusinessRegistrationResponse == null)
                            {
                                await ShowErrorSheet("Failed to parse server response", "Invalid Response");
                                return;
                            }

                            if (BusinessRegistrationResponse.Status == "00")
                            {
                                await ShowSuccessSheet(BusinessRegistrationResponse);
                            }
                            else
                            {
                                await ShowErrorSheet(
                                    BusinessRegistrationResponse.Message ?? "Unknown error occurred",
                                    $"Registration Failed (Code: {BusinessRegistrationResponse.Status})");
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                await ShowErrorSheet("Request timed out. Please check your internet connection and try again.", "Timeout Error");
            }
            catch (HttpRequestException httpEx)
            {
                await ShowErrorSheet($"Network error: {httpEx.Message}", "Connection Error");
            }
            catch (Exception ex)
            {
                await ShowErrorSheet($"An unexpected error occurred: {ex.Message}", "Error");
            }
            finally
            {
                isProcessing = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  SUCCESS SHEET
        // ══════════════════════════════════════════════════════════════════════════

        private async Task ShowSuccessSheet(BusinessRegistrationResponseObject response)
        {
            string message = $"✓ Registration Successful!\n\n" +
                             $"Business: {response.BusinessName}\n" +
                             $"Payer ID: {response.PayerId}\n\n" +
                             $"Would you like to print a receipt?";

            bool printReceipt = await DisplayAlert("Success", message, "Print Receipt", "Close");

            if (printReceipt)
            {
                var receipt = BuildReceiptData(response);
                await CallPrinterAsync(receipt);
                ClearForm();
            }
            else
            {
                ClearForm();
            }
        }

        private async Task ShowErrorSheet(string errorMessage, string title)
        {
            await DisplayAlert(title, $"❌ {errorMessage}", "OK");
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
                SuperAgent = MainPage.Super_Agent ?? string.Empty,
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = 0m,
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
                    await Device.InvokeOnMainThreadAsync(async () =>
                        await DisplayAlert("Bluetooth Permission",
                            "Bluetooth permission denied. Grant 'Nearby devices' permission in App Settings to print.", "OK"));
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
                System.Diagnostics.Debug.WriteLine($"[Default.Enumerate] PrinterException: {pex}");
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
                        "Could not connect to printer. Registration was saved successfully.", "OK"));
                System.Diagnostics.Debug.WriteLine($"[Default.Enumerate] Print error: {ex}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  FORM CLEAR
        // ══════════════════════════════════════════════════════════════════════════

        private void ClearForm()
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

            BusinessNameError.IsVisible = false;
            CategoryError.IsVisible = false;
            DescriptionError.IsVisible = false;
            OwnerError.IsVisible = false;
            PhoneError.IsVisible = false;
            EmailError.IsVisible = false;
            AddressError.IsVisible = false;
            LGAError.IsVisible = false;
            LocationError.IsVisible = false;
        }
    }

    // ── Data models ───────────────────────────────────────────────────────────────

    internal class BusinessCatData
    {
        public string businessName { get; set; }
    }

    internal class BusinessRegistrationResponseObject
    {
        public string BusinessName { get; set; }
        public string TIN { get; set; }
        public string Message { get; set; }
        public string Status { get; set; }
        public string PayerId { get; set; }
    }

    internal class BusinessRegistrationObject
    {
        public string BusinessName { get; set; }
        public string BizCategory { get; set; }
        public string BusinessOwner { get; set; }
        public string LGA { get; set; }
        public string TIN { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string AreaOffice { get; set; }
        public string Location { get; set; }
        public string SinageCat { get; set; }
        public string BusinesPermitCat { get; set; }
        public string TenamentCat { get; set; }
        public string Description { get; set; }
        public string RecordedBy { get; set; }
    }
}