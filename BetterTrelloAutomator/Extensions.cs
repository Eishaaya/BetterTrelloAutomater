using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomator
{
    enum Times
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
        public static int ToUTC(this int hour, Times timeIndicator) => (hour + (int)timeIndicator).ToUTC();
    }
}
