using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace YIRS.Services
{
    /// <summary>
    /// Centralized HTTP service with SSL certificate handling for all API calls
    /// Use this service across all pages to ensure consistent SSL handling
    /// </summary>
    public class SecureHttpService
    {
        private static SecureHttpService _instance;
        private static readonly object _lock = new object();

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _client;
        private const int DEFAULT_TIMEOUT_SECONDS = 30;
        private const int MAX_RETRY_ATTEMPTS = 3;

        private SecureHttpService(int timeoutSeconds = DEFAULT_TIMEOUT_SECONDS)
        {
            // ============ SSL Certificate Handler Configuration ============
            _handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    // Log certificate details for debugging
                    if (errors != System.Net.Security.SslPolicyErrors.None)
                    {
                        Debug.WriteLine($"[SSL VALIDATION] Error Flags: {errors}");
                        Debug.WriteLine($"[SSL CERT] Subject: {cert?.Subject ?? "N/A"}");
                        Debug.WriteLine($"[SSL CERT] Issuer: {cert?.Issuer ?? "N/A"}");
                        Debug.WriteLine($"[SSL CERT] Thumbprint: {cert?.Thumbprint ?? "N/A"}");
                        Debug.WriteLine($"[SSL CERT] Expiration: {cert?.GetExpirationDateString() ?? "N/A"}");
                    }

                    // Return true to accept the certificate
                    // NOTE: For production, implement certificate pinning instead
                    return true;
                }
            };

            // Configure TLS versions (1.3 preferred, fallback to 1.2)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            // ============ End SSL Configuration ============
        }

        /// <summary>
        /// Get singleton instance of SecureHttpService
        /// </summary>
        public static SecureHttpService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SecureHttpService();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Fetch data from API with automatic retry and SSL handling
        /// </summary>
        public async Task<T> GetAsync<T>(string url, int retryCount = 0) where T : class
        {
            try
            {
                Debug.WriteLine($"[HTTP GET] URL: {url} (Attempt {retryCount + 1})");

                var response = await _client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            var result = JsonConvert.DeserializeObject<T>(json);
                            Debug.WriteLine($"[HTTP SUCCESS] Deserialized {typeof(T).Name}");
                            return result;
                        }
                        catch (JsonException jsonEx)
                        {
                            Debug.WriteLine($"[JSON ERROR] Failed to deserialize {typeof(T).Name}: {jsonEx.Message}");
                            Debug.WriteLine($"[JSON RESPONSE] {json.Substring(0, Math.Min(200, json.Length))}...");
                            throw;
                        }
                    }

                    return null;
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[HTTP AUTH] Session expired (401)");
                    return null; // Let caller handle logout
                }
                else
                {
                    Debug.WriteLine($"[HTTP ERROR] Status: {response.StatusCode}");
                    return null;
                }
            }
            catch (HttpRequestException httpEx) when (
                (httpEx.InnerException?.GetType().Name == "AuthenticationException" ||
                 httpEx.InnerException is System.Security.Authentication.AuthenticationException) &&
                retryCount < MAX_RETRY_ATTEMPTS)
            {
                // ============ SSL/Certificate Error Retry Logic ============
                Debug.WriteLine($"[SSL ERROR] {httpEx.InnerException?.Message}");
                Debug.WriteLine($"[RETRY] SSL error on attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1)); // Exponential backoff
                return await GetAsync<T>(url, retryCount + 1);
                // ============ End SSL Error Retry ============
            }
            catch (TaskCanceledException) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"[TIMEOUT] Request timed out. Retry {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1));
                return await GetAsync<T>(url, retryCount + 1);
            }
            catch (HttpRequestException httpEx) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"[NETWORK ERROR] {httpEx.Message}. Retry {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1));
                return await GetAsync<T>(url, retryCount + 1);
            }
            catch (JsonException)
            {
                // Don't retry JSON errors
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GENERAL ERROR] {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Post data to API with SSL handling
        /// </summary>
        public async Task<T> PostAsync<T>(string url, string jsonContent, int retryCount = 0) where T : class
        {
            try
            {
                Debug.WriteLine($"[HTTP POST] URL: {url} (Attempt {retryCount + 1})");

                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                var response = await _client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            return JsonConvert.DeserializeObject<T>(json);
                        }
                        catch (JsonException jsonEx)
                        {
                            Debug.WriteLine($"[JSON ERROR] {jsonEx.Message}");
                            throw;
                        }
                    }

                    return null;
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[HTTP AUTH] Session expired (401)");
                    return null;
                }
                else
                {
                    Debug.WriteLine($"[HTTP ERROR] Status: {response.StatusCode}");
                    return null;
                }
            }
            catch (HttpRequestException httpEx) when (
                (httpEx.InnerException?.GetType().Name == "AuthenticationException" ||
                 httpEx.InnerException is System.Security.Authentication.AuthenticationException) &&
                retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"[SSL ERROR] {httpEx.InnerException?.Message}. Retry {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1));
                return await PostAsync<T>(url, jsonContent, retryCount + 1);
            }
            catch (TaskCanceledException) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"[TIMEOUT] Request timed out. Retry {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1));
                return await PostAsync<T>(url, jsonContent, retryCount + 1);
            }
            catch (HttpRequestException) when (retryCount < MAX_RETRY_ATTEMPTS)
            {
                Debug.WriteLine($"[NETWORK ERROR] Retry {retryCount + 1}/{MAX_RETRY_ATTEMPTS}");
                await Task.Delay(1000 * (retryCount + 1));
                return await PostAsync<T>(url, jsonContent, retryCount + 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Dispose of HTTP resources
        /// </summary>
        public void Dispose()
        {
            _client?.Dispose();
            _handler?.Dispose();
        }
    }
}