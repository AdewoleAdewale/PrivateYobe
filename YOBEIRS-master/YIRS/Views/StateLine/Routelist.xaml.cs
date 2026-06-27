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

namespace YIRS.Views.StateLine
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Routelist : ContentPage
    {
        private bool _isVerifying = false;
        private bool _isPrinting = false;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int REQUEST_TIMEOUT_SECONDS = 30;

        class ServicesList
        {
            public string route { get; set; }
        }

        class VehicleType
        {
            public int id { get; set; }
            public string vehicleName { get; set; }
        }

        class PaymentResponse
        {
            public string respondCode { get; set; }
            public string transactionNo { get; set; }
            public string message { get; set; }
            public string vehicle { get; set; }
            public string destination { get; set; }
            public int differences { get; set; }
            public int noofSeat { get; set; }
            public decimal amountRemitted { get; set; }
            public decimal amtCollected { get; set; }
        }

        class HistoryDataHeaderFooter
        {
            public List<ServicesList> HD { get; set; }
            public string Intro { get { return "You have a total of " + HD.Count + " StateLine Services"; } }
            public string Summary { get { return "Total: " + HD.Count + " Services Available"; } }
            public decimal Size { get { return HD.Count; } }
        }

        public static string routeName { get; set; }
        private List<VehicleType> vehicleTypes = new List<VehicleType>();
        private string lastTransactionNo = "";

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        private static HttpClient _httpClient;
        private static HttpClient HttpClientInstance
        {
            get
            {
                if (_httpClient == null)
                {
                    var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                    };
                    _httpClient = new HttpClient(handler)
                    {
                        Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
                    };
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                }
                return _httpClient;
            }
        }

        public Routelist()
        {
            InitializeComponent();

            try
            {
                Fullname.Text = MainPage.CollectionPoint;

                Task.Run(async () =>
                {
                    await LoadVehicleTypes();
                    await CallRevenueList();
                });

                AnimatePageEntrance();
                TrackUserActivity();
            }
            catch (Exception ex)
            {
                LogError(ex, "Error in Routelist constructor");
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Error", "Failed to initialize page. Please try again.", "OK"));
            }
        }

        private async void AnimatePageEntrance()
        {
            try
            {
                this.Content.Opacity = 0;
                await this.Content.FadeTo(1, 300, Easing.CubicInOut);
            }
            catch (Exception ex) { LogError(ex, "Animation error"); }
        }

        private void TrackUserActivity()
        {
            try
            {
                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
                this.Content.GestureRecognizers.Add(tapGesture);
            }
            catch (Exception ex) { LogError(ex, "Error setting up activity tracking"); }
        }

        private async Task LoadVehicleTypes()
        {
            try
            {
                string url = "https://yobe.osoftpay.net/Api/Fares/VehicycleTypes";

                using (HttpResponseMessage response = await HttpClientInstance.GetAsync(url))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        vehicleTypes = JsonConvert.DeserializeObject<List<VehicleType>>(json);

                        if (vehicleTypes != null && vehicleTypes.Count > 0)
                        {
                            Device.BeginInvokeOnMainThread(() =>
                                VehicleTypePicker.ItemsSource = vehicleTypes.Select(v => v.vehicleName).ToList());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Failed to load vehicle types");
                vehicleTypes = new List<VehicleType>
                {
                    new VehicleType { id = 1, vehicleName = "BUS" },
                    new VehicleType { id = 2, vehicleName = "TRAILER" },
                    new VehicleType { id = 3, vehicleName = "KEKE NAPEP" }
                };
                Device.BeginInvokeOnMainThread(() =>
                    VehicleTypePicker.ItemsSource = vehicleTypes.Select(v => v.vehicleName).ToList());
            }
        }

        private async Task CallRevenueList()
        {
            SessionManager.Instance.UpdateActivity();

            try
            {
                Device.BeginInvokeOnMainThread(() =>
                    UserDialogs.Instance.ShowLoading("🔄 Loading Services...", MaskType.Black));

                await Task.Delay(300);
                string url = "https://yobe.osoftpay.net/Api/SingleCollections/RouteServices?Email=" + MainPage.ValidUserMail;

                using (HttpResponseMessage response = await HttpClientInstance.GetAsync(url))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        List<ServicesList> items = JsonConvert.DeserializeObject<List<ServicesList>>(json);

                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            UserDialogs.Instance.HideLoading();

                            if (items != null && items.Count > 0)
                            {
                                BindingContext = new HistoryDataHeaderFooter { HD = items };

                                if (TotalServicesLabel != null)
                                    await AnimateNumberChange(TotalServicesLabel, items.Count);

                                await AnimateListItems();
                            }
                            else
                            {
                                await DisplayAlert("Info", "No services available at the moment.", "OK");
                            }
                        });
                    }
                    else
                    {
                        throw new Exception($"Server returned status code: {response.StatusCode}");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.HideLoading();
                    DisplayAlert("Timeout", "Request timed out. Please check your connection and try again.", "OK");
                });
            }
            catch (HttpRequestException ex)
            {
                LogError(ex, "Network error loading services");
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.HideLoading();
                    DisplayAlert("Network Error", "Unable to connect to server. Please check your internet connection.", "OK");
                });
            }
            catch (Exception ex)
            {
                LogError(ex, "Error loading services");
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.HideLoading();
                    DisplayAlert("Error", "Failed to load services. Please try again later.", "OK");
                });
            }
        }

        private async Task AnimateNumberChange(Label label, int targetNumber)
        {
            try
            {
                label.Opacity = 0;
                label.Text = targetNumber.ToString();
                await label.FadeTo(1, 300, Easing.CubicOut);
                await label.ScaleTo(1.1, 100, Easing.CubicOut);
                await label.ScaleTo(1, 100, Easing.CubicIn);
            }
            catch (Exception ex) { LogError(ex, "Animation error"); }
        }

        private async Task AnimateListItems()
        {
            try
            {
                await Task.Delay(50);
                await listView.ScaleTo(0.95, 0);
                await listView.ScaleTo(1, 200, Easing.CubicOut);
            }
            catch (Exception ex) { LogError(ex, "List animation error"); }
        }

        private async void listView_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                var myListView = (ListView)sender;
                var obj = (ServicesList)e.Item;
                if (obj == null) return;

                try
                {
                    Xamarin.Essentials.Vibration.Vibrate(TimeSpan.FromMilliseconds(50));
                }
                catch { }

                await myListView.ScaleTo(0.98, 50, Easing.CubicOut);
                await myListView.ScaleTo(1, 50, Easing.CubicIn);

                routeName = obj.route;
                SelectedRouteLabel.Text = obj.route;

                VehicleTypePicker.SelectedIndex = -1;
                NoOfSeatsEntry.Text = "";
                AmountCollectedEntry.Text = "";
                AmountRemittedEntry.Text = "";
                PinEntry.Text = "";

                await ShowBottomSheet();
                myListView.SelectedItem = null;
            }
            catch (Exception ex)
            {
                LogError(ex, "Error handling item tap");
                await DisplayAlert("Error", "An error occurred. Please try again.", "OK");
            }
        }

        private async Task ShowBottomSheet()
        {
            try
            {
                OverlayBackground.IsVisible = true;
                PaymentBottomSheet.IsVisible = true;

                await Task.WhenAll(
                    OverlayBackground.FadeTo(0.5, 200, Easing.CubicOut),
                    PaymentBottomSheet.TranslateTo(0, 0, 300, Easing.CubicOut));
            }
            catch (Exception ex) { LogError(ex, "Error showing bottom sheet"); }
        }

        private async void CloseBottomSheet(object sender, EventArgs e)
        {
            await HideBottomSheet();
        }

        private async Task HideBottomSheet()
        {
            try
            {
                await Task.WhenAll(
                    OverlayBackground.FadeTo(0, 200, Easing.CubicIn),
                    PaymentBottomSheet.TranslateTo(0, 1000, 300, Easing.CubicIn));

                OverlayBackground.IsVisible = false;
                PaymentBottomSheet.IsVisible = false;
            }
            catch (Exception ex) { LogError(ex, "Error hiding bottom sheet"); }
        }

        private void VehicleTypePicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            try { SessionManager.Instance.UpdateActivity(); }
            catch (Exception ex) { LogError(ex, "Error in vehicle type selection"); }
        }

        private void AmountCollectedEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                ValidateAmounts();
            }
            catch (Exception ex) { LogError(ex, "Error validating amount"); }
        }

        private void ValidateAmounts()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(AmountCollectedEntry.Text) ||
                    string.IsNullOrWhiteSpace(AmountRemittedEntry.Text))
                {
                    AmountValidationLabel.TextColor = Color.FromHex("#6C757D");
                    AmountValidationLabel.Text = "Must be less than or equal to amount collected";
                    return;
                }

                decimal collected = decimal.Parse(AmountCollectedEntry.Text.Replace(",", ""));
                decimal remitted = decimal.Parse(AmountRemittedEntry.Text.Replace(",", ""));

                if (remitted > collected)
                {
                    AmountValidationLabel.TextColor = Color.Red;
                    AmountValidationLabel.Text = "⚠️ Amount remitted cannot exceed collected";
                }
                else
                {
                    AmountValidationLabel.TextColor = Color.Green;
                    AmountValidationLabel.Text = "✓ Valid amount";
                }
            }
            catch
            {
                AmountValidationLabel.TextColor = Color.FromHex("#6C757D");
                AmountValidationLabel.Text = "Must be less than or equal to amount collected";
            }
        }

        private async void CancelPayment_Clicked(object sender, EventArgs e)
        {
            await HideBottomSheet();
        }

        private async void ConfirmPayment_Clicked(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (!ValidatePaymentForm()) return;

                bool confirm = await DisplayAlert(
                    "Confirm Payment",
                    $"Route: {routeName}\nVehicle: {VehicleTypePicker.SelectedItem}\nSeats: {NoOfSeatsEntry.Text}\nCollected: ₦{AmountCollectedEntry.Text}\nRemitted: ₦{AmountRemittedEntry.Text}",
                    "Confirm", "Cancel");

                if (!confirm) return;

                await ProcessPayment();
            }
            catch (Exception ex)
            {
                LogError(ex, "Error confirming payment");
                await DisplayAlert("Error", "An error occurred while processing payment.", "OK");
            }
        }

        private bool ValidatePaymentForm()
        {
            try
            {
                if (VehicleTypePicker.SelectedIndex == -1)
                {
                    DisplayAlert("Validation", "Please select a vehicle type", "OK");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(NoOfSeatsEntry.Text))
                {
                    DisplayAlert("Validation", "Please enter number of seats", "OK");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(AmountCollectedEntry.Text))
                {
                    DisplayAlert("Validation", "Please enter amount collected", "OK");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(AmountRemittedEntry.Text))
                {
                    DisplayAlert("Validation", "Please enter amount remitted", "OK");
                    return false;
                }

                decimal collected = decimal.Parse(AmountCollectedEntry.Text.Replace(",", ""));
                decimal remitted = decimal.Parse(AmountRemittedEntry.Text.Replace(",", ""));

                if (remitted > collected)
                {
                    DisplayAlert("Validation", "Amount remitted cannot exceed amount collected", "OK");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(PinEntry.Text) || PinEntry.Text.Length != 4)
                {
                    DisplayAlert("Validation", "Please enter a valid 4-digit PIN", "OK");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError(ex, "Validation error");
                DisplayAlert("Error", "Invalid input. Please check your entries.", "OK");
                return false;
            }
        }

        private async Task ProcessPayment()
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                    UserDialogs.Instance.ShowLoading("Processing Payment...", MaskType.Black));

                string url = "https://yobe.osoftpay.net/Api/SingleCollections/yobeline";

                var formData = new Dictionary<string, string>
                {
                    { "Email",       MainPage.ValidUserMail },
                    { "VehicleType", VehicleTypePicker.SelectedItem.ToString() },
                    { "ServiceName", routeName },
                    { "NoofSeat",    NoOfSeatsEntry.Text },
                    { "Pin",         PinEntry.Text },
                    { "AmtRemitted", AmountRemittedEntry.Text.Replace(",", "") },
                    { "AmtCollected",AmountCollectedEntry.Text.Replace(",", "") }
                };

                var content = new FormUrlEncodedContent(formData);

                using (HttpResponseMessage response = await HttpClientInstance.PostAsync(url, content))
                {
                    var responseJson = await response.Content.ReadAsStringAsync();

                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        UserDialogs.Instance.HideLoading();

                        if (response.IsSuccessStatusCode)
                        {
                            var paymentResponse = JsonConvert.DeserializeObject<PaymentResponse>(responseJson);

                            if (paymentResponse != null && paymentResponse.respondCode == "00")
                            {
                                lastTransactionNo = paymentResponse.transactionNo ?? DateTime.Now.Ticks.ToString();
                                await HideBottomSheet();
                                await ShowSuccessDialog(paymentResponse);
                            }
                            else
                            {
                                await DisplayAlert("Payment Failed",
                                    paymentResponse?.message ?? "Payment was not successful. Please try again.", "OK");
                            }
                        }
                        else
                        {
                            await DisplayAlert("Error", $"Server error: {response.StatusCode}\n{responseJson}", "OK");
                        }
                    });
                }
            }
            catch (TaskCanceledException)
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.HideLoading();
                    DisplayAlert("Timeout", "Payment request timed out. Please check your connection.", "OK");
                });
            }
            catch (HttpRequestException ex)
            {
                LogError(ex, "Network error during payment");
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.HideLoading();
                    DisplayAlert("Network Error", "Unable to connect to server. Please check your internet.", "OK");
                });
            }
            catch (Exception ex)
            {
                LogError(ex, "Payment processing error");
                Device.BeginInvokeOnMainThread(() =>
                {
                    UserDialogs.Instance.HideLoading();
                    DisplayAlert("Error", "An error occurred while processing payment. Please try again.", "OK");
                });
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  SUCCESS DIALOG
        // ══════════════════════════════════════════════════════════════════════════

        private async Task ShowSuccessDialog(PaymentResponse response)
        {
            try
            {
                string message = $"Transaction Reference: {response.transactionNo}\n" +
                                 $"Vehicle: {response.vehicle}\n" +
                                 $"Destination: {response.destination}\n" +
                                 $"Seats: {response.noofSeat}\n" +
                                 $"Amount Collected: ₦{response.amtCollected}\n" +
                                 $"Amount Remitted: ₦{response.amountRemitted}\n\n" +
                                 $"Would you like to print the receipt?";

                bool printReceipt = await DisplayAlert("✓ Payment Successful", message, "Print Receipt", "Continue");

                if (printReceipt)
                {
                    var receipt = BuildReceiptData(response);
                    await CallPrinterAsync(receipt);
                }
            }
            catch (Exception ex) { LogError(ex, "Error showing success dialog"); }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  RECEIPT DATA BUILDER — logo + QR barcode
        // ══════════════════════════════════════════════════════════════════════════

        private ReceiptData BuildReceiptData(PaymentResponse response)
        {
            string verifyUrl =
                $"https://yobe.osoftpay.net/singlecollections/verify" +
                $"?TransactId={Uri.EscapeDataString(response.transactionNo ?? "")}";

            var items = new List<ReceiptItem>
            {
                new ReceiptItem
                {
                    Description = "Vehicle Type",
                    Amount      = 0m,
                    SubText     = response.vehicle ?? VehicleTypePicker.SelectedItem?.ToString() ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "Route / Service",
                    Amount      = 0m,
                    SubText     = routeName ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "No. of Seats",
                    Amount      = 0m,
                    SubText     = response.noofSeat.ToString()
                },
                new ReceiptItem
                {
                    Description = "Amount Collected",
                    Amount      = 0m,
                    SubText     = $"₦{response.amtCollected:N2}"
                },
                new ReceiptItem
                {
                    Description = "Amount Remitted",
                    Amount      = response.amountRemitted
                }
            };

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: 09070701616,07017639494",
                ReceiptNumber = response.transactionNo ?? "N/A",
                AgentName = MainPage.Name ?? "N/A",
                CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                SuperAgent = string.Empty,
                PrintDate = DateTime.Now,
                Items = items,
                TotalAmount = response.amountRemitted,
                AmountPaid = response.amountRemitted,
                AmountLeft = 0m,
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = verifyUrl   // → QR code printed on receipt
            };
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  PRINTER — NEW SDK
        // ══════════════════════════════════════════════════════════════════════════

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
        // ══════════════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════════════════

        protected override void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                SessionManager.Instance.UpdateActivity();

                Device.BeginInvokeOnMainThread(async () =>
                {
                    listView.Opacity = 0;
                    listView.TranslationY = 20;
                    await Task.WhenAll(
                        listView.FadeTo(1, 300, Easing.CubicOut),
                        listView.TranslateTo(0, 0, 300, Easing.CubicOut));
                });
            }
            catch (Exception ex) { LogError(ex, "Error in OnAppearing"); }
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    bool result = await DisplayAlert("Confirm Exit", "Go back?", "Yes", "No");
                    if (result) await Navigation.PopAsync();
                }
                catch (Exception ex) { LogError(ex, "Error handling back button"); }
            });
            return true;
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
    }
}