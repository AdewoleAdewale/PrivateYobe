using System;
using System.Collections.Generic;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;
using System.Text;
using System.Threading.Tasks;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterDashboard : ContentPage
    {
        private readonly BluetoothPrinterService _printerService;
        private readonly WaterService _waterService;

        public WaterDashboard()
        {
            InitializeComponent();
            _printerService = new BluetoothPrinterService(use80mm: false);
            _waterService = new WaterService();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadDashboardData();
        }

        private void LoadDashboardData()
        {
            RecentActivityContainer.Children.Clear();

            var sampleHistory = new List<WaterConnectionStatusResponse>
            {
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00002", occupant = "Main Flat Tap", address = "No 14 Gujba Road", lastPaymentAmount = 1500, lastPaymentDate = DateTime.Now.AddHours(-2) },
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00004", occupant = "Domestic Borehole", address = "Old GRA, Damaturu", lastPaymentAmount = 2000, lastPaymentDate = DateTime.Now.AddHours(-4) },
            };

            CollectedTodayLabel.Text = $"₦3,500";
            PaymentsCountLabel.Text = "2";
            AvgTicketLabel.Text = "₦1,750";
            WeeklyTotalLabel.Text = "₦6,500";

            foreach (var item in sampleHistory)
            {
                var card = new Frame { BackgroundColor = Color.White, CornerRadius = 14, Padding = new Thickness(14, 12), HasShadow = false, BorderColor = Color.FromHex("#EAEAEA") };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };

                var badge = new Frame { BackgroundColor = Color.FromHex("#E0F5EE"), CornerRadius = 10, Padding = 0, HeightRequest = 42, WidthRequest = 42, HasShadow = false };
                badge.Content = new Label { Text = "WT", TextColor = Color.FromHex("#063E2A"), FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

                var details = new StackLayout { Spacing = 2, Margin = new Thickness(10, 0, 0, 0) };
                details.Children.Add(new Label { Text = item.occupant, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A1A1A"), FontSize = 13 });
                details.Children.Add(new Label { Text = $"{item.connectionNo} • {item.lastPaymentDate:MMM dd, h:mm tt}", TextColor = Color.FromHex("#888888"), FontSize = 11 });

                var rightInfo = new StackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.End };
                rightInfo.Children.Add(new Label { Text = $"₦{item.lastPaymentAmount:N0}", FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A1A1A"), FontSize = 13, HorizontalTextAlignment = TextAlignment.End });
                rightInfo.Children.Add(new Label { Text = "Paid", TextColor = Color.FromHex("#0E8A57"), FontSize = 11, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End });

                grid.Children.Add(badge, 0, 0);
                grid.Children.Add(details, 1, 0);
                grid.Children.Add(rightInfo, 2, 0);
                card.Content = grid;
                RecentActivityContainer.Children.Add(card);
            }
        }

        private async void OnNewPaymentTapped(object sender, EventArgs e) => await Navigation.PushAsync(new WaterPaymentPage());
        private async void OnEnumerateTapped(object sender, EventArgs e) => await Navigation.PushAsync(new WaterRegistration());
        private async void OnHistoryTapped(object sender, EventArgs e) => await Navigation.PushAsync(new WaterEnumerationHistoryPage());
        private async void OnSettingsTapped(object sender, EventArgs e) => await Navigation.PushModalAsync(new UserProfileModal());
        private async void OnVerifyClicked(object sender, EventArgs e) => await Navigation.PushModalAsync(new WaterVerifyConnectionPage());

        private async void OnTestPrintTapped(object sender, EventArgs e)
        {
            PrinterStatusLabel.Text = "Printing...";
            try
            {
                // Calls the built-in PrintTestPageAsync from your provided class
                await _printerService.PrintTestPageAsync(); 
                PrinterStatusLabel.Text = "Printer ready";
                await DisplayAlert("Diagnostics", "Test print completed successfully.", "OK");
            }
            catch (Exception ex)
            {
                PrinterStatusLabel.Text = "Printer error";
                await DisplayAlert("Printer Error", ex.Message, "OK");
            }
        }

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            bool answer = await DisplayAlert("Logout", "Do you want to log out?", "Yes", "No");
            if (answer) Application.Current.MainPage = new NavigationPage(new MainPage());
        }
    


    }
}