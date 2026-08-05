using Acr.UserDialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    /// Verifies a livestock transaction reference.
    ///
    /// There is no dedicated livestock verification endpoint yet — Haulage has
    /// <c>/api/HulageVehicles/verify</c> and Yorata has <c>/api/KekeTransactions/Verify</c>,
    /// but nothing equivalent exists here. So this looks the reference up in the agent's own
    /// transaction history via <c>GetAgentTransactions</c>, which is real server data and
    /// cannot be faked from the handset.
    ///
    /// The limitation is honest and stated on screen: it only finds transactions collected by
    /// the signed-in agent. When a proper endpoint lands, replace
    /// <see cref="FindTransactionAsync"/> and nothing else changes.
    /// </summary>
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class VerifyLivestock : ContentPage
    {
        private int _windowDays = 90;
        private bool _isBusy;
        private bool _isPrinting;
        private Transaction _found;

        public VerifyLivestock()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                LogError("Constructor", ex);
            }
        }

        /// <summary>Pre-fills the reference, e.g. straight after a payment.</summary>
        public VerifyLivestock(string reference) : this()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(reference))
                    ReferenceEntry.Text = reference.Trim();
            }
            catch (Exception ex)
            {
                LogError("Constructor(reference)", ex);
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SessionManager.Instance.UpdateActivity();
        }

        protected override bool OnBackButtonPressed()
        {
            if (BusyOverlay.IsVisible) return true;
            return base.OnBackButtonPressed();
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception ex) { LogError("OnBackTapped", ex); }
        }

        // ══════════════════════════════════════════════════════════════
        //  INPUT
        // ══════════════════════════════════════════════════════════════

        private async void OnPasteTapped(object sender, EventArgs e)
        {
            try
            {
                if (!Clipboard.HasText)
                {
                    UserDialogs.Instance.Toast("Clipboard is empty.", TimeSpan.FromSeconds(2));
                    return;
                }

                string text = await Clipboard.GetTextAsync();
                ReferenceEntry.Text = (text ?? string.Empty).Trim();
            }
            catch (Exception ex)
            {
                LogError("OnPasteTapped", ex);
            }
        }

        private void OnClearTapped(object sender, EventArgs e)
        {
            ReferenceEntry.Text = string.Empty;
            ResultCard.IsVisible = false;
            _found = null;
        }

        private void OnWindowTapped(object sender, EventArgs e)
        {
            try
            {
                if (ReferenceEquals(sender, Window30)) _windowDays = 30;
                else if (ReferenceEquals(sender, Window365)) _windowDays = 365;
                else _windowDays = 90;

                StyleChip(Window30, _windowDays == 30);
                StyleChip(Window90, _windowDays == 90);
                StyleChip(Window365, _windowDays == 365);
            }
            catch (Exception ex)
            {
                LogError("OnWindowTapped", ex);
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
        //  VERIFY
        // ══════════════════════════════════════════════════════════════

        private async void OnVerifyTapped(object sender, EventArgs e)
        {
            if (_isBusy) return;

            string reference = (ReferenceEntry.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(reference))
            {
                UserDialogs.Instance.Toast("Enter a transaction reference.", TimeSpan.FromSeconds(3));
                return;
            }

            _isBusy = true;

            try
            {
                SessionManager.Instance.UpdateActivity();
                ShowBusy(true);
                ResultCard.IsVisible = false;
                _found = null;

                var match = await FindTransactionAsync(reference);

                if (match == null)
                {
                    ShowNotFound(reference);
                }
                else
                {
                    _found = match;
                    ShowFound(match);
                }
            }
            catch (OperationCanceledException)
            {
                ShowError("Request timed out",
                    "The server took too long to respond. Try again, or narrow the search window.");
            }
            catch (HttpRequestException)
            {
                ShowError("No connection",
                    "Could not reach the revenue server. Check your network and try again.");
            }
            catch (InvalidOperationException ex)
            {
                ShowError("Could not verify", ex.Message);
            }
            catch (Exception ex)
            {
                LogError("OnVerifyTapped", ex);
                ShowError("Something went wrong", "An unexpected error occurred. Please try again.");
            }
            finally
            {
                ShowBusy(false);
                _isBusy = false;
            }
        }

        /// <summary>
        /// Swap this method out when a real livestock verification endpoint exists.
        /// Matching is case-insensitive and trims, because references get read off a printed
        /// receipt by hand and pick up stray whitespace.
        /// </summary>
        private async Task<Transaction> FindTransactionAsync(string reference)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            {
                var response = await LivestockTransactionService.GetAsync(
                    DateTime.Now.Date.AddDays(-_windowDays),
                    DateTime.Now.Date,
                    cts.Token);

                return response.Transactions.FirstOrDefault(t =>
                    !string.IsNullOrWhiteSpace(t.TransactionId) &&
                    string.Equals(t.TransactionId.Trim(), reference,
                                  StringComparison.OrdinalIgnoreCase));
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  RESULT RENDERING
        // ══════════════════════════════════════════════════════════════

        private void ShowFound(Transaction transaction)
        {
            bool good = transaction.IsSuccessful;

            ResultIconChip.BackgroundColor = transaction.StatusTintColor;
            ResultIconLabel.Text = good ? "✓" : transaction.StatusIcon;
            ResultIconLabel.TextColor = transaction.StatusColor;

            ResultTitleLabel.Text = good ? "Verified" : "Found — " + transaction.Status;
            ResultSubtitleLabel.Text = good
                ? "This receipt matches a genuine transaction on record."
                : "This reference exists but is marked \"" + transaction.Status + "\".";

            ResultAmountLabel.Text = transaction.AmountDisplay;
            ResultAmountLabel.TextColor = good ? Color.FromHex("#004225") : transaction.StatusColor;

            ResultRowStack.Children.Clear();
            AddRow("Reference", transaction.TransactionId);
            AddRow("Service", transaction.ServiceTypeName);
            AddRow("Date", transaction.DisplayDate);

            if (transaction.Quantity.HasValue && transaction.Quantity.Value > 0)
                AddRow("Quantity", transaction.Quantity.Value.ToString());

            if (!string.IsNullOrWhiteSpace(transaction.PaymentMethod))
                AddRow("Method", transaction.PaymentMethod);

            AddRow("Payer", transaction.PayerDisplay);
            AddRow("Revenue head", transaction.RevenueHead);
            AddRow("Agent", transaction.AgentName);

            ResultDetailSection.IsVisible = true;
            ResultCard.IsVisible = true;
        }

        private void ShowNotFound(string reference)
        {
            ResultIconChip.BackgroundColor = Color.FromHex("#FDECEA");
            ResultIconLabel.Text = "✕";
            ResultIconLabel.TextColor = Color.FromHex("#C0392B");

            ResultTitleLabel.Text = "Not found";
            ResultSubtitleLabel.Text =
                "No transaction with reference " + reference + " was collected by this agent in the last "
                + _windowDays + " days. Widen the search window, or check the reference on the receipt.";

            ResultDetailSection.IsVisible = false;
            ResultCard.IsVisible = true;
        }

        private void ShowError(string title, string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ResultIconChip.BackgroundColor = Color.FromHex("#FEF5E7");
                ResultIconLabel.Text = "⚠";
                ResultIconLabel.TextColor = Color.FromHex("#F39C12");

                ResultTitleLabel.Text = title;
                ResultSubtitleLabel.Text = message;

                ResultDetailSection.IsVisible = false;
                ResultCard.IsVisible = true;
            });
        }

        private void AddRow(string label, string value)
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

            ResultRowStack.Children.Add(grid);
        }

        private void ShowBusy(bool show, string message = "Verifying…")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BusyLabel.Text = message;
                BusyOverlay.IsVisible = show;
            });
        }

        // ══════════════════════════════════════════════════════════════
        //  DUPLICATE RECEIPT
        // ══════════════════════════════════════════════════════════════

        private async void OnReprintTapped(object sender, EventArgs e)
        {
            if (_isPrinting || _found == null) return;

            try
            {
                _isPrinting = true;
                ShowBusy(true, "Printing duplicate…");

                if (!await BluetoothPermissionHelper.RequestAsync())
                {
                    UserDialogs.Instance.Toast("Bluetooth permission denied.", TimeSpan.FromSeconds(5));
                    return;
                }

                var receipt = BuildReceipt(_found);
                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (p.Status == PrintProgressStatus.ChunkStarted)
                            BusyLabel.Text = string.Format("Printing {0}…", p.ChunkName);
                    }));

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    try
                    {
                        await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                        await App.PrintJobManager.DeleteJobAsync(job.JobId);
                        UserDialogs.Instance.Toast("Duplicate printed.", TimeSpan.FromSeconds(3));
                    }
                    catch (PrinterException pex)
                    {
                        UserDialogs.Instance.Toast("Print failed: " + pex.Message, TimeSpan.FromSeconds(7));
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

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StoreSubTitle = "LIVESTOCK — VERIFICATION COPY",
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
                FooterLine1 = "VERIFICATION COPY",
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = "https://yobe.osoftpay.net/singlecollections/verify?TransactId="
                             + Uri.EscapeDataString(transactionNo)
            };
        }

        private static void LogError(string scope, Exception ex)
            => System.Diagnostics.Debug.WriteLine(
                string.Format("[Livestock:Verify:{0}] {1}", scope, ex));
    }
}