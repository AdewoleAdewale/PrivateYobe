using Android.Locations;
using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;
using static Android.Hardware.Camera;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterRegistration : ContentPage
    {
        private readonly WaterService _waterService;
        private readonly WaterPrintSDK _printSDK;

        public WaterRegistration()
        {
            InitializeComponent();
            _waterService = new WaterService();
            _printSDK = new WaterPrintSDK();
            LoadDropdowns();
        }

        private async void LoadDropdowns()
        {
            LoadingSpinner.IsVisible = true;
            LoadingSpinner.IsRunning = true;

            try
            {
                var areaRes = await _waterService.GetAreasAsync();
                if (areaRes?.respondCode == "00") AreaPicker.ItemsSource = areaRes.areas; 

                var serviceRes = await _waterService.GetServicesAsync();
                if (serviceRes?.respondCode == "00") ServicePicker.ItemsSource = serviceRes.services; 
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Could not load areas or tariff plans: " + ex.Message, "OK");
            }
            finally
            {
                LoadingSpinner.IsVisible = false;
                LoadingSpinner.IsRunning = false;
            }
        }

        private void OnServiceSelected(object sender, EventArgs e)
        {
            if (ServicePicker.SelectedItem is WaterServiceTariff selected)
            {
                EstimatedRateLabel.Text = $"Estimated Rate: ₦{selected.amount:N2} / month"; 
            }
        }

        private async void OnSubmitRegistrationClicked(object sender, EventArgs e)
        {
            var selectedArea = AreaPicker.SelectedItem as WaterArea;
            var selectedService = ServicePicker.SelectedItem as WaterServiceTariff;

            if (string.IsNullOrWhiteSpace(OccupantEntry.Text) || string.IsNullOrWhiteSpace(PhoneEntry.Text) ||
                string.IsNullOrWhiteSpace(AddressEntry.Text) || selectedArea == null || selectedService == null)
            {
                await DisplayAlert("Validation Error", "Please fill in all required fields (*).", "OK");
                return;
            }

            SubmitBtn.IsEnabled = false;
            LoadingSpinner.IsVisible = true;
            LoadingSpinner.IsRunning = true;

            var req = new WaterEnumerateRequest
            {
                occupant = OccupantEntry.Text.Trim(),
              
                phone = PhoneEntry.Text.Trim(),
             
                email = EmailEntry.Text?.Trim() ?? "",
              
                address = AddressEntry.Text.Trim(),
              
                flatNo = FlatNoEntry.Text?.Trim() ?? "",
        
                lga = LgaEntry.Text?.Trim() ?? "",
             
                location = LocationEntry.Text?.Trim() ?? "",
             
                areaId = selectedArea.id,
            
                serviceId = selectedService.id,
               
                recordedBy = "adewatercorporation@gmail.com" // Replace with SessionManager email[cite: 1, 2]
            };

            var res = await _waterService.EnumerateAsync(req);

            LoadingSpinner.IsVisible = false;
            LoadingSpinner.IsRunning = false;
            SubmitBtn.IsEnabled = true;

            if (res != null && res.respondCode == "00") 
            {
                await DisplayAlert("Registration Complete", $"Customer ID: {res.connectionNo}\nMonthly Due: ₦{res.amount:N2}", "Print Slip"); 

                // Print Registration Slip via SDK
                bool printed = await _printSDK.PrintRegistrationReceiptAsync(res, req, selectedArea.name, selectedService.serviceName);
                if (!printed)
                {
                    await DisplayAlert("Printer Notice", "Registration saved, but Bluetooth printer was unavailable.", "OK");
                }

                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Registration Failed", res?.message ?? "Failed to save registration.", "OK");
            }
        }
    }
}