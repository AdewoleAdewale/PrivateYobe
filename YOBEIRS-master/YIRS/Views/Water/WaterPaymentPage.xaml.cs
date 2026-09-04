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
        }

        private async void OnLookupClicked(object sender, EventArgs e)
        {
            // Same lookup logic as previous responses...
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

        private void OnMonthsChanged(object sender, TextChangedEventArgs e) => RecalculateTotal();

        private void RecalculateTotal()
        {
            if (_currentConnection != null && int.TryParse(MonthsToPayEntry.Text, out int months) && months > 0)
                TotalChargeLabel.Text = $"₦{(_currentConnection.monthlyAmount * months):N2}";
            else TotalChargeLabel.Text = "₦0.00";
        }

        private async void OnPayClicked(object sender, EventArgs e)
        {
            if (!int.TryParse(MonthsToPayEntry.Text, out int months) || months < 1)
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
                string agentEmail = SessionManager.GetSession()?.Email ?? "agent@watercorp.gov.ng";

                var req = new WaterPaymentRequest
                {
                    payer = _currentConnection.connectionNo,
                    monthsToPay = months,
                    pin = PinEntry.Text.Trim(),
                    email = agentEmail
                };

                var res = await _waterService.MakePaymentAsync(req);

                if (res != null && res.respondCode == "00")
                {
                    var receiptInfo = await _waterService.GetReceiptAsync(res.transactionNo);

                    await Navigation.PushModalAsync(new WaterSuccessSheet(
                        "Payment Successful",
                        $"Payment recorded for {res.monthsPaid} month(s).",
                        res.transactionNo,
                        $"₦{res.totalAmount:N2}",
                        async () =>
                        {
                            if (receiptInfo != null)
                            {
                                // 1. Map API Receipt to BluetoothPrinterService ReceiptData
                                var receiptData = new ReceiptData
                                {
                                    StoreName = "YOBE STATE INTERNAL REVENUE SERVICE",
                                    StoreSubTitle = "OFFICIAL WATER RECEIPT",
                                    ReceiptNumber = receiptInfo.transactionId,
                                    AgentName = receiptInfo.performedBy,
                                    CollectionPoint = MainPage.CollectionPoint,
                                    AmountPaid = receiptInfo.amount,
                                    BarcodeLabel = $"https://yobeirs.gov.ng/receipt?tx={receiptInfo.transactionId}",
                                    Items = new List<ReceiptItem>
                                    {
                                        new ReceiptItem { Description = "Connection", SubText = receiptInfo.payer, Amount = 0 },
                                        new ReceiptItem { Description = "Occupant", SubText = receiptInfo.occupant, Amount = 0 },
                                        new ReceiptItem { Description = "Months Paid", SubText = $"{months} Month(s)", Amount = receiptInfo.amount }
                                    },
                                    FooterLine1 = "Thank you for your payment!",
                                    FooterLine2 = "POWERED BY OSOFTPAY"
                                };

                                // 2. Trigger Print
                                await _printerService.PrintReceiptAsync(receiptData, "Logo.png", "YOBE IRS", null, null, default(CancellationToken));
                            }
                        }));
                    PinEntry.Text = "";
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