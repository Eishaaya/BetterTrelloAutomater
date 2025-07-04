﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
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

        public static DateTimeOffset TrySetDate(this DateTimeOffset oldDateTimeOffset, DateTimeOffset newDate, TimeSpan buffer = default)
        {
            oldDateTimeOffset = oldDateTimeOffset.ToOffset(newDate.Offset);

            if (oldDateTimeOffset > newDate - buffer)
            {
                newDate = oldDateTimeOffset; //Avoiding moving tasks less than they should, only farther
            }

            oldDateTimeOffset = new DateTimeOffset(newDate.Year, newDate.Month, newDate.Day, oldDateTimeOffset.Hour, oldDateTimeOffset.Minute, oldDateTimeOffset.Second, oldDateTimeOffset.Offset);
            return oldDateTimeOffset;
        }

        public static void EnsureUriFormat(this string uri)
        {
            if (!uri.Contains('?') || (uri[^1] != '?' && uri[^1] != '&')) throw new ArgumentException("Missing ? or &");
        }

        public static DayOfWeek? AsDayOfWeek(this string input)
        {
            if (input.Length < 2) return null; //String too short to evaluate

            input = input.ToLower();

            for (DayOfWeek day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
            {
                string dayString = day.ToString().ToLower();
                if (dayString[0] != input[0]) continue;

                if (input[1] == dayString[1]) //Checking matching character from start, EG: mo, mon, mond, monda, monday
                {
                    return day;
                }


                if (input.Contains(dayString)) //Checking if day is included: EG: Lunch on tuesday
                {
                    return day;
                }
            }

            return null;
        }
    }
}
