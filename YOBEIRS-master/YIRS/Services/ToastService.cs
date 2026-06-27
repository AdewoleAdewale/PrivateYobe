using Xamarin.Forms;

namespace YIRS.Services
{
    public static class ToastService
    {
        public static void ShowToast(string message, int durationMs = 3000)
        {
            DependencyService.Get<IToastService>()?.ShowToast(message, durationMs);
        }
    }

    public interface IToastService
    {
        void ShowToast(string message, int durationMs);
    }
}
