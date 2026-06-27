using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DirectPayment : ContentPage
    {
        public bool DoesBusinessExist { get; set; }
        public string BusinessNames { get; set; }
        public string BusinessLGA { get; set; }
        private bool _isProcessing = false;
        private bool _isVerifying = false;
        private bool _isPrinting = false;

        // ── NEW: single BluetoothPrinterService instance (58 mm paper) ────────────
        private readonly BluetoothPrinterService _printer = new BluetoothPrinterService(use80mm: false);

        // ── Last receipt for re-print if needed ───────────────────────────────────
        private ReceiptData _lastReceipt;

        public DirectPayment()
        {
            InitializeComponent();

            try
            {
                TrackUserActivity();
                ServiceName.Text = Direct.ServiceName ?? "";
                DoesBusinessExist = true;
                Amount.Text = Direct.Amount ?? "0";
                BusinessName.Text = Direct.BusinessNames ?? "";
                LGA.Text = Direct.BusinessLGA ?? "";

                if (Amount.Text == "0" || string.IsNullOrWhiteSpace(Amount.Text))
                {
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        await ShowCustomAlert("NOTIFICATION", "Payer ID Balance is 0, kindly close the payment page", "OK");
                        Application.Current.MainPage = new NavigationPage(new Views.Home.HomeDashboard());
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Home.DirectPayment] Constructor error: {ex.Message}");
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await ShowCustomAlert("Error", "An error occurred while loading payment details. Please try again.", "OK");
                    await Navigation.PopAsync();
                });
            }
        }

        private void TrackUserActivity()
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            if (_isProcessing)
            {
                SessionManager.Instance.UpdateActivity();
                await ShowCustomAlert("Please Wait", "Payment is being processed", "OK");
                return;
            }

            if (!ValidateInputs()) return;

            _isProcessing = true;

            try
            {
                using (UserDialogs.Instance.Loading("Connecting to Service, Please Wait...", null, null, true, MaskType.Black))
                {
                    await Task.Delay(1500);
                    await ProcessPayment();
                }
            }
            catch (Exception ex)
            {
                await HandleError(ex, "An unexpected error occurred during payment processing");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private bool ValidateInputs()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                if (string.IsNullOrWhiteSpace(PIN.Text))
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await ShowCustomAlert("Validation Error", "Please enter your 4-digit PIN", "OK"));
                    return false;
                }

                if (PIN.Text.Length != 4)
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await ShowCustomAlert("Validation Error", "PIN must be exactly 4 digits", "OK"));
                    return false;
                }

                if (string.IsNullOrWhiteSpace(Amount.Text) || Amount.Text == "0")
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await ShowCustomAlert("Validation Error", "Invalid amount. Please check and try again.", "OK"));
                    return false;
                }

                decimal amount = Convert.ToDecimal(Amount.Text.Replace(",", ""));
                if (amount <= 0)
                {
                    Device.BeginInvokeOnMainThread(async () =>
                        await ShowCustomAlert("Validation Error", "Amount must be greater than zero", "OK"));
                    return false;
                }

                return true;
            }
            catch
            {
                Device.BeginInvokeOnMainThread(async () =>
                    await ShowCustomAlert("Validation Error", "Please check your input values and try again.", "OK"));
                return false;
            }
        }

        private async Task ProcessPayment()
        {
            SessionManager.Instance.UpdateActivity();

            if (!DoesBusinessExist)
            {
                await ShowCustomAlert("NOTIFICATION", "Business does not exist. Please register the business first.", "OK");
                return;
            }

            HttpClient client = null;
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            try
            {
                decimal amount = Convert.ToDecimal(Amount.Text.Replace(",", ""));

                var httpClientHandler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };

                string url = "https://yobe.osoftpay.net/api/SingleCollections/PostCollect/NewCollect";

                var nvc = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("ServiceName", ServiceName.Text ?? ""),
                    new KeyValuePair<string, string>("Amount",      Amount.Text ?? ""),
                    new KeyValuePair<string, string>("Email",       MainPage.ValidUserMail ?? ""),
                    new KeyValuePair<string, string>("Pin",         PIN.Text ?? ""),
                    new KeyValuePair<string, string>("Payer",       Direct.PayerIds ?? "")
                };

                client = new HttpClient(httpClientHandler) { Timeout = TimeSpan.FromSeconds(60) };

                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(nvc)
                };

                var res = await client.SendAsync(req, cts.Token);

                if (!res.IsSuccessStatusCode)
                {
                    await ShowFailureSheet("Network Error",
                        $"Server returned status code: {res.StatusCode}. Please try again.");
                    return;
                }

                var resultString = await res.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(resultString))
                {
                    await ShowFailureSheet("Response Error", "Empty response received from server.");
                    return;
                }

                var stateCollectionResponse = JsonConvert.DeserializeObject<StateCollectionResponseObject>(resultString);

                if (stateCollectionResponse == null)
                {
                    await ShowFailureSheet("Processing Error", "Unable to process server response.");
                    return;
                }

                if (stateCollectionResponse.RespondCode == "00")
                {
                    await Task.Delay(300);
                    await ShowSuccessSheet(stateCollectionResponse, amount);
                }
                else
                {
                    await ShowFailureSheet("Transaction Failed",
                        stateCollectionResponse.Message ?? "Unknown error occurred. Please try again.");
                }
            }
            catch (TaskCanceledException)
            {
                await ShowFailureSheet("Request Timeout",
                    "The request took too long to complete. Please check your internet connection and try again.");
            }
            catch (HttpRequestException httpEx)
            {
                await ShowFailureSheet("Network Error",
                    "Unable to connect to the payment service. Please check your internet connection.");
            }
            catch (JsonException)
            {
                await ShowFailureSheet("Data Error",
                    "Unable to process server response. Please check your transaction history.");
            }
            catch (Exception ex)
            {
                await ShowFailureSheet("Unexpected Error",
                    "An unexpected error occurred. Please check your transaction history before retrying.");
            }
            finally
            {
                cts?.Dispose();
                client?.Dispose();
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  SUCCESS SHEET
        // ══════════════════════════════════════════════════════════════════════════

        private async Task ShowSuccessSheet(StateCollectionResponseObject response, decimal amount)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();

                await Device.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        var sheet = new CustomSuccessSheet(
                            response,
                            amount,
                            Direct.ServiceName ?? "Payment",
                            Direct.BusinessNames ?? "Business");

                        // Wire print event — uses new SDK
                        sheet.PrintRequested += async (s, e) =>
                        {
                            _lastReceipt = BuildReceiptData(response, amount);
                            await CallPrinterAsync(_lastReceipt);
                        };

                        sheet.Dismissed += (s, e) => RedirecttoLandingPage();

                        await Navigation.PushModalAsync(sheet, animated: true);
                    }
                    catch (Exception modalEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Home.DirectPayment] Modal push error: {modalEx.Message}");

                        await ShowCustomAlert(
                            "Payment Successful",
                            $"✓ Transaction completed\n\nReference: {response.TransactionNo}\nAmount: ₦{amount:N2}\n\nCheck your transaction history for details.",
                            "OK");

                        await Task.Delay(2000);
                        RedirecttoLandingPage();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Home.DirectPayment] ShowSuccessSheet error: {ex.Message}");
                await Device.InvokeOnMainThreadAsync(async () =>
                {
                    await ShowCustomAlert("Payment Successful", "Your payment was processed. Check transaction history.", "OK");
                    RedirecttoLandingPage();
                });
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  RECEIPT DATA BUILDER — logo + QR barcode
        // ══════════════════════════════════════════════════════════════════════════

        private ReceiptData BuildReceiptData(StateCollectionResponseObject response, decimal amount)
        {
            string verifyUrl =
                $"https://yobe.osoftpay.net/singlecollections/verify" +
                $"?TransactId={Uri.EscapeDataString(response.TransactionNo ?? "")}";

            var items = new List<ReceiptItem>
            {
                new ReceiptItem
                {
                    Description = "Service",
                    Amount      = 0m,
                    SubText     = Direct.ServiceName ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "Payer ID",
                    Amount      = 0m,
                    SubText     = Direct.PayerIds ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "Business Name",
                    Amount      = 0m,
                    SubText     = Direct.BusinessNames ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "LGA",
                    Amount      = 0m,
                    SubText     = Direct.BusinessLGA ?? "N/A"
                },
                new ReceiptItem
                {
                    Description = "Amount",
                    Amount      = amount
                }
            };

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICE",
                StorePhone = "Contact us: 09070701616,07017639494",
                ReceiptNumber = response.TransactionNo ?? "N/A",
                AgentName = MainPage.Super_Agent ?? "N/A",
                CollectionPoint = MainPage.CollectionPoint ?? "N/A",
                SuperAgent = string.Empty,
                PrintDate = DateTime.Now,
                Items = items,
                AmountPaid = amount,
                AmountLeft = 0m,
                FooterLine2 = App.PrinterFooter ?? "POWERED BY OSOFTPAY",
                BarcodeLabel = verifyUrl   // → QR code printed on receipt
            };
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  PRINTER — NEW SDK
        // ══════════════════════════════════════════════════════════════════════════

        private static Task<bool> RequestBluetoothConnectPermissionAsync_Shared()
                   => BluetoothPermissionHelper.RequestAsync();

        private async Task<bool> CallPrinterAsync(ReceiptData receipt)   // ← return Task<bool>
        {
            if (_isPrinting)
            {
                System.Diagnostics.Debug.WriteLine("[Payment] Print already in progress – skipped.");
                return false;                                             // ← return false, not return
            }
            _isPrinting = true;

            try
            {
                if (!await RequestBluetoothConnectPermissionAsync_Shared())
                {
                    UserDialogs.Instance.Toast("Bluetooth permission denied.", TimeSpan.FromSeconds(6));
                    return false;                                         // ← return false
                }

                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");

                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.ChunkStarted:
                                ShowMessage(string.Format("Printing {0}…", p.ChunkName),
                                    "🖨️", Color.FromHex("#E3F2FD"), Color.Blue);
                                break;
                            case PrintProgressStatus.ChunkRetrying:
                                ShowMessage(string.Format("Reconnecting… retrying {0} (#{1})",
                                    p.ChunkName, p.AttemptNumber),
                                    "🔄", Color.FromHex("#FFF8E1"), Color.DarkOrange);
                                break;
                            case PrintProgressStatus.SessionCompleted:
                                ShowSuccessMessage("Receipt printed.");
                                break;
                            case PrintProgressStatus.ChunkFailed:
                                ShowErrorMessage(string.Format("Could not print {0}.", p.ChunkName));
                                break;
                        }
                    }));

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                    await App.PrintJobManager.DeleteJobAsync(job.JobId);
                    return true;                                          // ← success
                }
                catch (PrinterException pex)
                {
                    UserDialogs.Instance.Toast(string.Format("Print failed: {0}", pex.Message),
                        TimeSpan.FromSeconds(8));
                    return false;
                }
                catch (OperationCanceledException)
                {
                    UserDialogs.Instance.Toast("Print timed out. Will retry when printer is available.",
                        TimeSpan.FromSeconds(6));
                    return false;
                }
                catch (Exception ex)
                {
                    UserDialogs.Instance.Toast("Printer not connected. Transaction was successful.",
                        TimeSpan.FromSeconds(6));
                    System.Diagnostics.Debug.WriteLine(string.Format("[Payment] {0}", ex));
                    return false;
                }
                finally
                {
                    cts.Dispose();
                }
            }
            finally
            {
                _isPrinting = false;
            }
        }

        private void HandleException(Exception ex, string context)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("Error in {0}: {1}", context, ex.Message));
                System.Diagnostics.Debug.WriteLine(
                    string.Format("Stack trace: {0}", ex.StackTrace));

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        ShowErrorMessage("An unexpected error occurred. Please try again.");
                        await ShowToast("An unexpected error occurred. Please try again.", false);
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to show error message to user");
                    }
                });
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("Failed to handle exception properly");
            }
        }

        private async Task ShowToast(string message, bool isSuccess)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    string title = isSuccess ? "Success" : "Error";
                    await DisplayAlert(title, message, "OK");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    string.Format("Error showing toast: {0}. Original message: {1}",
                        ex.Message, message));
            }
        }

        private void ShowErrorMessage(string message)
        {
            try { ShowMessage(message, "❌", Color.FromHex("#FFE6E6"), Color.Red); }
            catch (Exception ex) { HandleException(ex, "Error showing error message"); }
        }
        private void ShowSuccessMessage(string message)
        {
            try { ShowMessage(message, "✅", Color.FromHex("#E8F5E8"), Color.ForestGreen); }
            catch (Exception ex) { HandleException(ex, "Error showing success message"); }
        }

        private void ShowMessage(string message, string icon, Color backgroundColor, Color textColor)
        {
            try
            {
                MessageContainer.IsVisible = true;
                MessageFrame.BackgroundColor = backgroundColor;
                MessageIcon.Text = icon;
                MessageIcon.TextColor = textColor;
                MessageLabel.Text = message;
                MessageLabel.TextColor = textColor;
            }
            catch (Exception ex) { HandleException(ex, "Error showing message"); }
        }
        // ══════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════════

        private async Task ShowFailureSheet(string title, string errorMessage)
        {
            SessionManager.Instance.UpdateActivity();
            var sheet = new CustomFailureSheet(title, errorMessage);
            await Navigation.PushModalAsync(sheet);
        }

        private async Task ShowCustomAlert(string title, string message, string buttonText)
        {
            var sheet = new CustomAlertSheet(title, message, buttonText);
            await Navigation.PushModalAsync(sheet);
        }

        private async Task HandleError(Exception ex, string userMessage)
        {
            Device.BeginInvokeOnMainThread(async () =>
                await ShowFailureSheet("Error", userMessage));
        }

        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                await Navigation.PushAsync(new Views.Home.Direct());
            }
            catch (Exception ex)
            {
                await ShowCustomAlert("Navigation Error", "Unable to navigate. Please try again.", "OK");
            }
        }

        private void RedirecttoLandingPage()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                SessionManager.Instance.UpdateActivity();
                try
                {
                    await Navigation.PushAsync(new Views.Home.RevenueList());
                }
                catch
                {
                    Application.Current.MainPage = new NavigationPage(new Views.Home.RevenueList());
                }
            });
        }

        // ── Response models ───────────────────────────────────────────────────────

        public class StateCollectionResponseObject
        {
            public string RespondCode { get; set; }
            public string Message { get; set; }
            public string TransactionNo { get; set; }
            public string PrintCode { get; set; }
            public AddSinglecollect addSinglecollect { get; set; }
            public string PayRef { get; set; }
        }

        public class AddSinglecollect { public string TransactionNo { get; set; } }

        internal class StateCollectionObject
        {
            public string ProcessedBy { get; set; }
            public string Payer { get; set; }
            public string ServiceName { get; set; }
            public string AuthCode { get; set; }
            public string Amount { get; set; }
            public string Email { get; set; }
            public string Pin { get; set; }
            public string ServiceDescription { get; set; }
        }
    }
}