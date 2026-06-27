using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Gazette : ContentPage
    {
        public static string transactionNo { get; set; }
        public static string serviceName { get; set; }
        public static string Status { get; set; }
        public static string payer { get; set; }

        public static string amount { get; set; }

        public static string payerContact { get; set; }

        public static string transactionDate { get; set; }



        public Gazette()
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
            using (IProgressDialog progress = UserDialogs.Instance.Progress("Connecting..", null, null, true, MaskType.Gradient))
            {
                for (int i = 0; i < 100; i++)
                {
                    progress.PercentComplete = i;
                    await Task.Delay(60);
                }

                if (Search.Text == null)
                {
                    await DisplayActionSheet("NOTIFICATION", "Kindly Fill in the right details", "THANKYOU");
                    Application.Current.MainPage = new NavigationPage(new Views.Home.Gazette());
                }
                //PIN FORCE CHANGE
                else if (Search.Text != null)
                {
                    string url = "https://yobe.osoftpay.net/api/TaskPayers/ConfirmTransaction?TransactionId=" + Search.Text;

                    using (HttpClient client = new HttpClient())
                    {
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        using (HttpResponseMessage response = client.GetAsync(url).Result)
                        {
                            using (HttpContent content = response.Content)
                            {
                                var json = content.ReadAsStringAsync().Result;
                                ConfirmationResponse result = JsonConvert.DeserializeObject<ConfirmationResponse>
                                    (json);

                                if (result != null)
                                {
                                    if (result.Status == "Approved Successful")
                                    {
                                        transactionNo = result.transactionNo;

                                        serviceName = result.serviceName;


                                        amount = result.amount;

                                        payer = result.payer;

                                        payerContact = result.payerContact;

                                        transactionDate = result.transactionDate;

                                        Status = result.Status;

                                        await Navigation.PushAsync(new Views.Home.ConfirmTransaction());

                                    }
                                    else
                                    {
                                        await Application.Current.MainPage.DisplayAlert("NOTIFICATION", result.Status, "OKAY");

                                    }
                                }
                                else
                                {
                                    await Application.Current.MainPage.DisplayAlert("NOTIFICATION", result.Status, "OKAY");
                                }
                            }
                        }
                    }
                }




            }

        }

        internal class ConfirmationResponse
        {


            public string transactionNo { get; set; }

            public string serviceName { get; set; }

            public string amount { get; set; }

            public string Status { get; set; }
            public string payer { get; set; }

            public string PayRef { get; set; }

            public BusinessNames business { get; set; }

            public string payerContact { get; set; }
            public string transactionDate { get; set; }
        }

        internal class BusinessNames
        {
            public string sUperAgent { get; set; }

            public string lga { get; set; }

            public string zonalOffice { get; set; }

            public string ato { get; set; }
            public string businessName { get; set; }


        }
    }
}