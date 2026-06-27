using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Haulage
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Payments : ContentPage
    {
        List<LGACatData> enums;

        private bool _isProcessing = false;
        private bool _isPrinting = false;
        private ReceiptData _lastReceiptData = null;

        public Payments()
        {
            InitializeComponent();
            ConfigureSSL();
            LoadLGACategory();

            sheetBehavior.IsOpen = false;
            makepaymentbutton.IsVisible = false;

            Servicename.Text = Verify.vehicleTypess;
            PayerId.Text = Verify.plateNumberss;

            bool amountIsZero = Verify.amountss is "0";
            Amount.IsEnabled = amountIsZero;
            Amount.Text = Verify.amountss;
            Amount.IsReadOnly = !amountIsZero;
        }

        // ─── SSL ─────────────────────────────────────────────────────────────

        private void ConfigureSSL()
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            ServicePointManager.ServerCertificateValidationCallback =
                new RemoteCertificateValidationCallback(ValidateServerCertificate);
            ServicePointManager.DefaultConnectionLimit = 10;
            ServicePointManager.Expect100Continue = true;
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None) return true;
            System.Diagnostics.Debug.WriteLine($"SSL: {sslPolicyErrors}");
            return true;
        }

        // ─── LGA Loader ──────────────────────────────────────────────────────

        private void LoadLGACategory()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (m, c, ch, er) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                };

                using (var client = new HttpClient(handler))
                using (var response = client.GetAsync("https://gurara.osoftpay.net/api/HulageVehicles/getlga").Result)
                {
                    var json = response.Content.ReadAsStringAsync().Result;
                    var items = JsonConvert.DeserializeObject<List<LGACatData>>(json);

                    picker.ItemDisplayBinding = new Binding("lgaName");
                    picker.ItemsSource = items?.ToList();
                    enums = items?.ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HaulagePayment] LoadLGA: {ex.Message}");
            }
        }
        // ─── Sheet / PIN ──────────────────────────────────────────────────────

        private void sheetBehavior_ActionClicked(object sender, EventArgs e)
            => sheetBehavior.IsOpen = false;

        private void Pin_Unfocused(object sender, FocusEventArgs e)
            => makepaymentbutton.IsVisible = true;

        // ─── Payment ──────────────────────────────────────────────────────────

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            if (_isProcessing) return;
            await ProcessPaymentAsync();
        }

        private async Task ProcessPaymentAsync()
        {
            // Resolve LGA
            string lgaSelected = (enums != null && picker.SelectedIndex >= 0 &&
                                  picker.SelectedIndex < enums.Count)
                                 ? enums[picker.SelectedIndex].lgaName ?? ""
                                 : "";

            // Validate
            if (string.IsNullOrWhiteSpace(PIN.Text) || string.IsNullOrWhiteSpace(lgaSelected))
            {
                await ShowFailurePopup("Kindly fill in all required fields before proceeding.");
                return;
            }

            if (PIN.Text != MainPage.Pin)
            {
                await ShowFailurePopup("Incorrect Transaction PIN. Please try again.");
                return;
            }

            _isProcessing = true;
            SetLoadingState(true);

            try
            {
                string newAmount = Amount.Text?.Replace(",", "") ?? "";
                string finalAmount = newAmount.Replace(".00", "");

                var nvc = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("ServiceName", Verify.vehicleTypess),
            new KeyValuePair<string, string>("Email", MainPage.ValidUserMail),
            new KeyValuePair<string, string>("Amount", finalAmount),
            new KeyValuePair<string, string>("Pin", PIN.Text),
            new KeyValuePair<string, string>("Payer", Verify.plateNumberss),
            new KeyValuePair<string, string>("LgaTo", lgaSelected)
        };

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (m, c, ch, er) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                 | System.Security.Authentication.SslProtocols.Tls11
                };

                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
                {
                    HttpResponseMessage res;

                    try
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post,
                            "https://yobe.osoftpay.net/api/SingleCollections/v1/Hulage")
                        {
                            Content = new FormUrlEncodedContent(nvc)
                        };

                        res = await client.SendAsync(req);
                    }
                    catch (TaskCanceledException)
                    {
                        await ShowFailurePopup("Request timed out. Please check your internet connection and try again.");
                        return;
                    }
                    catch (HttpRequestException httpEx)
                    {
                        await ShowFailurePopup($"Network error: {httpEx.Message}");
                        return;
                    }

                    var resultString = await res.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[HaulagePayment] Response: {resultString}");

                    StateCollectionResponseObject resp;
                    try
                    {
                        resp = JsonConvert.DeserializeObject<StateCollectionResponseObject>(resultString);
                        if (resp == null) throw new Exception("Null response");
                    }
                    catch
                    {
                        await ShowFailurePopup("Server returned an unexpected response. Please try again.");
                        return;
                    }

                    if (resp.RespondCode == "00")
                    {
                        var receipt = BuildReceiptData(resp, finalAmount, lgaSelected);
                        _lastReceiptData = receipt;

                        // Show rich success popup
                        await ShowSuccessPopup(resp, lgaSelected, finalAmount);

                        // Attempt print silently after popup shown
                        _ = AttemptPrintAsync(receipt, isReprint: false);
                    }
                    else
                    {
                        await ShowFailurePopup(resp.Message ?? "Transaction failed. Please try again.");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowFailurePopup("Check your internet connection. If amount was deducted, verify in History.");
                System.Diagnostics.Debug.WriteLine($"[HaulagePayment] ProcessPayment: {ex}");
            }
            finally
            {
                _isProcessing = false;
                SetLoadingState(false);
            }
        }
        // ─── Rich Popup helpers ────────────────────────────────────────────────

        private Task ShowSuccessPopup(StateCollectionResponseObject resp, string lgaTo, string amount)
        {
            return Device.InvokeOnMainThreadAsync(() =>
            {
                PopupRef.Text = resp.TransactionNo ?? "N/A";
                PopupVehicle.Text = Verify.vehicleTypess ?? "—";
                PopupPlate.Text = Verify.plateNumberss ?? "—";
                PopupLGA.Text = lgaTo;
                PopupAmount.Text = $"₦{(decimal.TryParse(amount, out var a) ? a.ToString("N2") : amount)}";
                PrintingStatusLabel.Text = "🖨️  Sending receipt to printer…";

                SuccessOverlay.IsVisible = true;
                FailureOverlay.IsVisible = false;
            });
        }

        private Task ShowFailurePopup(string message)
        {
            return Device.InvokeOnMainThreadAsync(() =>
            {
                FailureMessage.Text = message;
                FailureOverlay.IsVisible = true;
                SuccessOverlay.IsVisible = false;
            });
        }

        private void SuccessContinue_Tapped(object sender, EventArgs e)
        {
            SuccessOverlay.IsVisible = false;
            Navigation.PushAsync(new Verify());
        }

        private void FailureRetry_Tapped(object sender, EventArgs e)
        {
            FailureOverlay.IsVisible = false;
        }

        // ─── Receipt Builder ──────────────────────────────────────────────────

        private ReceiptData BuildReceiptData(StateCollectionResponseObject resp,
       string amount, string lgaTo, bool isReprint = false)
        {
            decimal amtDecimal = decimal.TryParse(amount, out decimal d) ? d : 0m;

            string verifyUrl = $"https://yobe.osoftpay.net/singlecollections/verify?TransactId={Uri.EscapeDataString(resp.TransactionNo ?? "")}";

            var items = new List<ReceiptItem>
    {
        new ReceiptItem { Description = "AGENT NAME", Amount = 0m, SubText = MainPage.Name },
        new ReceiptItem { Description = "VEHICLE TYPE", Amount = 0m, SubText = Verify.vehicleTypess },
        new ReceiptItem { Description = "VEHICLE LIC. NO", Amount = 0m, SubText = Verify.plateNumberss },
        new ReceiptItem { Description = "LGA TO", Amount = 0m, SubText = lgaTo }
    };

            return new ReceiptData
            {
                StoreName = App.RevenueServiceName ?? "YOBE STATE INTERNAL REVENUE SERVICES",
                StorePhone = "Contact us: +234 810 046 6363",
                ReceiptNumber = resp.TransactionNo ?? "N/A",
                AgentName = MainPage.Name,
                CollectionPoint = "HAULAGE",
                AmountPaid = amtDecimal,
                PrintDate = DateTime.Now,
                Items = items,
                FooterLine1 = isReprint ? "*** REPRINTED RECEIPT ***" : App.ThankYouMessage ?? "Thank You!",
                FooterLine2 = isReprint
                    ? $"Reprinted: {DateTime.Now:dd MMM yyyy HH:mm} | POWERED BY OSOFTPAY"
                    : "POWERED BY OSOFTPAY",
                BarcodeLabel = verifyUrl
            };
        }
        // ─── Print ────────────────────────────────────────────────────────────

        private async Task AttemptPrintAsync(ReceiptData receipt, bool isReprint)
        {
            if (_isPrinting) return;
            _isPrinting = true;
            try
            {
                bool granted = await BluetoothPermissionHelper.RequestAsync();
                if (!granted)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        UserDialogs.Instance.Toast("Bluetooth permission denied.", TimeSpan.FromSeconds(6));
                        ShowReprintButton();
                        if (SuccessOverlay.IsVisible)
                            PrintingStatusLabel.Text = "⚠️  Print failed — tap Reprint below";
                    });
                    return;
                }

                var job = await App.PrintJobManager.EnqueueAsync(receipt, logoAssetName: "Logo.png");
                var progress = new Progress<PrintProgress>(p =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        switch (p.Status)
                        {
                            case PrintProgressStatus.SessionCompleted:
                                HideReprintButton();
                                UserDialogs.Instance.Toast(
                                    isReprint ? "Receipt reprinted." : "Receipt printed.",
                                    TimeSpan.FromSeconds(3));
                                if (SuccessOverlay.IsVisible)
                                    PrintingStatusLabel.Text = "✅  Receipt printed successfully";
                                break;
                            case PrintProgressStatus.ChunkFailed:
                                ShowReprintButton();
                                if (SuccessOverlay.IsVisible)
                                    PrintingStatusLabel.Text = "⚠️  Print failed — tap Reprint";
                                break;
                        }
                    }));

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    await App.PrintJobManager.ExecuteAsync(job.JobId, progress, cts.Token);
                    await App.PrintJobManager.DeleteJobAsync(job.JobId);
                    MainThread.BeginInvokeOnMainThread(HideReprintButton);
                }
                catch
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ShowReprintButton();
                        if (SuccessOverlay.IsVisible)
                            PrintingStatusLabel.Text = "⚠️  Print failed — tap Reprint below";
                    });
                }
            }
            finally { _isPrinting = false; }
        }

        // ─── Reprint ──────────────────────────────────────────────────────────

        private async void OnReprintClicked(object sender, EventArgs e)
        {
            if (_lastReceiptData == null)
            {
                UserDialogs.Instance.Toast("No receipt data available.", TimeSpan.FromSeconds(4));
                return;
            }
            ReprintButton.IsEnabled = false;
            ReprintButton.Text = "Reprinting…";
            try
            {
                var reprint = new ReceiptData
                {
                    StoreName = _lastReceiptData.StoreName,
                    StorePhone = _lastReceiptData.StorePhone,
                    ReceiptNumber = _lastReceiptData.ReceiptNumber,
                    AgentName = _lastReceiptData.AgentName,
                    CollectionPoint = _lastReceiptData.CollectionPoint,
                    PrintDate = DateTime.Now,
                    Items = _lastReceiptData.Items,
                    AmountPaid = _lastReceiptData.AmountPaid,
                    BarcodeLabel = _lastReceiptData.BarcodeLabel,
                    FooterLine1 = "*** REPRINTED RECEIPT ***",
                    FooterLine2 = $"Reprinted: {DateTime.Now:dd MMM yyyy HH:mm} | POWERED BY OSOFTPAY",
                };
                await AttemptPrintAsync(reprint, isReprint: true);
            }
            finally
            {
                ReprintButton.IsEnabled = true;
                ReprintButton.Text = "REPRINT RECEIPT";
            }
        }

        private void ShowReprintButton() { try { ReprintButtonView.IsVisible = true; } catch { } }
        private void HideReprintButton() { try { ReprintButtonView.IsVisible = false; } catch { } }

        // ─── Loading state ────────────────────────────────────────────────────

        private void SetLoadingState(bool loading)
        {
            try
            {
                LoadingOverlay.IsVisible = loading;
                makepaymentbutton.IsEnabled = !loading;
                PIN.IsEnabled = !loading;
                picker.IsEnabled = !loading;
            }
            catch { }
        }

        // ─── Legacy sheet / navigation ────────────────────────────────────────

        private async void Button_Clicked(object sender, EventArgs e)
            => await Navigation.PushAsync(new Views.Haulage.Verify());
    }

    internal class GetSateCatData { public string name { get; set; } }

    internal class StateCollectionResponseObject
    {
        public string RespondCode { get; set; }
        public string Message { get; set; }
        public AddSinglecollect addSinglecollect { get; set; }
        public string PrintCode { get; set; }
        public string TransactionNo { get; set; }
    }

    internal class AddSinglecollect { public string TransactionNo { get; set; } }
}