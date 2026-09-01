using System;
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
        private readonly WaterPrintSDK _printService;
        private WaterConnectionStatusResponse _currentConnection;

        public WaterPaymentPage()
        {
            InitializeComponent();
            _waterService = new WaterService();
            _printService = new WaterPrintSDK();
        }

        private async void OnLookupClicked(object sender, EventArgs e)
        {
            string connectionNo = ConnectionNoEntry.Text?.Trim();
            if (string.IsNullOrEmpty(connectionNo))
            {
                await DisplayAlert("Lookup", "Please enter a valid connection number.", "OK");
                return;
            }

            var res = await _waterService.GetConnectionStatusAsync(connectionNo);
            if (res != null && res.respondCode == "00") //[cite: 2]
            {
                _currentConnection = res;
                DetailsFrame.IsVisible = true;
                OccupantLbl.Text = res.occupant; //[cite: 2]
                AddressLbl.Text = res.address; //[cite: 2]
                TariffLbl.Text = $"Tariff: {res.tarifPlan}"; //[cite: 2]
                MonthlyChargeLbl.Text = $"₦{res.monthlyAmount:N2}"; //[cite: 2]
                BacklogLbl.Text = $"₦{res.backlogAmount:N2}"; //[cite: 2]
                StatusLbl.Text = $"{res.status} ({res.monthsOwedOrAhead} mo)"; //[cite: 2]

                if (res.status == "Owing") //[cite: 2]
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
            else
            {
                DetailsFrame.IsVisible = false;
                await DisplayAlert("Lookup Failed", res?.message ?? "Connection number not found.", "OK");
            }
        }

        private void OnMonthsChanged(object sender, TextChangedEventArgs e)
        {
            RecalculateTotal();
        }

        private void RecalculateTotal()
        {
            if (_currentConnection != null && int.TryParse(MonthsToPayEntry.Text, out int months) && months > 0)
            {
                decimal total = _currentConnection.monthlyAmount * months;
                TotalChargeLabel.Text = $"₦{total:N2}";
            }
            else
            {
                TotalChargeLabel.Text = "₦0.00";
            }
        }

        private async void OnPayClicked(object sender, EventArgs e)
        {
            if (!int.TryParse(MonthsToPayEntry.Text, out int months) || months < 1) //[cite: 2]
            {
                await DisplayAlert("Validation", "Please enter a valid number of months (minimum 1).", "OK"); //[cite: 2]
                return;
            }

            if (string.IsNullOrWhiteSpace(PinEntry.Text))
            {
                await DisplayAlert("Validation", "Please enter your agent wallet PIN.", "OK");
                return;
            }

            var req = new WaterPaymentRequest
            {
                payer = _currentConnection.connectionNo, //[cite: 2]
                monthsToPay = months, //[cite: 2]
                pin = PinEntry.Text.Trim(), //[cite: 2]
                email = "adewatercorporation@gmail.com" // Replace with AppSession.UserEmail
            };

            var res = await _waterService.MakePaymentAsync(req);
            if (res != null && res.respondCode == "00") //[cite: 2]
            {
                await DisplayAlert("Payment Successful", $"Transaction No: {res.transactionNo}\nMonths Paid: {res.monthsPaid}", "OK"); //[cite: 2]

                // Fetch receipt details and automatically send to Bluetooth Thermal Printer
                var receipt = await _waterService.GetReceiptAsync(res.transactionNo); //[cite: 2]
                if (receipt != null && receipt.respondCode == "00") //[cite: 2]
                {
                    await _printService.PrintPaymentReceiptAsync(receipt, months);
                }

                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Payment Failed", res?.message ?? "An error occurred during payment.", "OK");
            }
        }
    }
}