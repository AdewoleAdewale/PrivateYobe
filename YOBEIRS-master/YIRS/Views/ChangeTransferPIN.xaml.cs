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
    public partial class ChangeTransferPIN : ContentPage
    {
        public ChangeTransferPIN()
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
            //change pin
            using (UserDialogs.Instance.Loading("Connecting to Service, Please Wait...", null, null, true))
            {
                SessionManager.Instance.UpdateActivity();
                await Task.Delay(2000);
                if (OldPINEntry.Text == MainPage.Pin)
                {
                    //Connect to cloud and retrieve email and password combination
                    string url = "https://yobe.osoftpay.net/api/TaskPayers/ChangePin?UserName=" + MainPage.ValidUserMail + "&NewPin=" + ConfirmPIN.Text;
                    try
                    {
                        // ============ SSL FIX FOR CHANGE PIN: Add HttpClientHandler ============
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
                                        InterfacePass result = JsonConvert.DeserializeObject<InterfacePass>(json);

                                        if (result != null)
                                        {
                                            if (result.status == "00")
                                            {
                                                App.IsUserLoggedIn = false;
                                                await DisplayAlert("NOTIFICATION", "Pin Change Successful. Please Login Again!", "OKAY");
                                                Application.Current.MainPage = new NavigationPage(new Views.MainPage());
                                            }
                                            else
                                            {
                                                await DisplayAlert("NOTIFICATION", "Error, PIN was not changed!", "OKAY");
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
                        // ============ End SSL Fix ============
                    }
                    catch (Exception exe)
                    {
                        await DisplayAlert("NOTIFICATION", "Check your Internet", "OKAY");
                        exe.ToString();
                    }
                }
                else
                {
                    await DisplayAlert("NOTIFICATION", "Can't Confirm Your Old Pin Please Try Again", "OKAY");
                }
            }
        }
    }
}