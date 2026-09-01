using Android.Accounts;
using Android.Locations;
using System;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using YIRS.Models;
using YIRS.Services;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterVerifyConnectionPage : ContentPage
    {
        private readonly WaterService _waterService;
        private readonly WaterPrintSDK _printSDK;

        public WaterVerifyConnectionPage()
        {
            InitializeComponent();
            _waterService = new WaterService();
            _printSDK = new WaterPrintSDK();
        }

        public WaterVerifyConnectionPage(string prefillConnectionNo) : this()
        {
            ConnectionEntry.Text = prefillConnectionNo;
            if (!string.IsNullOrEmpty(prefillConnectionNo))
            {
                OnVerifyClicked(this, EventArgs.Empty);
            }
        }

        private async void OnVerifyClicked(object sender, EventArgs e)
        {
            string connNo = ConnectionEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(connNo))
            {
                await DisplayAlert("Validation", "Please enter a connection number.", "OK");
                return;
            }

            LoadingSpinner.IsVisible = true;
            LoadingSpinner.IsRunning = true;
            StatusCard.IsVisible = false;
            PaymentHistorySection.IsVisible = false;
            PaymentHistoryContainer.Children.Clear();

            try
            {
                // 1. Fetch connection status
                var statusRes = await _waterService.GetConnectionStatusAsync(connNo); 
                if (statusRes != null && statusRes.respondCode == "00") 
                {
                    StatusCard.IsVisible = true;
                    OccupantNameLabel.Text = statusRes.occupant;
                    AddressLabel.Text = statusRes.address; 
                    TariffLabel.Text = $"Tariff: {statusRes.tarifPlan}";
                    MonthlyAmountLabel.Text = $"₦{statusRes.monthlyAmount:N2}";
                    BacklogAmountLabel.Text = $"₦{statusRes.backlogAmount:N2}"; 
                    DueDateLabel.Text = statusRes.dueDate?.ToString("dd-MMM-yyyy") ?? "N/A";
                    MonthsLabel.Text = $"{statusRes.monthsOwedOrAhead} Month(s)";
                    StatusLabel.Text = statusRes.status; 

                    if (statusRes.status == "Owing") 
                    {
                        StatusBadge.BackgroundColor = Color.FromHex("#FDE8E8");
                        StatusLabel.TextColor = Color.FromHex("#C41C1C");
                    }
                    else
                    {
                        StatusBadge.BackgroundColor = Color.FromHex("#E3F7EF");
                        StatusLabel.TextColor = Color.FromHex("#0E8A57");
                    }
                }
                else
                {
                    await DisplayAlert("Not Found", statusRes?.message ?? "Connection does not exist.", "OK");
                    return;
                }

                // 2. Fetch specific client payment history
                var historyRes = await _waterService.GetClientPaymentHistoryAsync(connNo);
                if (historyRes != null && historyRes.respondCode == "00" && historyRes.payments != null && historyRes.payments.Count > 0)
                {
                    PaymentHistorySection.IsVisible = true;
                    foreach (var item in historyRes.payments)
                    {
                        var card = new Frame
                        {
                            BackgroundColor = Color.White,
                            CornerRadius = 10,
                            Padding = new Thickness(12, 10),
                            HasShadow = false,
                            BorderColor = Color.FromHex("#E5E9EB")
                        };

                        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };

                        var left = new StackLayout { Spacing = 2 };
                        left.Children.Add(new Label { Text = $"Tx: {item.transactionId}", FontAttributes = FontAttributes.Bold, FontSize = 12, TextColor = Color.FromHex("#222") });
                        left.Children.Add(new Label { Text = $"Ref: {item.debitRef} • {item.datelIst:dd-MMM-yyyy HH:mm}", FontSize = 10, TextColor = Color.FromHex("#777") });
                        left.Children.Add(new Label { Text = $"Agent: {item.performedBy}", FontSize = 10, TextColor = Color.FromHex("#063E2A") });

                        var right = new StackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
                        right.Children.Add(new Label { Text = $"₦{item.amount:N2}", FontAttributes = FontAttributes.Bold, FontSize = 13, TextColor = Color.FromHex("#0E8A57"), HorizontalTextAlignment = TextAlignment.End });

                        var printBtn = new Button
                        {
                            Text = "🖨 Reprint",
                            BackgroundColor = Color.FromHex("#F0F3F4"),
                            TextColor = Color.FromHex("#333"),
                            FontSize = 10,
                            HeightRequest = 32,
                            Padding = new Thickness(6, 0),
                            CornerRadius = 6
                        };
                        printBtn.Clicked += async (s, args) =>
                        {
                            var mockReceipt = new WaterReceiptResponse
                            {
                                transactionId = item.transactionId,
                                payer = connNo,
                                occupant = statusRes.occupant,
                               
                                address = statusRes.address,
                              
                                amount = item.amount,
                                datelIst = item.datelIst,
                                debitRef = item.debitRef,
                                performedBy = item.performedBy
                            };
                            await _printSDK.PrintPaymentReceiptAsync(mockReceipt, 1);
                        };
                        right.Children.Add(printBtn);

                        grid.Children.Add(left, 0, 0);
                        grid.Children.Add(right, 1, 0);

                        card.Content = grid;
                        PaymentHistoryContainer.Children.Add(card);
                    }
                }
                else
                {
                    PaymentHistorySection.IsVisible = true;
                    PaymentHistoryContainer.Children.Add(new Label { Text = "No prior payments found for this connection.", FontSize = 12, TextColor = Color.FromHex("#777"), Margin = new Thickness(4) });
                }
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
    }
}