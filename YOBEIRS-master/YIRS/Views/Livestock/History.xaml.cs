using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Livestock
{
    /// <summary>
    /// Livestock collection history.
    ///
    /// Reworked from the YIRSHospital version with four substantive changes:
    ///   • Dates are formatted with InvariantCulture (see LivestockTransactionService).
    ///   • Sorting is on the parsed DateTime, not the raw string.
    ///   • Rows are grouped by day with a per-day total, which is what an agent actually
    ///     reconciles against at close of business.
    ///   • The ListView owns its own scrolling instead of sitting inside a ScrollView with a
    ///     computed HeightRequest.
    /// </summary>
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class History : ContentPage
    {
        private readonly HistoryViewModel _viewModel = new HistoryViewModel();

        private CancellationTokenSource _requestCts;
        private Transaction _selected;
        private bool _isLoading;
        private bool _isPrinting;

        public History()
        {
            try
            {
                InitializeComponent();
                BindingContext = _viewModel;

                CollectionPointLabel.Text = string.IsNullOrWhiteSpace(MainPage.CollectionPoint)
                    ? "Unknown Collection Point"
                    : MainPage.CollectionPoint;

                endDatePicker.Date = DateTime.Now.Date;
                startDatePicker.Date = DateTime.Now.Date.AddDays(-30);
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

                if (!_viewModel.HasLoadedOnce)
                    Device.BeginInvokeOnMainThread(async () => await LoadAsync());
            }
            catch (Exception ex)
            {
                LogError("OnAppearing", ex);
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try { _requestCts?.Cancel(); }
            catch (Exception ex) { LogError("OnDisappearing", ex); }
        }

        protected override bool OnBackButtonPressed()
        {
            if (BusyOverlay.IsVisible) return true;

            if (DetailOverlay.IsVisible)
            {
                DetailOverlay.IsVisible = false;
                return true;
            }

            return base.OnBackButtonPressed();
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception ex) { LogError("OnBackTapped", ex); }
        }

        // ══════════════════════════════════════════════════════════════
        //  RANGE + FILTER CONTROLS
        // ══════════════════════════════════════════════════════════════

        private async void OnQuickRangeTapped(object sender, EventArgs e)
        {
            try
            {
                int days;

                if (ReferenceEquals(sender, ChipToday)) days = 0;
                else if (ReferenceEquals(sender, ChipWeek)) days = 7;
                else days = 30;

                startDatePicker.Date = DateTime.Now.Date.AddDays(-days);
                endDatePicker.Date = DateTime.Now.Date;

                StyleChip(ChipToday, days == 0);
                StyleChip(ChipWeek, days == 7);
                StyleChip(ChipMonth, days == 30);

                await LoadAsync();
            }
            catch (Exception ex)
            {
                LogError("OnQuickRangeTapped", ex);
            }
        }

        /// <summary>A hand-picked date no longer matches a quick chip, so clear their highlight.</summary>
        private void OnDateSelected(object sender, DateChangedEventArgs e)
        {
            try
            {
                StyleChip(ChipToday, false);
                StyleChip(ChipWeek, false);
                StyleChip(ChipMonth, false);
            }
            catch (Exception ex)
            {
                LogError("OnDateSelected", ex);
            }
        }

        private void OnStatusFilterTapped(object sender, EventArgs e)
        {
            try
            {
                string filter;

                if (ReferenceEquals(sender, FilterSuccessful)) filter = "Successful";
                else if (ReferenceEquals(sender, FilterPending)) filter = "Pending";
                else if (ReferenceEquals(sender, FilterRefunded)) filter = "Refunded";
                else filter = "All";

                StyleChip(FilterAll, filter == "All");
                StyleChip(FilterSuccessful, filter == "Successful");
                StyleChip(FilterPending, filter == "Pending");
                StyleChip(FilterRefunded, filter == "Refunded");

                _viewModel.StatusFilter = filter;
                _viewModel.Rebuild();

                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                LogError("OnStatusFilterTapped", ex);
            }
        }

        private static void StyleChip(Xamarin.Forms.PancakeView.PancakeView chip, bool active)
        {
            if (chip == null) return;

            chip.BackgroundColor = active ? Color.FromHex("#004225") : Color.FromHex("#EDF2F0");

            var label = chip.Content as Label;
            if (label != null)
                label.TextColor = active ? Color.White : Color.FromHex("#004225");
        }

        // ══════════════════════════════════════════════════════════════
        //  LOAD
        // ══════════════════════════════════════════════════════════════

        private async void OnSearchTapped(object sender, EventArgs e)
        {
            try { await LoadAsync(); }
            catch (Exception ex) { LogError("OnSearchTapped", ex); }
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            try { await LoadAsync(); }
            catch (Exception ex)
            {
                LogError("OnRefreshing", ex);
                refreshView.IsRefreshing = false;
            }
        }

        private async Task LoadAsync()
        {
            if (_isLoading) return;

            if (!ValidateRange()) return;

            _isLoading = true;
            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            try
            {
                SessionManager.Instance.UpdateActivity();
                ShowBusy(true);
                EmptyState.IsVisible = false;

                var response = await LivestockTransactionService.GetAsync(
                    startDatePicker.Date, endDatePicker.Date, _requestCts.Token);

                _viewModel.Load(response.Transactions);
                UpdateSummary();
                UpdateEmptyState();
            }
            catch (OperationCanceledException)
            {
                ShowEmpty("⏱", "Request timed out",
                    "The server took too long to respond. Check your connection and try again.", true);
            }
            catch (HttpRequestException)
            {
                ShowEmpty("📡", "No connection",
                    "Could not reach the revenue server. Check your network and try again.", true);
            }
            catch (InvalidOperationException ex)
            {
                // Thrown by the service for a non-"00" response code or an unreadable body.
                ShowEmpty("⚠", "Could not load history", ex.Message, true);
            }
            catch (Exception ex)
            {
                LogError("LoadAsync", ex);
                ShowEmpty("⚠", "Something went wrong",
                    "An unexpected error occurred. Please try again.", true);
            }
            finally
            {
                ShowBusy(false);
                refreshView.IsRefreshing = false;
                _isLoading = false;
            }
        }

        private bool ValidateRange()
        {
            if (startDatePicker.Date > endDatePicker.Date)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Check the dates",
                        "The start date cannot be after the end date.", "OK"));
                return false;
            }

            if ((endDatePicker.Date - startDatePicker.Date).TotalDays > 365)
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await DisplayAlert("Range too wide",
                        "Please choose a range of 365 days or less.", "OK"));
                return false;
            }

            return true;
        }

        // ══════════════════════════════════════════════════════════════
        //  SUMMARY + EMPTY STATE
        // ══════════════════════════════════════════════════════════════

        private void UpdateSummary()
        {
            SummaryStrip.IsVisible = _viewModel.AllCount > 0;
            SummaryCollectedLabel.Text = string.Format("₦{0:N2}", _viewModel.SuccessfulAmount);
            SummaryCountLabel.Text = _viewModel.AllCount.ToString();
            SummaryRefundedLabel.Text = _viewModel.RefundedCount.ToString();
        }

        private void UpdateEmptyState()
        {
            if (_viewModel.VisibleCount > 0)
            {
                EmptyState.IsVisible = false;
                return;
            }

            if (_viewModel.AllCount > 0)
            {
                ShowEmpty("🔍", "Nothing matches that filter",
                    "There are transactions in this range, but none with that status.", false);
            }
            else
            {
                ShowEmpty("🐄", "No transactions",
                    "Nothing was collected between "
                    + startDatePicker.Date.ToString("dd MMM")
                    + " and " + endDatePicker.Date.ToString("dd MMM") + ".", false);
            }
        }

        private void ShowEmpty(string icon, string title, string message, bool showRetry)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EmptyStateIcon.Text = icon;
                EmptyStateTitle.Text = title;
                EmptyStateLabel.Text = message;
                EmptyStateAction.IsVisible = showRetry;
                EmptyState.IsVisible = true;
            });
        }

        private void ShowBusy(bool show, string message = "Loading transactions…")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BusyLabel.Text = message;
                BusyOverlay.IsVisible = show;
            });
        }

        // ══════════════════════════════════════════════════════════════
        //  DETAIL SHEET
        // ══════════════════════════════════════════════════════════════

        private void OnTransactionTapped(object sender, ItemTappedEventArgs e)
        {
            try
            {
                var transaction = e.Item as Transaction;
                if (transaction == null) return;

                _selected = transaction;
                SessionManager.Instance.UpdateActivity();

               
                DetailIconChip.BackgroundColor = transaction.StatusTintColor;
                DetailServiceLabel.Text = transaction.ServiceTypeName;
                DetailStatusLabel.Text = transaction.Status;
                DetailStatusLabel.TextColor = transaction.StatusColor;
                DetailAmountLabel.Text = transaction.AmountDisplay;

                DetailRowStack.Children.Clear();
                AddDetailRow("Reference", transaction.TransactionId);
                AddDetailRow("Date", transaction.DisplayDate);

                if (transaction.Quantity.HasValue && transaction.Quantity.Value > 0)
                    AddDetailRow("Quantity", transaction.Quantity.Value.ToString());

                if (!string.IsNullOrWhiteSpace(transaction.PaymentMethod))
                    AddDetailRow("Method", transaction.PaymentMethod);

                AddDetailRow("Payer", transaction.PayerDisplay);
                AddDetailRow("Revenue head", transaction.RevenueHead);
                AddDetailRow("Service", transaction.RemitaServiceName);
                AddDetailRow("Agent", transaction.AgentName);

                DetailOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                LogError("OnTransactionTapped", ex);
            }
        }

        private void AddDetailRow(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "N/A") return;

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            var left = new Label
            {
                Text = label,
                FontSize = 12.5,
                TextColor = Color.FromHex("#95A5A6")
            };

            var right = new Label
            {
                Text = value,
                FontSize = 12.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromHex("#22313F"),
                HorizontalTextAlignment = TextAlignment.End,
                LineBreakMode = LineBreakMode.WordWrap
            };

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            DetailRowStack.Children.Add(grid);
        }

        private void OnDismissDetailTapped(object sender, EventArgs e)
            => DetailOverlay.IsVisible = false;

        private async void OnCopyReferenceTapped(object sender, EventArgs e)
        {
            try
            {
                if (_selected == null) return;

                await Clipboard.SetTextAsync(_selected.TransactionId);
                UserDialogs.Instance.Toast("Reference copied.", TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                LogError("OnCopyReferenceTapped", ex);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  REPRINT
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Rebuilds a receipt from the history record. The transactions endpoint returns a
        /// single service line rather than the full breakdown the payment response carried,
        /// so a reprint shows one line — enough for a duplicate, not byte-identical to the
        /// original. If the endpoint gains a breakdown array, feed it in here.
        /// </summary>
        private async void OnReprintTapped(object sender, EventArgs e)
        {
            if (_isPrinting) return;

            try
            {
                if (_selected == null) return;

                if (!_selected.IsSuccessful)
                {
                    bool proceed = await DisplayAlert("Reprint",
                        "This transaction is marked \"" + _selected.Status +
                        "\". Print a copy anyway?", "Print", "Cancel");

                    if (!proceed) return;
                }

                _isPrinting = true;
                ShowBusy(true, "Printing receipt…");

                if (!await BluetoothPermissionHelper.RequestAsync())
                {
                    UserDialogs.Instance.Toast("Bluetooth permission denied.", TimeSpan.FromSeconds(5));
                    return;
                }

                var receipt = BuildReceipt(_selected);
                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (p.Status == PrintProgressStatus.ChunkStarted)
                            BusyLabel.Text = string.Format("Printing {0}…", p.ChunkName);
                        else if (p.Status == PrintProgressStatus.ChunkRetrying)
                            BusyLabel.Text = string.Format("Reconnecting… retrying {0}", p.ChunkName);
                    }));

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    try
                    {
                        await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                        await App.PrintJobManager.DeleteJobAsync(job.JobId);
                        UserDialogs.Instance.Toast("Receipt printed.", TimeSpan.FromSeconds(3));
                    }
                    catch (PrinterException pex)
                    {
                        UserDialogs.Instance.Toast("Print failed: " + pex.Message, TimeSpan.FromSeconds(7));
                    }
                    catch (OperationCanceledException)
                    {
                        UserDialogs.Instance.Toast("Print timed out.", TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        LogError("OnReprintTapped/execute", ex);
                        UserDialogs.Instance.Toast("Printer not connected.", TimeSpan.FromSeconds(5));
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("OnReprintTapped", ex);
            }
            finally
            {
                _isPrinting = false;
                ShowBusy(false);
            }
        }

        private static ReceiptData BuildReceipt(Transaction transaction)
        {
            string transactionNo = transaction.TransactionId ?? "N/A";

            var items = new List<ReceiptItem>
            {
                new ReceiptItem
                {
                    Description = transaction.ServiceTypeName,
                    Amount = transaction.Amount,
                    SubText = transaction.Quantity.HasValue && transaction.Quantity.Value > 0
                        ? string.Format("Quantity: {0}", transaction.Quantity.Value)
                        : null
                },
                new ReceiptItem
                {
                    Description = "Status",
                    Amount = 0m,
                    SubText = transaction.Status
                }
            };

            if (!string.IsNullOrWhiteSpace(transaction.PaymentMethod))
            {
                items.Add(new ReceiptItem
                {
                    Description = "Payment Method",
                    Amount = 0m,
                    SubText = transaction.PaymentMethod
                });
            }

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StoreSubTitle = "LIVESTOCK COLLECTION — DUPLICATE",
                StorePhone = "Contact us: 09070701616,07017639494",
                ReceiptNumber = transactionNo,
                AgentName = transaction.AgentName ?? MainPage.Name ?? "N/A",
                CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                SuperAgent = MainPage.Super_Agent ?? string.Empty,
                PrintDate = transaction.ParsedDate ?? DateTime.Now,
                Items = items,
                TotalAmount = transaction.Amount,
                AmountPaid = transaction.Amount,
                AmountLeft = 0m,
                FooterLine1 = "DUPLICATE RECEIPT",
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = "https://yobe.osoftpay.net/singlecollections/verify?TransactId="
                             + Uri.EscapeDataString(transactionNo)
            };
        }

        private static void LogError(string scope, Exception ex)
            => System.Diagnostics.Debug.WriteLine(
                string.Format("[Livestock:History:{0}] {1}", scope, ex));
    }

    // ══════════════════════════════════════════════════════════════════
    //  VIEW MODEL
    // ══════════════════════════════════════════════════════════════════

    public class HistoryViewModel : INotifyPropertyChanged
    {
        private List<Transaction> _all = new List<Transaction>();

        public ObservableCollection<TransactionDayGroup> Groups { get; }
            = new ObservableCollection<TransactionDayGroup>();

        public bool HasLoadedOnce { get; private set; }

        public string StatusFilter { get; set; } = "All";

        public int AllCount => _all.Count;

        public int VisibleCount => Groups.Sum(g => g.Count);

        public decimal SuccessfulAmount => _all.Where(t => t.IsSuccessful).Sum(t => t.Amount);

        public int RefundedCount => _all.Count(t => t.IsRefunded);

        public void Load(List<Transaction> transactions)
        {
            _all = transactions ?? new List<Transaction>();
            HasLoadedOnce = true;
            Rebuild();
        }

        public void Rebuild()
        {
            Groups.Clear();

            IEnumerable<Transaction> source = _all;

            switch (StatusFilter)
            {
                case "Successful":
                    source = _all.Where(t => t.IsSuccessful);
                    break;
                case "Pending":
                    source = _all.Where(t => t.IsPending);
                    break;
                case "Refunded":
                    source = _all.Where(t => t.IsRefunded);
                    break;
            }

            foreach (var group in TransactionDayGroup.Build(source))
                Groups.Add(group);

            OnPropertyChanged(nameof(Groups));
            OnPropertyChanged(nameof(AllCount));
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(SuccessfulAmount));
            OnPropertyChanged(nameof(RefundedCount));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}