using Android.Accounts;
using Android.Locations;
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
    public partial class WaterHistoryPage : ContentPage
    {
        private readonly WaterPrintSDK _printSDK;
        private List<WaterConnectionStatusResponse> _allHistoryList;

        public WaterHistoryPage()
        {
            InitializeComponent();
            _printSDK = new WaterPrintSDK();
            LoadHistory();
        }

        private void LoadHistory()
        {
            // Sample historical data records matching the API schema
            _allHistoryList = new List<WaterConnectionStatusResponse>
            {
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00002", occupant = "Musa Ibrahim Testing", address = "No 14 Gujba Road", lastPaymentAmount = 1500, lastPaymentDate = DateTime.Now.AddHours(-3), status = "Paid Ahead", tarifPlan = "1-2 Bedroom Flat" },
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00004", occupant = "Fatima Usman", address = "Old GRA, Damaturu", lastPaymentAmount = 2000, lastPaymentDate = DateTime.Now.AddDays(-1), status = "Up to Date", tarifPlan = "3+ Bedroom Flat" },
                new WaterConnectionStatusResponse { connectionNo = "POT/WTC/00018", occupant = "Abubakar Ali", address = "Potiskum Market Area", lastPaymentAmount = 3000, lastPaymentDate = DateTime.Now.AddDays(-2), status = "Paid Ahead", tarifPlan = "Commercial Post" },
                new WaterConnectionStatusResponse { connectionNo = "DTR/WTC/00021", occupant = "Aliyu Mohammed", address = "Bukar Abba Way", lastPaymentAmount = 1000, lastPaymentDate = DateTime.Now.AddDays(-4), status = "Up to Date", tarifPlan = "Single Tap" }
            };

            HistoryListView.ItemsSource = _allHistoryList;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = e.NewTextValue?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(keyword))
            {
                HistoryListView.ItemsSource = _allHistoryList;
            }
            else
            {
                HistoryListView.ItemsSource = _allHistoryList.Where(x =>
                    (x.occupant != null && x.occupant.ToLower().Contains(keyword)) ||
                    (x.connectionNo != null && x.connectionNo.ToLower().Contains(keyword))).ToList();
            }
        }

        private async void OnHistoryItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (e.Item is WaterConnectionStatusResponse selected)
            {
                bool answer = await DisplayAlert("Receipt Options", $"Connection: {selected.connectionNo}\nOccupant: {selected.occupant}\nAmount: ₦{selected.lastPaymentAmount:N2}", "Re-Print Slip", "Close");
                if (answer)
                {
                    var mockReceipt = new WaterReceiptResponse
                    {
                        transactionId = "TXN" + DateTime.Now.Ticks.ToString().Substring(10),
                        payer = selected.connectionNo,
                    
                        occupant = selected.occupant,
                
                        address = selected.address,
                      
                        amount = selected.lastPaymentAmount ?? 0,
                    
                        datelIst = selected.lastPaymentDate ?? DateTime.Now,
                     
                        debitRef = "RE-PRINT-" + selected.connectionNo,
                        performedBy = "adewatercorporation@gmail.com"
                    };

                    await _printSDK.PrintPaymentReceiptAsync(mockReceipt, 1);
                }
            }
        }

        private void OnHistoryRefreshing(object sender, EventArgs e)
        {
            LoadHistory();
            HistoryListView.IsRefreshing = false;
        }
    }
}