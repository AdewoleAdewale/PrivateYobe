using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RevenueList : ContentPage
    {
        #region Fields
        private readonly RevenueListViewModel _viewModel;
        private CancellationTokenSource _searchCancellationTokenSource;
        private const int SEARCH_DELAY_MS = 300;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 1000;
        #endregion

        #region Constructor
        public RevenueList()
        {
            try
            {
                InitializeComponent();
                _viewModel = new RevenueListViewModel();
                BindingContext = _viewModel;
                InitializePage();
                TrackUserActivity();
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
                DisplayError("Initialization Failed", "Unable to initialize the page. Please restart the app.");
            }
        }
        #endregion

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        #region Lifecycle Methods
        private void InitializePage()
        {
            try
            {
                Fullname.Text = MainPage.CollectionPoint ?? "Unknown Collection Point";

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
            }
            catch (Exception ex)
            {
                LogError("InitializePage", ex);
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                SessionManager.Instance.UpdateActivity();
                if (!_viewModel.IsDataLoaded)
                {
                    Device.BeginInvokeOnMainThread(async () => await LoadServicesAsync());
                }
            }
            catch (Exception ex)
            {
                LogError("OnAppearing", ex);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _searchCancellationTokenSource?.Cancel();
        }

        protected override bool OnBackButtonPressed()
        {
            try
            {
                if (!string.IsNullOrEmpty(searchBar?.Text))
                {
                    searchBar.Text = string.Empty;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError("OnBackButtonPressed", ex);
            }

            return base.OnBackButtonPressed();
        }
        #endregion

        #region Data Loading
        private async Task LoadServicesAsync()
        {
            if (_viewModel.IsLoading) return;

            _viewModel.IsLoading = true;
            UpdateLoadingState(true);
            SessionManager.Instance.UpdateActivity();
            try
            {
                using (UserDialogs.Instance.Loading("Loading services...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(300); // UX delay
                    var services = await FetchServicesWithRetryAsync();

                    await Device.InvokeOnMainThreadAsync(() =>
                    {
                        _viewModel.LoadServices(services);
                        UpdateUI();

                        // Debug logging
                        LogError("LoadServicesAsync", new Exception($"Loaded {services?.Count ?? 0} services"));
                    });
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                LogError("LoadServicesAsync - Unauthorized", ex);
                await HandleUnauthorizedAccess();
            }
            catch (HttpRequestException ex)
            {
                await DisplayError("Network Error",
                    "Unable to connect to the server. Please check your internet connection and try again.");
                LogError("LoadServicesAsync - Network", ex);
            }
            catch (TaskCanceledException ex)
            {
                await DisplayError("Request Timeout",
                    "The request took too long to complete. Please try again.");
                LogError("LoadServicesAsync - Timeout", ex);
            }
            catch (Exception ex)
            {
                await DisplayError("Error Loading Services",
                    $"An unexpected error occurred: {ex.Message}");
                LogError("LoadServicesAsync - General", ex);
            }
            finally
            {
                _viewModel.IsLoading = false;
                UpdateLoadingState(false);
            }
        }

        private async Task<List<ServicesList>> FetchServicesWithRetryAsync()
        {
            int attempt = 0;
            Exception lastException = null;
            SessionManager.Instance.UpdateActivity();
            while (attempt < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    return await FetchServicesFromApiAsync();
                }
                catch (HttpRequestException ex) when (attempt < MAX_RETRY_ATTEMPTS - 1)
                {
                    lastException = ex;
                    attempt++;
                    await Task.Delay(RETRY_DELAY_MS * attempt);
                    LogError($"FetchServices - Retry {attempt}", ex);
                }
                catch (Exception ex)
                {
                    LogError("FetchServicesWithRetry", ex);
                    throw;
                }
            }

            throw lastException ?? new Exception("Failed to fetch services after multiple attempts");
        }


        private async Task<List<ServicesList>> FetchServicesFromApiAsync()
        {
            if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
            {
                throw new UnauthorizedAccessException("User email is not available");
            }
            SessionManager.Instance.UpdateActivity();

            string url = $"https://yobe.osoftpay.net/api/TaskPayers/getservices?Email={Uri.EscapeDataString(MainPage.ValidUserMail)}";

            LogError("API Call", new Exception($"Calling: {url}"));

            var handler = new HttpClientHandler();

            // Solution 1: Accept all certificates temporarily (NOT RECOMMENDED for production)
            // Use this only if you need a quick fix while sorting out the certificate
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // Log certificate details for debugging
                if (cert != null)
                {
                    LogError("Certificate Info", new Exception($"Subject: {cert.Subject}, Issuer: {cert.Issuer}, Thumbprint: {cert.Thumbprint}"));
                }

                // For production, you should validate the certificate properly
                // Option A: Accept specific certificate thumbprint
                // string expectedThumbprint = "YOUR_CERTIFICATE_THUMBPRINT_HERE";
                // if (cert?.Thumbprint?.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase) == true)
                //     return true;

                // Option B: Accept certificates from specific issuers
                // if (cert?.Issuer?.Contains("Your CA Name") == true)
                //     return true;

                // Temporary solution - accept all (replace with proper validation)
                return true;
            };

            // Ensure TLS 1.2 or higher
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11;

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "YIRS-Mobile-App");

                // Add additional headers that might be required
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                try
                {
                    var response = await client.GetAsync(url);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new UnauthorizedAccessException("Session expired");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        LogError("API Error Response", new Exception($"Status: {response.StatusCode}, Content: {content}"));
                        throw new HttpRequestException($"Server error: {response.StatusCode} - {response.ReasonPhrase}");
                    }

                    var json = await response.Content.ReadAsStringAsync();

                    LogError("API Response", new Exception($"JSON Length: {json?.Length ?? 0}"));

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        LogError("API Response", new Exception("Empty response from server"));
                        return new List<ServicesList>();
                    }

                    try
                    {
                        var items = JsonConvert.DeserializeObject<List<ServicesList>>(json);
                        LogError("Deserialization", new Exception($"Deserialized {items?.Count ?? 0} items"));
                        return items ?? new List<ServicesList>();
                    }
                    catch (JsonException ex)
                    {
                        LogError("JSON Deserialization", ex);
                        LogError("JSON Content", new Exception(json));
                        throw new Exception("Invalid data format received from server", ex);
                    }
                }
                catch (Exception ex)
                {
                    LogError("HttpClient Request", ex);
                    throw;
                }
            }
        }
        private bool IsSelfSignedCertificate()
        {
            // TODO: Replace with your actual logic to determine if using self-signed cert
            // This could be based on configuration, app settings, or build configuration
#if DEBUG
            return true; // Use custom validation in debug mode
#else
            return false; // Strict validation in release mode
#endif
        }
        #endregion

        #region Search Functionality
        private async void SearchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                _searchCancellationTokenSource?.Cancel();
                _searchCancellationTokenSource = new CancellationTokenSource();
                var token = _searchCancellationTokenSource.Token;

                await Task.Delay(SEARCH_DELAY_MS, token);

                if (token.IsCancellationRequested) return;

                var searchText = e.NewTextValue?.Trim() ?? string.Empty;
                _viewModel.FilterServices(searchText);
                UpdateUI();
            }
            catch (TaskCanceledException)
            {
                // Expected when user types quickly
            }
            catch (Exception ex)
            {
                LogError("SearchBar_TextChanged", ex);
                await DisplayError("Search Error", "An error occurred while searching. Please try again.");
            }
        }
        #endregion

        #region Payment Navigation
        private async void PaymentButton_Clicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            SessionManager.Instance.UpdateActivity();
            try
            {
                if (button?.CommandParameter is ServicesList service)
                {
                    if (button.IsEnabled == false) return;

                    button.IsEnabled = false;
                    await AnimateButton(button);

                    if (!ValidateService(service))
                    {
                        await DisplayAlert("Invalid Service",
                            "This service contains invalid data. Please contact support.", "OK");
                        return;
                    }

                    bool proceed = await DisplayAlert(
                        "Confirm Payment",
                        $"Proceed to make payment for:\n\n{service.ServiceName}\nAmount: ₦{service.ServiceAmount:N2}",
                        "Yes, Proceed",
                        "Cancel"
                    );

                    if (proceed)
                    {
                        await NavigateToPayment(service);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("PaymentButton_Clicked", ex);
                await DisplayError("Navigation Error",
                    "Unable to proceed to payment. Please try again.");
            }
            finally
            {
                if (button != null)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private bool ValidateService(ServicesList service)
        {
            return service != null &&
                   !string.IsNullOrWhiteSpace(service.ServiceName) &&
                   !string.IsNullOrWhiteSpace(service.ServiceAmount);
        }

        private async Task NavigateToPayment(ServicesList service)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                ServiceName = service.ServiceName;
                ServiceDescription = service.ServiceDescription ?? "No description available";

                if (decimal.TryParse(service.ServiceAmount, out decimal amount))
                {
                    ServiceAmount = amount;
                }
                else
                {
                    throw new InvalidOperationException("Invalid service amount");
                }

                // Add small delay to ensure properties are set
                await Task.Delay(100);

                await Navigation.PushAsync(new Views.Default.Payment());
            }
            catch (Exception ex)
            {
                LogError("NavigateToPayment", ex);
                throw;
            }
        }

        private async Task AnimateButton(Button button)
        {
            try
            {
                await button.ScaleTo(0.95, 50);
                await button.ScaleTo(1, 50);
            }
            catch (Exception ex)
            {
                LogError("AnimateButton", ex);
            }
        }
        #endregion

        #region UI Updates
        private void UpdateUI()
        {
            try
            {
                var count = _viewModel.FilteredServices?.Count ?? 0;
                UpdateServiceCount(count);
                ShowEmptyState(count == 0 && _viewModel.IsDataLoaded);

                LogError("UpdateUI", new Exception($"UI Updated - Count: {count}, DataLoaded: {_viewModel.IsDataLoaded}"));
            }
            catch (Exception ex)
            {
                LogError("UpdateUI", ex);
            }
        }

        private void UpdateServiceCount(int count)
        {
            try
            {
                if (serviceCount != null)
                {
                    serviceCount.Text = count == 0 ? "No services available" :
                                       count == 1 ? "1 service available" :
                                       $"{count} services available";
                }
            }
            catch (Exception ex)
            {
                LogError("UpdateServiceCount", ex);
            }
        }

        private void ShowEmptyState(bool show)
        {
            try
            {
                if (emptyStateView != null && listView != null)
                {
                    emptyStateView.IsVisible = show;
                    listView.IsVisible = !show;
                }
            }
            catch (Exception ex)
            {
                LogError("ShowEmptyState", ex);
            }
        }

        private void UpdateLoadingState(bool isLoading)
        {
            try
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    if (loadingIndicator != null)
                    {
                        loadingIndicator.IsVisible = isLoading;
                        loadingIndicator.IsRunning = isLoading;
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("UpdateLoadingState", ex);
            }
        }
        #endregion

        #region Error Handling
        private async Task HandleUnauthorizedAccess()
        {
            await Device.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Session Expired",
                    "Your session has expired. Please log in again.", "OK");
                await Navigation.PopToRootAsync();
            });
        }

        private async Task DisplayError(string title, string message)
        {
            try
            {
                await Device.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert(title, message, "OK");
                });
            }
            catch (Exception ex)
            {
                LogError("DisplayError", ex);
            }
        }

        private void LogError(string method, Exception ex)
        {
            // TODO: Implement proper logging (e.g., AppCenter, Serilog, etc.)
            System.Diagnostics.Debug.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {method}: {ex.Message}");
            if (ex.StackTrace != null)
            {
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }
        #endregion

        #region Additional Event Handlers
        private async void RefreshView_Refreshing(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await LoadServicesAsync();
            }
            catch (Exception ex)
            {
                LogError("RefreshView_Refreshing", ex);
            }
            finally
            {
                if (refreshView != null)
                {
                    refreshView.IsRefreshing = false;
                }
            }
        }

        private void ClearSearch_Clicked(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (searchBar != null)
                {
                    searchBar.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                LogError("ClearSearch_Clicked", ex);
            }
        }

        private async void CloseErrorBanner_Tapped(object sender, EventArgs e)
        {
            try
            {
                await HideErrorBanner();
            }
            catch (Exception ex)
            {
                LogError("CloseErrorBanner_Tapped", ex);
            }
        }

        private async Task ShowErrorBanner(string message)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Device.InvokeOnMainThreadAsync(async () =>
                {
                    if (errorBanner != null && errorMessage != null)
                    {
                        errorMessage.Text = message;
                        errorBanner.IsVisible = true;
                        await errorBanner.TranslateTo(0, 0, 250, Easing.CubicOut);

                        // Auto-hide after 5 seconds
                        await Task.Delay(5000);
                        await HideErrorBanner();
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("ShowErrorBanner", ex);
            }
        }

        private async Task HideErrorBanner()
        {
            try
            {
                if (errorBanner != null && errorBanner.IsVisible)
                {
                    await errorBanner.TranslateTo(0, -100, 250, Easing.CubicIn);
                    errorBanner.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                LogError("HideErrorBanner", ex);
            }
        }
        #endregion

        #region Static Properties (for backward compatibility)
        public static string ServiceName { get; set; }
        public static string ServiceDescription { get; set; }
        public static decimal ServiceAmount { get; set; }
        #endregion
    }

    #region Models
    public class ServicesList
    {
        public string ServiceName { get; set; }
        public string ServiceDescription { get; set; }
        public string ServiceAmount { get; set; }

        public string Samount
        {
            get
            {
                if (decimal.TryParse(ServiceAmount, out decimal amount))
                {
                    return amount.ToString("N2");
                }
                return ServiceAmount ?? "0.00";
            }
        }
    }
    #endregion

    #region ViewModel
    public class RevenueListViewModel : INotifyPropertyChanged
    {
        private List<ServicesList> _allServices;
        private List<ServicesList> _filteredServices;
        private bool _isLoading;
        private bool _isDataLoaded;
        private string _currentSearchText;

        public List<ServicesList> FilteredServices
        {
            get => _filteredServices;
            private set
            {
                _filteredServices = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Summary));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            private set
            {
                _isDataLoaded = value;
                OnPropertyChanged();
            }
        }

        public string Summary
        {
            get
            {
                if (!IsDataLoaded || FilteredServices == null)
                    return "Loading...";

                var count = FilteredServices.Count;
                if (count == 0)
                    return "No revenue heads available";

                return $"Total: {count} Revenue Head{(count != 1 ? "s" : "")}";
            }
        }

        public RevenueListViewModel()
        {
            _allServices = new List<ServicesList>();
            _filteredServices = new List<ServicesList>();
            _currentSearchText = string.Empty;
        }

        public void LoadServices(List<ServicesList> services)
        {
            _allServices = services ?? new List<ServicesList>();
            FilterServices(_currentSearchText);
            IsDataLoaded = true;
        }

        public void FilterServices(string searchText)
        {
            _currentSearchText = searchText?.Trim().ToLower() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_currentSearchText))
            {
                FilteredServices = new List<ServicesList>(_allServices);
                return;
            }

            FilteredServices = _allServices.Where(s =>
                (s.ServiceName?.ToLower().Contains(_currentSearchText) ?? false) ||
                (s.ServiceDescription?.ToLower().Contains(_currentSearchText) ?? false) ||
                (s.ServiceAmount?.Contains(_currentSearchText) ?? false)
            ).ToList();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    #endregion

    #region Value Converters
    public class StringToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion
}