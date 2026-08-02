using Acr.UserDialogs;
using CarouselView.FormsPlugin.Abstractions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PlatformConfiguration.iOSSpecific;
using Xamarin.Forms.Xaml;
using YIRS.Renderers;
using YIRS.Services;

namespace YIRS.Views.Yorata_Ops
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Dashboard : ContentPage
    {
        #region Static Properties
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
        public static string superAgent { get; set; }
        public static string agent { get; set; }
        public static string cashoutBalance { get; set; }
        #endregion

        #region Private Fields
        private MainViewModel _vm;
        private bool _isRefreshing = false;
        private bool _isInitialized = false;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int API_TIMEOUT_SECONDS = 30;
        private const int CONNECTION_RETRY_DELAY_MS = 2000;

        // Master list of all fetched transactions (raw)
        private List<UnifiedHistoryData> _allTransactions = new List<UnifiedHistoryData>();
        // Displayed list (wrapped for proper ListView binding)
        private List<TransactionListItem> _displayItems = new List<TransactionListItem>();

        private string _searchText = string.Empty;
        private CancellationTokenSource _cancellationTokenSource;
        private SemaphoreSlim _refreshSemaphore = new SemaphoreSlim(1, 1);

        // ── Bluetooth Printer SDK instance (58 mm paper) ─────────────────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);
        #endregion

        #region SSL Configuration
        private static readonly HttpClientHandler _httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            MaxConnectionsPerServer = 10,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        #endregion

        #region Data Models

        class DefaultHistoryData
        {
            public string transactionId { get; set; }
            public string businessName { get; set; }
            public string serviceName { get; set; }
            public string payerId { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }
        }

        class YorataHistoryData
        {
            public string transactionId { get; set; }
            public string vehicleType { get; set; }
            public string serviceName { get; set; }
            public string vehicleNo { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }
        }

        /// <summary>
        /// Internal unified model — dateRecorded stored as raw string from API.
        /// </summary>
        class UnifiedHistoryData
        {
            public string transactionId { get; set; }
            public string primaryField { get; set; }
            public string serviceName { get; set; }
            public string secondaryField { get; set; }
            public decimal amount { get; set; }
            public string dateRecorded { get; set; }   // raw API string
            public string categoryType { get; set; }
        }

        /// <summary>
        /// ListView binding wrapper — pre-formats all display strings so XAML
        /// bindings are simple and reliable (no StringFormat on DateTime needed).
        /// </summary>
        public class TransactionListItem
        {
            public string transactionId { get; set; }
            public string serviceName { get; set; }
            public string primaryField { get; set; }
            public string secondaryField { get; set; }

            /// <summary>Pre-formatted amount string e.g. "₦1,200.00"</summary>
            public string AmountFormatted { get; set; }

            /// <summary>Pre-formatted date string e.g. "Nov 03, 02:55 PM"</summary>
            public string DateFormatted { get; set; }

            public decimal RawAmount { get; set; }
        }

        internal class BalanceResponse
        {
            public string superAgent { get; set; }
            public string agent { get; set; }
            public string cashoutBalance { get; set; }
        }

        class ErrorLog
        {
            public DateTime Timestamp { get; set; }
            public string Method { get; set; }
            public string Message { get; set; }
            public string StackTrace { get; set; }
        }

        #endregion

        #region Constructor

        public Dashboard()
        {
            try
            {
                InitializeComponent();
                InitializeUI();
                TrackUserActivity();

                BindingContext = _vm = new MainViewModel();
                On<Xamarin.Forms.PlatformConfiguration.iOS>().SetUseSafeArea(true);

                if (searchEntry != null)
                    searchEntry.TextChanged += OnSearchTextChanged;
                else
                    LogError("Constructor", new NullReferenceException("searchEntry control not found"));

                _cancellationTokenSource = new CancellationTokenSource();

                Device.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await InitializeDataAsync();
                        _isInitialized = true;
                    }
                    catch (Exception ex)
                    {
                        LogError("Constructor - InitializeDataAsync", ex);
                        await ShowErrorAlert("Initialization Warning",
                            "Some data could not be loaded. Please pull down to refresh.", "OK");
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowCriticalErrorAlert("Initialization Error",
                        "Failed to initialize dashboard. Please restart the app."));
            }
        }

        #endregion

        #region UI Initialization

        private void InitializeUI()
        {
            try
            {
                if (WelcomeMessage != null)
                    WelcomeMessage.Text = !string.IsNullOrWhiteSpace(MainPage.Name)
                        ? MainPage.Name.ToUpper() : "AGENT";

                if (agent2 != null)
                    agent2.Text = !string.IsNullOrWhiteSpace(MainPage.Super_Agent)
                        ? MainPage.Super_Agent : "N/A";

                if (cat != null)
                    cat.Text = !string.IsNullOrWhiteSpace(MainPage.Category)
                        ? MainPage.Category.ToUpper() : "DEFAULT";

                if (cllpoint != null)
                    cllpoint.Text = !string.IsNullOrWhiteSpace(MainPage.CollectionPoint)
                        ? $"COLLECTION POINT: {MainPage.CollectionPoint}"
                        : "COLLECTION POINT: NOT ASSIGNED";

                UpdateBalanceDisplay();
            }
            catch (Exception ex)
            {
                LogError("InitializeUI", ex);
            }
        }

        private void TrackUserActivity()
        {
            try
            {
                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
                if (this.Content != null)
                    this.Content.GestureRecognizers.Add(tapGesture);
            }
            catch (Exception ex)
            {
                LogError("TrackUserActivity", ex);
            }
        }

        private void UpdateBalanceDisplay()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (cashoutB == null) return;

                cashoutB.Text = (string.IsNullOrWhiteSpace(cashoutBalance) ||
                                 !decimal.TryParse(cashoutBalance, out decimal balance))
                    ? "₦ 0.00"
                    : $"₦ {balance:N2}";
            }
            catch (Exception ex)
            {
                LogError("UpdateBalanceDisplay", ex);
                if (cashoutB != null) cashoutB.Text = "₦ 0.00";
            }
        }

        #endregion

        #region Data Loading

        private async Task InitializeDataAsync()
        {
            await Task.WhenAll(LoadBalanceAsync(), LoadRecentTransactionAsync());
        }

        private async Task LoadBalanceAsync()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (string.IsNullOrWhiteSpace(MainPage.Pin))
                    throw new InvalidOperationException("User PIN is not set");
                if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                    throw new InvalidOperationException("User email is not set");

                string url = $"https://yobe.osoftpay.net/api/singlecollections/getbalance" +
                             $"?Pin={Uri.EscapeDataString(MainPage.Pin)}" +
                             $"&Email={Uri.EscapeDataString(MainPage.ValidUserMail)}";

                var result = await SecureHttpService.Instance.GetAsync<BalanceResponse>(url);
                if (result != null)
                {
                    agent = result.agent ?? "N/A";
                    superAgent = result.superAgent ?? "N/A";
                    cashoutBalance = result.cashoutBalance ?? "0";
                    Device.BeginInvokeOnMainThread(UpdateBalanceDisplay);
                }
                else
                {
                    throw new InvalidOperationException("Balance API returned null response");
                }
            }
            catch (HttpRequestException httpEx)
            {
                LogError("LoadBalanceAsync - Network", httpEx);
                Device.BeginInvokeOnMainThread(() =>
                    ToastService.ShowToast("Network error: Could not load balance."));
            }
            catch (Exception ex)
            {
                LogError("LoadBalanceAsync", ex);
                Device.BeginInvokeOnMainThread(() =>
                    ToastService.ShowToast("Failed to load balance information."));
            }
        }

        /// <summary>
        /// Fetches the last 7 days of Yorata transactions and binds them to the ListView
        /// via a <see cref="TransactionListItem"/> wrapper list (no UserDialogs — uses the
        /// inline <see cref="transactionLoader"/> ActivityIndicator instead).
        /// </summary>
        private async Task LoadRecentTransactionAsync()
        {
            if (_isRefreshing) return;
            await _refreshSemaphore.WaitAsync();
            _isRefreshing = true;

            try
            {
                SessionManager.Instance.UpdateActivity();

                // Show inline loader (safe on any thread)
                SetTransactionLoader(true);

                if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                    throw new InvalidOperationException("User email not found. Please login again.");

                string searchFrom = DateTime.Today.AddDays(-7).ToString("MM-dd-yyyy");
                string searchTo = DateTime.Today.AddDays(1).ToString("MM-dd-yyyy");

                bool isYorata = !string.IsNullOrWhiteSpace(MainPage.Category) &&
                                MainPage.Category.Equals("YORATA", StringComparison.OrdinalIgnoreCase);

                List<UnifiedHistoryData> unified = null;

                if (isYorata)
                {
                    string url = $"https://yobe.osoftpay.net/api/TaskPayers/getYorotaTransaction" +
                                 $"?Email={Uri.EscapeDataString(MainPage.ValidUserMail)}" +
                                 $"&SearchFrom={Uri.EscapeDataString(searchFrom)}" +
                                 $"&SearchTo={Uri.EscapeDataString(searchTo)}";

                    var yorataList = await FetchDataWithRetryAsync<List<YorataHistoryData>>(url);

                    if (yorataList != null && yorataList.Any())
                    {
                        unified = yorataList.Select(t => new UnifiedHistoryData
                        {
                            transactionId = t.transactionId ?? "N/A",
                            primaryField = t.vehicleType ?? "N/A",
                            serviceName = t.serviceName ?? "N/A",
                            secondaryField = t.vehicleNo ?? "N/A",
                            amount = t.amount,
                            dateRecorded = t.dateRecorded ?? string.Empty,
                            categoryType = "YORATA"
                        }).ToList();
                    }
                }
                else
                {
                    // Default / fallback — load standard transactions
                    string url = $"https://yobe.osoftpay.net/api/TaskPayers/gettransaction" +
                                 $"?Email={Uri.EscapeDataString(MainPage.ValidUserMail)}" +
                                 $"&SearchFrom={Uri.EscapeDataString(searchFrom)}" +
                                 $"&SearchTo={Uri.EscapeDataString(searchTo)}";

                    var defaultList = await FetchDataWithRetryAsync<List<DefaultHistoryData>>(url);
                    if (defaultList != null && defaultList.Any())
                    {
                        unified = defaultList.Select(t => new UnifiedHistoryData
                        {
                            transactionId = t.transactionId ?? "N/A",
                            primaryField = t.businessName ?? "N/A",
                            serviceName = t.serviceName ?? "N/A",
                            secondaryField = t.payerId ?? "N/A",
                            amount = t.amount,
                            dateRecorded = t.dateRecorded ?? string.Empty,
                            categoryType = "DEFAULT"
                        }).ToList();
                    }
                }

                if (unified != null && unified.Any())
                {
                    _allTransactions = unified.OrderByDescending(x => x.dateRecorded).ToList();
                    BindTransactionList(_allTransactions.Take(10).ToList());
                }
                else
                {
                    _allTransactions = new List<UnifiedHistoryData>();
                    ShowEmptyState();
                }
            }
            catch (InvalidOperationException ioEx)
            {
                LogError("LoadRecentTransactionAsync - Operation", ioEx);
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowErrorAlert("Error", ioEx.Message, "OK"));
                ShowEmptyState();
            }
            catch (HttpRequestException httpEx)
            {
                LogError("LoadRecentTransactionAsync - Network", httpEx);
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowErrorAlert("Network Error",
                        "Unable to connect to server. Please check your internet connection.", "OK"));
                ShowEmptyState();
            }
            catch (TaskCanceledException tcEx)
            {
                LogError("LoadRecentTransactionAsync - Timeout", tcEx);
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowErrorAlert("Timeout", "Request timed out. Please try again.", "OK"));
                ShowEmptyState();
            }
            catch (JsonException jsonEx)
            {
                LogError("LoadRecentTransactionAsync - JSON", jsonEx);
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowErrorAlert("Data Error",
                        "Invalid data received from server. Please contact support.", "OK"));
                ShowEmptyState();
            }
            catch (Exception ex)
            {
                LogError("LoadRecentTransactionAsync", ex);
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowErrorAlert("Error",
                        "Failed to load transactions. Please try again later.", "OK"));
                ShowEmptyState();
            }
            finally
            {
                SetTransactionLoader(false);
                _isRefreshing = false;
                _refreshSemaphore.Release();
            }
        }

        /// <summary>
        /// Converts raw <see cref="UnifiedHistoryData"/> to display wrappers and binds to ListView.
        /// All formatting happens here so XAML only uses simple {Binding PropertyName}.
        /// </summary>
        private void BindTransactionList(List<UnifiedHistoryData> source)
        {
            try
            {
                var items = source.Select(t =>
                {
                    // Parse date safely — avoids StringFormat crash on string fields
                    string dateFmt = "N/A";
                    if (!string.IsNullOrWhiteSpace(t.dateRecorded) &&
                        DateTime.TryParse(t.dateRecorded, out DateTime dt))
                    {
                        dateFmt = dt.ToString("MMM dd, hh:mm tt");
                    }

                    return new TransactionListItem
                    {
                        transactionId = t.transactionId,
                        serviceName = t.serviceName,
                        primaryField = t.primaryField,
                        secondaryField = t.secondaryField,
                        RawAmount = t.amount,
                        AmountFormatted = $"₦{t.amount:N2}",
                        DateFormatted = dateFmt
                    };
                }).ToList();

                _displayItems = items;

                Device.BeginInvokeOnMainThread(() =>
                {
                    if (listView == null || emptyState == null) return;

                    if (items.Any())
                    {
                        listView.ItemsSource = items;
                        listView.IsVisible = true;
                        emptyState.IsVisible = false;
                        if (searchBar != null) searchBar.IsVisible = true;
                    }
                    else
                    {
                        ShowEmptyState();
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("BindTransactionList", ex);
            }
        }

        private void SetTransactionLoader(bool show)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (transactionLoader == null) return;
                transactionLoader.IsRunning = show;
                transactionLoader.IsVisible = show;
            });
        }

        #endregion

        #region Search & Filter

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (e == null) return;
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
                if (_allTransactions == null || !_allTransactions.Any()) return;

                List<UnifiedHistoryData> filtered;

                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    filtered = _allTransactions;
                }
                else
                {
                    string q = _searchText.ToLower();
                    filtered = _allTransactions.Where(t =>
                        (t.transactionId?.ToLower().Contains(q) ?? false) ||
                        (t.primaryField?.ToLower().Contains(q) ?? false) ||
                        (t.serviceName?.ToLower().Contains(q) ?? false) ||
                        (t.secondaryField?.ToLower().Contains(q) ?? false) ||
                        t.amount.ToString().Contains(q)
                    ).ToList();
                }

                BindTransactionList(filtered.OrderByDescending(x => x.dateRecorded).Take(10).ToList());
            }
            catch (Exception ex)
            {
                LogError("FilterTransactions", ex);
            }
        }

        private void ShowEmptyState()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (listView != null) listView.IsVisible = false;
                if (emptyState != null) emptyState.IsVisible = true;
                if (searchBar != null) searchBar.IsVisible = false;
            });
        }

        #endregion

        #region Fetch Helper

        private async Task<T> FetchDataWithRetryAsync<T>(string url, int retryCount = 0) where T : class
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(url))
                    throw new ArgumentException("URL cannot be null or empty", nameof(url));

                using (var client = new HttpClient(_httpClientHandler)
                {
                    Timeout = TimeSpan.FromSeconds(API_TIMEOUT_SECONDS)
                })
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                    var response = await client.GetAsync(url, _cancellationTokenSource.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (string.IsNullOrWhiteSpace(json))
                            throw new InvalidOperationException("Empty response received from server");

                        var result = JsonConvert.DeserializeObject<T>(json);
                        if (result == null)
                            throw new JsonException("Deserialization resulted in null object");
                        return result;
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            await ShowCriticalErrorAlert("Session Expired",
                                "Your session has expired. Please login again.");
                            await LogoutAsync(false);
                        });
                        return null;
                    }
                    else
                    {
                        throw new HttpRequestException(
                            $"Server returned {response.StatusCode}: {response.ReasonPhrase}");
                    }
                }
            }
            catch (TaskCanceledException) when (
                retryCount < MAX_RETRY_ATTEMPTS &&
                !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await Task.Delay(CONNECTION_RETRY_DELAY_MS * (retryCount + 1));
                return await FetchDataWithRetryAsync<T>(url, retryCount + 1);
            }
            catch (HttpRequestException httpEx) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                LogError($"HTTP Request Error (Retry {retryCount})", httpEx);
                await Task.Delay(CONNECTION_RETRY_DELAY_MS * (retryCount + 1));
                return await FetchDataWithRetryAsync<T>(url, retryCount + 1);
            }
            catch (JsonException) { throw; }
            catch (Exception ex)
            {
                LogError("FetchDataWithRetryAsync", ex);
                throw;
            }
        }

        #endregion

        #region Printer Functions

        private async Task TestPrinterConnectionAsync()
        {
            try
            {
                bool available = await _printer.IsPrinterAvailableAsync();
                if (!available)
                {
                    await DisplayAlert("Printer Not Found",
                        "No paired printer detected.\n\n" +
                        "• Ensure Bluetooth is ON\n" +
                        "• Printer is powered on\n" +
                        "• Printer is paired in Android Settings", "OK");
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
                    "• Printer is paired & powered on", "OK");
                Debug.WriteLine($"[Dashboard] TestPrinterConnectionAsync error: {ex}");
            }
        }

        #endregion

        #region Navigation Handlers

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
            => await SafeNavigationAsync(() =>
                Navigation.PushAsync(new Views.Yorata_Ops.ServiceList()),
                "Tapped_1 - ServiceList");

        private async void TapGestureRecognizer_Tapped_4(object sender, EventArgs e)
            => await SafeNavigationAsync(() =>
                Navigation.PushAsync(new Views.Yorata_Ops.GHistory()),
                "Tapped_4 - GHistory (Invoice)");

        private async void TapGestureRecognizer_Tapped_5(object sender, EventArgs e)
            => await SafeNavigationAsync(() =>
                Navigation.PushAsync(new Views.Yorata_Ops.VerifyInvoice()),
                "Tapped_5 - VerifyInvoice");

        private async void TapGestureRecognizer_Tapped_6(object sender, EventArgs e)
            => await SafeNavigationAsync(() =>
                Navigation.PushAsync(new Views.Yorata_Ops.ServiceList()),
                "Tapped_6 - SuperAgent ServiceList");

        /// <summary>New: navigate to Yorata Transaction History.</summary>
        private async void TapGestureRecognizer_Tapped_7(object sender, EventArgs e)
            => await SafeNavigationAsync(() =>
                Navigation.PushAsync(new Views.Yorata_Ops.History()),
                "Tapped_7 - Transaction History");

        /// <summary>Inline refresh button in Recent Transactions header.</summary>
        private async void OnRefreshTransactions_Tapped(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            await LoadRecentTransactionAsync();
        }

        private async Task SafeNavigationAsync(Func<Task> navigationAction, string actionName)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await navigationAction();
            }
            catch (Exception ex)
            {
                LogError($"SafeNavigationAsync - {actionName}", ex);
                await ShowErrorAlert("Navigation Error",
                    "Failed to navigate to the requested page. Please try again.", "OK");
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
                        "INVOICE HISTORY", "AGENT HISTORY",
                        "CHANGE PASSWORD", "CHANGE PIN", "LOGOUT");

                    switch (action)
                    {
                        case "LOGOUT":
                            await LogoutAsync();
                            break;
                        case "CHANGE PASSWORD":
                        case "CHANGE PIN":
                            await Navigation.PushAsync(new Views.ChangeTransferPIN());
                            break;
                        case "INVOICE HISTORY":
                            await Navigation.PushAsync(new Views.Yorata_Ops.GHistory());
                            break;
                        case "AGENT HISTORY":
                            await Navigation.PushAsync(new Views.Yorata_Ops.History());
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
            => await LogoutAsync();

        private async void TapGestureRecognizer_Tapped_10(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            await TestPrinterConnectionAsync();
        }

        private async void OnProfileTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await DisplayAlert("Profile",
                    $"Name: {MainPage.Name}\nEmail: {MainPage.ValidUserMail}\nSuper Agent: {superAgent}",
                    "OK");
            }
            catch (Exception ex)
            {
                LogError("OnProfileTapped", ex);
            }
        }

        private async Task LogoutAsync(bool confirm = true)
        {
            try
            {
                if (confirm)
                {
                    var result = await DisplayAlert("Logout",
                        "Are you sure you want to logout?", "Yes", "No");
                    if (!result) return;
                }

                SessionManager.Instance.StopSession();
                await SecureStorageService.ClearCredentialsAsync();

                cashoutBalance = null;
                agent = null;
                superAgent = null;

                Device.BeginInvokeOnMainThread(() => App.SetRoot(new Views.MainPage()));
            }
            catch (Exception ex)
            {
                LogError("LogoutAsync", ex);
                await ShowErrorAlert("Logout Error",
                    "Failed to logout properly. Please restart the app.", "OK");
            }
        }

        #endregion

        #region Lifecycle

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var result = await DisplayAlert("Exit",
                    "Do you really want to exit?", "Yes", "No");
                if (result)
                {
                    SessionManager.Instance.StopSession();
                    System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
                }
            });
            return true;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SessionManager.Instance.UpdateActivity();
            Device.BeginInvokeOnMainThread(async () => await LoadBalanceAsync());
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }
            catch (Exception ex)
            {
                LogError("OnDisappearing", ex);
            }
        }

        #endregion

        #region Carousel

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

        #endregion

        #region Alerts & Logging

        private async Task ShowErrorAlert(string title, string message, string buttonText)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert(title, message, buttonText));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to show alert: {ex.Message}");
            }
        }

        private async Task ShowCriticalErrorAlert(string title, string message)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await DisplayAlert(title, message, "OK"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to show critical alert: {ex.Message}");
            }
        }

        private void LogError(string method, Exception ex)
        {
            try
            {
                Debug.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {method}");
                Debug.WriteLine($"[ERROR] Message: {ex?.Message}");
                Debug.WriteLine($"[ERROR] StackTrace: {ex?.StackTrace}");

                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YIRS", "Logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                string logFile = Path.Combine(logDir, $"error_log_{DateTime.Now:yyyy-MM-dd}.txt");
                File.AppendAllText(logFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {method}: {ex?.Message}\n{ex?.StackTrace}\n\n");
            }
            catch { }
        }

        #endregion
    }
}