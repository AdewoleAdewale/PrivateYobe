using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterFailureSheet : ContentPage
    {
        public WaterFailureSheet(string title, string reason)
        {
            InitializeComponent();
            TitleLabel.Text = title;
            ReasonLabel.Text = reason;
        }

        private async void OnDismissClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}