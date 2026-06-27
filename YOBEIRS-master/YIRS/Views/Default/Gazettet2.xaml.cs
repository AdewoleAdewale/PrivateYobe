using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Gazettet2 : ContentPage
    {
        private readonly HttpClient _httpClient;
        private const string ApiUrl = "https://yobe.osoftpay.net/api/taskpayers/GetBizCat";

        class ServicesList
        {
            public string businessName { get; set; }
            public string renewal { get; set; }
            public string regAmt { get; set; }
        }

        class HistoryDataHeaderFooter
        {
            public List<ServicesList> HD { get; set; }
            public decimal Size { get { return HD.Count; } }
        }

        public Gazettet2()
        {
            InitializeComponent();
            TrackUserActivity();
            // Initialize HttpClient with SSL handling
            _httpClient = CreateHttpClientWithSSLHandler();

            // Load data asynchronously
            Device.BeginInvokeOnMainThread(async () =>
            {
                await LoadGazetteDataAsync();
            });
        }

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        /// <summary>
        /// Creates an HttpClient with proper SSL certificate validation
        /// </summary>
        private HttpClient CreateHttpClientWithSSLHandler()
        {
            var handler = new HttpClientHandler();

            // Configure SSL certificate validation
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // If certificate is valid, accept it
                if (errors == System.Net.Security.SslPolicyErrors.None)
                    return true;

                // Log certificate details for debugging
                System.Diagnostics.Debug.WriteLine($"SSL Error: {errors}");
                System.Diagnostics.Debug.WriteLine($"Certificate Subject: {cert?.Subject}");
                System.Diagnostics.Debug.WriteLine($"Certificate Issuer: {cert?.Issuer}");

                // OPTION 1: Accept self-signed certificate (development only - NOT for production)
                // Uncomment the line below if testing with self-signed certificates
                // return true;

                // OPTION 2: Validate specific certificate thumbprint (RECOMMENDED for production)
                if (cert != null)
                {
                    string certThumbprint = cert.GetCertHashString();
                    // Replace with your certificate thumbprint from your hosting provider
                    string expectedThumbprint = "YOUR_CERTIFICATE_THUMBPRINT_HERE";

                    if (!string.IsNullOrEmpty(expectedThumbprint) && expectedThumbprint != "YOUR_CERTIFICATE_THUMBPRINT_HERE")
                    {
                        bool isValid = certThumbprint.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase);

                        if (isValid)
                            System.Diagnostics.Debug.WriteLine($"Certificate validated: {certThumbprint}");
                        else
                            System.Diagnostics.Debug.WriteLine($"Certificate validation failed. Expected: {expectedThumbprint}, Got: {certThumbprint}");

                        return isValid;
                    }

                    // Fallback: Use debug mode distinction
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"WARNING: Using fallback SSL validation in DEBUG mode. Certificate Thumbprint: {certThumbprint}");
                    return true;
#else
                    System.Diagnostics.Debug.WriteLine($"ERROR: Certificate validation failed in RELEASE mode. Certificate Thumbprint: {certThumbprint}");
                    return false;
#endif
                }

                return false;
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Set security protocol - TLS 1.2 minimum recommended
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            return client;
        }

        /// <summary>
        /// Loads gazette data asynchronously
        /// </summary>
        private async Task LoadGazetteDataAsync()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                System.Diagnostics.Debug.WriteLine($"Fetching gazette data from: {ApiUrl}");

                var response = await _httpClient.GetAsync(ApiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"API Error: {response.StatusCode} - {response.ReasonPhrase}");
                    await DisplayAlert("Error", $"Failed to load data: {response.StatusCode}", "OK");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                {
                    System.Diagnostics.Debug.WriteLine("Empty response from server");
                    await DisplayAlert("Error", "Empty response from server", "OK");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Response received: {json.Length} characters");

                // Parse JSON
                List<ServicesList> items = JsonConvert.DeserializeObject<List<ServicesList>>(json);

                if (items == null || items.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No items returned from API");
                    await DisplayAlert("No Data", "No gazette entries found", "OK");
                    listView.ItemsSource = new List<ServicesList>();
                    return;
                }

                // Sort and display
                var sortedGazette = items.OrderBy(x => x.businessName).ToList();
                System.Diagnostics.Debug.WriteLine($"Loaded and sorted {sortedGazette.Count} items");

                listView.ItemsSource = sortedGazette;
            }
            catch (HttpRequestException httpEx)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP Error: {httpEx.Message}");
                await DisplayAlert("Network Error",
                    "Failed to connect to the server. Please check your internet connection and ensure the SSL certificate is valid.",
                    "OK");
            }
            catch (JsonException jsonEx)
            {
                System.Diagnostics.Debug.WriteLine($"JSON Deserialization Error: {jsonEx.Message}");
                await DisplayAlert("Data Error",
                    "Failed to process the server response. The data format may be invalid.",
                    "OK");
            }
            catch (TaskCanceledException timeoutEx)
            {
                System.Diagnostics.Debug.WriteLine($"Request Timeout: {timeoutEx.Message}");
                await DisplayAlert("Timeout Error",
                    "The request took too long to complete. Please check your connection and try again.",
                    "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                await DisplayAlert("Error",
                    $"An unexpected error occurred: {ex.Message}",
                    "OK");
            }
        }
    }
}