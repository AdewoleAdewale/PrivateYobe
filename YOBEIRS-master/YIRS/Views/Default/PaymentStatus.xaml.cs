using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Default
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class PaymentStatus : ContentPage
    {
        class HistoryData
        {
            public string businessName { get; set; }
            public string bizCategory { get; set; }
            public string address { get; set; }
            public string businessOwner { get; set; }
            public string lga { get; set; }
            public string tin { get; set; }
            public string phoneNumber { get; set; }
            public string email { get; set; }
            public string payerId { get; set; }
        }

        class HistoryDataHeaderFooter
        {
            public List<HistoryData> HD { get; set; }
            public string Intro { get { return " Total Businesses " + HD.Count; } }
            public string Summary { get { return " Total Businesses " + HD.Count; } }
            public decimal Size { get { return HD.Count; } }
        }

        public PaymentStatus()
        {
            InitializeComponent();
            TrackUserActivity();
        }

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }



        private async void Button_Clicked(object sender, EventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            //Search Business
            using (UserDialogs.Instance.Loading("Connecting to Service, Please Wait...", null, null, true))
            {
                await Task.Delay(1500);
                //call osoftpay for agent list
                string url = "https://yobe.osoftpay.net/api/taskpayers/Getbiz/" + Search.Text.Trim();
                try
                {
                    // ============ SSL FIX FOR PAYMENT STATUS: Add HttpClientHandler ============
                    using (var httpClientHandler = new HttpClientHandler())
                    {
                        // CRITICAL FIX: Add this for new SSL certificate validation
                        httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                        {
                            // Allow connection to new SSL certificate
                            return true;
                        };

                        using (HttpClient client = new HttpClient(httpClientHandler))
                        {
                            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                            using (HttpResponseMessage response = client.GetAsync(url).Result)
                            {
                                using (HttpContent content = response.Content)
                                {
                                    var json = content.ReadAsStringAsync().Result;
                                    MemoryStream memStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                                    // convert to string
                                    StreamReader reader = new StreamReader(memStream);
                                    string text = reader.ReadToEnd();
                                    List<HistoryData> items = JsonConvert.DeserializeObject<List<HistoryData>>(text);
                                    BindingContext = new HistoryDataHeaderFooter { HD = items };
                                }
                            }
                        }
                    }
                    // ============ End SSL Fix ============
                }
                catch (Exception exe)
                {
                    exe.ToString();
                }
            }
        }

        private async void listView_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            SessionManager.Instance.UpdateActivity();
            //payid.IsVisible = true;
            var obj = (HistoryData)e.Item;
            // Initial properties Set
            await Clipboard.SetTextAsync(obj.payerId);
            if (Clipboard.HasText)
            {
                await DisplayAlert("NOTIFICATION", "Payer Id: " + obj.payerId, "OKAY");
            }
        }
    }
}