using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

using static BetterTrelloAutomator.Helpers.TrelloLabel;
     

namespace BetterTrelloAutomator.Helpers
{
    static class Extensions
    {
        /// <summary>
        /// Converts to UCT from hardcoded PST
        /// </summary>
        /// <returns>hour in UTC</returns>
        public static int ToUTC(this int hour) => (hour + 7) % 24; //TODO: use pinged data instead of hard-coding
        /// <summary>
        /// Converts to UCT from hardcoded PST factoring in AM/PM times
        /// </summary>
        /// <returns>hour in UTC</returns>
        public static int ToUTC(this int hour, AMPM timeIndicator) => (hour + (int)timeIndicator).ToUTC();

        public static bool ContainsId<TIdable>(this IEnumerable<TIdable> records, string id) where TIdable : IHasId => records.Any(m => m.Id == id);
        public static bool ContainsName<TRecord>(this IEnumerable<TRecord> records, string name) where TRecord : SimpleTrelloRecord => records.Any(m => m.Name == name);
        public static bool ContainsName<TRecord>(this IEnumerable<TRecord> records, out string? foundName, params string[] names) where TRecord : SimpleTrelloRecord
            => (foundName = names.Where(cName => records.Any(r => r.Name == cName)).FirstOrDefault()) != default;
        public static bool ContainsTime<TRecord>(this IEnumerable<TRecord> records, out TimeUnit foundTime, params TimeUnit[] times) where TRecord : SimpleTrelloRecord 
            => (foundTime = times.Where(cName => records.Any(r => r.Name == cName)).FirstOrDefault()) != default;

        public static SimpleTrelloCard Simpify<TCard>(this TCard card) where TCard : SimpleTrelloCard => SimpleTrelloCard.LossyClone(card);

        public static string? TrySetDate(this string? oldDateTime, DateTime newDate)
        {
            if (DateTime.TryParse(oldDateTime, out DateTime newDateTime))
            {
                newDateTime = new DateTime(newDate.Year, newDate.Month, newDate.Day, newDateTime.Hour, newDateTime.Minute, newDateTime.Second);
                return newDateTime.ToString();
            }
            return null;
        }

        public static string? TryAddDays(this string? oldDate, int dayChange)
        {
            if (DateTime.TryParse(oldDate, out DateTime newDate))
            {
                newDate += TimeSpan.FromDays(dayChange);
                return newDate.ToString();
            }
            return null;
        }

        public static void EnsureUriFormat(this string uri)
        {
            if (!uri.Contains('?') || (uri[^1] != '?' && uri[^1] != '&')) throw new ArgumentException("Missing ? or &");
        }
    }
}
