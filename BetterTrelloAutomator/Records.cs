using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterTrelloAutomator
{
    interface IHasId
    {
        public string Id { get; }
    }
    public record class SimpleTrelloRecord(string Name, string Id) : IHasId;
    public record class TrelloCard(string Name, string Id, string Start, string Due, TrelloLabel[] Labels) : SimpleTrelloRecord(Name, Id);
    public record class TrelloLabel(string Name, string Id, string Color) : SimpleTrelloRecord(Name, Id)
    {
        public static TrelloLabel Night => new TrelloLabel("Night", null!, null!);
        public static TrelloLabel Morning => new TrelloLabel("Morning", null!, null!);
    }
    public record class TrelloListPosition(string ListId, string Pos = "top")
    {
        public static implicit operator TrelloListPosition(SimpleTrelloRecord record) => new TrelloListPosition(record.Id);
    }
}
