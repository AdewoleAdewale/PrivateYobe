using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Gazette2 : ContentPage
    {
        class ServicesList
        {
            public string businessName { get; set; }
            public string renewal { get; set; }
            public string regAmt { get; set; }
        }

        class HistoryDataHeaderFooter
        {
            public List<ServicesList> HD { get; set; }
            public decimal Size { get { return HD.Count; } }
        }

        public Gazette2()
        {
            InitializeComponent();
            TrackUserActivity();
            string url = "https://yobe.osoftpay.net/api/taskpayers/GetBizCat";
            try
            {
                // ============ SSL FIX FOR GAZETTE2: Add HttpClientHandler ============
                using (var httpClientHandler = new HttpClientHandler())
                {
                    // CRITICAL FIX: Add ServerCertificateCustomValidationCallback for new SSL certificate
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
                                List<ServicesList> items = JsonConvert.DeserializeObject<List<ServicesList>>(text);
                                var SortedGazette = items.OrderBy(x => x.businessName).ToList();
                                //Set the ItemsSource with the ordered contacts
                                listView.ItemsSource = SortedGazette;
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

        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

    }
}