using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterTrelloAutomator
{
    class IdComparer : IEqualityComparer<IHasId>, IComparer<IHasId>
    {
        public static IdComparer? Instance { get; } = new();

        public int Compare(IHasId? x, IHasId? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            return x.Id.CompareTo(y.Id);
        }

        public bool Equals(IHasId? x, IHasId? y)
        {
            if (x == y) return true;
            if (x == null || y == null) return false;

            return x.Id == y.Id;
        }

        public int GetHashCode([DisallowNull] IHasId obj) => obj.GetHashCode();
    }

    class TrelloNameComparer : IEqualityComparer<SimpleTrelloRecord>, IComparer<SimpleTrelloRecord>
    {
        public static TrelloNameComparer? Instance { get; } = new();

        public int Compare(SimpleTrelloRecord? x, SimpleTrelloRecord? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            return x.Name.CompareTo(y.Name);
        }

        public bool Equals(SimpleTrelloRecord? x, SimpleTrelloRecord? y)
        {
            if (x == y) return true;
            if (x == null || y == null) return false;

            return x.Name == y.Name;
        }

        public int GetHashCode([DisallowNull] SimpleTrelloRecord obj) => obj.GetHashCode();
    }

    static class RecordHelpers
    {
        public static string GetFields<TRecord>() => string.Join(',', (typeof(TRecord)).GetProperties().Select(m => JsonNamingPolicy.CamelCase.ConvertName(m.Name)));
    }
}
