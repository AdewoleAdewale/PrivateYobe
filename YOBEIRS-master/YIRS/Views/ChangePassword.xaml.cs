using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ChangePassword : ContentPage
    {
        public ChangePassword()
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
        private async void TapGestureRecognizer_Tapped_1(object sender, EventArgs e)
        {
            //change password
            SessionManager.Instance.UpdateActivity();
            using (UserDialogs.Instance.Loading("Connecting to Service, Please Wait...", null, null, true))
            {
                await Task.Delay(2000);

                //Connect to cloud and retrieve email and password combination
                string url = "https://yobe.osoftpay.net/api/TaskPayers/ChangePassword?UserName=" + MainPage.ValidUserMail + "&NewPassword=" + ConfirmPassword.Text;

                try
                {

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
                                    InterfacePass result = JsonConvert.DeserializeObject<InterfacePass>
                                        (json);

                                    if (result != null)
                                    {
                                        if (result.status == "00")
                                        {
                                            App.IsUserLoggedIn = false;
                                            await DisplayAlert("NOTIFICATION", "Password Change Successful. Please Login Again!", "OKAY");
                                            Application.Current.MainPage = new NavigationPage(new Views.MainPage());
                                        }
                                        else
                                        {
                                            await DisplayAlert("NOTIFICATION", "Error, Password was not changed!", "OKAY");

                                        }
                                    }
                                    else
                                    {
                                        await DisplayAlert("NOTIFICATION", "Connection Failed", "OKAY");
                                    }
                                }
                            }
                        }

                    }
                }
                catch (Exception exe)
                {
                    await DisplayAlert("NOTIFICATION", "Check your Internet", "OKAY");
                    exe.ToString();
                }

            }
        }
    }


    internal class InterfacePass
    {
        public string MerchantSubUser { get; set; }

        public string status { get; set; }

        public string Password { get; set; }

        public string PhoneNumber { get; set; }

        public string FullName { get; set; }
    }
}