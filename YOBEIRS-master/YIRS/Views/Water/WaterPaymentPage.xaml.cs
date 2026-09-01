using System;
using System.Net.NetworkInformation;
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
            if (!int.TryParse(MonthsToPayEntry.Text, out int months) || months < 1) 
    {
                await Navigation.PushModalAsync(new WaterFailureSheet("Invalid Months", "Please enter a valid number of months (minimum 1).")); 
        return;
            }

            if (string.IsNullOrWhiteSpace(PinEntry.Text))
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Missing PIN", "Please enter your agent wallet PIN."));
                return;
            }

            try
            {
                var req = new WaterPaymentRequest
                {
                    payer = _currentConnection.connectionNo,
                  
        
                    monthsToPay = months,
                  
        
                    pin = PinEntry.Text.Trim(),
                   
        
                    email = SessionManager.GetSession()?.Email ?? "agent@watercorp.gov.ng"
                };

                var res = await _waterService.MakePaymentAsync(req);

                if (res != null && res.respondCode == "00") 
        {
                    var receipt = await _waterService.GetReceiptAsync(res.transactionNo); 

            await Navigation.PushModalAsync(new WaterSuccessSheet(
                "Payment Successful",
                $"Payment recorded for {res.monthsPaid} month(s).", 
                res.transactionNo,
                $"₦{res.totalAmount:N2}", 
                async () =>
                {
                    if (receipt != null)
                    {
                        await _printService.PrintPaymentReceiptAsync(receipt, months);
                    }
                }));

                    PinEntry.Text = "";
                }
        else
                {
                    await Navigation.PushModalAsync(new WaterFailureSheet("Payment Failed", res?.message ?? "Unable to complete transaction."));
                }
            }
            catch (Exception ex)
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Payment Error", ex.Message));
            }
        }
    }
}