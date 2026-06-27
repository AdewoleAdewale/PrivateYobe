using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RevenueList : ContentPage
    {
        class ServicesList
        {
            public string ServiceName { get; set; }
            public string ServiceDescription { get; set; }
            public decimal ServiceAmount { get; set; }
            public string Samount
            {
                get { return ServiceAmount.ToString("###,###.00"); }
                set
                {
                    ServiceAmount.ToString("###,###.00");
                }
            }
        }

        class HistoryDataHeaderFooter
        {
            public List<ServicesList> HD { get; set; }
            public string Intro { get { return "You have a total of " + HD.Count + " Revenue Heads"; } }
            public string Summary { get { return "Total: " + HD.Count + " Revenue Services Available"; } }
            public decimal Size { get { return HD.Count; } }
        }

        public static string ServiceName { get; set; }
        public static string ServiceDescription { get; set; }
        public static decimal ServiceAmount { get; set; }

        private Label TotalServicesLabel2;

        public RevenueList()
        {
            InitializeComponent();

            // Find the TotalServicesLabel from XAML
            TotalServicesLabel2 = this.FindByName<Label>("TotalServicesLabel");

            // Add entrance animation
            AnimatePageEntrance();

            CallRevenueList();
            TrackUserActivity();
            Fullname.Text = MainPage.CollectionPoint;

            // Add pull-to-refresh functionality
            AddPullToRefresh();
        }

        private async void AnimatePageEntrance()
        {
            // Animate the content with a fade-in effect
            this.Content.Opacity = 0;
            await this.Content.FadeTo(1, 500, Easing.CubicInOut);
        }

        private void AddPullToRefresh()
        {
            // Add swipe gesture for refresh
            var swipeGesture = new SwipeGestureRecognizer { Direction = SwipeDirection.Down };
            swipeGesture.Swiped += async (s, e) =>
            {
                await DisplayAlert("Refreshing", "Updating service list...", "OK");
                CallRevenueList();
            };
            this.Content.GestureRecognizers.Add(swipeGesture);
        }

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private async void CallRevenueList()
        {
            SessionManager.Instance.UpdateActivity();

            using (UserDialogs.Instance.Loading("🔄 Loading Services...", null, null, true))
            {
                await Task.Delay(800);
                string url = "https://yobe.osoftpay.net/api/TaskPayers/getservices?Email=" + MainPage.ValidUserMail;

                try
                {
                    using (var httpClientHandler = new HttpClientHandler())
                    {
                        // SSL certificate validation
                        httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                        {
                            return true;
                        };

                        using (HttpClient client = new HttpClient(httpClientHandler))
                        {
                            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                            using (HttpResponseMessage response = await client.GetAsync(url))
                            {
                                using (HttpContent content = response.Content)
                                {
                                    var json = await content.ReadAsStringAsync();
                                    MemoryStream memStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                                    StreamReader reader = new StreamReader(memStream);
                                    string text = reader.ReadToEnd();

                                    List<ServicesList> items = JsonConvert.DeserializeObject<List<ServicesList>>(text);

                                    if (items != null && items.Count > 0)
                                    {
                                        BindingContext = new HistoryDataHeaderFooter { HD = items };

                                        // Update the total services label with animation
                                        if (TotalServicesLabel != null)
                                        {
                                            await AnimateNumberChange(TotalServicesLabel, items.Count);
                                        }

                                        // Animate list items
                                        await AnimateListItems();
                                    }
                                    else
                                    {
                                        await DisplayAlert("Info", "No services available at the moment.", "OK");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception exe)
                {
                    await DisplayAlert("Error", "Failed to load services. Please check your connection and try again.", "OK");
                    System.Diagnostics.Debug.WriteLine($"Error: {exe.Message}");
                }
            }
        }

        private async Task AnimateNumberChange(Label label, int targetNumber)
        {
            // Animate number counting up
            label.Opacity = 0;
            label.Text = targetNumber.ToString();
            await label.FadeTo(1, 500, Easing.CubicOut);
            await label.ScaleTo(1.2, 150, Easing.CubicOut);
            await label.ScaleTo(1, 150, Easing.CubicIn);
        }

        private async Task AnimateListItems()
        {
            // Add staggered animation to list items
            await Task.Delay(100);

            // Subtle scale animation for the entire list
            await listView.ScaleTo(0.95, 0);
            await listView.ScaleTo(1, 300, Easing.CubicOut);
        }

        private async void listView_ItemTapped(object sender, ItemTappedEventArgs e)
        {

            SessionManager.Instance.UpdateActivity();

            var myListView = (ListView)sender;
            var obj = (ServicesList)e.Item;

            // Add haptic feedback (vibration) on tap
            try
            {
                var duration = TimeSpan.FromMilliseconds(50);
                Xamarin.Essentials.Vibration.Vibrate(duration);
            }
            catch { /* Vibration not supported */ }

            // Simple scale animation on the list view
            await myListView.ScaleTo(0.98, 50, Easing.CubicOut);
            await myListView.ScaleTo(1, 50, Easing.CubicIn);

            // Modern action sheet with enhanced styling
            string action = await DisplayActionSheet(
                $"💰 {obj.ServiceName}",
                "❌ Cancel",
                null,
                "💳 Make Payment",
                "📋 View Details",
                "📤 Share Service"
            );

            if (action == "💳 Make Payment")
            {
                ServiceName = obj.ServiceName;
                ServiceDescription = obj.ServiceDescription;
                ServiceAmount = obj.ServiceAmount;

                // Show confirmation with animation
                bool confirm = await DisplayAlert(
                    "Confirm Payment",
                    $"Proceed to pay ₦{obj.Samount} for {obj.ServiceName}?",
                    "Yes, Continue",
                    "Cancel"
                );

                if (confirm)
                {
                    await Navigation.PushAsync(new Views.Default.Payment());
                }
            }
            else if (action == "📋 View Details")
            {
                await DisplayAlert(
                    "Service Details",
                    $"Name: {obj.ServiceName}\n\nDescription: {obj.ServiceDescription}\n\nAmount: ₦{obj.Samount}",
                    "Close"
                );
            }
            else if (action == "📤 Share Service")
            {
                try
                {
                    await Xamarin.Essentials.Share.RequestAsync(new Xamarin.Essentials.ShareTextRequest
                    {
                        Text = $"Check out this service: {obj.ServiceName} - ₦{obj.Samount}",
                        Title = "Share YIRS Service"
                    });
                }
                catch
                {
                    await DisplayAlert("Info", "Sharing is not available on this device.", "OK");
                }
            }

            // Deselect the item with animation
            myListView.SelectedItem = null;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Refresh activity tracking
            SessionManager.Instance.UpdateActivity();

            // Add subtle scale animation when page appears
            Device.BeginInvokeOnMainThread(async () =>
            {
                listView.Opacity = 0;
                listView.TranslationY = 20;
                await Task.WhenAll(
                    listView.FadeTo(1, 400, Easing.CubicOut),
                    listView.TranslateTo(0, 0, 400, Easing.CubicOut)
                );
            });
        }

        protected override bool OnBackButtonPressed()
        {
            // Add confirmation before going back
            Device.BeginInvokeOnMainThread(async () =>
            {
                bool result = await DisplayAlert("Confirm Exit", "Do you want to go back?", "Yes", "No");
                if (result)
                {
                    await Navigation.PopAsync();
                }
            });

            return true; // Prevent default back button behavior
        }
    }
}