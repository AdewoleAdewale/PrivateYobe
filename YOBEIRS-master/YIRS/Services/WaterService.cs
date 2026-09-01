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
            var handler = SslHandler.GetInsecureHandler();
            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        // 1. Get Area Offices
        public async Task<WaterAreaResponse> GetAreasAsync()
        {
            var res = await _client.GetAsync("areas");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterAreaResponse>(json);
        }

        // 2. Get Tariff Services
        public async Task<WaterServicesResponse> GetServicesAsync()
        {
            var res = await _client.GetAsync("services");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterServicesResponse>(json);
        }

        // 3. Enumerate New Connection
        public async Task<WaterEnumerateResponse> EnumerateAsync(WaterEnumerateRequest payload)
        {
            var body = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("enumerate", body);
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterEnumerateResponse>(json);
        }

        // 4. Get Live Enumeration History
        public async Task<WaterEnumerationHistoryResponse> GetEnumerationHistoryAsync()
        {
            var res = await _client.GetAsync("EnumerationHistory");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterEnumerationHistoryResponse>(json);
        }

        // 5. Connection Status
        public async Task<WaterConnectionStatusResponse> GetConnectionStatusAsync(string connectionNo)
        {
            var res = await _client.GetAsync($"GetConnectionStatus?connectionNo={Uri.EscapeDataString(connectionNo)}");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterConnectionStatusResponse>(json);
        }

        // 6. Client Specific Payment History
        public async Task<WaterPaymentHistoryResponse> GetClientPaymentHistoryAsync(string connectionNo)
        {
            var res = await _client.GetAsync($"History?connectionNo={Uri.EscapeDataString(connectionNo)}");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterPaymentHistoryResponse>(json);
        }

        // 7. Make Payment
        public async Task<WaterPaymentResponse> MakePaymentAsync(WaterPaymentRequest payload)
        {
            var body = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await _client.PostAsync("WaterResoursePayment", body);
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterPaymentResponse>(json);
        }

        // 8. Get Single Receipt
        public async Task<WaterReceiptResponse> GetReceiptAsync(string transactionId)
        {
            var res = await _client.GetAsync($"receipt/{transactionId}");
            var json = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WaterReceiptResponse>(json);
        }
    }
}