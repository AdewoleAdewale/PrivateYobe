using System;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace YIRS.Views.Water
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class WaterSuccessSheet : ContentPage
    {
        private readonly Func<Task> _printAction;

        public WaterSuccessSheet(string title, string message, string refId, string amount, Func<Task> printAction = null)
        {
            InitializeComponent();
            TitleLabel.Text = title;
            MessageLabel.Text = message;
            RefLabel.Text = refId;
            AmountLabel.Text = amount;
            _printAction = printAction;

            if (_printAction == null)
            {
                PrintBtn.IsVisible = false;
            }
        }

        private async void OnPrintClicked(object sender, EventArgs e)
        {
            if (_printAction != null)
            {
                await _printAction.Invoke();
            }
        }

        private async void OnDoneClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}