using System;
using System.Linq;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

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
            SessionManager.Instance.UpdateActivity();

            if (!SessionManager.IsAuthenticated)
            {
                Device.BeginInvokeOnMainThread(() => App.SetRoot(new Views.MainPage()));
                return;
            }
            base.OnAppearing();
            AgentNameLabel.Text = SessionManager.FullName;
            LoadDashboardData();
        }

        private async void LoadDashboardData()
        {
            RecentActivityContainer.Children.Clear();

            // Display loading indicator state if you have one, or just clear the list
            var loadingLabel = new Label { Text = "Loading recent enumerations...", HorizontalOptions = LayoutOptions.Center, TextColor = Color.Gray };
            RecentActivityContainer.Children.Add(loadingLabel);

            try
            {
                // Fetch real enumeration history from API
                var res = await _waterService.GetEnumerationHistoryAsync();

                RecentActivityContainer.Children.Clear(); // Remove loading label

                if (res != null && res.respondCode == "00" && res.connections != null)
                {
                    // Order by date descending and take the top 6
                    var recentEnumerations = res.connections
                        .OrderByDescending(x => x.dateRecorded)
                        .Take(6)
                        .ToList();

                    foreach (var item in recentEnumerations)
                    {
                        var card = new Frame { BackgroundColor = Color.White, CornerRadius = 14, Padding = new Thickness(14, 12), HasShadow = false, BorderColor = Color.FromHex("#EAEAEA") };
                        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };

                        var badge = new Frame { BackgroundColor = Color.FromHex("#E0F5EE"), CornerRadius = 10, Padding = 0, HeightRequest = 42, WidthRequest = 42, HasShadow = false };
                        badge.Content = new Label { Text = "WT", TextColor = Color.FromHex("#063E2A"), FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

                        var details = new StackLayout { Spacing = 2, Margin = new Thickness(10, 0, 0, 0) };
                        details.Children.Add(new Label { Text = item.occupant, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1A1A1A"), FontSize = 13 });
                        details.Children.Add(new Label { Text = $"{item.connectionNo} • {(item.dateRecorded.HasValue ? item.dateRecorded.Value.ToString("MMM dd, h:mm tt") : "")}", TextColor = Color.FromHex("#888888"), FontSize = 11 });

                        var rightInfo = new StackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.End };
                        rightInfo.Children.Add(new Label { Text = $"₦{item.amount:N0}", FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#0E8A57"), FontSize = 13, HorizontalTextAlignment = TextAlignment.End });
                        rightInfo.Children.Add(new Label { Text = "Registered", TextColor = Color.FromHex("#555555"), FontSize = 11, HorizontalTextAlignment = TextAlignment.End });

                        grid.Children.Add(badge, 0, 0);
                        grid.Children.Add(details, 1, 0);
                        grid.Children.Add(rightInfo, 2, 0);
                        card.Content = grid;
                        RecentActivityContainer.Children.Add(card);
                    }
                }
                else
                {
                    RecentActivityContainer.Children.Add(new Label { Text = "No recent enumerations found.", HorizontalOptions = LayoutOptions.Center });
                }
            }
            catch (Exception ex)
            {
                RecentActivityContainer.Children.Clear();
                RecentActivityContainer.Children.Add(new Label { Text = "Failed to load history.", HorizontalOptions = LayoutOptions.Center, TextColor = Color.Red });
            }
        }

        private async void OnNewPaymentTapped(object sender, EventArgs e) { SessionManager.Instance.UpdateActivity(); await Navigation.PushAsync(new WaterPaymentPage()); }
        private async void OnEnumerateTapped(object sender, EventArgs e) { SessionManager.Instance.UpdateActivity(); await Navigation.PushAsync(new WaterRegistration()); }
        private async void OnHistoryTapped(object sender, EventArgs e) { SessionManager.Instance.UpdateActivity(); await Navigation.PushAsync(new WaterEnumerationHistoryPage()); }
        private async void OnSettingsTapped(object sender, EventArgs e) { SessionManager.Instance.UpdateActivity(); await Navigation.PushModalAsync(new UserProfileModal()); }
        private async void OnVerifyClicked(object sender, EventArgs e) { SessionManager.Instance.UpdateActivity(); await Navigation.PushAsync(new WaterVerifyConnectionPage()); }

        private async void OnTestPrintTapped(object sender, EventArgs e)
        {
            PrinterStatusLabel.Text = "Printing...";
            try
            {
                // Native Bluetooth Test Print
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