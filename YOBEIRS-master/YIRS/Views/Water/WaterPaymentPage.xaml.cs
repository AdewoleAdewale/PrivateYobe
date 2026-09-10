using System;
using System.Collections.Generic;
using System.Threading;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterPaymentPage : ContentPage
    {
        private readonly WaterService _waterService;
        private readonly BluetoothPrinterService _printerService;
        private WaterConnectionStatusResponse _currentConnection;

        public WaterPaymentPage()
        {
            InitializeComponent();
            _waterService = new WaterService();
            _printerService = new BluetoothPrinterService(use80mm: false);
            MonthsToPayPicker.SelectedIndex = 0; // Default to 1 month
        }

        private async void OnLookupClicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            string connectionNo = ConnectionNoEntry.Text?.Trim();
            if (string.IsNullOrEmpty(connectionNo)) { await DisplayAlert("Error", "Enter connection number.", "OK"); return; }

            var res = await _waterService.GetConnectionStatusAsync(connectionNo);
            if (res != null && res.respondCode == "00")
            {
                _currentConnection = res;
                DetailsFrame.IsVisible = true;
                OccupantLbl.Text = res.occupant;
                AddressLbl.Text = res.address;
                TariffLbl.Text = $"Tariff: {res.tarifPlan}";
                MonthlyChargeLbl.Text = $"₦{res.monthlyAmount:N2}";
                BacklogLbl.Text = $"₦{res.backlogAmount:N2}";
                StatusLbl.Text = $"{res.status} ({res.monthsOwedOrAhead} mo)";

                if (res.status == "Owing")
                {
                    StatusBadge.BackgroundColor = Color.FromHex("#FDE8E8");
                    StatusLbl.TextColor = Color.FromHex("#C41C1C");
                }
                else
                {
                    StatusBadge.BackgroundColor = Color.FromHex("#E3F7EF");
                    StatusLbl.TextColor = Color.FromHex("#0E8A57");
                }
                RecalculateTotal();
            }
            else { await DisplayAlert("Not Found", res?.message, "OK"); }
        }

        private void OnMonthsChanged(object sender, EventArgs e) => RecalculateTotal();

        private void RecalculateTotal()
        {
            if (_currentConnection != null && MonthsToPayPicker.SelectedIndex >= 0)
            {
                int months = MonthsToPayPicker.SelectedIndex + 1;
                TotalChargeLabel.Text = $"₦{(_currentConnection.monthlyAmount * months):N2}";
            }
            else
            {
                TotalChargeLabel.Text = "₦0.00";
            }
        }
        private async void OnPayClicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            int months = MonthsToPayPicker.SelectedIndex + 1;

            if (months < 1)
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Invalid Months", "Minimum 1 month required."));
                return;
            }

            if (string.IsNullOrWhiteSpace(PinEntry.Text))
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Missing PIN", "Please enter your agent PIN."));
                return;
            }

            try
            {
                string agentEmail = !string.IsNullOrWhiteSpace(SessionManager.Email) ? SessionManager.Email : "agent@watercorp.gov.ng";

                // Get Year from Picker
                int selectedYear = 2026;
                if (YearPicker.SelectedIndex >= 0)
                {
                    int.TryParse(YearPicker.SelectedItem.ToString(), out selectedYear);
                }

                var req = new WaterPaymentRequest
                {
                    connectionno = _currentConnection.connectionNo, // Updated field
                    monthsToPay = months,
                    year = selectedYear, // New field
                    pin = PinEntry.Text.Trim(),
                    email = agentEmail
                };

                var res = await _waterService.MakePaymentAsync(req);

                if (res != null && res.respondCode == "00")
                {
                    var receiptInfo = await _waterService.GetReceiptAsync(res.transactionNo);
                    decimal paidAmount = res.lastPaymentAmount ?? (_currentConnection.monthlyAmount * months);
                    string txRef = !string.IsNullOrEmpty(res.transactionNo) ? res.transactionNo : "WTR-" + DateTime.Now.ToString("yyMMddHHmmss");
                         await Navigation.PushModalAsync(new WaterSuccessSheet(
                             "Payment Successful",
                             $"Payment recorded for {months} month(s).",
                             txRef,
                             $"₦{paidAmount:N2}",
                             async () =>
                        {
                            if (receiptInfo != null)
                            {
                                // Print using the built-in BluetoothPrinterService
                                var receiptData = new ReceiptData
                                {
                                    StoreName = "YOBE STATE INTERNAL REVENUE SERVICE",
                                    StoreSubTitle = "OFFICIAL WATER RECEIPT",
                                    StorePhone = "Contact us:  08101029977",
                                    ReceiptNumber = txRef,
                                    AgentName = MainPage.Name,
                                    CollectionPoint = MainPage.CollectionPoint,
                                    AmountPaid = paidAmount,
                                    BarcodeLabel = $"https://yobe.osoftpay.net/Api/Singlecollections/VerifyTransaction?TransactId={txRef}",
                                    Items = new List<ReceiptItem>
                                {
                                    new ReceiptItem { Description = "CONNECTION NO", SubText = res.connectionNo ?? _currentConnection.connectionNo, Amount = 0 },
                                    new ReceiptItem { Description = "NAME", SubText = res.occupant ?? _currentConnection.occupant, Amount = 0 },
                                    new ReceiptItem { Description = "MONTHS PAID", SubText = $"{months} MONTH(S) ({selectedYear})", Amount = 0 },
                                    new ReceiptItem { Description = "TOTAL CHARGE", SubText = "", Amount = paidAmount }
                                },
                                    FooterLine1 = "THANK YOU FOR MAKING PAYMENTS",
                                    FooterLine2 = "POWERED BY OSOFTPAY"
                                };

                                await _printerService.PrintReceiptAsync(receiptData, "Logo.png", "YOBE IRS", null, null, default(CancellationToken));
                            }
                        }));
                    PinEntry.Text = "";
                    MonthsToPayPicker.SelectedIndex = 0;
                    YearPicker.SelectedIndex = 0;
                    DetailsFrame.IsVisible = false;
                    ConnectionNoEntry.Text = "";
                }
                else
                {
                    await Navigation.PushModalAsync(new WaterFailureSheet("Payment Failed", res?.message));
                }
            }
            catch (Exception ex)
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Error", ex.Message));
            }
        }
    }
}