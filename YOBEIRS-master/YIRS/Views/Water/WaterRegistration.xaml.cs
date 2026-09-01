using Android.Locations;
using System;
using System.Net;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;

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
                await Navigation.PushModalAsync(new WaterFailureSheet("Validation Error", "Please fill in all required fields (*)."));
                return;
            }

            SubmitBtn.IsEnabled = false;
            LoadingSpinner.IsVisible = true;
            LoadingSpinner.IsRunning = true;

            try
            {
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
                  
        
                    recordedBy = SessionManager.GetSession()?.Email ?? "agent@watercorp.gov.ng"
                };

                var res = await _waterService.EnumerateAsync(req);

                if (res != null && res.respondCode == "00") 
        {
                    await Navigation.PushModalAsync(new WaterSuccessSheet(
                        "Registration Successful",
                        "Customer connection has been created.",
                        res.connectionNo, 
        
                        $"₦{res.amount:N2} / mo",
        
                        async () =>
                        {
                            await _printSDK.PrintRegistrationReceiptAsync(res, req, selectedArea.name, selectedService.serviceName);
                        }));

                    // Clear inputs
                    OccupantEntry.Text = "";
                    PhoneEntry.Text = "";
                    AddressEntry.Text = "";
                }
        else
                {
                    await Navigation.PushModalAsync(new WaterFailureSheet("Registration Failed", res?.message ?? "Failed to save registration."));
                }
            }
            catch (Exception ex)
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Network/SSL Error", ex.Message));
            }
            finally
            {
                LoadingSpinner.IsVisible = false;
                LoadingSpinner.IsRunning = false;
                SubmitBtn.IsEnabled = true;
            }
        }
    }
}