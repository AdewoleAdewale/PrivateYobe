using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class CustomSuccessSheet : ContentPage
    {
        public event EventHandler PrintRequested;
        public event EventHandler Dismissed;

        public CustomSuccessSheet(DirectPayment.StateCollectionResponseObject response, decimal amount,
                                 string serviceName, string businessName)
        {
            try
            {
                InitializeComponent();
                NavigationPage.SetHasNavigationBar(this, false);

                // Add debug logging
                System.Diagnostics.Debug.WriteLine("CustomSuccessSheet constructor called");
                System.Diagnostics.Debug.WriteLine($"Transaction: {response?.TransactionNo}");
                System.Diagnostics.Debug.WriteLine($"Amount: {amount}");
                System.Diagnostics.Debug.WriteLine($"Service: {serviceName}");
                System.Diagnostics.Debug.WriteLine($"Business: {businessName}");

                // Validate inputs
                if (response == null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Response is null in constructor");
                    throw new ArgumentNullException(nameof(response));
                }

                // Set transaction details with null safety
                TransactionNoLabel.Text = response.TransactionNo ?? "N/A";
                AmountLabel.Text = $"₦{amount:N2}";
                ServiceLabel.Text = serviceName ?? "N/A";
                BusinessLabel.Text = businessName ?? "N/A";
                DateLabel.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                MessageLabel.Text = response.Message ?? "Transaction completed successfully";

                System.Diagnostics.Debug.WriteLine("All labels set successfully");

                // Animate entry
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await AnimateEntry();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in CustomSuccessSheet constructor: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task AnimateEntry()
        {
            try
            {
                SheetContainer.TranslationY = 800;
                await SheetContainer.TranslateTo(0, 0, 300, Easing.CubicOut);
                System.Diagnostics.Debug.WriteLine("Animation completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Animation error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task AnimateExit()
        {
            try
            {
                await SheetContainer.TranslateTo(0, 800, 250, Easing.CubicIn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exit animation error: {ex.Message}");
            }
        }

        private async void OnPrintClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Print button clicked");
                PrintRequested?.Invoke(this, EventArgs.Empty);
                await AnimateExit();
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Print click error: {ex.Message}");
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Done button clicked");
                await AnimateExit();
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Done click error: {ex.Message}");
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }

        private async void OnBackgroundTapped(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Background tapped");
                await AnimateExit();
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Background tap error: {ex.Message}");
                await Navigation.PopModalAsync(false);
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Back button pressed");
                    await AnimateExit();
                    await Navigation.PopModalAsync(false);
                    Dismissed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Back button error: {ex.Message}");
                    await Navigation.PopModalAsync(false);
                    Dismissed?.Invoke(this, EventArgs.Empty);
                }
            });
            return true;
        }
    }
}