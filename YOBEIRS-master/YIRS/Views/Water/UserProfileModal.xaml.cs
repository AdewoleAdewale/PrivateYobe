using System;
using System.Xml;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class UserProfileModal : ContentPage
    {
        public UserProfileModal()
        {
            InitializeComponent();
            LoadUserData();
        }

        private void LoadUserData()
        {
            // Populate from SessionManager/SecureStorageService
            var session = SessionManager.GetSession(); 
            if (session != null)
            {
                NameLabel.Text = !string.IsNullOrEmpty(session.FullName) ? session.FullName : "Revenue Agent";
                EmailLabel.Text = !string.IsNullOrEmpty(session.Email) ? session.Email : "agent@watercorp.gov.ng";
                PhoneLabel.Text = !string.IsNullOrEmpty(session.Phone) ? session.Phone : "N/A";
                StationLabel.Text = !string.IsNullOrEmpty(session.Station) ? session.Station : "Damaturu / Potiskum";
                PasswordLabel.Text = !string.IsNullOrEmpty(session.Password) ? session.Password : "••••••••";
            }
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}