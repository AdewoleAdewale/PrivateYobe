using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Direct : ContentPage
    {
        public bool DoesBusinessExist { get; set; }
        public static string BusinessNames { get; set; }
        public static string ServiceName { get; set; }
        public static string PayerIds { get; set; }
        public static string Amount { get; set; }
        public static string BusinessLGA { get; set; }

        private bool _isProcessing = false;

        public Direct()
        {
            InitializeComponent();
            TrackUserActivity();
            InitializeAnimations();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            AnimatePageEntry();
        }

        private void InitializeAnimations()
        {
            // Set initial states for animations
            if (VerifyButton != null)
            {
                VerifyButton.Scale = 1;
                VerifyButton.Opacity = 1;
            }
        }

        private async void AnimatePageEntry()
        {
            try
            {
                // Animate the verify button on page load
                if (VerifyButton != null)
                {
                    await VerifyButton.ScaleTo(0.95, 0);
                    await VerifyButton.ScaleTo(1, 300, Easing.SpringOut);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Animation error: {ex.Message}");
            }
        }

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);

            // Track entry focus
            if (PayerId != null)
            {
                PayerId.Focused += (s, e) => SessionManager.Instance.UpdateActivity();
                PayerId.TextChanged += (s, e) => SessionManager.Instance.UpdateActivity();
            }
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            // Prevent multiple taps
            if (_isProcessing)
                return;

            SessionManager.Instance.UpdateActivity();

            // Validate input
            if (string.IsNullOrWhiteSpace(PayerId?.Text))
            {
                await AnimateButtonError();
                await DisplayAlert("Validation Error", "Please enter a valid Payer ID", "OK");
                return;
            }

            // Animate button press
            await AnimateButtonPress();

            _isProcessing = true;

            try
            {
                await VerifyPayerId();
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task AnimateButtonPress()
        {
            try
            {
                if (VerifyButton != null)
                {
                    await VerifyButton.ScaleTo(0.95, 100, Easing.CubicIn);
                    await VerifyButton.ScaleTo(1, 100, Easing.CubicOut);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Button animation error: {ex.Message}");
            }
        }

        private async Task AnimateButtonError()
        {
            try
            {
                if (VerifyButton != null)
                {
                    // Shake animation
                    await VerifyButton.TranslateTo(-10, 0, 50);
                    await VerifyButton.TranslateTo(10, 0, 50);
                    await VerifyButton.TranslateTo(-10, 0, 50);
                    await VerifyButton.TranslateTo(10, 0, 50);
                    await VerifyButton.TranslateTo(0, 0, 50);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error animation error: {ex.Message}");
            }
        }

        private async Task VerifyPayerId()
        {
            var loadingDialog = UserDialogs.Instance.Loading("Verifying Payer ID...", null, null, true, MaskType.Black);

            try
            {
                await Task.Delay(500); // Small delay for better UX

                string url = $"https://yobe.osoftpay.net/api/taskpayers/GetbizCount/{PayerId.Text.Trim()}";

                using (var httpClientHandler = new HttpClientHandler())
                {
                    // SSL certificate validation callback
                    httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                    using (HttpClient client = new HttpClient(httpClientHandler))
                    {
                        // Set timeout
                        client.Timeout = TimeSpan.FromSeconds(30);

                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                        var response = await client.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            var result = JsonConvert.DeserializeObject<PayerIdResponse>(json);

                            loadingDialog?.Hide();
                            await ProcessVerificationResult(result);
                        }
                        else
                        {
                            loadingDialog?.Hide();
                            await HandleErrorResponse(response.StatusCode);
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                loadingDialog?.Hide();
                await DisplayAlert("Connection Timeout",
                    "The request took too long to complete. Please check your internet connection and try again.",
                    "OK");
            }
            catch (HttpRequestException httpEx)
            {
                loadingDialog?.Hide();
                await DisplayAlert("Network Error",
                    "Unable to connect to the server. Please check your internet connection.",
                    "OK");
                System.Diagnostics.Debug.WriteLine($"HTTP Error: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                loadingDialog?.Hide();
                await DisplayAlert("Error",
                    "An unexpected error occurred. Please try again later.",
                    "OK");
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task ProcessVerificationResult(PayerIdResponse result)
        {
            if (result == null)
            {
                await DisplayAlert("Error", "Unable to process server response. Please try again.", "OK");
                return;
            }

            if (result.status == "00")
            {
                // Success - show success animation
                await AnimateSuccess();

                DoesBusinessExist = true;
                BusinessNames = result.businessName;
                BusinessLGA = result.LGA;
                ServiceName = result.ServiceName;
                PayerIds = PayerId.Text;
                Amount = result.Amount;

                // Show success message
                UserDialogs.Instance.Toast(new ToastConfig("Verification Successful!")
                {
                    Duration = TimeSpan.FromSeconds(2),
                    BackgroundColor = System.Drawing.Color.FromArgb(0, 66, 37)
                });

                await Task.Delay(500);

                // Navigate to next page
                Application.Current.MainPage = new NavigationPage(new Views.Home.DirectPayment());
            }
            else
            {
                DoesBusinessExist = false;
                await AnimateButtonError();

                string message = !string.IsNullOrEmpty(result.message)
                    ? result.message
                    : "Payer ID not found. Please check and try again.";

                await DisplayAlert("Verification Failed", message, "OK");
            }
        }

        private async Task AnimateSuccess()
        {
            try
            {
                if (VerifyButton != null)
                {
                    // Pulse animation
                    await VerifyButton.ScaleTo(1.1, 150, Easing.CubicOut);
                    await VerifyButton.ScaleTo(1, 150, Easing.CubicIn);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Success animation error: {ex.Message}");
            }
        }

        private async Task HandleErrorResponse(HttpStatusCode statusCode)
        {
            string errorMessage;
            if (statusCode == HttpStatusCode.NotFound)
            {
                errorMessage = "Service endpoint not found. Please contact support.";
            }
            else if (statusCode == HttpStatusCode.Unauthorized)
            {
                errorMessage = "Authentication failed. Please try again.";
            }
            else if (statusCode == HttpStatusCode.InternalServerError)
            {
                errorMessage = "Server error occurred. Please try again later.";
            }
            else if (statusCode == HttpStatusCode.ServiceUnavailable)
            {
                errorMessage = "Service is temporarily unavailable. Please try again later.";
            }
            else
            {
                errorMessage = $"Request failed with status code: {statusCode}";
            }

            await DisplayAlert("Connection Error", errorMessage, "OK");
        }

        protected override bool OnBackButtonPressed()
        {
            // Handle back button press with confirmation if needed
            return base.OnBackButtonPressed();
        }
    }

    internal class PayerIdResponse
    {
        public string businessName { get; set; }
        public string ServiceName { get; set; }
        public string Amount { get; set; }
        public string status { get; set; }
        public string message { get; set; }
        public string payerId { get; set; }
        public string LGA { get; set; }
    }
}