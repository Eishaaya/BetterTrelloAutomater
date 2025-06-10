using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterTrelloAutomator
{
    public record class SimplifiedTrelloRecord(string Name, string Id);
    public record class TrelloCard(string Name, string Id, string Start, string Due, TrelloLabel[] Labels);
    public record class TrelloLabel(string Name, string Id, string Color);
    public record class TrelloListPosition(string ListId, string Pos = "top");

    static class RecordHelpers
    {
        public static string GetFields<TRecord>() => string.Join(',', (typeof(TRecord)).GetProperties().Select(m => JsonNamingPolicy.CamelCase.ConvertName(m.Name)));
    }
}
