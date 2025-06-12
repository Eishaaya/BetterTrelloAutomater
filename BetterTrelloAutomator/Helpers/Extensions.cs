using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomator.Helpers
{
    enum AMPM
    {
        AM = 0,
        PM = 12
    }
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

    }
}
