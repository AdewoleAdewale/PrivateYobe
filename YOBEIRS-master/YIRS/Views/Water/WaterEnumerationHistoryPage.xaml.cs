using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterEnumerationHistoryPage : ContentPage
    {
        private readonly WaterService _waterService;
        private List<WaterEnumerationItem> _allConnections;

        public WaterEnumerationHistoryPage()
        {
            InitializeComponent();
            _waterService = new WaterService();
            LoadHistory();
        }

        private async void LoadHistory()
        {
            try
            {
                var res = await _waterService.GetEnumerationHistoryAsync();
                if (res != null && res.respondCode == "00" && res.connections != null)
                {
                    _allConnections = res.connections;
                    EnumerationListView.ItemsSource = _allConnections;
                }
                else
                {
                    await DisplayAlert("History", res?.message ?? "No connections recorded.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
            finally
            {
                EnumerationListView.IsRefreshing = false;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = e.NewTextValue?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(keyword) || _allConnections == null)
            {
                EnumerationListView.ItemsSource = _allConnections;
            }
            else
            {
                EnumerationListView.ItemsSource = _allConnections.Where(x =>
                    (x.occupant != null && x.occupant.ToLower().Contains(keyword)) ||
                    (x.connectionNo != null && x.connectionNo.ToLower().Contains(keyword)) ||
                    (x.address != null && x.address.ToLower().Contains(keyword))).ToList();
            }
        }

        private async void OnItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.Item is WaterEnumerationItem selected)
            {
                // Open Verify & Payment History page for this connection
                await Navigation.PushAsync(new WaterVerifyConnectionPage(selected.connectionNo));
            }
        }

        private void OnRefreshing(object sender, EventArgs e)
        {
            LoadHistory();
        }
    }
}