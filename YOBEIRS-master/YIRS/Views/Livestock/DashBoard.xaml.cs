using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PlatformConfiguration.WindowsSpecific;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Livestock
{
    /// <summary>
    /// Landing page for agents whose login category is "Damaturu" (livestock collection).
    ///
    /// Surfaces, in priority order for someone standing at a market gate:
    ///   • who they are and which collection point they are posting to
    ///   • wallet balance and what they have collected today
    ///   • one large action to start a collection
    ///   • verify, history, change PIN, change password
    ///   • the five most recent transactions, tapping through to full history
    ///
    /// Logout rebuilds the root as <c>Views.MainPage</c> rather than calling PopToRootAsync —
    /// the Yorata-Ops and Default modules get this wrong and land the agent back on a
    /// dashboard with a dead session.
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
        private bool _isRefreshing;

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

                if (!SessionManager.IsAuthenticated)
                {
                    Device.BeginInvokeOnMainThread(() => App.SetRoot(new Views.MainPage()));
                    return;
                }

                BindAgentHeader();

                // Refreshed on every appearance, not just the first, so returning from a
                // payment immediately shows the transaction that was just taken.
                Device.BeginInvokeOnMainThread(async () => await RefreshAllAsync());
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
                AgentNameLabel.Text = string.IsNullOrWhiteSpace(MainPage.Name)
                    ? "Agent"
                    : MainPage.Name;

                CollectionPointLabel.Text = string.IsNullOrWhiteSpace(MainPage.CollectionPoint)
                    ? "Unknown Collection Point"
                    : MainPage.CollectionPoint;

                string revHead = LivestockModule.RevenueHead;
                CategoryBadgeLabel.Text = revHead.ToUpperInvariant();

                string category = (MainPage.Category ?? string.Empty).Trim();
                CategoryBadgeLabel.Text = string.IsNullOrWhiteSpace(category)
                    ? "LIVESTOCK"
                    : category.ToUpperInvariant();

                int hour = DateTime.Now.Hour;
                GreetingLabel.Text = hour < 12
                    ? "Good morning"
                    : (hour < 17 ? "Good afternoon" : "Good evening");
            }
            catch (Exception ex)
            {
                LogError("BindAgentHeader", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  REFRESH
        // ══════════════════════════════════════════════════════════════

        private async void OnRefreshing(object sender, EventArgs e)
        {
            try { await RefreshAllAsync(); }
            catch (Exception ex)
            {
                LogError("OnRefreshing", ex);
                refreshView.IsRefreshing = false;
            }
        }

        private async Task RefreshAllAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                // Balance and transactions are independent — run them together rather than
                // making the agent wait for one before the other starts.
                await Task.WhenAll(LoadBalanceAsync(), LoadRecentAsync());
            }
            finally
            {
                _isRefreshing = false;
                MainThread.BeginInvokeOnMainThread(() => refreshView.IsRefreshing = false);
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
                await LoadBalanceAsync();
            }
            catch (Exception ex)
            {
                LogError("OnBalanceTapped", ex);
            }
        }

        private async Task LoadBalanceAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(MainPage.Pin) ||
                    string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    BalanceLabel.Text = "Sign in again";
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
                    System.Diagnostics.Debug.WriteLine("[Livestock:Balance] " + json);

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
                LogError("LoadBalanceAsync", ex);
                BalanceLabel.Text = "Unavailable";
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  RECENT TRANSACTIONS + TODAY'S TOTALS
        // ══════════════════════════════════════════════════════════════

        private async Task LoadRecentAsync()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecentLoadingCard.IsVisible = true;
                    RecentEmptyCard.IsVisible = false;
                });

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40)))
                {
                    // One call covers both panels: today's totals are derived from the same
                    // 30-day window used for the recent list, so there is no second request.
                    var response = await LivestockTransactionService.GetAsync(
                        DateTime.Now.Date.AddDays(-30), DateTime.Now.Date, cts.Token);

                    var all = response.Transactions ?? new List<Transaction>();

                    var today = all
                        .Where(t => t.ParsedDate.HasValue &&
                                    t.ParsedDate.Value.Date == DateTime.Now.Date &&
                                    t.IsSuccessful)
                        .ToList();

                    var recent = all.Take(5).ToList();

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        TodayAmountLabel.Text = string.Format("₦{0:N2}", today.Sum(t => t.Amount));
                        TodayCountLabel.Text = today.Count.ToString();

                        RecentLoadingCard.IsVisible = false;

                        if (recent.Count == 0)
                        {
                            ShowRecentPlaceholder("🐄",
                                "No collections in the last 30 days. Tap New Collection to start.");
                        }
                        else
                        {
                            RecentEmptyCard.IsVisible = false;
                            RecentStack.IsVisible = true;
                            BindableLayout.SetItemsSource(RecentStack, recent);
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                ShowRecentPlaceholder("⏱", "Recent collections timed out. Pull down to retry.");
            }
            catch (HttpRequestException)
            {
                ShowRecentPlaceholder("📡", "No connection. Pull down to retry.");
            }
            catch (Exception ex)
            {
                LogError("LoadRecentAsync", ex);
                ShowRecentPlaceholder("⚠", "Could not load recent collections.");
            }
        }

        private void ShowRecentPlaceholder(string icon, string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RecentLoadingCard.IsVisible = false;
                RecentStack.IsVisible = false;
                BindableLayout.SetItemsSource(RecentStack, null);

                RecentEmptyIcon.Text = icon;
                RecentEmptyLabel.Text = message;
                RecentEmptyCard.IsVisible = true;
            });
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

                // Fully qualified on purpose: YIRS.Views.Yorata_Ops also has a ServiceList,
                // and an unqualified name here silently opens the wrong module's page.
                await Navigation.PushAsync(new YIRS.Views.Livestock.ServicePayment());
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

        private async void OnHistoryTapped(object sender, EventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;

            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new YIRS.Views.Livestock.History());
            }
            catch (Exception ex)
            {
                LogError("OnHistoryTapped", ex);
            }
            finally
            {
                _isBusy = false;
            }
        }

        /// <summary>
        /// A recent row opens the full history rather than a one-off detail page — the agent
        /// is almost always reaching for the wider list anyway.
        /// </summary>
        private async void OnRecentTapped(object sender, EventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;

            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new History());
            }
            catch (Exception ex)
            {
                LogError("OnRecentTapped", ex);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async void OnVerifyTapped(object sender, EventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;

            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new VerifyLivestock());
            }
            catch (Exception ex)
            {
                LogError("OnVerifyTapped", ex);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async void OnChangePinTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new ChangeTransferPIN());
            }
            catch (Exception ex)
            {
                LogError("OnChangePinTapped", ex);
            }
        }

        private async void OnChangePasswordTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new ChangePassword());
            }
            catch (Exception ex)
            {
                LogError("OnChangePasswordTapped", ex);
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
                string.Format("[Livestock:Dashboard:{0}] {1}", scope, ex));

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