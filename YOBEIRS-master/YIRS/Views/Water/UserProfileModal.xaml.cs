using System;
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
            var session = SessionManager.GetSession();
            if (session != null)
            {
                NameLabel.Text = !string.IsNullOrWhiteSpace(session.FullName) ? session.FullName : "Revenue Agent";
                EmailLabel.Text = !string.IsNullOrWhiteSpace(session.Email) ? session.Email : "adewatercorporation@gmail.com";
                PhoneLabel.Text = !string.IsNullOrWhiteSpace(session.Phone) ? session.Phone : "N/A";
                StationLabel.Text = !string.IsNullOrWhiteSpace(session.Station) ? session.Station : "Unknown";
                PasswordLabel.Text = !string.IsNullOrWhiteSpace(session.Password) ? session.Password : "••••••••";
            }
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}