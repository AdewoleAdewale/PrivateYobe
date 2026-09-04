using System;
using System.Collections.Generic;
using System.Threading;
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
        private readonly BluetoothPrinterService _printerService;

        public WaterRegistration()
        {
            InitializeComponent();
            _waterService = new WaterService();
            _printerService = new BluetoothPrinterService(use80mm: false);
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
                await DisplayAlert("Error", ex.Message, "OK");
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
                string agentEmail = SessionManager.GetSession()?.Email ?? "agent@watercorp.gov.ng";

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
                    recordedBy = agentEmail
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
                            // 1. Build ReceiptData required by your BluetoothPrinterService
                            var receiptData = new ReceiptData
                            {
                                StoreName = "YOBE STATE INTERNAL REVENUE SERVICE",
                                StoreSubTitle = "ENUMERATION SLIP",
                                ReceiptNumber = res.connectionNo,
                                AgentName = agentEmail,
                                CollectionPoint = MainPage.CollectionPoint,
                                AmountPaid = res.amount,
                                Items = new List<ReceiptItem>
                                {
                                    new ReceiptItem { Description = "Occupant", SubText = req.occupant, Amount = 0 },
                                    new ReceiptItem { Description = "Phone", SubText = req.phone, Amount = 0 },
                                    new ReceiptItem { Description = "Tariff", SubText = selectedService.serviceName, Amount = res.amount }
                                },
                                FooterLine1 = "KEEP THIS CONNECTION NUMBER",
                                FooterLine2 = "POWERED BY OSOFTPAY"
                            };

                            // 2. Print using existing Service
                            await _printerService.PrintReceiptAsync(receiptData, "Logo.png", "YOBE IRS", null, null, default(CancellationToken));
                        }));

                    OccupantEntry.Text = ""; PhoneEntry.Text = ""; AddressEntry.Text = ""; FlatNoEntry.Text = "";
                }
                else
                {
                    await Navigation.PushModalAsync(new WaterFailureSheet("Registration Failed", res?.message ?? "Failed to save registration."));
                }
            }
            catch (Exception ex)
            {
                await Navigation.PushModalAsync(new WaterFailureSheet("Error", ex.Message));
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