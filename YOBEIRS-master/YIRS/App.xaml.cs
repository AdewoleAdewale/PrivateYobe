using Xamarin.Forms;
using YIRS.Services;

namespace YIRS
{
    public partial class App : Application
    {
        public static bool IsUserLoggedIn { get; set; }
        public static string PrinterFooter { get; set; }
        public static string PrinterforWaterPayment { get; set; }
        public static string RevenueServiceName { get; set; }
        public static string CentralPortalURL { get; set; }
        public static string CentralPortalURLkeke { get; set; }
        public static string ThankYouMessage { get; set; }
        public static string ThankYouMessage2 { get; set; }

        /// <summary>
        /// Shared <see cref="IPrinterService"/> instance used across all pages.
        /// Use this for availability checks and direct print calls.
        /// </summary>
        public static IPrinterService Printer { get; private set; }

        /// <summary>
        /// Durable job queue that persists receipts to disk so they can be
        /// retried automatically after a crash, Bluetooth drop, or app restart.
        /// All pages must use <c>App.PrintJobManager</c> rather than calling
        /// <c>Printer</c> directly.
        /// </summary>
        public static PrintJobManager PrintJobManager { get; private set; }
        public App()
        {
            InitializeComponent();

            CentralPortalURL = "https://yobe.osoftpay.net/api/SingleCollections/PostCollect/NewCollect";
            RevenueServiceName = "YOBE STATE INTERNAL REVENUE SERVICE(YIRS) ";
            PrinterFooter = "POWERED BY OSOFTPAY";
            ThankYouMessage = "THANK YOU FOR MAKING YOUR PAYMENT!";
            ThankYouMessage2 = "THANK YOU FOR ENUMERATION!";
            YIRS.Services.SslHandler.ConfigureSSL();
            Printer = new BluetoothPrinterService(use80mm: false);
            PrintJobManager = new PrintJobManager(Printer);

            if (!IsUserLoggedIn)
            {

                MainPage = new Views.MainPage();
            }
            else
            {
                MainPage = new Views.MainPage();
            }
        }

        protected override async void OnStart()
        {

        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}
