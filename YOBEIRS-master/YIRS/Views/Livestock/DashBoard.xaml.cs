using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;
using YIRS.Views.Yorata_Ops;

namespace YIRS.Views.Livestock
{
    /// <summary>
    /// Landing page for agents whose login category is "Damaturu" (livestock collection).
    ///
    /// Two entry points, per the module spec:
    ///   1. Livestock Collection  → <see cref="ServiceList"/> (select services, set quantities, pay, print)
    ///   2. Dashboard utilities   → wallet balance and settings
    ///
    /// Logout deliberately rebuilds the root as <c>Views.MainPage</c> rather than calling
    /// PopToRootAsync — the Yorata-Ops and Default modules get this wrong and land the agent
    /// back on a dashboard with a dead session.
    /// </summary>
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DashBoard : ContentPage
    {
        // ══════════════════════════════════════════════════════════════
        //  HTTP  (single static instance — avoids socket exhaustion)
        // ══════════════════════════════════════════════════════════════

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };

            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private const string BalanceUrl =
            "https://yobe.osoftpay.net/api/singlecollections/getbalance";

        private bool _isBusy;

        // ══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════

        public DashBoard()
        {
            try
            {
                InitializeComponent();
                BindAgentHeader();
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                SessionManager.Instance.UpdateActivity();
                BindAgentHeader();

                // The session can only be gone if logout raced the page; bail out cleanly.
                if (!SessionManager.IsAuthenticated)
                {
                    Device.BeginInvokeOnMainThread(() => App.SetRoot(new Views.MainPage()));
                }
            }
            catch (Exception ex)
            {
                LogError("OnAppearing", ex);
            }
        }

        /// <summary>Hardware back on the dashboard must not unwind to a stale page.</summary>
        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                bool leave = await DisplayAlert("Exit", "Log out of YIRS?", "Logout", "Stay");
                if (leave) await PerformLogoutAsync();
            });

            return true;
        }

        private void BindAgentHeader()
        {
            try
            {
                CollectionPointLabel.Text = string.IsNullOrWhiteSpace(MainPage.CollectionPoint)
                    ? "Unknown Collection Point"
                    : MainPage.CollectionPoint;

                AgentNameLabel.Text = string.IsNullOrWhiteSpace(MainPage.Name)
                    ? "Agent"
                    : MainPage.Name;
            }
            catch (Exception ex)
            {
                LogError("BindAgentHeader", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  NAVIGATION
        // ══════════════════════════════════════════════════════════════

        private async void OnCollectTapped(object sender, EventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;

            try
            {
                SessionManager.Instance.UpdateActivity();
                await CollectCard.ScaleTo(0.97, 80);
                await CollectCard.ScaleTo(1.0, 80);

                await Navigation.PushAsync(new ServiceList());
            }
            catch (Exception ex)
            {
                LogError("OnCollectTapped", ex);
                await DisplayAlert("Error", "Unable to open the collection screen.", "OK");
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async void OnSettingsTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                string choice = await DisplayActionSheet(
                    "Settings", "Cancel", null, "Change Password", "Change Transfer PIN");

                if (choice == "Change Password")
                    await Navigation.PushAsync(new ChangePassword());
                else if (choice == "Change Transfer PIN")
                    await Navigation.PushAsync(new ChangeTransferPIN());
            }
            catch (Exception ex)
            {
                LogError("OnSettingsTapped", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  WALLET BALANCE
        // ══════════════════════════════════════════════════════════════

        private async void OnBalanceTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(MainPage.Pin) ||
                    string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    await DisplayAlert("Session", "Please sign in again to view your balance.", "OK");
                    return;
                }

                BalanceLabel.Text = "Checking…";

                string url = BalanceUrl +
                             "?Pin=" + Uri.EscapeDataString(MainPage.Pin) +
                             "&Email=" + Uri.EscapeDataString(MainPage.ValidUserMail);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                using (var response = await Http.GetAsync(url, cts.Token))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine("[LiveStock:Balance] " + json);

                    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                    {
                        BalanceLabel.Text = "Unavailable";
                        return;
                    }

                    var result = JsonConvert.DeserializeObject<BalanceResponse>(json);
                    BalanceLabel.Text = result == null
                        ? "Unavailable"
                        : string.Format("₦{0:N2}", result.Balance);
                }
            }
            catch (OperationCanceledException)
            {
                BalanceLabel.Text = "Timed out";
            }
            catch (Exception ex)
            {
                LogError("OnBalanceTapped", ex);
                BalanceLabel.Text = "Unavailable";
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LOGOUT
        // ══════════════════════════════════════════════════════════════

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            try
            {
                bool confirm = await DisplayAlert("Logout", "Are you sure you want to logout?", "Yes", "No");
                if (confirm) await PerformLogoutAsync();
            }
            catch (Exception ex)
            {
                LogError("OnLogoutTapped", ex);
            }
        }

        private async Task PerformLogoutAsync()
        {
            try
            {
                UserDialogs.Instance.ShowLoading("Logging out...");
                await Task.Delay(400);

                SessionManager.Instance.StopSession();
                App.IsUserLoggedIn = false;
                await SecureStorageService.ClearCredentialsAsync();

                UserDialogs.Instance.HideLoading();

                Device.BeginInvokeOnMainThread(() => App.SetRoot(new Views.MainPage()));
            }
            catch (Exception ex)
            {
                LogError("PerformLogoutAsync", ex);
                UserDialogs.Instance.HideLoading();
                await DisplayAlert("Logout", "Logout failed. Please try again.", "OK");
            }
        }

        private static void LogError(string scope, Exception ex)
            => System.Diagnostics.Debug.WriteLine(
                string.Format("[LiveStock:Dashboard:{0}] {1}", scope, ex));

        // ══════════════════════════════════════════════════════════════
        //  MODELS
        // ══════════════════════════════════════════════════════════════

        private class BalanceResponse
        {
            [JsonProperty("balance")]
            public decimal Balance { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }
    }
}