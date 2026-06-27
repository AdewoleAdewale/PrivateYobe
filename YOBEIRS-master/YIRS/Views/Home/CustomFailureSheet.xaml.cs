using System;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class CustomFailureSheet : ContentPage
    {
        public event EventHandler Retry;
        public event EventHandler Dismissed;

        public CustomFailureSheet(string title, string errorMessage)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);

            // Set error details
            TitleLabel.Text = title?.ToUpper() ?? "TRANSACTION FAILED";
            MessageLabel.Text = errorMessage ?? "An error occurred. Please try again.";
            DateLabel.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            // Animate entry
            AnimateEntry();
        }

        private async void AnimateEntry()
        {
            SheetContainer.TranslationY = 600;
            await SheetContainer.TranslateTo(0, 0, 300, Easing.CubicOut);
        }

        private async Task AnimateExit()
        {
            await SheetContainer.TranslateTo(0, 600, 250, Easing.CubicIn);
        }

        private async void OnRetryClicked(object sender, EventArgs e)
        {
            Retry?.Invoke(this, EventArgs.Empty);
            await AnimateExit();
            await Navigation.PopModalAsync(false);
        }

        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await AnimateExit();
            await Navigation.PopModalAsync(false);
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private async void OnBackgroundTapped(object sender, EventArgs e)
        {
            await AnimateExit();
            await Navigation.PopModalAsync(false);
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                await AnimateExit();
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            });
            return true;
        }
    }
}