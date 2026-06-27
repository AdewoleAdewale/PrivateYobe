using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
    public partial class StateLineDashboard : ContentPage
    {

        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);
        #region Private Fields
        // Add this static field at the top of your class
        private static HttpClient _httpClient;
        private static readonly object _httpClientLock = new object();
        private bool _isLoadingHistory = false;
        private bool _isFloatingMenuOpen = false;

        private const int REQUEST_TIMEOUT_SECONDS = 30;
        private const int RECENT_TRANSACTIONS_COUNT = 5;
        #endregion

        #region Properties
        public string AgentName { get; set; }
        public string AgentEmail { get; set; }
        public string CollectionPoint { get; set; }
        public ObservableCollection<TransactionHistory> RecentTransactions { get; set; }
        #endregion

        #region Constructor
        public StateLineDashboard()
        {
            try
            {
                InitializeComponent();


                RecentTransactions = new ObservableCollection<TransactionHistory>();

                LoadUserDetails();
                BindingContext = this;

                // Load recent history on page load
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(500); // Small delay for UI to render
                    await LoadRecentHistoryAsync();
                });
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
            }
        }


        #endregion

        #region Lifecycle Methods
        protected override void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                // Start session monitoring
                SessionManager.Instance.StartSession();

                // Animate page appearance
                _ = AnimatePageAppearance();
            }
            catch (Exception ex)
            {
                LogError("OnAppearing", ex);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try
            {
                // Close floating menu if open
                if (_isFloatingMenuOpen)
                {
                    _ = CloseFloatingMenu();
                }
            }
            catch (Exception ex)
            {
                LogError("OnDisappearing", ex);
            }
        }
        #endregion

        #region Data Loading Methods
        private void LoadUserDetails()
        {
            try
            {
                AgentName = MainPage.Name ?? "Agent";
                AgentEmail = MainPage.ValidUserMail ?? "N/A";
                CollectionPoint = MainPage.CollectionPoint ?? "Not Assigned";

                // Update UI
                Device.BeginInvokeOnMainThread(() =>
                {
                    AgentNameLabel.Text = AgentName;
                    CollectionPointLabel.Text = CollectionPoint;
                });

                System.Diagnostics.Debug.WriteLine($"Loaded Agent: {AgentName}, Email: {AgentEmail}");
            }
            catch (Exception ex)
            {
                LogError("LoadUserDetails", ex);
            }
        }



        // Fixed GetHttpClient method
        private static HttpClient GetHttpClient()
        {
            if (_httpClient == null)
            {
                lock (_httpClientLock)
                {
                    if (_httpClient == null)
                    {
                        try
                        {
                            var handler = new HttpClientHandler
                            {
                                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                                {
                                    // Accept all certificates temporarily
                                    System.Diagnostics.Debug.WriteLine($"SSL Validation - Errors: {errors}");
                                    if (cert != null)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Certificate Subject: {cert.Subject}");
                                        System.Diagnostics.Debug.WriteLine($"Certificate Issuer: {cert.Issuer}");
                                    }
                                    return true; // Accept all certificates
                                }
                            };

                            // Only set SslProtocols if on Android

                            handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                                  System.Security.Authentication.SslProtocols.Tls11;


                            _httpClient = new HttpClient(handler)
                            {
                                Timeout = TimeSpan.FromSeconds(30)
                            };

                            System.Diagnostics.Debug.WriteLine("HttpClient initialized with SSL bypass");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error creating HttpClient: {ex.Message}");
                            // Fallback to basic HttpClient
                            _httpClient = new HttpClient
                            {
                                Timeout = TimeSpan.FromSeconds(30)
                            };
                        }
                    }
                }
            }
            return _httpClient;
        }

        private async Task LoadRecentHistoryAsync()
        {
            if (_isLoadingHistory || string.IsNullOrEmpty(AgentEmail))
            {
                return;
            }

            _isLoadingHistory = true;

            try
            {
                await Device.InvokeOnMainThreadAsync(() =>
                {
                    HistoryLoadingIndicator.IsVisible = true;
                    HistoryLoadingIndicator.IsRunning = true;
                    EmptyHistoryMessage.IsVisible = false;
                });

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string searchFrom = DateTime.Today.AddDays(-7).ToString("MM-dd-yyyy");
                string searchTo = Uri.EscapeDataString($"{today} 23:59:59");
                string email = Uri.EscapeDataString(AgentEmail);

                string url = $"https://yobe.osoftpay.net/Api/SingleCollections/YobelineHistory?Email={email}&SearchFrom={searchFrom}&SearchTo={searchTo}";

                System.Diagnostics.Debug.WriteLine($"Fetching history from: {url}");

                var httpClient = GetHttpClient();

                System.Diagnostics.Debug.WriteLine("Making HTTP request...");
                var response = await httpClient.GetAsync(url);

                System.Diagnostics.Debug.WriteLine($"Response Status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Response received: {json.Substring(0, Math.Min(200, json.Length))}...");

                var transactions = JsonConvert.DeserializeObject<List<TransactionHistory>>(json);

                await Device.InvokeOnMainThreadAsync(() =>
                {
                    RecentTransactions.Clear();

                    if (transactions != null && transactions.Any())
                    {
                        var recentItems = transactions
                            .OrderByDescending(t => t.DateRecorded)
                            .Take(RECENT_TRANSACTIONS_COUNT)
                            .ToList();

                        foreach (var transaction in recentItems)
                        {
                            RecentTransactions.Add(transaction);
                        }

                        TransactionHistoryList.ItemsSource = RecentTransactions;
                        EmptyHistoryMessage.IsVisible = false;
                        System.Diagnostics.Debug.WriteLine($"Loaded {RecentTransactions.Count} recent transactions");
                    }
                    else
                    {
                        EmptyHistoryMessage.IsVisible = true;
                        System.Diagnostics.Debug.WriteLine("No transactions found");
                    }
                });
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
                LogError("LoadRecentHistoryAsync - HTTP", ex);
                await ShowToast("Unable to load transaction history. Check your connection.", ToastType.Error);
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON Error: {ex.Message}");
                LogError("LoadRecentHistoryAsync - JSON", ex);
                await ShowToast("Invalid data received from server", ToastType.Error);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"General Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                LogError("LoadRecentHistoryAsync", ex);
                await ShowToast("Failed to load transaction history", ToastType.Error);
            }
            finally
            {
                _isLoadingHistory = false;
                await Device.InvokeOnMainThreadAsync(() =>
                {
                    HistoryLoadingIndicator.IsVisible = false;
                    HistoryLoadingIndicator.IsRunning = false;
                });
            }
        }
        #endregion

        #region Event Handlers
        private async void OnRemitButtonClicked(object sender, EventArgs e)
        {
            try
            {
                await AnimateButtonPress((View)sender);
                await ShowToast("Opening remittance page...", ToastType.Info);
                await Navigation.PushAsync(new Routelist());
            }
            catch (Exception ex)
            {
                LogError("OnRemitButtonClicked", ex);
            }
        }

        private async void OnHistoryButtonClicked(object sender, EventArgs e)
        {
            try
            {
                await AnimateButtonPress((View)sender);
                await Navigation.PushAsync(new History());
            }
            catch (Exception ex)
            {
                LogError("OnHistoryButtonClicked", ex);
            }
        }

        private async void OnSettingsButtonClicked(object sender, EventArgs e)
        {
            try
            {
                await AnimateButtonPress((View)sender);

                await AppsettingCode();
            }
            catch (Exception ex)
            {
                LogError("OnSettingsButtonClicked", ex);
            }
        }

        private async void OnFloatingActionButtonClicked(object sender, EventArgs e)
        {
            try
            {
                if (_isFloatingMenuOpen)
                {
                    await CloseFloatingMenu();
                }
                else
                {
                    await OpenFloatingMenu();
                }
            }
            catch (Exception ex)
            {
                LogError("OnFloatingActionButtonClicked", ex);
            }
        }

        private async void OnTestPrintClicked(object sender, EventArgs e)
        {
            try
            {
                await CloseFloatingMenu();
                await AnimateButtonPress((View)sender);
                await TestPrinterConnectionAsync();
            }
            catch (Exception ex)
            {
                LogError("OnTestPrintClicked", ex);
            }
        }

        private async void OnAppSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                await CloseFloatingMenu();
                await AnimateButtonPress((View)sender);

                await AppsettingCode();
            }
            catch (Exception ex)
            {
                LogError("OnAppSettingsClicked", ex);
            }
        }

        private async Task AppsettingCode()
        {
            try
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    var action = await DisplayActionSheet("MENU OPTIONS", "CANCEL", null, "CHANGE PASSWORD", "CHANGE PIN", "LOGOUT");

                    switch (action)
                    {
                        case "LOGOUT":
                            await PerformLogout();
                            break;

                        case "CHANGE PASWWORD":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;

                        case "CHANGE PIN":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;


                    }
                });
            }
            catch (Exception ex)
            {
                LogError("Appsetting", ex);
                UserDialogs.Instance.HideLoading();
                await ShowToast("Appsetting Failed. Please try again.", ToastType.Error);
            }
        }

        private void TapGestureRecognizer_Tapped_3(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                Device.BeginInvokeOnMainThread(async () =>
                {
                    var action = await DisplayActionSheet("MENU OPTIONS", "CANCEL", null, "CHANGE PASSWORD", "CHANGE PIN", "LOGOUT");

                    switch (action)
                    {
                        case "LOGOUT":
                            await PerformLogout();
                            break;

                        case "CHANGE PASWWORD":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;

                        case "CHANGE PIN":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;


                    }
                });
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_3", ex);
            }
        }

        private async Task TestPrinterConnectionAsync()
        {
            try
            {
                // Quick probe first — gives a fast, friendly message if nothing is paired/on
                bool available = await _printer.IsPrinterAvailableAsync();
                if (!available)
                {
                    await DisplayAlert("Printer Not Found",
                        "No paired printer detected.\n\n" +
                        "• Ensure Bluetooth is ON\n" +
                        "• Printer is powered on\n" +
                        "• Printer is paired in Android Settings",
                        "OK");
                    return;
                }

                using (UserDialogs.Instance.Loading("Printing test page...", null, null, true, MaskType.Black))
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    await _printer.PrintTestPageAsync(retryPolicy: null, progress: null, cancellationToken: cts.Token);
                }

                await DisplayAlert("Test Print ✅", "Test page sent successfully!", "OK");
            }
            catch (PrinterException pex)
            {
                await DisplayAlert("Printer Error", pex.Message, "OK");
                Debug.WriteLine($"[Dashboard] PrinterException: {pex}");
            }
            catch (OperationCanceledException)
            {
                await DisplayAlert("Timed Out",
                    "Print timed out. Check that the printer is powered on and in range.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Printer Error",
                    "Failed to connect to printer.\n\n" +
                    "• Ensure Bluetooth is ON\n" +
                    "• Printer is paired & powered on",
                    "OK");
                Debug.WriteLine($"[Dashboard] TestPrinterConnectionAsync error: {ex}");
            }
        }
        private async Task ShowErrorAlert(string title, string message, string buttonText)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert(title, message, buttonText);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to show alert: {ex.Message}");
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            try
            {
                await CloseFloatingMenu();

                bool confirm = await DisplayAlert(
                    "Logout",
                    "Are you sure you want to logout?",
                    "Yes",
                    "No");

                if (confirm)
                {
                    await PerformLogout();
                }
            }
            catch (Exception ex)
            {
                LogError("OnLogoutClicked", ex);
            }
        }

        private async void OnTransactionItemTapped(object sender, ItemTappedEventArgs e)
        {
            try
            {
                if (e.Item is TransactionHistory transaction)
                {
                    await DisplayAlert(
                        "Transaction Details",
                        $"Destination: {transaction.Destination}\n" +
                        $"Transaction ID: {transaction.TransactionId}\n" +
                        $"Amount Collected: ₦{transaction.AmountCollected:N2}\n" +
                        $"Amount Remitted: ₦{transaction.AmountRemitted:N2}\n" +
                        $"Difference: ₦{transaction.Differences:N2}\n" +
                        $"Date: {transaction.FormattedDate}",
                        "OK");
                }

                // Deselect item
                ((ListView)sender).SelectedItem = null;
            }
            catch (Exception ex)
            {
                LogError("OnTransactionItemTapped", ex);
            }
        }
        #endregion

        #region Floating Menu Methods
        private async Task OpenFloatingMenu()
        {
            try
            {
                _isFloatingMenuOpen = true;

                FloatingMenuOverlay.IsVisible = true;
                FloatingMenu.IsVisible = true;

                FloatingMenuOverlay.Opacity = 0;
                FloatingMenu.TranslationY = 300;
                FloatingMenu.Opacity = 0;

                // Rotate FAB
                await FloatingActionButton.RotateTo(45, 200, Easing.CubicOut);

                // Show overlay and menu
                await Task.WhenAll(
                    FloatingMenuOverlay.FadeTo(1, 200),
                    FloatingMenu.TranslateTo(0, 0, 300, Easing.SpringOut),
                    FloatingMenu.FadeTo(1, 300)
                );
            }
            catch (Exception ex)
            {
                LogError("OpenFloatingMenu", ex);
            }
        }

        private async Task CloseFloatingMenu()
        {
            try
            {
                _isFloatingMenuOpen = false;

                // Rotate FAB back
                await FloatingActionButton.RotateTo(0, 200, Easing.CubicIn);

                // Hide menu and overlay
                await Task.WhenAll(
                    FloatingMenuOverlay.FadeTo(0, 200),
                    FloatingMenu.TranslateTo(0, 300, 250, Easing.CubicIn),
                    FloatingMenu.FadeTo(0, 250)
                );

                FloatingMenuOverlay.IsVisible = false;
                FloatingMenu.IsVisible = false;
            }
            catch (Exception ex)
            {
                LogError("CloseFloatingMenu", ex);
            }
        }

        private async void OnFloatingMenuOverlayTapped(object sender, EventArgs e)
        {
            await CloseFloatingMenu();
        }
        #endregion

        #region Logout Method
        private async Task PerformLogout()
        {
            try
            {
                UserDialogs.Instance.ShowLoading("Logging out...");

                await Task.Delay(500);

                // Stop session
                SessionManager.Instance.StopSession();

                // Clear app state
                App.IsUserLoggedIn = false;
                await SecureStorageService.ClearCredentialsAsync();

                UserDialogs.Instance.HideLoading();

                await ShowToast("Logged out successfully", ToastType.Success);

                await Task.Delay(500);

                // Navigate to login
                Device.BeginInvokeOnMainThread(() =>
                {
                    Application.Current.MainPage = new NavigationPage(new MainPage())
                    {
                        BarBackgroundColor = Color.FromHex("#004225"),
                        BarTextColor = Color.White
                    };
                });
            }
            catch (Exception ex)
            {
                LogError("PerformLogout", ex);
                UserDialogs.Instance.HideLoading();
                await ShowToast("Logout failed. Please try again.", ToastType.Error);
            }
        }
        #endregion

        #region Animation Methods
        private async Task AnimatePageAppearance()
        {
            try
            {
                HeaderCard.Opacity = 0;
                HeaderCard.TranslationY = -30;
                ActionButtonsContainer.Opacity = 0;
                ActionButtonsContainer.TranslationY = -20;
                RemitCard.Opacity = 0;
                RemitCard.Scale = 0.9;
                HistorySection.Opacity = 0;
                HistorySection.TranslationY = 30;

                await Task.WhenAll(
                    HeaderCard.FadeTo(1, 400),
                    HeaderCard.TranslateTo(0, 0, 400, Easing.CubicOut)
                );

                await Task.Delay(100);

                await Task.WhenAll(
                    ActionButtonsContainer.FadeTo(1, 400),
                    ActionButtonsContainer.TranslateTo(0, 0, 400, Easing.CubicOut)
                );

                await Task.Delay(100);

                await Task.WhenAll(
                    RemitCard.FadeTo(1, 500),
                    RemitCard.ScaleTo(1, 500, Easing.SpringOut)
                );

                await Task.Delay(100);

                await Task.WhenAll(
                    HistorySection.FadeTo(1, 400),
                    HistorySection.TranslateTo(0, 0, 400, Easing.CubicOut)
                );
            }
            catch (Exception ex)
            {
                LogError("AnimatePageAppearance", ex);
            }
        }

        private async Task AnimateButtonPress(View button)
        {
            try
            {
                await button.ScaleTo(0.95, 100);
                await button.ScaleTo(1.0, 100, Easing.SpringOut);
            }
            catch (Exception ex)
            {
                LogError("AnimateButtonPress", ex);
            }
        }
        #endregion

        #region Helper Methods
        private enum ToastType
        {
            Success,
            Error,
            Info,
            Warning
        }

        private async Task ShowToast(string message, ToastType type)
        {
            try
            {
                await Device.InvokeOnMainThreadAsync(() =>
                {
                    ToastConfig bgColor;

                    switch (type)
                    {
                        case ToastType.Success:
                            bgColor = new ToastConfig(message) { BackgroundColor = System.Drawing.Color.FromArgb(76, 175, 80) };
                            break;
                        case ToastType.Error:
                            bgColor = new ToastConfig(message) { BackgroundColor = System.Drawing.Color.FromArgb(244, 67, 54) };
                            break;
                        case ToastType.Warning:
                            bgColor = new ToastConfig(message) { BackgroundColor = System.Drawing.Color.FromArgb(255, 152, 0) };
                            break;
                        default:
                            bgColor = new ToastConfig(message) { BackgroundColor = System.Drawing.Color.FromArgb(33, 150, 243) };
                            break;
                    }

                    bgColor.Duration = TimeSpan.FromSeconds(3);
                    UserDialogs.Instance.Toast(bgColor);
                });
            }
            catch (Exception ex)
            {
                LogError("ShowToast", ex);
            }
        }

        private void LogError(string method, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] StateLineDashboard.{method}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[INNER ERROR]: {ex.InnerException.Message}");
            }
        }
        #endregion
    }

    #region Transaction History Model
    public class TransactionHistory
    {
        [JsonProperty("destination")]
        public string Destination { get; set; }

        [JsonProperty("amountRemitted")]
        public decimal AmountRemitted { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }

        [JsonProperty("amountCollected")]
        public decimal AmountCollected { get; set; }

        [JsonProperty("dateRecorded")]
        public DateTime DateRecorded { get; set; }

        [JsonProperty("differences")]
        public decimal Differences { get; set; }

        // Display properties
        public string FormattedDate => DateRecorded.ToString("MMM dd, yyyy hh:mm tt");

        public string FormattedAmountCollected => $"₦{AmountCollected:N2}";

        public string FormattedAmountRemitted => $"₦{AmountRemitted:N2}";

        public string FormattedDifferences => $"₦{Differences:N2}";

        public Color DifferenceColor => Differences >= 0 ? Color.Green : Color.Red;

        public string ShortTransactionId => TransactionId?.Length > 12
            ? $"{TransactionId.Substring(0, 6)}...{TransactionId.Substring(TransactionId.Length - 6)}"
            : TransactionId;
    }
    #endregion
}