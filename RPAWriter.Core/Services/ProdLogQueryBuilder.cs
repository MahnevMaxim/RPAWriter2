using System;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Builds the <see cref="ProdLogQuery"/> the log analysis panel sends to a provider.
    /// Extracted from the view so the date normalisation rules are testable.
    /// </summary>
    public static class ProdLogQueryBuilder
    {
        /// <summary>
        /// Creates a query. <paramref name="fromDate"/> becomes the start of that day (UTC);
        /// <paramref name="toDate"/> becomes the last tick of that day (UTC), so the upper bound
        /// stays inclusive for day-based pickers.
        /// </summary>
        public static ProdLogQuery Build(LogLevel? minLevel, DateTime? fromDate, DateTime? toDate)
        {
            var query = new ProdLogQuery { MinLevel = minLevel };

            if (fromDate.HasValue)
                query.FromUtc = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);

            if (toDate.HasValue)
                query.ToUtc = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

            return query;
        }
    }
}
