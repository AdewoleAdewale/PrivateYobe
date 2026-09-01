using System;
using System.Collections.Generic;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterDashboard : ContentPage
    {
        private readonly WaterReceiptPrintService _printService;
        private readonly WaterService _waterService;

        public WaterDashBoard()
        {
            InitializeComponent();
            _printService = new WaterReceiptPrintService();
            _waterService = new WaterService();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadDashboardData();
        }

        private void LoadDashboardData()
        {
            // Populate sample / cached recent activity items
            RecentActivityContainer.Children.Clear();

            var sampleHistory = new List<WaterConnectionStatusResponse>
            {
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00002", occupant = "Main Flat Tap", address = "No 14 Gujba Road", lastPaymentAmount = 1500, lastPaymentDate = DateTime.Now.AddHours(-2) }, //[cite: 2]
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00004", occupant = "Domestic Borehole", address = "Old GRA, Damaturu", lastPaymentAmount = 2000, lastPaymentDate = DateTime.Now.AddHours(-4) }, //[cite: 2]
                new WaterConnectionStatusResponse { connectionNo = "POT/WTC/00010", occupant = "Commercial Flat", address = "Potiskum Central", lastPaymentAmount = 3000, lastPaymentDate = DateTime.Now.AddDays(-1) }
            };

            decimal totalToday = 3500;
            CollectedTodayLabel.Text = $"₦{totalToday:N0}";
            PaymentsCountLabel.Text = "2";
            AvgTicketLabel.Text = "₦1,750";
            WeeklyTotalLabel.Text = "₦6,500";

            foreach (var item in sampleHistory)
            {
                var card = new Frame
                {
                    BackgroundColor = Color.White,
                    CornerRadius = 14,
                    Padding = new Thickness(14, 12),
                    HasShadow = false,
                    BorderColor = Color.FromHex("#EAEAEA")
                };

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };

                var badge = new Frame
                {
                    BackgroundColor = Color.FromHex("#E0F5EE"),
                    CornerRadius = 10,
                    Padding = 0,
                    HeightRequest = 42,
                    WidthRequest = 42,
                    HasShadow = false
                };
                badge.Content = new Label { Text = "WT", TextColor = Color.FromHex("#063E2A"), FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

                var details = new StackLayout { Spacing = 2, Margin = new Thickness(10, 0, 0, 0) };
                details.Children.Add(new Label { Text = item.occupant, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A1A1A"), FontSize = 13 }); //[cite: 2]
                details.Children.Add(new Label { Text = $"{item.connectionNo} • {item.lastPaymentDate:MMM dd, h:mm tt}", TextColor = Color.FromHex("#888888"), FontSize = 11 }); //[cite: 2]

                var rightInfo = new StackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.End };
                rightInfo.Children.Add(new Label { Text = $"₦{item.lastPaymentAmount:N0}", FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A1A1A"), FontSize = 13, HorizontalTextAlignment = TextAlignment.End }); //[cite: 2]
                rightInfo.Children.Add(new Label { Text = "Paid", TextColor = Color.FromHex("#0E8A57"), FontSize = 11, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End });

                grid.Children.Add(badge, 0, 0);
                grid.Children.Add(details, 1, 0);
                grid.Children.Add(rightInfo, 2, 0);

                card.Content = grid;
                RecentActivityContainer.Children.Add(card);
            }
        }

        private async void OnNewPaymentTapped(object sender, EventArgs e) => await Navigation.PushAsync(new WaterPaymentPage());
        private async void OnSettingsTapped(object sender, EventArgs e) => await Navigation.PushAsync(new ChangePassword());

        private async void OnHistoryTapped(object sender, EventArgs e)
    => await Navigation.PushAsync(new WaterHistoryPage());

        private async void OnEnumerateTapped(object sender, EventArgs e)
            => await Navigation.PushAsync(new WaterRegistration());
        private async void OnTestPrintTapped(object sender, EventArgs e)
        {
            PrinterStatusLabel.Text = "Printing...";
            bool res = await _printService.PrintTestAsync();
            PrinterStatusLabel.Text = res ? "Printer Ready" : "Printer Error";
            await DisplayAlert("Printer Diagnostics", res ? "Test receipt printed successfully." : "Unable to reach bluetooth printer. Please ensure printer is paired and on.", "OK");
        }

        private async void OnLogoutTapped(object sender, EventArgs e)
        {
            bool answer = await DisplayAlert("Logout", "Do you want to log out?", "Yes", "No");
            if (answer) Application.Current.MainPage = new NavigationPage(new MainPage());
        }
    }
}