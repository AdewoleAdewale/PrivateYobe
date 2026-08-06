using System;
using System.Linq;

namespace YIRS.Views.Livestock
{
    /// <summary>
    /// One place that owns the module's naming.
    ///
    /// The revenue head was renamed from "LIVE STOCK DAMATURU LOCAL GOVERNMENT" to
    /// "Damaturu Local Government Collection". Scattering that string across four pages is
    /// how the next rename breaks the app again, so every display label and every revenue-head
    /// comparison goes through here instead.
    ///
    /// Note what is deliberately NOT in this file: the API route segments
    /// (<c>/api/Agents/LiveStockServicesList</c>, <c>/api/Agents/LiveStockPayment</c>) and the
    /// C# type names (<c>LiveStockService</c>, <c>LiveStockPaymentRequest</c>). Those are a
    /// server contract and internal identifiers respectively — neither is shown to an agent,
    /// and renaming them does not change behaviour. See the notes doc.
    /// </summary>
    public static class LivestockModule
    {
        // ── Naming ────────────────────────────────────────────────────

        /// <summary>Full name, used in page headers and on printed receipts.</summary>
        public const string DisplayName = "Damaturu Local Government Collection";

        /// <summary>Uppercase form for the dashboard badge.</summary>
        public const string BadgeText = "DAMATURU LG COLLECTION";

        /// <summary>Short form for tight spaces.</summary>
        public const string ShortName = "Damaturu LG Collection";

        /// <summary>Login category that routes an agent into this module, lowercased.</summary>
        public const string CategoryKey = "damaturu";

        // ── Revenue head ──────────────────────────────────────────────

        /// <summary>
        /// The revenue head sent to the server as <c>revHead</c> and matched against
        /// <c>revName</c> in the services list.
        ///
        /// The session value wins, because the server is the authority on what this agent's
        /// collection point is actually called. <see cref="DisplayName"/> is only a fallback
        /// for the case where the session somehow carries no collection point — better than
        /// posting an empty string.
        /// </summary>
        public static string RevenueHead
        {
            get
            {
                string fromSession = (MainPage.CollectionPoint ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(fromSession) ? DisplayName : fromSession;
            }
        }

        /// <summary>True when this login category belongs to the Damaturu module.</summary>
        public static bool IsModuleCategory(string category)
            => string.Equals(
                (category ?? string.Empty).Trim(),
                CategoryKey,
                StringComparison.OrdinalIgnoreCase);

        // ── Revenue head matching ─────────────────────────────────────

        /// <summary>
        /// Decides whether a services-list row belongs to this agent.
        ///
        /// Two tiers, because a rename never lands everywhere at once. During the changeover
        /// the login response and the services catalogue can disagree — one says
        /// "LIVE STOCK DAMATURU LOCAL GOVERNMENT", the other "Damaturu Local Government
        /// Collection" — and a strict comparison shows the agent an empty screen on a day
        /// when the market is open.
        ///
        ///   Tier 1 — normalised equality (case, spacing and punctuation ignored)
        ///   Tier 2 — both names refer to the same LGA
        ///
        /// The caller still falls back to showing the whole catalogue if neither tier matches,
        /// so an agent is never blocked by a naming difference alone.
        /// </summary>
        public static bool MatchesRevenueHead(string revName, string revenueHead = null)
        {
            if (string.IsNullOrWhiteSpace(revName)) return false;

            string target = revenueHead ?? RevenueHead;
            if (string.IsNullOrWhiteSpace(target)) return false;

            string a = Normalise(revName);
            string b = Normalise(target);

            if (a.Length == 0 || b.Length == 0) return false;

            // Tier 1 — same name once case, spacing and punctuation are stripped.
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;

            // Tier 2 — same LGA. "livestockdamaturulocalgovernment" and
            // "damaturulocalgovernmentcollection" both contain the LGA token.
            string lga = Normalise(CategoryKey);
            return a.Contains(lga) && b.Contains(lga);
        }

        /// <summary>Lowercases and strips everything that is not a letter or digit.</summary>
        private static string Normalise(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            return new string(value
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
        }
    }
}