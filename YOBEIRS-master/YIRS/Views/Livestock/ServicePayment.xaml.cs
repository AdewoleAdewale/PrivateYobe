using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.PlatformConfiguration.WindowsSpecific;
using Xamarin.Forms.Xaml;
using YIRS.Services;
using Label = Xamarin.Forms.Label;

namespace YIRS.Views.Livestock
{
    /// <summary>
    /// Single-screen livestock collection.
    ///
    /// Everything happens here — there is no separate payment page:
    ///   list services → select any number → set a quantity per service →
    ///   running total → confirm sheet (method / reference / PIN) →
    ///   POST /api/Agents/LiveStockPayment → success or failure popup →
    ///   receipt print → reset and take the next payment.
    ///
    /// API contract note
    /// ─────────────────
    /// The written spec describes this endpoint as multipart/form-data with a MerchantNo
    /// field. The captured Postman request contradicts that: it is raw JSON, there is no
    /// MerchantNo, and the collection point is carried in "revHead". The capture wins —
    /// see <see cref="LiveStockPaymentRequest"/>.
    ///
    /// The success response field is "respondCode" (not "responseCode"). Both spellings are
    /// accepted here so a server-side correction cannot silently break the success path.
    /// </summary>
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ServicePayment : ContentPage
    {
        // ══════════════════════════════════════════════════════════════
        //  ENDPOINTS
        // ══════════════════════════════════════════════════════════════

        private const string ServicesUrl =
            "https://yobe.osoftpay.net/api/Agents/LiveStockServicesList";

        private const string PaymentUrl =
            "https://yobe.osoftpay.net/api/Agents/LiveStockPayment";

        private const string VerifyUrlBase =
            "https://yobe.osoftpay.net/singlecollections/verify?TransactId=";

        // ══════════════════════════════════════════════════════════════
        //  HTTP  (one static instance for the whole app — Android will
        //  exhaust sockets if every page news up its own HttpClient)
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
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        // ══════════════════════════════════════════════════════════════
        //  STATE
        // ══════════════════════════════════════════════════════════════

        private const int SearchDebounceMs = 300;

        private readonly LiveStockServiceListViewModel _viewModel;
        private CancellationTokenSource _searchCts;
        private bool _isSubmitting;
        private bool _isPrinting;

        /// <summary>Receipt for the last successful transaction, kept so Reprint works.</summary>
        private ReceiptData _lastReceipt;

        public ServicePayment()
        {
            try
            {
                InitializeComponent();

                _viewModel = new LiveStockServiceListViewModel();
                _viewModel.SelectionChanged += OnSelectionChanged;
                BindingContext = _viewModel;

                CollectionPointLabel.Text = string.IsNullOrWhiteSpace(MainPage.CollectionPoint)
                    ? "Unknown Collection Point"
                    : MainPage.CollectionPoint;

                PaymentMethodPicker.SelectedIndex = 0;   // Cash
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════

        protected override void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(MainPage.ValidUserMail))
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Session Expired",
                            "Please sign in again to continue.", "OK");
                        App.SetRoot(new Views.MainPage());
                    });
                    return;
                }

                if (!_viewModel.IsDataLoaded)
                    Device.BeginInvokeOnMainThread(async () => await LoadServicesAsync());
            }
            catch (Exception ex)
            {
                LogError("OnAppearing", ex);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try { _searchCts?.Cancel(); }
            catch (Exception ex) { LogError("OnDisappearing", ex); }
        }

        /// <summary>Back closes an open overlay before it leaves the page.</summary>
        protected override bool OnBackButtonPressed()
        {
            try
            {
                if (BusyOverlay.IsVisible) return true;               // never interrupt a POST

                if (SuccessOverlay.IsVisible) { ResetForNextPayment(); return true; }
                if (FailureOverlay.IsVisible) { FailureOverlay.IsVisible = false; return true; }
                if (ConfirmOverlay.IsVisible) { ConfirmOverlay.IsVisible = false; return true; }

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

        private async void OnBackTapped(object sender, EventArgs e)
        {
            try
            {
                if (_viewModel.SelectedCount > 0)
                {
                    bool leave = await DisplayAlert("Discard selection?",
                        "You have services selected that have not been paid for.",
                        "Discard", "Stay");

                    if (!leave) return;
                }

                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                LogError("OnBackTapped", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LOAD SERVICES
        // ══════════════════════════════════════════════════════════════

        private async Task LoadServicesAsync()
        {
            try
            {
                ShowBusy(true, "Loading services…");
                EmptyState.IsVisible = false;

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40)))
                using (var response = await Http.GetAsync(ServicesUrl, cts.Token))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine("[LiveStock:Services] " + json);

                    if (!response.IsSuccessStatusCode)
                    {
                        ShowEmpty("Could not reach the server (" + (int)response.StatusCode + ").");
                        return;
                    }

                    var services = JsonConvert.DeserializeObject<List<LiveStockService>>(json)
                                   ?? new List<LiveStockService>();

                    // The endpoint returns every collection point's catalogue. Keep only the
                    // rows that belong to this agent's revenue head; if nothing matches (a
                    // trailing-space or casing difference in revName), fall back to the full
                    // list rather than showing the agent an empty screen.
                    string revHead = LivestockModule.RevenueHead;
                    var mine = services
                        .Where(s => LivestockModule.MatchesRevenueHead(s.RevName, revHead))
                        .ToList();

                    if (mine.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[LiveStock:Services] No revName match for '" + revHead + "' — showing all.");
                        mine = services;
                    }

                    _viewModel.Load(mine);
                    UpdateCountLabel();

                    if (_viewModel.FilteredServices.Count == 0)
                        ShowEmpty("No services are configured for this collection point.");
                    else
                        EmptyState.IsVisible = false;
                }
            }
            catch (OperationCanceledException)
            {
                ShowEmpty("The request timed out. Pull down to retry.");
            }
            catch (HttpRequestException)
            {
                ShowEmpty("No connection. Check your network and pull down to retry.");
            }
            catch (JsonException ex)
            {
                LogError("LoadServicesAsync/json", ex);
                ShowEmpty("The server returned an unexpected response.");
            }
            catch (Exception ex)
            {
                LogError("LoadServicesAsync", ex);
                ShowEmpty("Something went wrong loading services.");
            }
            finally
            {
                ShowBusy(false);
                refreshView.IsRefreshing = false;
            }
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            try
            {
                _viewModel.IsDataLoaded = false;
                await LoadServicesAsync();
            }
            catch (Exception ex)
            {
                LogError("OnRefreshing", ex);
                refreshView.IsRefreshing = false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  SEARCH
        // ══════════════════════════════════════════════════════════════

        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _searchCts = new CancellationTokenSource();

                var token = _searchCts.Token;
                await Task.Delay(SearchDebounceMs, token);
                if (token.IsCancellationRequested) return;

                _viewModel.ApplyFilter(e.NewTextValue);
                UpdateCountLabel();

                if (_viewModel.FilteredServices.Count == 0 && _viewModel.IsDataLoaded)
                    ShowEmpty("No service matches \"" + (e.NewTextValue ?? string.Empty) + "\".");
                else
                    EmptyState.IsVisible = false;
            }
            catch (TaskCanceledException) { /* superseded by a newer keystroke */ }
            catch (Exception ex)
            {
                LogError("OnSearchTextChanged", ex);
            }
        }

        private void OnClearSelectionTapped(object sender, EventArgs e)
        {
            try { _viewModel.ClearSelection(); }
            catch (Exception ex) { LogError("OnClearSelectionTapped", ex); }
        }

        // ══════════════════════════════════════════════════════════════
        //  SELECTION → PAY BAR
        // ══════════════════════════════════════════════════════════════

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    int count = _viewModel.SelectedCount;
                    int units = _viewModel.TotalUnits;

                    PayBar.IsVisible = count > 0;
                    ClearSelectionLabel.IsVisible = count > 0;

                    SelectionSummaryLabel.Text = string.Format(
                        "{0} service{1} · {2} unit{3}",
                        count, count == 1 ? string.Empty : "s",
                        units, units == 1 ? string.Empty : "s");

                    GrandTotalLabel.Text = Money(_viewModel.GrandTotal);
                });
            }
            catch (Exception ex)
            {
                LogError("OnSelectionChanged", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  CONFIRM SHEET
        // ══════════════════════════════════════════════════════════════

        private void OnPayTapped(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (_viewModel.SelectedCount == 0)
                {
                    UserDialogs.Instance.Toast("Select at least one service.", TimeSpan.FromSeconds(3));
                    return;
                }

                var selected = _viewModel.SelectedItems();

                ConfirmTotalLabel.Text = Money(_viewModel.GrandTotal);
                ConfirmBreakdownLabel.Text = string.Join("\n",
                    selected.Select(s => string.Format("{0} × {1}  =  {2}",
                        s.ServiceName, s.Quantity, Money(s.SubTotal))));

                ConfirmErrorLabel.IsVisible = false;
                PinEntry.Text = string.Empty;
                ReferenceEntry.Text = string.Empty;

                ConfirmOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                LogError("OnPayTapped", ex);
            }
        }

        private void OnPaymentMethodChanged(object sender, EventArgs e)
        {
            try
            {
                ReferenceSection.IsVisible =
                    !string.Equals(SelectedMethod(), "Cash", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                LogError("OnPaymentMethodChanged", ex);
            }
        }

        private void OnCancelConfirmTapped(object sender, EventArgs e)
            => ConfirmOverlay.IsVisible = false;

        private string SelectedMethod()
        {
            int i = PaymentMethodPicker.SelectedIndex;
            return i >= 0 && i < PaymentMethodPicker.Items.Count
                ? PaymentMethodPicker.Items[i]
                : "Cash";
        }

        // ══════════════════════════════════════════════════════════════
        //  PAYMENT
        // ══════════════════════════════════════════════════════════════

        private async void OnSubmitPaymentTapped(object sender, EventArgs e)
        {
            if (_isSubmitting) return;

            try
            {
                string method = SelectedMethod();
                string pin = (PinEntry.Text ?? string.Empty).Trim();
                string reference = (ReferenceEntry.Text ?? string.Empty).Trim();

                // ── Local validation ──────────────────────────────────
                if (string.IsNullOrWhiteSpace(pin))
                {
                    ShowConfirmError("Enter your agent PIN.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(MainPage.Pin) && pin != MainPage.Pin)
                {
                    ShowConfirmError("Incorrect PIN.");
                    return;
                }

                bool isCash = string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase);
                if (!isCash && string.IsNullOrWhiteSpace(reference))
                {
                    ShowConfirmError("A payment reference is required for " + method + " payments.");
                    return;
                }

                var selected = _viewModel.SelectedItems();
                if (selected.Count == 0)
                {
                    ShowConfirmError("Select at least one service.");
                    return;
                }

                _isSubmitting = true;
                ConfirmOverlay.IsVisible = false;
                ShowBusy(true, "Processing payment…");

                var request = new LiveStockPaymentRequest
                {
                    RevHead = LivestockModule.RevenueHead,
                    PaymentMethod = method,
                    Email = (MainPage.ValidUserMail ?? string.Empty).Trim(),
                    Pin = pin,
                    PaymentReference = isCash ? null : reference,
                    Services = selected
                        .Select(s => new LiveStockPaymentLine
                        {
                            ServiceName = s.ServiceName,
                            Quantity = s.Quantity
                        })
                        .ToList()
                };

                // Client-side expectation. The server recomputes from its own price table,
                // so the response total is authoritative — this is only used to detect drift.
                decimal expectedTotal = _viewModel.GrandTotal;

                LiveStockPaymentResponse result = await PostPaymentAsync(request);

                ShowBusy(false);

                if (result == null)
                {
                    ShowFailure("No response was received from the server. "
                              + "Check the transaction history before retrying so you do not collect twice.",
                                null);
                    return;
                }

                if (result.IsSuccessful)
                {
                    if (result.TotalAmount > 0 && expectedTotal > 0 && result.TotalAmount != expectedTotal)
                    {
                        System.Diagnostics.Debug.WriteLine(string.Format(
                            "[LiveStock:Pay] Total drift — expected {0}, server charged {1}.",
                            expectedTotal, result.TotalAmount));
                    }

                    await HandleSuccessAsync(result, method, reference);
                }
                else
                {
                    ShowFailure(FriendlyFailure(result), result.ResolvedCode);
                }
            }
            catch (OperationCanceledException)
            {
                ShowBusy(false);
                ShowFailure("The request timed out. Check your transaction history before retrying "
                          + "so you do not collect twice.", null);
            }
            catch (HttpRequestException)
            {
                ShowBusy(false);
                ShowFailure("No connection to the server. Check your network and try again.", null);
            }
            catch (Exception ex)
            {
                LogError("OnSubmitPaymentTapped", ex);
                ShowBusy(false);
                ShowFailure("An unexpected error occurred. Please try again.", null);
            }
            finally
            {
                _isSubmitting = false;
            }
        }

        private async Task<LiveStockPaymentResponse> PostPaymentAsync(LiveStockPaymentRequest request)
        {
            string payload = JsonConvert.SerializeObject(request,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            System.Diagnostics.Debug.WriteLine("[LiveStock:Pay:Request] " + payload);

            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
            using (var response = await Http.PostAsync(PaymentUrl, content, cts.Token))
            {
                string json = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine(string.Format(
                    "[LiveStock:Pay:Response] {0} {1}", (int)response.StatusCode, json));

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                try
                {
                    return JsonConvert.DeserializeObject<LiveStockPaymentResponse>(json);
                }
                catch (JsonException)
                {
                    // Some error paths return a bare string rather than the wrapper object.
                    return new LiveStockPaymentResponse
                    {
                        RespondCode = "99",
                        Message = json.Trim().Trim('"')
                    };
                }
            }
        }

        private static string FriendlyFailure(LiveStockPaymentResponse result)
        {
            string message = result.ResolvedMessage;

            if (!string.IsNullOrWhiteSpace(message))
                return message;

            return "The payment could not be completed. Please try again.";
        }

        // ══════════════════════════════════════════════════════════════
        //  SUCCESS
        // ══════════════════════════════════════════════════════════════

        private async Task HandleSuccessAsync(
            LiveStockPaymentResponse result, string method, string reference)
        {
            try
            {
                decimal total = result.TotalAmount > 0 ? result.TotalAmount : _viewModel.GrandTotal;

                SuccessMessageLabel.Text = string.IsNullOrWhiteSpace(result.ResolvedMessage)
                    ? "Successful"
                    : result.ResolvedMessage;

                SuccessAmountLabel.Text = Money(total);
                SuccessTransactionLabel.Text = string.IsNullOrWhiteSpace(result.TransactionNo)
                    ? "—" : result.TransactionNo;
                SuccessMethodLabel.Text = string.IsNullOrWhiteSpace(result.PaymentMethod)
                    ? method : result.PaymentMethod;
                SuccessDateLabel.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                RenderBreakdown(result);

                SuccessOverlay.IsVisible = true;

                _lastReceipt = BuildReceipt(result, total, method, reference);

                // Fire the print immediately — the agent should not have to ask for the receipt.
                await CallPrinterAsync(_lastReceipt);
            }
            catch (Exception ex)
            {
                LogError("HandleSuccessAsync", ex);
            }
        }

        private void RenderBreakdown(LiveStockPaymentResponse result)
        {
            try
            {
                SuccessBreakdownStack.Children.Clear();

                var lines = result.Breakdown != null && result.Breakdown.Count > 0
                    ? result.Breakdown
                    : _viewModel.SelectedItems()
                        .Select(s => new LiveStockBreakdownLine
                        {
                            ServiceName = s.ServiceName,
                            Amount = s.Amount,
                            Quantity = s.Quantity,
                            SubTotal = s.SubTotal
                        })
                        .ToList();

                foreach (var line in lines)
                {
                    var grid = new Grid { ColumnSpacing = 8 };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var left = new Label
                    {
                        Text = string.Format("{0} × {1}", line.ServiceName, line.Quantity),
                        FontSize = 12.5,
                        TextColor = Color.FromHex("#5D6D7E"),
                        LineBreakMode = LineBreakMode.TailTruncation
                    };

                    var right = new Xamarin.Forms.Label
                    {
                        Text = Money(line.SubTotal),
                        FontSize = 12.5,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromHex("#22313F")
                    };

                    Grid.SetColumn(left, 0);
                    Grid.SetColumn(right, 1);
                    grid.Children.Add(left);
                    grid.Children.Add(right);

                    SuccessBreakdownStack.Children.Add(grid);
                }
            }
            catch (Exception ex)
            {
                LogError("RenderBreakdown", ex);
            }
        }

        private void OnNewPaymentTapped(object sender, EventArgs e)
        {
            try { ResetForNextPayment(); }
            catch (Exception ex) { LogError("OnNewPaymentTapped", ex); }
        }

        /// <summary>
        /// Clears the previous transaction so the agent can immediately take the next one
        /// without leaving the page.
        /// </summary>
        private void ResetForNextPayment()
        {
            SuccessOverlay.IsVisible = false;
            FailureOverlay.IsVisible = false;
            ConfirmOverlay.IsVisible = false;

            _viewModel.ClearSelection();

            PinEntry.Text = string.Empty;
            ReferenceEntry.Text = string.Empty;
            searchBar.Text = string.Empty;
            PaymentMethodPicker.SelectedIndex = 0;
            ReferenceSection.IsVisible = false;

            SessionManager.Instance.UpdateActivity();
        }

        // ══════════════════════════════════════════════════════════════
        //  FAILURE
        // ══════════════════════════════════════════════════════════════

        private void ShowFailure(string reason, string code)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FailureReasonLabel.Text = reason;

                if (string.IsNullOrWhiteSpace(code))
                {
                    FailureCodeLabel.IsVisible = false;
                }
                else
                {
                    FailureCodeLabel.Text = "Response code: " + code;
                    FailureCodeLabel.IsVisible = true;
                }

                FailureOverlay.IsVisible = true;
            });
        }

        private void OnDismissFailureTapped(object sender, EventArgs e)
            => FailureOverlay.IsVisible = false;

        /// <summary>
        /// Reopens the confirm sheet with the selection intact — the agent only has to
        /// re-enter the PIN, not rebuild the basket.
        /// </summary>
        private void OnRetryPaymentTapped(object sender, EventArgs e)
        {
            try
            {
                FailureOverlay.IsVisible = false;
                ConfirmErrorLabel.IsVisible = false;
                PinEntry.Text = string.Empty;
                ConfirmOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                LogError("OnRetryPaymentTapped", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  RECEIPT + PRINTING  (existing PrintJobManager pipeline)
        // ══════════════════════════════════════════════════════════════

        private ReceiptData BuildReceipt(
            LiveStockPaymentResponse result, decimal total, string method, string reference)
        {
            string transactionNo = result.TransactionNo ?? "N/A";

            var items = new List<ReceiptItem>();

            var lines = result.Breakdown != null && result.Breakdown.Count > 0
                ? result.Breakdown
                : _viewModel.SelectedItems()
                    .Select(s => new LiveStockBreakdownLine
                    {
                        ServiceName = s.ServiceName,
                        Amount = s.Amount,
                        Quantity = s.Quantity,
                        SubTotal = s.SubTotal
                    })
                    .ToList();

            foreach (var line in lines)
            {
                items.Add(new ReceiptItem
                {
                    Description = line.ServiceName,
                    Amount = line.SubTotal,
                    SubText = string.Format("{0} × {1}", line.Quantity, Money(line.Amount))
                });
            }

            items.Add(new ReceiptItem
            {
                Description = "Payment Method",
                Amount = 0m,
                SubText = string.IsNullOrWhiteSpace(result.PaymentMethod) ? method : result.PaymentMethod
            });

            if (!string.IsNullOrWhiteSpace(reference))
            {
                items.Add(new ReceiptItem
                {
                    Description = "Reference",
                    Amount = 0m,
                    SubText = reference
                });
            }

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: 09070701616,07017639494",
                ReceiptNumber = transactionNo,
                StoreSubTitle = LivestockModule.DisplayName,
                AgentName = MainPage.Name ?? "N/A",
                CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                SuperAgent = MainPage.Super_Agent ?? string.Empty,
                PrintDate = DateTime.Now,
                Items = items,
                TotalAmount = total,
                AmountPaid = total,
                AmountLeft = 0m,
                FooterLine1 = App.ThankYouMessage ?? "THANK YOU FOR MAKING YOUR PAYMENT!",
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = VerifyUrlBase + Uri.EscapeDataString(transactionNo)
            };
        }

        private async void OnReprintTapped(object sender, EventArgs e)
        {
            try
            {
                if (_lastReceipt == null)
                {
                    UserDialogs.Instance.Toast("Nothing to reprint.", TimeSpan.FromSeconds(3));
                    return;
                }

                await CallPrinterAsync(_lastReceipt);
            }
            catch (Exception ex)
            {
                LogError("OnReprintTapped", ex);
            }
        }

        /// <summary>
        /// Mirrors the Yorata-Ops / Haulage print path: enqueue a durable job, then execute it.
        /// A print failure never invalidates the payment — the money has already moved.
        /// </summary>
        private async Task<bool> CallPrinterAsync(ReceiptData receipt)
        {
            if (_isPrinting)
            {
                System.Diagnostics.Debug.WriteLine("[LiveStock:Print] Already printing — skipped.");
                return false;
            }

            _isPrinting = true;

            try
            {
                if (!await BluetoothPermissionHelper.RequestAsync())
                {
                    UserDialogs.Instance.Toast("Bluetooth permission denied. Payment was successful.",
                        TimeSpan.FromSeconds(6));
                    return false;
                }

                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.ChunkStarted:
                                BusyLabel.Text = string.Format("Printing {0}…", p.ChunkName);
                                break;
                            case PrintProgressStatus.ChunkRetrying:
                                BusyLabel.Text = string.Format("Reconnecting… retrying {0} (#{1})",
                                    p.ChunkName, p.AttemptNumber);
                                break;
                        }
                    }));

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    try
                    {
                        await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                        await App.PrintJobManager.DeleteJobAsync(job.JobId);

                        UserDialogs.Instance.Toast("Receipt printed.", TimeSpan.FromSeconds(3));
                        return true;
                    }
                    catch (PrinterException pex)
                    {
                        UserDialogs.Instance.Toast("Print failed: " + pex.Message + " Payment was successful.",
                            TimeSpan.FromSeconds(8));
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        UserDialogs.Instance.Toast("Print timed out. Payment was successful — use Reprint.",
                            TimeSpan.FromSeconds(6));
                        return false;
                    }
                    catch (Exception ex)
                    {
                        LogError("CallPrinterAsync/execute", ex);
                        UserDialogs.Instance.Toast("Printer not connected. Payment was successful.",
                            TimeSpan.FromSeconds(6));
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("CallPrinterAsync", ex);
                return false;
            }
            finally
            {
                _isPrinting = false;
                MainThread.BeginInvokeOnMainThread(() => BusyLabel.Text = "Processing…");
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════════════════════

        private static string Money(decimal value) => string.Format("₦{0:N2}", value);

        private void ShowBusy(bool show, string message = "Processing…")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BusyLabel.Text = message;
                BusyOverlay.IsVisible = show;
            });
        }

        private void ShowConfirmError(string message)
        {
            ConfirmErrorLabel.Text = message;
            ConfirmErrorLabel.IsVisible = true;
        }

        private void ShowEmpty(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EmptyStateLabel.Text = message;
                EmptyState.IsVisible = true;
            });
        }

        private void UpdateCountLabel()
        {
            int count = _viewModel.FilteredServices.Count;
            ServiceCountLabel.Text = count == 1
                ? "1 service available"
                : count + " services available";
        }

        private static void LogError(string scope, Exception ex)
            => System.Diagnostics.Debug.WriteLine(
                string.Format("[LiveStock:ServiceList:{0}] {1}", scope, ex));
    }

    // ══════════════════════════════════════════════════════════════════
    //  VIEW MODEL
    // ══════════════════════════════════════════════════════════════════

    public class LiveStockServiceListViewModel : INotifyPropertyChanged
    {
        private readonly List<LiveStockServiceItem> _all = new List<LiveStockServiceItem>();

        public ObservableCollection<LiveStockServiceItem> FilteredServices { get; }
            = new ObservableCollection<LiveStockServiceItem>();

        public bool IsDataLoaded { get; set; }

        /// <summary>Raised whenever a quantity or selection changes anywhere in the list.</summary>
        public event EventHandler SelectionChanged;

        public decimal GrandTotal => _all.Sum(i => i.SubTotal);

        public int SelectedCount => _all.Count(i => i.IsSelected);

        public int TotalUnits => _all.Where(i => i.IsSelected).Sum(i => i.Quantity);

        public List<LiveStockServiceItem> SelectedItems()
            => _all.Where(i => i.IsSelected).ToList();

        public void Load(IEnumerable<LiveStockService> services)
        {
            foreach (var old in _all)
                old.PropertyChanged -= OnItemPropertyChanged;

            _all.Clear();

            foreach (var service in services.OrderBy(s => s.ServiceName))
            {
                var item = new LiveStockServiceItem(service);
                item.PropertyChanged += OnItemPropertyChanged;
                _all.Add(item);
            }

            IsDataLoaded = true;
            ApplyFilter(null);
            RaiseSelectionChanged();
        }

        public void ApplyFilter(string term)
        {
            FilteredServices.Clear();

            IEnumerable<LiveStockServiceItem> source = _all;

            if (!string.IsNullOrWhiteSpace(term))
            {
                string needle = term.Trim();
                source = _all.Where(i =>
                    !string.IsNullOrEmpty(i.ServiceName) &&
                    i.ServiceName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (var item in source)
                FilteredServices.Add(item);

            OnPropertyChanged(nameof(FilteredServices));
        }

        public void ClearSelection()
        {
            foreach (var item in _all)
                item.Reset();

            RaiseSelectionChanged();
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LiveStockServiceItem.Quantity) ||
                e.PropertyName == nameof(LiveStockServiceItem.IsSelected))
            {
                RaiseSelectionChanged();
            }
        }

        private void RaiseSelectionChanged()
        {
            OnPropertyChanged(nameof(GrandTotal));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalUnits));

            var handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ITEM VIEW MODEL
    // ══════════════════════════════════════════════════════════════════

    public class LiveStockServiceItem : INotifyPropertyChanged
    {
        private const int MaxQuantity = 999;

        private int _quantity;

        public LiveStockServiceItem(LiveStockService service)
        {
            ServiceName = service.ServiceName;
            Amount = service.Amount;
            Id = service.Id;
            RevHeadId = service.RevHeadId;
            RevName = service.RevName;

            IncrementCommand = new Command(Increment);
            DecrementCommand = new Command(Decrement);
            ToggleCommand = new Command(Toggle);
        }

        public int Id { get; }
        public string ServiceName { get; }
        public decimal Amount { get; }
        public int RevHeadId { get; }
        public string RevName { get; }

        public Command IncrementCommand { get; }
        public Command DecrementCommand { get; }
        public Command ToggleCommand { get; }

        /// <summary>Quantity 0 means the service is not part of the basket.</summary>
        public int Quantity
        {
            get { return _quantity; }
            set
            {
                int clamped = value < 0 ? 0 : (value > MaxQuantity ? MaxQuantity : value);
                if (_quantity == clamped) return;

                bool wasSelected = _quantity > 0;
                _quantity = clamped;

                OnPropertyChanged(nameof(Quantity));
                OnPropertyChanged(nameof(SubTotal));
                OnPropertyChanged(nameof(SubTotalDisplay));
                OnPropertyChanged(nameof(CardBorderColor));
                OnPropertyChanged(nameof(TickBackgroundColor));

                if (wasSelected != IsSelected)
                    OnPropertyChanged(nameof(IsSelected));
            }
        }

        public bool IsSelected => _quantity > 0;

        public decimal SubTotal => Amount * _quantity;

        public string UnitPriceDisplay => string.Format("₦{0:N2} each", Amount);

        public string SubTotalDisplay => IsSelected ? string.Format("₦{0:N2}", SubTotal) : string.Empty;

        public Color CardBorderColor => IsSelected ? Color.FromHex("#004225") : Color.FromHex("#E4E9EC");

        public Color TickBackgroundColor => IsSelected ? Color.FromHex("#004225") : Color.Transparent;

        public void Increment() => Quantity = Quantity + 1;

        public void Decrement() => Quantity = Quantity - 1;

        public void Toggle() => Quantity = IsSelected ? 0 : 1;

        public void Reset() => Quantity = 0;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  API MODELS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>GET /api/Agents/LiveStockServicesList</summary>
    public class LiveStockService
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("revName")]
        public string RevName { get; set; }

        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("revHeadId")]
        public int RevHeadId { get; set; }

        [JsonProperty("remitaServiceTypeId")]
        public string RemitaServiceTypeId { get; set; }
    }

    /// <summary>
    /// POST /api/Agents/LiveStockPayment — raw JSON body, exactly as captured in Postman.
    /// Amount is deliberately absent: the server prices every line from its own table.
    /// </summary>
    public class LiveStockPaymentRequest
    {
        [JsonProperty("revHead")]
        public string RevHead { get; set; }

        [JsonProperty("PaymentMethod")]
        public string PaymentMethod { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("Pin")]
        public string Pin { get; set; }

        [JsonProperty("PaymentReference")]
        public string PaymentReference { get; set; }

        [JsonProperty("Services")]
        public List<LiveStockPaymentLine> Services { get; set; }
    }

    public class LiveStockPaymentLine
    {
        [JsonProperty("ServiceName")]
        public string ServiceName { get; set; }

        [JsonProperty("Quantity")]
        public int Quantity { get; set; }
    }

    public class LiveStockPaymentResponse
    {
        // The live API spells this "respondCode". "responseCode" is accepted too so a
        // server-side spelling fix cannot silently turn every success into a failure.
        [JsonProperty("respondCode")]
        public string RespondCode { get; set; }

        [JsonProperty("responseCode")]
        public string ResponseCode { get; set; }

        [JsonProperty("transactionNo")]
        public string TransactionNo { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("responseMessage")]
        public string ResponseMessage { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonProperty("paymentMethod")]
        public string PaymentMethod { get; set; }

        [JsonProperty("paymentReference")]
        public string PaymentReference { get; set; }

        [JsonProperty("breakdown")]
        public List<LiveStockBreakdownLine> Breakdown { get; set; }

        [JsonIgnore]
        public string ResolvedCode =>
            !string.IsNullOrWhiteSpace(RespondCode) ? RespondCode : ResponseCode;

        [JsonIgnore]
        public string ResolvedMessage =>
            !string.IsNullOrWhiteSpace(Message) ? Message : ResponseMessage;

        [JsonIgnore]
        public bool IsSuccessful =>
            string.Equals(ResolvedCode, "00", StringComparison.Ordinal);
    }

    public class LiveStockBreakdownLine
    {
        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("subTotal")]
        public decimal SubTotal { get; set; }
    }
}