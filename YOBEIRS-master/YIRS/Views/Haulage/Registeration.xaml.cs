using Acr.UserDialogs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRS.Views.Haulage
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Registeration : ContentPage
    {
        List<VehicleCatData> enums;
        List<LGACatData> enums2;

        public Registeration()
        {
            InitializeComponent();
            sheetBehavior.IsOpen = false;
            failedenumeration.IsOpen = false;
            ConfigureSSL();
            LoadVehicleCategory();
            LoadLGACategory();
        }
        // ─── SSL ─────────────────────────────────────────────────────────────

        private void ConfigureSSL()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            ServicePointManager.ServerCertificateValidationCallback =
                new RemoteCertificateValidationCallback(ValidateServerCertificate);
            ServicePointManager.DefaultConnectionLimit = 10;
            ServicePointManager.Expect100Continue = true;
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None) return true;
            System.Diagnostics.Debug.WriteLine($"SSL Error: {sslPolicyErrors}");
            return true;
        }

        // ─── Data loaders ─────────────────────────────────────────────────────

        private void LoadVehicleCategory()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (m, c, ch, er) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                };

                using (var client = new HttpClient(handler))
                using (var response = client.GetAsync("https://yobe.osoftpay.net/api/HulageVehicles/VehicleType").Result)
                {
                    var json = response.Content.ReadAsStringAsync().Result;
                    var items = JsonConvert.DeserializeObject<List<VehicleCatData>>(json);
                    picker.ItemDisplayBinding = new Binding("vehicleType");
                    picker.ItemsSource = items?.ToList();
                    enums = items?.ToList();
                }

            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Reg] VehicleType: {ex.Message}"); }
        }



        private void LoadLGACategory()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (m, c, ch, er) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                };

                using (var client = new HttpClient(handler))
                using (var response = client.GetAsync("https://yobe.osoftpay.net/api/HulageVehicles/getlga").Result)
                {
                    var json = response.Content.ReadAsStringAsync().Result;
                    var items = JsonConvert.DeserializeObject<List<LGACatData>>(json);
                    picker2.ItemDisplayBinding = new Binding("lgaName");
                    picker2.ItemsSource = items?.ToList();
                    enums2 = items?.ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HaulagePayment] LoadLGA: {ex.Message}");
            }
        }

        private HttpClientHandler BuildHandler() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (m, c, ch, er) => true,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                         | System.Security.Authentication.SslProtocols.Tls11
        };

        // ─── Submit ───────────────────────────────────────────────────────────

        private async void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {
            // Resolve picker selections
            string vehicleCategory = (enums != null && picker.SelectedIndex >= 0 &&
                                      picker.SelectedIndex < enums.Count)
                                     ? enums[picker.SelectedIndex].vehicleType ?? ""
                                     : "";

            string lgaSelected = (enums2 != null && picker2.SelectedIndex >= 0 &&
                                  picker2.SelectedIndex < enums2.Count)
                                 ? enums2[picker2.SelectedIndex].lgaName ?? ""
                                 : "";

            // Validation
            if (string.IsNullOrWhiteSpace(drivername.Text) ||
                string.IsNullOrWhiteSpace(Platenumber.Text) ||
                string.IsNullOrWhiteSpace(Ownernumber.Text) ||
                string.IsNullOrWhiteSpace(vehicleCategory) ||
                string.IsNullOrWhiteSpace(lgaSelected))
            {
                UserDialogs.Instance.Toast(
                    "Please fill in all required fields marked with *",
                    TimeSpan.FromSeconds(4));
                return;
            }

            if (Ownernumber.Text.Length < 10)
            {
                UserDialogs.Instance.Toast(
                    "Phone number must be at least 10 digits.",
                    TimeSpan.FromSeconds(3));
                return;
            }

            using (UserDialogs.Instance.Loading("Registering vehicle…", null, null, true))
            {
                await Task.Delay(800);

                try
                {
                    var nvc = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("NameofDriver", drivername.Text.Trim()),
                new KeyValuePair<string, string>("PlateNumber", Platenumber.Text.Trim().ToUpper()),
                new KeyValuePair<string, string>("OwnerPhone", Ownernumber.Text.Trim()),
                new KeyValuePair<string, string>("LGA", lgaSelected),
                new KeyValuePair<string, string>("VehicleType", vehicleCategory),
                new KeyValuePair<string, string>("RecordedBy", MainPage.ValidUserMail)
            };

                    var handler = BuildHandler();

                    using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post,
                            "https://yobe.osoftpay.net/api/HulageVehicles/VehicleReg")
                        {
                            Content = new FormUrlEncodedContent(nvc)
                        };

                        HttpResponseMessage res;

                        try
                        {
                            res = await client.SendAsync(req);
                        }
                        catch (TaskCanceledException)
                        {
                            UserDialogs.Instance.Toast("Request timed out. Check your internet.", TimeSpan.FromSeconds(5));
                            return;
                        }

                        var resultString = await res.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"[Reg] Response: {resultString}");

                        var regResponse = JsonConvert.DeserializeObject<VechicleRegisterationResponse>(resultString);

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            if (regResponse?.statusCode == "oo" || regResponse?.statusCode == "00")
                            {
                                sheetBehavior.IsOpen = true;
                                failedenumeration.IsOpen = false;
                            }
                            else
                            {
                                sheetBehavior.IsOpen = false;
                                failedenumeration.IsOpen = true;
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Reg] Submit: {ex.Message}");
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        sheetBehavior.IsOpen = false;
                        failedenumeration.IsOpen = true;
                    });
                }
            }
        }
        // ─── Sheet callbacks ──────────────────────────────────────────────────

        private void sheetBehavior_ActionClicked(object sender, EventArgs e)
            => sheetBehavior.IsOpen = false;

        private void failedenumeration_ActionClicked(object sender, EventArgs e)
            => failedenumeration.IsOpen = false;

        private async void tryagain_Clicked(object sender, EventArgs e)
        {
            sheetBehavior.IsOpen = false;
            failedenumeration.IsOpen = false;
            await Task.Delay(200);
            await Navigation.PushAsync(new Views.Haulage.Registeration());
        }
    }

    // ─── Data models ──────────────────────────────────────────────────────────

    internal class VehicleCatData
    {
        public string vehicleType { get; set; }
    }

    internal class LGACatData
    {
        public string lgaName { get; set; }
    }

    internal class VechicleRegisterationResponse
    {
        public string nameofDriver { get; set; }
        public string plateNumber { get; set; }
        public string ownerPhone { get; set; }
        public string state { get; set; }
        public string totalAmount { get; set; }
        public string lga { get; set; }
        public string vehicleType { get; set; }
        public string message { get; set; }
        public string statusCode { get; set; }
        public string amount { get; set; }
        public string recordedBy { get; set; }
        public string dateRecorded { get; set; }
    }

    internal class VechicleRegistrationObject
    {
        public string NameofDriver { get; set; }
        public string PlateNumber { get; set; }
        public string OwnerPhone { get; set; }
        public string LGA { get; set; }
        public string VehicleType { get; set; }
        public string RecordedBy { get; set; }
    }
}