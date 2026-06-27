using System;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class CustomAlertSheet : ContentPage
    {
        public event EventHandler Dismissed;

        public enum AlertType
        {
            Info,
            Success,
            Warning,
            Error
        }

        public CustomAlertSheet(string title, string message, string buttonText = "OK", AlertType alertType = AlertType.Info)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);

            // Set alert details
            TitleLabel.Text = title?.ToUpper() ?? "NOTIFICATION";
            MessageLabel.Text = message ?? "";
            ActionButton.Text = buttonText ?? "OK";

            // Set colors and icon based on alert type
            SetAlertStyle(alertType);

            // Animate entry
            AnimateEntry();
        }

        private void SetAlertStyle(AlertType alertType)
        {
            switch (alertType)
            {
                case AlertType.Success:
                    HeaderBar.BackgroundColor = Color.FromHex("#4CAF50");
                    IconFrame.BackgroundColor = Color.FromHex("#E8F5E9");
                    IconLabel.Text = "✓";
                    ActionButton.BackgroundColor = Color.FromHex("#4CAF50");
                    break;

                case AlertType.Warning:
                    HeaderBar.BackgroundColor = Color.FromHex("#FF9800");
                    IconFrame.BackgroundColor = Color.FromHex("#FFF3E0");
                    IconLabel.Text = "⚠️";
                    ActionButton.BackgroundColor = Color.FromHex("#FF9800");
                    break;

                case AlertType.Error:
                    HeaderBar.BackgroundColor = Color.FromHex("#F44336");
                    IconFrame.BackgroundColor = Color.FromHex("#FFEBEE");
                    IconLabel.Text = "✕";
                    ActionButton.BackgroundColor = Color.FromHex("#F44336");
                    break;

                case AlertType.Info:
                default:
                    HeaderBar.BackgroundColor = Color.FromHex("#2196F3");
                    IconFrame.BackgroundColor = Color.FromHex("#E3F2FD");
                    IconLabel.Text = "ℹ️";
                    ActionButton.BackgroundColor = Color.FromHex("#2196F3");
                    break;
            }
        }

        private async void AnimateEntry()
        {
            AlertContainer.Scale = 0.8;
            AlertContainer.Opacity = 0;

            await Task.WhenAll(
                AlertContainer.FadeTo(1, 200),
                AlertContainer.ScaleTo(1, 200, Easing.SpringOut)
            );
        }

        private async
        Task
AnimateExit()
        {
            await Task.WhenAll(
                AlertContainer.FadeTo(0, 150),
                AlertContainer.ScaleTo(0.8, 150, Easing.CubicIn)
            );
        }

        private async void OnActionClicked(object sender, EventArgs e)
        {
            await AnimateExit();
            await Navigation.PopModalAsync(false);
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private async Task OnBackgroundTapped(object sender, EventArgs e)
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

        private void TapGestureRecognizer_Tapped(object sender, EventArgs e)
        {

        }
    }
}