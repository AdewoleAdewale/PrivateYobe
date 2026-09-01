using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using YIRS.Models;

namespace YIRS.Services
{
    public class WaterService
    {
        private readonly HttpClient _client;
        private const string BaseUrl = "https://yobe.osoftpay.net/Api/WaterMobile/";

        public WaterService()
        {
            _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        }

        public async Task<WaterAreaResponse> GetAreasAsync()
        {
            var res = await _client.GetAsync("areas");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterAreaResponse>(json);
        }

        public async Task<WaterServicesResponse> GetServicesAsync()
        {
            var res = await _client.GetAsync("services");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterServicesResponse>(json);
        }

        public async Task<WaterEnumerateResponse> EnumerateAsync(WaterEnumerateRequest payload)
        {
            var body = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("enumerate", body);
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterEnumerateResponse>(json);
        }

        public async Task<WaterConnectionStatusResponse> GetConnectionStatusAsync(string connectionNo)
        {
            var res = await _client.GetAsync($"GetConnectionStatus?connectionNo={Uri.EscapeDataString(connectionNo)}");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterConnectionStatusResponse>(json);
        }

        public async Task<WaterPaymentResponse> MakePaymentAsync(WaterPaymentRequest payload)
        {
            var body = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("WaterResoursePayment", body);
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterPaymentResponse>(json);
        }

        public async Task<WaterReceiptResponse> GetReceiptAsync(string transactionId)
        {
            var res = await _client.GetAsync($"receipt/{transactionId}");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterReceiptResponse>(json);
        }
    }
}