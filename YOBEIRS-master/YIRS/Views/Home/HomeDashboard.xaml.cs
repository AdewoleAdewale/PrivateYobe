using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class HomeDashboard : ContentPage
    {

        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);
        public static string superAgent { get; set; }
        public static string agent { get; set; }
        public static string cashoutBalance { get; set; }
        private bool isFloatingMenuOpen = false;

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
            public string Intro { get { return " You have Performed a total of " + HD.Count + " transactions within your search dates"; } }
            public string Summary { get { return " You have Performed a total of " + HD.Count + " transactions"; } }
            public decimal Size { get { return HD.Count; } }
        }

        public HomeDashboard()
        {
            InitializeComponent();
            Wlcomenites.Text = "WELCOME, " + MainPage.Name;
            supagentname.Text = MainPage.Super_Agent;
            collectionpointname.Text = MainPage.CollectionPoint;
            LoadRecentTransaction();

            // Track user activity on page interactions
            TrackUserActivity();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Update session activity when page appears
            SessionManager.Instance.UpdateActivity();
        }

        /// <summary>
        /// Track user activity for session management
        /// </summary>
        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private async void LoadRecentTransaction()
        {
            try
            {
                // Update session activity
                SessionManager.Instance.UpdateActivity();

                using (UserDialogs.Instance.Loading("Fetching Recent Transactions...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(1000);

                    string SearchStringFrom = DateTime.Today.AddDays(-7).ToString("MM/dd/yyyy");
                    string SearchStringTo = DateTime.Today.AddDays(+1).ToString("MM/dd/yyyy");

                    string url = $"https://yobe.osoftpay.net/api/TaskPayers/gettransaction?Email={MainPage.ValidUserMail}&SearchFrom={SearchStringFrom}&SearchTo={SearchStringTo}";

                    using (var httpClientHandler = new HttpClientHandler())
                    {
                        httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                        using (HttpClient client = new HttpClient(httpClientHandler))
                        {
                            client.Timeout = TimeSpan.FromSeconds(30);
                            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                            using (HttpResponseMessage response = await client.GetAsync(url))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    using (HttpContent content = response.Content)
                                    {
                                        var json = await content.ReadAsStringAsync();

                                        if (!string.IsNullOrEmpty(json))
                                        {
                                            List<HistoryData> items = JsonConvert.DeserializeObject<List<HistoryData>>(json);

                                            if (items != null && items.Count > 0)
                                            {
                                                BindingContext = new HistoryDataHeaderFooter { HD = items.Take(10).ToList() };
                                                ShowToast("Transactions loaded successfully", ToastType.Success);
                                            }
                                            else
                                            {
                                                ShowToast("No recent transactions found", ToastType.Info);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    ShowToast($"Server error: {response.StatusCode}", ToastType.Error);
                                }
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                ShowToast("Request timeout. Please check your connection", ToastType.Error);
            }
            catch (HttpRequestException ex)
            {
                ShowToast("Network error. Please check your internet connection", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"HTTP Error: {ex.Message}");
            }
            catch (JsonException)
            {
                ShowToast("Error processing transaction data", ToastType.Error);
            }
            catch (Exception ex)
            {
                ShowToast("An unexpected error occurred", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Home.Direct());
            }
            catch (Exception ex)
            {
                ShowToast("Failed to open collection page", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
            }
        }

        private void TapGestureRecognizer_Tapped_3(object sender, EventArgs e)
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    SessionManager.Instance.UpdateActivity();

                    string action = await DisplayActionSheet(
                        "QUICK ACTIONS",
                        "CANCEL",
                        null,
                        "CHANGE PASSWORD",
                        "CHANGE PIN",
                        "TEST PRINTER",
                        "LOGOUT"
                    );

                    switch (action)
                    {
                        case "LOGOUT":
                            await HandleLogout();
                            break;
                        case "CHANGE PASSWORD":
                            await Navigation.PushAsync(new Views.ChangePassword());
                            break;
                        case "CHANGE PIN":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;
                        case "TEST PRINTER":
                            await TestPrinterConnectionAsync();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ShowToast("Action failed. Please try again", ToastType.Error);
                    System.Diagnostics.Debug.WriteLine($"Action Error: {ex.Message}");
                }
            });
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    SessionManager.Instance.UpdateActivity();

                    var result = await this.DisplayAlert(
                        "EXIT APPLICATION",
                        "Do you really want to exit?",
                        "Yes",
                        "No"
                    );

                    if (result)
                    {
                        // Stop session monitoring on exit
                        SessionManager.Instance.StopSession();
                        System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Back Button Error: {ex.Message}");
                }
            });

            return true;
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
                    await _printer.PrintTestPageAsync(
     retryPolicy: null,        // null → uses PrintRetryPolicy.Default
     progress: null,        // no UI progress needed on the test page
     cancellationToken: cts.Token);
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
        private async void TapGestureRecognizer_Tapped_2(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                using (UserDialogs.Instance.Loading("Loading...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(1000);
                    await Navigation.PushAsync(new Views.Home.Enumerate());
                }
            }
            catch (Exception ex)
            {
                ShowToast("Failed to open enumeration page", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_4(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Home.History());
            }
            catch (Exception ex)
            {
                ShowToast("Failed to open history page", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_5(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                using (UserDialogs.Instance.Loading("Fetching balance...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(500);

                    string url = $"https://yobe.osoftpay.net/api/singlecollections/getbalance?Pin={MainPage.Pin}&Email={MainPage.ValidUserMail}";

                    using (var httpClientHandler = new HttpClientHandler())
                    {
                        httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                        using (HttpClient client = new HttpClient(httpClientHandler))
                        {
                            client.Timeout = TimeSpan.FromSeconds(30);
                            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                            using (HttpResponseMessage response = await client.GetAsync(url))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    using (HttpContent content = response.Content)
                                    {
                                        var json = await content.ReadAsStringAsync();
                                        BalanceResponse result = JsonConvert.DeserializeObject<BalanceResponse>(json);

                                        if (result != null)
                                        {
                                            agent = result.agent;
                                            superAgent = result.superAgent;
                                            cashoutBalance = result.cashoutBalance;

                                            await Navigation.PushAsync(new Views.Home.AgentBalance());
                                        }
                                        else
                                        {
                                            ShowToast("Unable to fetch balance data", ToastType.Warning);
                                        }
                                    }
                                }
                                else
                                {
                                    ShowToast($"Server error: {response.StatusCode}", ToastType.Error);
                                }
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                ShowToast("Request timeout. Please try again", ToastType.Error);
            }
            catch (HttpRequestException)
            {
                ShowToast("Network error. Check your connection", ToastType.Error);
            }
            catch (Exception ex)
            {
                ShowToast("Failed to fetch balance", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Balance Error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_6(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Home.PaymentStatus());
            }
            catch (Exception ex)
            {
                ShowToast("Failed to open payment status", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_7(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Home.Gazette2());
            }
            catch (Exception ex)
            {
                ShowToast("Failed to open services page", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
            }
        }

        private async void TapGestureRecognizer_Tapped_8(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.ChangeTransferPIN());
            }
            catch (Exception ex)
            {
                ShowToast("Failed to open PIN change page", ToastType.Error);
                System.Diagnostics.Debug.WriteLine($"Navigation Error: {ex.Message}");
            }
        }

        // Floating Button Animation and Menu Toggle
        private async void FloatingButton_Tapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (isFloatingMenuOpen)
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
                System.Diagnostics.Debug.WriteLine($"Floating Menu Error: {ex.Message}");
            }
        }

        private async Task OpenFloatingMenu()
        {
            floatingMenu.IsVisible = true;
            floatingMenu.Opacity = 0;
            floatingMenu.TranslationY = 50;

            await floatingPrintButton.RotateTo(45, 250, Easing.CubicOut);

            await Task.WhenAll(
                floatingMenu.FadeTo(1, 250, Easing.CubicOut),
                floatingMenu.TranslateTo(0, 0, 300, Easing.SpringOut)
            );

            isFloatingMenuOpen = true;
        }

        private async Task CloseFloatingMenu()
        {
            await floatingPrintButton.RotateTo(0, 250, Easing.CubicIn);

            await Task.WhenAll(
                floatingMenu.FadeTo(0, 200, Easing.CubicIn),
                floatingMenu.TranslateTo(0, 50, 250, Easing.CubicIn)
            );

            floatingMenu.IsVisible = false;
            isFloatingMenuOpen = false;
        }

        private async void TestPrinter_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            await CloseFloatingMenu();
            await TestPrinterConnectionAsync();
        }

        private async void Logout_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            await CloseFloatingMenu();
            await HandleLogout();
        }

        private async Task HandleLogout()
        {
            try
            {
                var result = await DisplayAlert(
                    "LOGOUT",
                    "Are you sure you want to logout?",
                    "Yes",
                    "No"
                );

                if (result)
                {
                    ShowToast("Logging out...", ToastType.Info);
                    await Task.Delay(500);

                    // Stop session monitoring
                    SessionManager.Instance.StopSession();

                    // Clear session but keep credentials if Remember Me was checked
                    App.IsUserLoggedIn = false;

                    // Navigate back to login
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        Application.Current.MainPage = new NavigationPage(new Views.MainPage())
                        {
                            BarBackgroundColor = Color.FromHex("#004225"),
                            BarTextColor = Color.White
                        };
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logout Error: {ex.Message}");
            }
        }

        // Toast Notification Helper
        private enum ToastType
        {
            Success,
            Error,
            Warning,
            Info
        }

        private void ShowToast(string message, ToastType type)
        {
            try
            {
                var toastConfig = new ToastConfig(message)
                {
                    Duration = TimeSpan.FromSeconds(3),
                    Position = ToastPosition.Bottom
                };

                switch (type)
                {
                    case ToastType.Success:
                        toastConfig.BackgroundColor = System.Drawing.Color.FromArgb(76, 175, 80);
                        toastConfig.MessageTextColor = System.Drawing.Color.White;
                        break;
                    case ToastType.Error:
                        toastConfig.BackgroundColor = System.Drawing.Color.FromArgb(244, 67, 54);
                        toastConfig.MessageTextColor = System.Drawing.Color.White;
                        break;
                    case ToastType.Warning:
                        toastConfig.BackgroundColor = System.Drawing.Color.FromArgb(255, 152, 0);
                        toastConfig.MessageTextColor = System.Drawing.Color.White;
                        break;
                    case ToastType.Info:
                        toastConfig.BackgroundColor = System.Drawing.Color.FromArgb(33, 150, 243);
                        toastConfig.MessageTextColor = System.Drawing.Color.White;
                        break;
                }

                UserDialogs.Instance.Toast(toastConfig);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Toast Error: {ex.Message}");
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

    }

    internal class BalanceResponse
    {
        public string superAgent { get; set; }
        public string agent { get; set; }
        public string cashoutBalance { get; set; }
    }
}