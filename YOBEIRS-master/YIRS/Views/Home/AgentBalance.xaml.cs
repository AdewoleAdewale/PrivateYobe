using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;
using YIRS.Views.Default;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class AgentBalance : ContentPage
    {
        private bool _isProcessing = false;

        public AgentBalance()
        {
            InitializeComponent();
            InitializePageData();
            TrackUserActivity();
        }

        private void InitializePageData()
        {
            try
            {
                AgentSupervisor.Text = DefaultDashboard.superAgent ?? "N/A";
                Agentname.Text = MainPage.Name ?? "N/A";
                CashoutBalance.Text = DefaultDashboard.cashoutBalance ?? "₦0.00";
            }
            catch (Exception ex)
            {
                DisplayAlert("Initialization Error", "Failed to load agent information. Please try again.", "OK");
                System.Diagnostics.Debug.WriteLine($"InitializePageData Error: {ex.Message}");
            }
        }

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }
        private async void Button_Clicked(object sender, EventArgs e)
        {
            // Prevent double-clicking
            if (_isProcessing)
                return;
            SessionManager.Instance.UpdateActivity();
            try
            {
                _isProcessing = true;

                // Validate inputs
                if (!ValidateInputs())
                {
                    _isProcessing = false;
                    return;
                }

                string selectedDropdown = picker.SelectedItem.ToString();
                string selectedName = picker.SelectedItem.ToString();
                string cashoutMethodCode = GetCashoutMethodCode(selectedDropdown);

                // Confirm action with user
                var choice = await DisplayAlert(
                    "CONFIRM CASHOUT",
                    $"Are you sure you want to cashout using {selectedName}?",
                    "YES",
                    "NO"
                );

                if (!choice)
                {
                    await DisplayAlert("NOTIFICATION", "Cashout request cancelled.", "OK");
                    _isProcessing = false;
                    return;
                }

                // Show loading indicator
                await ProcessCashout(cashoutMethodCode);
            }
            catch (Exception ex)
            {
                await HandleException(ex, "An unexpected error occurred during cashout.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private bool ValidateInputs()
        {
            SessionManager.Instance.UpdateActivity();
            try
            {
                // Check if cashout method is selected
                if (picker.SelectedItem == null)
                {
                    DisplayAlert("VALIDATION ERROR", "Please select a cashout method.", "OK");
                    return false;
                }

                // Check if PIN is entered
                if (string.IsNullOrWhiteSpace(Password.Text))
                {
                    DisplayAlert("VALIDATION ERROR", "Please enter your 4-digit PIN.", "OK");
                    return false;
                }

                // Validate PIN length
                if (Password.Text.Length != 4)
                {
                    DisplayAlert("VALIDATION ERROR", "PIN must be exactly 4 digits.", "OK");
                    return false;
                }

                // Validate PIN contains only numbers
                if (!int.TryParse(Password.Text, out _))
                {
                    DisplayAlert("VALIDATION ERROR", "PIN must contain only numbers.", "OK");
                    return false;
                }

                // Check if email is available
                if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    DisplayAlert("AUTHENTICATION ERROR", "User email not found. Please login again.", "OK");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                DisplayAlert("VALIDATION ERROR", "Error validating input fields.", "OK");
                System.Diagnostics.Debug.WriteLine($"ValidateInputs Error: {ex.Message}");
                return false;
            }
        }

        private string GetCashoutMethodCode(string methodName)
        {
            switch (methodName)
            {
                case "Super Agent":
                    return "1";
                case "Commercial Account":
                    return "2";
                default:
                    return "1";
            }
        }

        private async Task ProcessCashout(string cashoutMethodCode)
        {
            string url = "https://yobe.osoftpay.net/api/singlecollections/cashout";
            HttpClient client = null;
            SessionManager.Instance.UpdateActivity();
            try
            {
                string email = MainPage.ValidUserMail;

                var nvc = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Email", email),
                    new KeyValuePair<string, string>("Pin", Password.Text),
                    new KeyValuePair<string, string>("CashOutMethod", cashoutMethodCode)
                };

                client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(nvc)
                };

                var res = await client.SendAsync(req);

                if (!res.IsSuccessStatusCode)
                {
                    await DisplayAlert(
                        "CONNECTION ERROR",
                        $"Server returned error: {res.StatusCode}. Please try again.",
                        "OK"
                    );
                    return;
                }

                var resultString = await res.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(resultString))
                {
                    await DisplayAlert("ERROR", "Empty response from server. Please try again.", "OK");
                    return;
                }

                var cashOutResponse = JsonConvert.DeserializeObject<CashOutResponse>(resultString);

                if (cashOutResponse == null)
                {
                    await DisplayAlert("ERROR", "Failed to process server response. Please try again.", "OK");
                    return;
                }

                await HandleCashoutResponse(cashOutResponse);
            }
            catch (TaskCanceledException)
            {
                await DisplayAlert(
                    "TIMEOUT ERROR",
                    "Request timed out. Please check your internet connection and try again.",
                    "OK"
                );
            }
            catch (HttpRequestException httpEx)
            {
                await DisplayAlert(
                    "NETWORK ERROR",
                    "Unable to connect to server. Please check your internet connection.",
                    "OK"
                );
                System.Diagnostics.Debug.WriteLine($"HttpRequestException: {httpEx.Message}");
            }
            catch (JsonException jsonEx)
            {
                await DisplayAlert(
                    "DATA ERROR",
                    "Failed to process response data. Please contact support.",
                    "OK"
                );
                System.Diagnostics.Debug.WriteLine($"JsonException: {jsonEx.Message}");
            }
            finally
            {
                client?.Dispose();
                // Clear PIN for security
                Password.Text = string.Empty;
            }
        }

        private async Task HandleCashoutResponse(CashOutResponse response)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                if (response.status == "AB3")
                {
                    await DisplayAlert("SUCCESS", response.message ?? "Cashout processed successfully!", "OK");

                    // Navigate back to dashboard
                    await Navigation.PopAsync();
                }
                else
                {
                    string errorMessage = !string.IsNullOrWhiteSpace(response.message)
                        ? response.message
                        : "Cashout failed. Please try again.";

                    await DisplayAlert("CASHOUT FAILED", errorMessage, "OK");
                }
            }
            catch (Exception ex)
            {
                await HandleException(ex, "Error processing cashout response.");
            }
        }

        private async Task HandleException(Exception ex, string userMessage)
        {
            System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");

            string detailedMessage = userMessage;

            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }

            await DisplayAlert("ERROR", detailedMessage, "OK");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Clear sensitive data when leaving page
            Password.Text = string.Empty;
        }

        #region Response Models

        internal class CashOutResponse
        {
            public string status { get; set; }
            public string message { get; set; }
            public CashoutClass cashoutClass { get; set; }
        }

        internal class CashoutClass
        {
            public string SuperAgent { get; set; }
            public string AmountReceived { get; set; }
            public string agent { get; set; }
        }

        #endregion
    }
}