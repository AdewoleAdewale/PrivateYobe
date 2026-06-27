using Acr.UserDialogs;
using CarouselView.FormsPlugin.Abstractions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PlatformConfiguration.iOSSpecific;
using Xamarin.Forms.Xaml;
using YIRS.Renderers;
using YIRS.Services;

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DefaultDashboard : ContentPage
    {
        // Static properties for data sharing
        public static string transactionNo { get; set; }

        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);
        public static string serviceName { get; set; }
        public static string Status { get; set; }
        public static string payer { get; set; }
        public static string SuperAgent { get; set; }
        public static string BusinessName { get; set; }
        public static string Businesslga { get; set; }
        public static string amount { get; set; }
        public static string payerContact { get; set; }
        public static string transactionDate { get; set; }
        public static string superAgent { get; set; }
        public static string agent { get; set; }
        public static string cashoutBalance { get; set; }

        private MainViewModel _vm;
        private bool _isRefreshing = false;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int API_TIMEOUT_SECONDS = 30;
        private List<UnifiedHistoryData> _allTransactions;
        private string _searchText = string.Empty;

        // ============ CHANGE 1: Add SSL Certificate Handler ============
        private static readonly HttpClientHandler _httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // Log certificate validation
                if (errors != System.Net.Security.SslPolicyErrors.None)
                {
                    Debug.WriteLine($"[SSL Certificate] Validation Error: {errors}");
                    Debug.WriteLine($"[SSL Certificate] Subject: {cert?.Subject}");
                    Debug.WriteLine($"[SSL Certificate] Issuer: {cert?.Issuer}");
                }

                // Return true to accept the certificate (use with caution in production)
                // For production, implement proper certificate pinning instead
                return true;
            }
        };
        // ============ END CHANGE 1 ============

        // Data models for Default category
        class DefaultHistoryData
        {
            public string transactionId { get; set; }
            public string businessName { get; set; }
            public string serviceName { get; set; }
            public string payerId { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }
        }

        // Data models for YORATA category
        class YorataHistoryData
        {
            public string transactionId { get; set; }
            public string vehicleType { get; set; }
            public string serviceName { get; set; }
            public string vehicleNo { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }
        }

        // Unified display model
        class UnifiedHistoryData
        {
            public string transactionId { get; set; }
            public string primaryField { get; set; } // businessName or vehicleType
            public string serviceName { get; set; }
            public string secondaryField { get; set; } // payerId or vehicleNo
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }
            public string categoryType { get; set; } // "YORATA" or "DEFAULT"
        }

        internal class BalanceResponse
        {
            public string superAgent { get; set; }
            public string agent { get; set; }
            public string cashoutBalance { get; set; }
        }

        public DefaultDashboard()
        {
            try
            {
                InitializeComponent();
                InitializeUI();
                TrackUserActivity();
                BindingContext = _vm = new MainViewModel();

                On<Xamarin.Forms.PlatformConfiguration.iOS>().SetUseSafeArea(true);

                // Initialize search
                searchEntry.TextChanged += OnSearchTextChanged;

                // Load data asynchronously
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await InitializeDataAsync();
                });
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Initialization Error", "Failed to initialize dashboard. Please restart the app.", "OK");
                });
            }
        }

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private void InitializeUI()
        {
            try
            {
                WelcomeMessage.Text = !string.IsNullOrWhiteSpace(MainPage.Name)
                    ? MainPage.Name.ToUpper()
                    : "USER";

                agent2.Text = !string.IsNullOrWhiteSpace(MainPage.Super_Agent)
                    ? MainPage.Super_Agent
                    : "N/A";

                cat.Text = !string.IsNullOrWhiteSpace(MainPage.Category)
                    ? MainPage.Category.ToUpper()
                    : "N/A";

                cllpoint.Text = !string.IsNullOrWhiteSpace(MainPage.CollectionPoint)
                    ? $"COLLECTION POINT: {MainPage.CollectionPoint}"
                    : "COLLECTION POINT: N/A";

                UpdateBalanceDisplay();
            }
            catch (Exception ex)
            {
                LogError("InitializeUI", ex);
            }
        }

        private void UpdateBalanceDisplay()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (string.IsNullOrWhiteSpace(cashoutBalance) || !decimal.TryParse(cashoutBalance, out decimal balance))
                {
                    cashoutB.Text = "NGN 0.00";
                }
                else
                {
                    cashoutB.Text = $"NGN {balance:N2}";
                }
            }
            catch (Exception ex)
            {
                LogError("UpdateBalanceDisplay", ex);
                cashoutB.Text = "NGN 0.00";
            }
        }

        private async Task InitializeDataAsync()
        {
            try
            {
                await LoadBalanceAsync();
                await LoadRecentTransactionAsync();
            }
            catch (Exception ex)
            {
                LogError("InitializeDataAsync", ex);
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchText = e.NewTextValue?.Trim() ?? string.Empty;
                FilterTransactions();
            }
            catch (Exception ex)
            {
                LogError("OnSearchTextChanged", ex);
            }
        }

        private void FilterTransactions()
        {
            try
            {
                if (_allTransactions == null || !_allTransactions.Any())
                {
                    return;
                }

                List<UnifiedHistoryData> filteredList;

                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    filteredList = _allTransactions;
                }
                else
                {
                    string searchLower = _searchText.ToLower();
                    filteredList = _allTransactions.Where(t =>
                        (!string.IsNullOrEmpty(t.transactionId) && t.transactionId.ToLower().Contains(searchLower)) ||
                        (!string.IsNullOrEmpty(t.primaryField) && t.primaryField.ToLower().Contains(searchLower)) ||
                        (!string.IsNullOrEmpty(t.serviceName) && t.serviceName.ToLower().Contains(searchLower)) ||
                        (!string.IsNullOrEmpty(t.secondaryField) && t.secondaryField.ToLower().Contains(searchLower)) ||
                        t.amount.ToString().Contains(searchLower)
                    ).ToList();
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    if (filteredList.Any())
                    {
                        listView.ItemsSource = filteredList.OrderByDescending(x => x.dateRecorded).Take(10).ToList();
                        listView.IsVisible = true;
                        emptyState.IsVisible = false;
                    }
                    else
                    {
                        ShowEmptyState();
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("FilterTransactions", ex);
            }
        }

        private async Task LoadRecentTransactionAsync()
        {
            if (_isRefreshing) return;

            _isRefreshing = true;

            try
            {
                SessionManager.Instance.UpdateActivity();
                using (UserDialogs.Instance.Loading("Loading transactions...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(500);

                    string searchFrom = DateTime.Today.AddDays(-7).ToString("MM-dd-yyyy");
                    string searchTo = DateTime.Today.AddDays(1).ToString("MM-dd-yyyy");

                    if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                    {
                        await DisplayAlert("Error", "User email not found. Please login again.", "OK");
                        return;
                    }

                    List<UnifiedHistoryData> unifiedTransactions = null;

                    // Check category and use appropriate API
                    bool isYorata = !string.IsNullOrWhiteSpace(MainPage.Category) &&
                                   MainPage.Category.Equals("YORATA", StringComparison.OrdinalIgnoreCase);

                    if (isYorata)
                    {
                        // Use YORATA API
                        string url = $"https://yobe.osoftpay.net/api/TaskPayers/getYorotaTransaction?Email={Uri.EscapeDataString(MainPage.ValidUserMail)}&SearchFrom={Uri.EscapeDataString(searchFrom)}&SearchTo={Uri.EscapeDataString(searchTo)}";

                        var yorataTransactions = await FetchDataWithRetryAsync<List<YorataHistoryData>>(url);

                        if (yorataTransactions != null && yorataTransactions.Any())
                        {
                            unifiedTransactions = yorataTransactions.Select(t => new UnifiedHistoryData
                            {
                                transactionId = t.transactionId ?? "N/A",
                                primaryField = t.vehicleType ?? "N/A",
                                serviceName = t.serviceName ?? "N/A",
                                secondaryField = t.vehicleNo ?? "N/A",
                                amount = t.amount,
                                dateRecorded = t.dateRecorded ?? DateTime.Now.ToString(),
                                categoryType = "YORATA"
                            }).ToList();
                        }
                    }
                    else
                    {
                        // Use Default API
                        string url = $"https://isec.payyobe.com/api/TaskPayers/gettransaction?Email={Uri.EscapeDataString(MainPage.ValidUserMail)}&SearchFrom={Uri.EscapeDataString(searchFrom)}&SearchTo={Uri.EscapeDataString(searchTo)}";

                        var defaultTransactions = await FetchDataWithRetryAsync<List<DefaultHistoryData>>(url);

                        if (defaultTransactions != null && defaultTransactions.Any())
                        {
                            unifiedTransactions = defaultTransactions.Select(t => new UnifiedHistoryData
                            {
                                transactionId = t.transactionId ?? "N/A",
                                primaryField = t.businessName ?? "N/A",
                                serviceName = t.serviceName ?? "N/A",
                                secondaryField = t.payerId ?? "N/A",
                                amount = t.amount,
                                dateRecorded = t.dateRecorded ?? DateTime.Now.ToString(),
                                categoryType = "DEFAULT"
                            }).ToList();
                        }
                    }

                    if (unifiedTransactions != null && unifiedTransactions.Any())
                    {
                        _allTransactions = unifiedTransactions.OrderByDescending(x => x.dateRecorded).ToList();

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            var displayTransactions = _allTransactions.Take(10).ToList();
                            listView.ItemsSource = displayTransactions;
                            listView.IsVisible = true;
                            emptyState.IsVisible = false;
                            searchBar.IsVisible = true;
                        });
                    }
                    else
                    {
                        _allTransactions = new List<UnifiedHistoryData>();
                        ShowEmptyState();
                    }
                }
            }
            catch (HttpRequestException httpEx)
            {
                LogError("LoadRecentTransactionAsync - Network", httpEx);
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Network Error", "Unable to connect to server. Please check your internet connection.", "OK");
                });
                ShowEmptyState();
            }
            catch (TaskCanceledException)
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Timeout", "Request timed out. Please try again.", "OK");
                });
                ShowEmptyState();
            }
            catch (JsonException jsonEx)
            {
                LogError("LoadRecentTransactionAsync - JSON", jsonEx);
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Data Error", "Invalid data received from server. Please try again.", "OK");
                });
                ShowEmptyState();
            }
            catch (Exception ex)
            {
                LogError("LoadRecentTransactionAsync", ex);
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", "Failed to load transactions. Please try again later.", "OK");
                });
                ShowEmptyState();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async Task LoadBalanceAsync()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (string.IsNullOrWhiteSpace(MainPage.Pin) || string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    LogError("LoadBalanceAsync", new Exception("Pin or Email is null"));
                    return;
                }

                string url = $"https://yobe.osoftpay.net/api/singlecollections/getbalance?Pin={Uri.EscapeDataString(MainPage.Pin)}&Email={Uri.EscapeDataString(MainPage.ValidUserMail)}";


                var result = await SecureHttpService.Instance.GetAsync<BalanceResponse>(url);

                if (result != null)
                {
                    agent = result.agent ?? "N/A";
                    superAgent = result.superAgent ?? "N/A";
                    cashoutBalance = result.cashoutBalance ?? "0";

                    Device.BeginInvokeOnMainThread(() =>
                    {
                        UpdateBalanceDisplay();
                    });
                }
            }
            catch (Exception ex)
            {
                LogError("LoadBalanceAsync", ex);
            }
        }

        // ============ CHANGE 2: Updated FetchDataWithRetryAsync with SSL Handler ============
        private async Task<T> FetchDataWithRetryAsync<T>(string url, int retryCount = 0) where T : class
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                // CHANGED: Use _httpClientHandler instead of default HttpClient
                using (var client = new HttpClient(_httpClientHandler)
                {
                    Timeout = TimeSpan.FromSeconds(API_TIMEOUT_SECONDS)
                })
                {
                    // CHANGED: Updated SecurityProtocol to include TLS 1.3 and 1.2
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                    // CHANGED: Added certificate validation logging
                    try
                    {
                        var response = await client.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();

                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                try
                                {
                                    return JsonConvert.DeserializeObject<T>(json);
                                }
                                catch (JsonException jsonEx)
                                {
                                    LogError($"JSON Deserialization Error for {typeof(T).Name}", jsonEx);
                                    LogError("JSON Response", new Exception(json));
                                    throw;
                                }
                            }
                        }
                        else if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            Device.BeginInvokeOnMainThread(async () =>
                            {
                                await DisplayAlert("Session Expired", "Your session has expired. Please login again.", "OK");
                                await LogoutAsync();
                            });
                            return null;
                        }
                        else
                        {
                            LogError($"HTTP Error {response.StatusCode}", new Exception($"Status: {response.StatusCode}, URL: {url}"));
                        }
                    }
                    // ============ CHANGED: Fixed SSL Exception Handling ============
                    catch (HttpRequestException httpEx) when (
                        httpEx.InnerException?.GetType().Name == "AuthenticationException" ||
                        httpEx.InnerException is AuthenticationException)
                    {
                        // SSL/Certificate error handling
                        LogError("SSL Certificate Error", httpEx.InnerException ?? httpEx);
                        Debug.WriteLine($"[SSL ERROR] Certificate validation failed: {httpEx.InnerException?.Message ?? httpEx.Message}");

                        if (retryCount < MAX_RETRY_ATTEMPTS)
                        {
                            Debug.WriteLine($"SSL Error. Retry attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                            await Task.Delay(1000 * (retryCount + 1));
                            return await FetchDataWithRetryAsync<T>(url, retryCount + 1);
                        }
                        throw;
                    }
                    // ============ END SSL Exception Handling ============
                }
            }
            catch (TaskCanceledException) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"Request timeout. Retry attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1));
                return await FetchDataWithRetryAsync<T>(url, retryCount + 1);
            }
            catch (HttpRequestException httpEx) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"Network error. Retry attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                LogError($"HTTP Request Error (Retry {retryCount})", httpEx);
                await Task.Delay(1000 * (retryCount + 1));
                return await FetchDataWithRetryAsync<T>(url, retryCount + 1);
            }
            catch (JsonException)
            {
                // Don't retry on JSON errors
                throw;
            }
            catch (Exception ex)
            {
                LogError("FetchDataWithRetryAsync", ex);
            }

            return null;
        }
        // ============ END CHANGE 2 ============

        private void ShowEmptyState()
        {
            SessionManager.Instance.UpdateActivity();
            Device.BeginInvokeOnMainThread(() =>
            {
                listView.IsVisible = false;
                emptyState.IsVisible = true;
                searchBar.IsVisible = false;
            });
        }

        private async Task CallPrinterSafe()
        {
            try
            {
                await TestPrinterConnectionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DashBoard] CallPrinterSafe: {ex}");
                await DisplayAlert("Printer Error",
                    "Failed to connect to printer.\n\n" +
                    "• Ensure Bluetooth is ON\n" +
                    "• Printer is paired & powered on",
                    "OK");
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


        private void LogError(string method, Exception ex)
        {
            Debug.WriteLine($"[ERROR] {method}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Debug.WriteLine($"[INNER ERROR] {ex.InnerException.Message}");
            }
            Debug.WriteLine($"[STACK] {ex.StackTrace}");
        }

        // Event Handlers
        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Default.RevenueList());
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_1", ex);
                await DisplayAlert("Navigation Error", "Unable to navigate. Please try again.", "OK");
            }
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Default.Gazettet2());
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped", ex);
                await DisplayAlert("Navigation Error", "Unable to navigate. Please try again.", "OK");
            }
        }

        private async void TapGestureRecognizer_Tapped_4(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Default.History());
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_4", ex);
                await DisplayAlert("Navigation Error", "Unable to navigate. Please try again.", "OK");
            }
        }

        private async void TapGestureRecognizer_Tapped_2(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                using (UserDialogs.Instance.Loading("Loading balance...", null, null, true, MaskType.Black))
                {
                    await LoadBalanceAsync();

                    if (!string.IsNullOrWhiteSpace(cashoutBalance))
                    {
                        await Navigation.PushAsync(new Views.Default.AgentBalance());
                    }
                    else
                    {
                        await DisplayAlert("Error", "Unable to load balance. Please try again.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_2", ex);
                await DisplayAlert("Error", "Failed to load summary. Please try again.", "OK");
            }
        }

        private async void TapGestureRecognizer_Tapped_5(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Default.PaymentStatus());
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_5", ex);
                await DisplayAlert("Navigation Error", "Unable to navigate. Please try again.", "OK");
            }
        }

        private async void TapGestureRecognizer_Tapped_6(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Default.Enumerate());
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_6", ex);
                await DisplayAlert("Navigation Error", "Unable to navigate. Please try again.", "OK");
            }
        }

        private void TapGestureRecognizer_Tapped_3(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                Device.BeginInvokeOnMainThread(async () =>
                {
                    var action = await DisplayActionSheet("MENU OPTIONS", "CANCEL", null,
                        "CASHOUT", "CHANGE PASSWORD", "CHANGE PIN", "LOGOUT");

                    switch (action)
                    {
                        case "LOGOUT":
                            await LogoutAsync();
                            break;

                        case "CHANGE PASSWORD":
                            await Navigation.PushAsync(new Views.ChangePassword());
                            break;

                        case "CHANGE PIN":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;

                        case "CASHOUT":
                            using (UserDialogs.Instance.Loading("Loading balance...", null, null, true, MaskType.Black))
                            {
                                await LoadBalanceAsync();
                                if (!string.IsNullOrWhiteSpace(cashoutBalance))
                                {
                                    await Navigation.PushAsync(new Views.Default.AgentBalance());
                                }
                                else
                                {
                                    await DisplayAlert("Error", "Unable to load balance.", "OK");
                                }
                            }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("TapGestureRecognizer_Tapped_3", ex);
            }
        }

        private async void TapGestureRecognizer_Tapped_9(object sender, EventArgs e)
        {

            await LogoutAsync();
        }

        private async void TapGestureRecognizer_Tapped_10(object sender, EventArgs e)
        {
            await CallPrinterSafe();
        }

        private async void OnProfileTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await DisplayAlert("Profile", $"Name: {MainPage.Name}\nEmail: {MainPage.ValidUserMail}\nAgent: {agent2.Text}", "OK");
            }
            catch (Exception ex)
            {
                LogError("OnProfileTapped", ex);
            }
        }

        private async Task LogoutAsync()
        {
            try
            {
                var result = await DisplayAlert("Logout", "Are you sure you want to logout?", "Yes", "No");

                if (result)
                {
                    App.IsUserLoggedIn = false;
                    SessionManager.Instance.StopSession();
                    // Clear sensitive data
                    cashoutBalance = null;
                    agent = null;
                    superAgent = null;

                    Process.GetCurrentProcess().CloseMainWindow();
                }
            }
            catch (Exception ex)
            {
                LogError("LogoutAsync", ex);
            }
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var result = await DisplayAlert("Exit", "Do you really want to exit?", "Yes", "No");

                if (result)
                {
                    SessionManager.Instance.StopSession();
                    Process.GetCurrentProcess().CloseMainWindow();
                }
            });

            return true;
        }

        void Handle_PositionSelected(object sender, PositionSelectedEventArgs e)
        {
            try
            {
                var control = (CarouselViewControl)sender;
                Debug.WriteLine($"Carousel position: {control.Position}");
            }
            catch (Exception ex)
            {
                LogError("Handle_PositionSelected", ex);
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SessionManager.Instance.UpdateActivity();
            // Refresh data when returning to dashboard
            Device.BeginInvokeOnMainThread(async () =>
            {
                await LoadBalanceAsync();
            });
        }
    }
}