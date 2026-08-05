using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace YIRS.Views.Livestock
{
    // ══════════════════════════════════════════════════════════════════
    //  TRANSACTION SERVICE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Single place that talks to <c>GET /api/Agents/GetAgentTransactions</c>.
    ///
    /// The Dashboard (last five), History (full range) and Verify (lookup by ID) all go
    /// through here so the date format, the response-code check and the model shape exist
    /// in exactly one place. Duplicating that across three pages is how contract drift
    /// starts.
    /// </summary>
    public static class LivestockTransactionService
    {
        private const string TransactionsUrl =
            "https://yobe.osoftpay.net/api/Agents/GetAgentTransactions";

        // One static client for the whole module — Android exhausts sockets otherwise.
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (m, c, ch, errors) =>
                {
                    if (errors != System.Net.Security.SslPolicyErrors.None)
                        System.Diagnostics.Debug.WriteLine("[Livestock:SSL] " + errors);

                    return true;
                }
            };

            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            ServicePointManager.CheckCertificateRevocationList = false;
            ServicePointManager.DefaultConnectionLimit = 10;

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        }

        /// <summary>
        /// Fetches transactions for the signed-in agent between two dates (inclusive).
        ///
        /// Dates are formatted with <see cref="CultureInfo.InvariantCulture"/> on purpose.
        /// A plain <c>ToString("M/d/yyyy")</c> picks up the device culture, so a phone set to
        /// a locale with a different date separator silently sends a URL the server cannot
        /// parse — and it only reproduces on that agent's handset.
        /// </summary>
        public static async Task<TransactionApiResponse> GetAsync(
            DateTime fromDate,
            DateTime toDate,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string email = (MainPage.ValidUserMail ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("No signed-in agent email.");

            string url = TransactionsUrl +
                "?agentEmail=" + Uri.EscapeDataString(email) +
                "&fromDate=" + Uri.EscapeDataString(fromDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture)) +
                "&toDate=" + Uri.EscapeDataString(toDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture));

            System.Diagnostics.Debug.WriteLine("[Livestock:History:Request] " + url);

            using (var response = await Http.GetAsync(url, cancellationToken))
            {
                string json = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine(string.Format(
                    "[Livestock:History:Response] {0} {1}",
                    (int)response.StatusCode,
                    json.Substring(0, Math.Min(600, json.Length))));

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Server returned " + (int)response.StatusCode + ".");

                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidOperationException("The server returned an empty response.");

                var result = JsonConvert.DeserializeObject<TransactionApiResponse>(json);

                if (result == null)
                    throw new InvalidOperationException("The server returned an unreadable response.");

                if (!result.IsSuccessful)
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(result.Message)
                            ? "The server rejected the request."
                            : result.Message);

                result.Transactions = Normalise(result.Transactions);
                return result;
            }
        }

        /// <summary>Most recent <paramref name="count"/> transactions, newest first.</summary>
        public static async Task<List<Transaction>> GetRecentAsync(
            int count = 5,
            int lookBackDays = 30,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var response = await GetAsync(
                DateTime.Now.Date.AddDays(-lookBackDays),
                DateTime.Now.Date,
                cancellationToken);

            return response.Transactions.Take(count).ToList();
        }

        /// <summary>
        /// Fills in nulls and sorts newest-first.
        ///
        /// Sorting is done on the parsed <see cref="DateTime"/>, not the raw string. Ordering
        /// by the string puts "9/12" after "10/3" and silently scrambles the list.
        /// </summary>
        private static List<Transaction> Normalise(List<Transaction> source)
        {
            if (source == null) return new List<Transaction>();

            foreach (var t in source)
            {
                if (t.TransactionId == null) t.TransactionId = "N/A";
                if (t.ServiceTypeName == null) t.ServiceTypeName = "Unknown Service";
                if (t.AgentName == null) t.AgentName = "Unknown Agent";
                if (t.RevenueHead == null) t.RevenueHead = "N/A";
                if (t.RemitaServiceName == null) t.RemitaServiceName = "N/A";
                if (t.Status == null) t.Status = "Unknown";

                if (t.Extra != null && t.Extra.Count > 0)
                {
                    // Surfaces any field the livestock endpoint returns that this model does
                    // not map yet — cheap early warning against contract drift.
                    System.Diagnostics.Debug.WriteLine(
                        "[Livestock:History:Unmapped] " + string.Join(", ", t.Extra.Keys));
                }
            }

            return source
                .OrderByDescending(t => t.ParsedDate ?? DateTime.MinValue)
                .ToList();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  MODELS
    // ══════════════════════════════════════════════════════════════════

    public class TransactionApiResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        // The endpoint spells this "respondCode"; "responseCode" is accepted too so a
        // server-side spelling fix cannot turn every success into a failure overnight.
        [JsonProperty("respondCode")]
        public string RespondCode { get; set; }

        [JsonProperty("responseCode")]
        public string ResponseCode { get; set; }

        [JsonProperty("agent")]
        public string Agent { get; set; }

        [JsonProperty("totalTransactionCount")]
        public int TotalTransactionCount { get; set; }

        [JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonProperty("transactions")]
        public List<Transaction> Transactions { get; set; }

        [JsonIgnore]
        public string ResolvedCode =>
            !string.IsNullOrWhiteSpace(RespondCode) ? RespondCode : ResponseCode;

        [JsonIgnore]
        public bool IsSuccessful => string.Equals(ResolvedCode, "00", StringComparison.Ordinal);
    }

    public class Transaction
    {
        // Yes, the server really does spell it "datelIst".
        [JsonProperty("datelIst")]
        public string DateRaw { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }

        [JsonProperty("serviceTypeName")]
        public string ServiceTypeName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("payer")]
        public string Payer { get; set; }

        [JsonProperty("agentName")]
        public string AgentName { get; set; }

        [JsonProperty("revenueHead")]
        public string RevenueHead { get; set; }

        [JsonProperty("remitaServiceName")]
        public string RemitaServiceName { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("quantity")]
        public int? Quantity { get; set; }

        [JsonProperty("paymentMethod")]
        public string PaymentMethod { get; set; }

        /// <summary>Anything the endpoint returns that is not mapped above.</summary>
        [JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> Extra { get; set; }

        // ── Display helpers ───────────────────────────────────────────

        [JsonIgnore]
        public DateTime? ParsedDate
        {
            get
            {
                DateTime parsed;
                if (DateTime.TryParse(DateRaw, CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out parsed))
                    return parsed;

                if (DateTime.TryParse(DateRaw, out parsed))
                    return parsed;

                return null;
            }
        }

        [JsonIgnore]
        public string DisplayTime =>
            ParsedDate.HasValue ? ParsedDate.Value.ToString("h:mm tt") : "—";

        [JsonIgnore]
        public string DisplayDate =>
            ParsedDate.HasValue
                ? ParsedDate.Value.ToString("MMM dd, yyyy h:mm tt")
                : (DateRaw ?? "N/A");

        [JsonIgnore]
        public string AmountDisplay => string.Format("₦{0:N2}", Amount);

        [JsonIgnore]
        public string PayerDisplay =>
            string.IsNullOrWhiteSpace(Payer) ? "Walk-in" : Payer;

        [JsonIgnore]
        public string QuantityDisplay =>
            Quantity.HasValue && Quantity.Value > 0
                ? string.Format("× {0}", Quantity.Value)
                : string.Empty;

        [JsonIgnore]
        public bool IsSuccessful =>
            !string.IsNullOrEmpty(Status) &&
            (Status.IndexOf("Approved", StringComparison.OrdinalIgnoreCase) >= 0 ||
             Status.IndexOf("Successful", StringComparison.OrdinalIgnoreCase) >= 0);

        [JsonIgnore]
        public bool IsRefunded =>
            !string.IsNullOrEmpty(Status) &&
            Status.IndexOf("Refund", StringComparison.OrdinalIgnoreCase) >= 0;

        [JsonIgnore]
        public bool IsPending =>
            !string.IsNullOrEmpty(Status) &&
            Status.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0;

        [JsonIgnore]
        public Color StatusColor
        {
            get
            {
                if (IsSuccessful) return Color.FromHex("#1E8E4E");
                if (IsRefunded) return Color.FromHex("#E74C3C");
                if (IsPending) return Color.FromHex("#F39C12");
                return Color.FromHex("#95A5A6");
            }
        }

        [JsonIgnore]
        public Color StatusTintColor
        {
            get
            {
                if (IsSuccessful) return Color.FromHex("#E8F5EC");
                if (IsRefunded) return Color.FromHex("#FDECEA");
                if (IsPending) return Color.FromHex("#FEF5E7");
                return Color.FromHex("#ECEFF1");
            }
        }

        [JsonIgnore]
        public string StatusIcon
        {
            get
            {
                if (IsSuccessful) return "✓";
                if (IsRefunded) return "↩";
                if (IsPending) return "⏳";
                return "?";
            }
        }

        /// <summary>
        /// Rough species icon inferred from the service name. Purely cosmetic — an unmatched
        /// name falls back to a generic tag so a new service never renders blank.
        /// </summary>
        [JsonIgnore]
        public string ServiceIcon
        {
            get
            {
                string name = (ServiceTypeName ?? string.Empty).ToLowerInvariant();

                if (name.Contains("cattle") || name.Contains("cow")) return "🐄";
                if (name.Contains("sheep") || name.Contains("goat")) return "🐐";
                if (name.Contains("camel") || name.Contains("donkey")) return "🐫";
                if (name.Contains("horse")) return "🐎";
                if (name.Contains("produce") || name.Contains("bag")) return "🌾";

                return "🏷";
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  DAY GROUPING
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// One day's transactions with a header total, newest day first. Grouping is done in the
    /// view model rather than by a converter so the per-day total is computed once.
    /// </summary>
    public class TransactionDayGroup : List<Transaction>
    {
        public TransactionDayGroup(DateTime? day, IEnumerable<Transaction> items)
            : base(items)
        {
            Day = day;
            DayTotal = this.Where(t => t.IsSuccessful).Sum(t => t.Amount);
        }

        public DateTime? Day { get; }

        public decimal DayTotal { get; }

        public string DayHeader
        {
            get
            {
                if (!Day.HasValue) return "Undated";

                DateTime today = DateTime.Now.Date;

                if (Day.Value.Date == today) return "Today";
                if (Day.Value.Date == today.AddDays(-1)) return "Yesterday";

                return Day.Value.ToString("dddd, MMM dd yyyy");
            }
        }

        public string DayTotalDisplay => string.Format("₦{0:N2}", DayTotal);

        public string DayCountDisplay =>
            Count == 1 ? "1 transaction" : Count + " transactions";

        public static List<TransactionDayGroup> Build(IEnumerable<Transaction> source)
        {
            if (source == null) return new List<TransactionDayGroup>();

            return source
                .GroupBy(t => t.ParsedDate.HasValue
                    ? t.ParsedDate.Value.Date
                    : (DateTime?)null)
                .OrderByDescending(g => g.Key ?? DateTime.MinValue)
                .Select(g => new TransactionDayGroup(
                    g.Key,
                    g.OrderByDescending(t => t.ParsedDate ?? DateTime.MinValue)))
                .ToList();
        }
    }
}