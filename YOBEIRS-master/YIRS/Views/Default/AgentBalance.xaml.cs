using FFImageLoading;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class AgentBalance : ContentPage
    {
        private const string API_URL = "https://yobe.osoftpay.net/api/singlecollections/cashout";
        private const string EXPECTED_CERT_THUMBPRINT = "YOUR_CERTIFICATE_THUMBPRINT_HERE";
        private const string TRUSTED_CA_ISSUER = "Your CA Name";

        private bool isProcessing = false;

        public AgentBalance()
        {
            InitializeComponent();
            InitializeUI();
            TrackUserActivity();
            ConfigureSSLSecurity();
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private void InitializeUI()
        {
            AgentSupervisor.Text = DefaultDashboard.superAgent;
            Agentname.Text = MainPage.Name;
            CashoutBalance.Text = DefaultDashboard.cashoutBalance;
        }

        private void ConfigureSSLSecurity()
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11;

            ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            throw new NotImplementedException();
        }

        private bool ValidateServerCertificate(object sender, X509Certificate2 certificate,
           X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                return true;

            if (sslPolicyErrors.HasFlag(System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors))
            {
                if (!ValidateCertificateChain(chain))
                {
                    LogError("Certificate Chain Error",
                        new Exception($"Chain validation failed for {certificate?.Subject}"));
                    return false;
                }
            }

            if (certificate != null && !ValidateCertificateThumbprint(certificate))
            {
                LogError("Certificate Thumbprint Mismatch",
                    new Exception($"Thumbprint: {certificate.Thumbprint}"));
                return false;
            }

            if (certificate != null && ValidateCertificateIssuer(certificate))
                return true;

            LogError("Certificate Validation Failed",
                new Exception($"Subject: {certificate?.Subject}, Issuer: {certificate?.Issuer}"));
            return false;
        }

        private bool ValidateCertificateThumbprint(X509Certificate2 certificate)
        {
            if (!string.IsNullOrEmpty(EXPECTED_CERT_THUMBPRINT))
                return certificate.Thumbprint.Equals(EXPECTED_CERT_THUMBPRINT,
                    StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private bool ValidateCertificateIssuer(X509Certificate2 certificate)
        {
            if (string.IsNullOrEmpty(TRUSTED_CA_ISSUER))
                return true;
            return certificate.Issuer.Contains(TRUSTED_CA_ISSUER, StringComparison.OrdinalIgnoreCase);
        }

        private bool ValidateCertificateChain(X509Chain chain)
        {
            if (chain == null) return false;
            foreach (X509ChainStatus status in chain.ChainStatus)
            {
                if (status.Status != X509ChainStatusFlags.NoError)
                {
                    LogError("Chain Status Error", new Exception(status.StatusInformation));
                    return false;
                }
            }
            return true;
        }

        private HttpClientHandler CreateSecureHttpClientHandler()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                ValidateServerCertificate(message, cert, chain, errors);
            return handler;
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            if (isProcessing) return;

            string selectedDropdown = picker.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDropdown))
            {
                await DisplayAlert("NOTIFICATION",
                    "Kindly select the dropdown before you proceed", "TRY AGAIN");
                return;
            }

            var choice = await DisplayAlert("NOTIFICATION",
                $"Are you sure you want to make this cashout using {selectedDropdown}?", "YES", "NO");

            if (!choice)
            {
                await DisplayAlert("NOTIFICATION",
                    "Your cashout payment has been cancelled", "THANK YOU");
                return;
            }

            await PerformCashout(selectedDropdown);
        }

        private async System.Threading.Tasks.Task PerformCashout(string selectedMethod)
        {
            isProcessing = true;
            await AnimateButton(true);
            SessionManager.Instance.UpdateActivity();

            // Hide previous summary
            TransactionSummaryCard.IsVisible = false;

            try
            {
                string methodCode = selectedMethod == "Super Agent" ? "1" : "2";
                string email = MainPage.ValidUserMail;
                string pin = Password.Text;

                if (string.IsNullOrEmpty(pin) || pin.Length != 4)
                {
                    await DisplayAlert("NOTIFICATION", "Please enter a valid 4-digit PIN", "TRY AGAIN");
                    return;
                }

                ShowLoadingIndicator(true);

                using (var handler = CreateSecureHttpClientHandler())
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(30);

                    var requestData = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("Email", email),
                        new KeyValuePair<string, string>("Pin", pin),
                        new KeyValuePair<string, string>("CashOutMethod", methodCode)
                    };

                    var content = new FormUrlEncodedContent(requestData);
                    var request = new HttpRequestMessage(HttpMethod.Post, API_URL) { Content = content };
                    request.Headers.Add("User-Agent", "YIRS-Mobile/1.0");
                    request.Headers.Add("X-Requested-With", "XMLHttpRequest");

                    var response = await client.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"Server returned status code: {response.StatusCode}");

                    var responseString = await response.Content.ReadAsStringAsync();
                    var cashOutResponse = JsonConvert.DeserializeObject<CashOutResponse>(responseString);

                    await ShowResultAnimation(cashOutResponse, selectedMethod);

                    Password.Text = string.Empty;
                }
            }
            catch (HttpRequestException ex)
            {
                LogError("HTTP Request Error", ex);
                await DisplayAlert("ERROR", "Network error. Please check your connection and try again.", "TRY AGAIN");
            }
            catch (TaskCanceledException ex)
            {
                LogError("Request Timeout", ex);
                await DisplayAlert("ERROR", "Request timed out. Please try again.", "TRY AGAIN");
            }
            catch (JsonException ex)
            {
                LogError("JSON Parse Error", ex);
                await DisplayAlert("ERROR", "Invalid response from server. Please try again.", "TRY AGAIN");
            }
            catch (Exception ex)
            {
                LogError("Unexpected Error", ex);
                await DisplayAlert("ERROR", "An unexpected error occurred. Please try again.", "TRY AGAIN");
            }
            finally
            {
                ShowLoadingIndicator(false);
                await AnimateButton(false);
                isProcessing = false;
            }
        }

        private async System.Threading.Tasks.Task ShowResultAnimation(CashOutResponse response, string selectedMethod)
        {
            if (response?.status == "AB3")
            {
                // Populate transaction summary card
                PopulateTransactionSummary(response, selectedMethod, true);

                await AnimateSuccessMessage(response.message);
                await DisplayAlert("SUCCESS", response.message, "THANK YOU");
            }
            else
            {
                PopulateTransactionSummary(response, selectedMethod, false);
                await AnimateErrorMessage();
                await DisplayAlert("NOTIFICATION", response?.message ?? "Transaction failed", "TRY AGAIN");
            }
        }

        /// <summary>
        /// Fills the Transaction Summary Card with response data and reveals it with animation.
        /// </summary>
        private void PopulateTransactionSummary(CashOutResponse response, string selectedMethod, bool success)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Populate labels
                    SummaryAgentName.Text = response?.cashoutClass?.SuperAgent ?? AgentSupervisor.Text ?? "N/A";
                    SummaryAmountReceived.Text = response?.cashoutClass?.AmountReceived != null
                        ? $"₦{response.cashoutClass.AmountReceived}"
                        : CashoutBalance.Text ?? "N/A";
                    SummaryAgent.Text = response?.cashoutClass?.Agent ?? Agentname.Text ?? "N/A";
                    SummaryMethod.Text = selectedMethod;
                    SummaryStatus.Text = success ? "✓  Successful" : "✗  Failed";
                    SummaryStatus.TextColor = success
                        ? Color.FromHex("#059669")
                        : Color.FromHex("#DC2626");
                    SummaryTimestamp.Text = DateTime.Now.ToString("hh:mm tt");

                    // Animate card in
                    TransactionSummaryCard.IsVisible = true;
                    TransactionSummaryCard.Opacity = 0;
                    TransactionSummaryCard.TranslationY = 20;
                    await Task.WhenAll(
                        TransactionSummaryCard.FadeTo(1, 400),
                        TransactionSummaryCard.TranslateTo(0, 0, 400, Easing.CubicOut)
                    );
                }
                catch (Exception ex)
                {
                    LogError("PopulateTransactionSummary", ex);
                }
            });
        }

        private async System.Threading.Tasks.Task AnimateButton(bool isLoading)
        {
            if (isLoading)
            {
                await CashOutButton.FadeTo(0.6, 200);
                await CashOutButton.ScaleTo(0.95, 200);
            }
            else
            {
                await CashOutButton.FadeTo(1, 200);
                await CashOutButton.ScaleTo(1, 200);
            }
        }

        private async System.Threading.Tasks.Task AnimateSuccessMessage(string message)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                SuccessMessage.Text = message;
                ErrorMessageBox.IsVisible = false;
                SuccessMessageBox.IsVisible = true;
                SuccessMessageBox.Opacity = 0;
                await SuccessMessageBox.FadeTo(1, 350);
            });
        }

        private async System.Threading.Tasks.Task AnimateErrorMessage()
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                SuccessMessageBox.IsVisible = false;
                ErrorMessageBox.IsVisible = true;
                ErrorMessageBox.Opacity = 0;
                await ErrorMessageBox.FadeTo(1, 350);
                // Subtle shake
                for (int i = 0; i < 3; i++)
                {
                    await ErrorMessageBox.TranslateTo(-6, 0, 60);
                    await ErrorMessageBox.TranslateTo(6, 0, 60);
                }
                await ErrorMessageBox.TranslateTo(0, 0, 60);
            });
        }

        private void ShowLoadingIndicator(bool show)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingIndicator.IsRunning = show;
                LoadingIndicator.IsVisible = show;
            });
        }

        private void LogError(string title, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{title}] {ex?.Message}\n{ex?.StackTrace}");
        }

        #region Response Classes

        internal class CashOutResponse
        {
            [JsonProperty("status")]
            public string status { get; set; }

            [JsonProperty("message")]
            public string message { get; set; }

            [JsonProperty("cashoutClass")]
            public CashoutClass cashoutClass { get; set; }
        }

        internal class CashoutClass
        {
            [JsonProperty("superAgent")]
            public string SuperAgent { get; set; }

            [JsonProperty("amountReceived")]
            public string AmountReceived { get; set; }

            [JsonProperty("agent")]
            public string Agent { get; set; }
        }

        #endregion
    }
}