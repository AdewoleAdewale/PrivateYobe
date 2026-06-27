using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Services;

namespace YIRS.Views.Home
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ConfirmTransaction : ContentPage
    {
        public ConfirmTransaction()
        {
            try
            {
                InitializeComponent();
                LoadTransactionData();
                TrackUserActivity();
            }
            catch (Exception ex)
            {
                HandleInitializationError(ex);
            }
        }
        private void TrackUserActivity()
        {
            // Add tap gesture to main grid to track any user interaction
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => SessionManager.Instance.UpdateActivity();
            this.Content.GestureRecognizers.Add(tapGesture);
        }

        private void LoadTransactionData()
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                // Safely load transaction number
                if (!string.IsNullOrWhiteSpace(Gazette.transactionNo))
                {
                    transactionNo.Text = Gazette.transactionNo;
                }
                else
                {
                    transactionNo.Text = "N/A";
                }

                // Safely load status
                if (!string.IsNullOrWhiteSpace(Gazette.Status))
                {
                    var status = Gazette.Status.ToLower();
                    lblStatus.Text = Gazette.Status;

                    // Set status color based on value
                    if (status.Contains("success") || status.Contains("approved") || status.Contains("completed"))
                    {
                        lblStatus.TextColor = Color.FromHex("#4CAF50"); // Green
                    }
                    else if (status.Contains("pending") || status.Contains("processing"))
                    {
                        lblStatus.TextColor = Color.FromHex("#FF9800"); // Orange
                    }
                    else if (status.Contains("failed") || status.Contains("declined") || status.Contains("rejected"))
                    {
                        lblStatus.TextColor = Color.FromHex("#F44336"); // Red
                    }
                    else
                    {
                        lblStatus.TextColor = Color.FromHex("#2196F3"); // Blue
                    }
                }
                else
                {
                    lblStatus.Text = "Unknown";
                    lblStatus.TextColor = Color.Gray;
                }

                // Safely load payer
                if (!string.IsNullOrWhiteSpace(Gazette.payer))
                {
                    payer.Text = Gazette.payer;
                }
                else
                {
                    payer.Text = "N/A";
                }

                // Safely load amount with formatting
                if (!string.IsNullOrWhiteSpace(Gazette.amount))
                {
                    // Try to parse and format amount
                    if (decimal.TryParse(Gazette.amount.Replace("₦", "").Replace(",", "").Trim(), out decimal amountValue))
                    {
                        amount.Text = $"₦{amountValue:N2}";
                    }
                    else
                    {
                        amount.Text = Gazette.amount;
                    }
                }
                else
                {
                    amount.Text = "₦0.00";
                }

                // Safely load payer contact
                if (!string.IsNullOrWhiteSpace(Gazette.payerContact))
                {
                    payerContact.Text = Gazette.payerContact;
                }
                else
                {
                    payerContact.Text = "N/A";
                }

                // Safely load transaction date with formatting
                if (!string.IsNullOrWhiteSpace(Gazette.transactionDate))
                {
                    // Try to parse and format date
                    if (DateTime.TryParse(Gazette.transactionDate, out DateTime dateValue))
                    {
                        transactionDate.Text = dateValue.ToString("MMM dd, yyyy hh:mm tt");
                    }
                    else
                    {
                        transactionDate.Text = Gazette.transactionDate;
                    }
                }
                else
                {
                    transactionDate.Text = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");
                }

                // Safely load service name
                if (!string.IsNullOrWhiteSpace(Gazette.serviceName))
                {
                    serviceName.Text = Gazette.serviceName;
                }
                else
                {
                    serviceName.Text = "N/A";
                }
            }
            catch (NullReferenceException nex)
            {
                HandleNullReferenceError(nex);
            }
            catch (FormatException fex)
            {
                HandleFormatError(fex);
            }
            catch (Exception ex)
            {
                HandleGeneralError(ex);
            }
        }

        private async void OnDownloadClicked(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                // Disable button to prevent multiple clicks
                if (sender is Button button)
                {
                    button.IsEnabled = false;
                }


                // Re-enable button
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                HandleButtonError(ex, "Download");

                // Re-enable button in case of error
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void OnShareClicked(object sender, EventArgs e)
        {
            try
            {
                SessionManager.Instance.UpdateActivity();
                // Disable button to prevent multiple clicks
                if (sender is Button button)
                {
                    button.IsEnabled = false;
                }


                // Re-enable button
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                HandleButtonError(ex, "Share");

                // Re-enable button in case of error
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                }
            }
        }

        // Error handling methods
        private async void HandleInitializationError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initialization Error: {ex.Message}");
            await DisplayAlert("Error", "Failed to load transaction page. Please try again.", "OK");

            // Try to navigate back or to a safe page
            try
            {
                await Navigation.PopAsync();
            }
            catch
            {
                // If navigation fails, just log it
                System.Diagnostics.Debug.WriteLine("Failed to navigate back after initialization error");
            }
        }

        private async void HandleNullReferenceError(NullReferenceException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Null Reference Error: {ex.Message}");
            await DisplayAlert("Warning", "Some transaction data is missing. Showing available information.", "OK");
        }

        private async void HandleFormatError(FormatException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Format Error: {ex.Message}");
            await DisplayAlert("Warning", "Some transaction data has an invalid format. Displaying as-is.", "OK");
        }

        private async void HandleGeneralError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"General Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
            await DisplayAlert("Error", "An unexpected error occurred while loading transaction data.", "OK");
        }

        private async void HandleButtonError(Exception ex, string buttonName)
        {
            System.Diagnostics.Debug.WriteLine($"{buttonName} Button Error: {ex.Message}");
            await DisplayAlert("Error", $"Failed to execute {buttonName.ToLower()} action. Please try again.", "OK");
        }

        protected override void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                SessionManager.Instance.UpdateActivity();
                // Any additional logic when page appears
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnAppearing Error: {ex.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
                // Cleanup if needed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnDisappearing Error: {ex.Message}");
            }
        }
    }
}