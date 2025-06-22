using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


namespace BetterTrelloAutomator.Helpers
{
    interface IHasId
    {
        public string Id { get; }
    }
    interface IQueryableRecord
    {
        public static abstract string QueryInfo { get; }
    }
    public record class SimpleTrelloRecord(string Name, string Id) : IHasId, IQueryableRecord
    {
        public static string QueryInfo => "";
    }
    public record class SimpleTrelloCard(string Name, string Id, DateTimeOffset? Start, DateTimeOffset? Due, bool DueComplete) : SimpleTrelloRecord(Name, Id)
    {
        internal static SimpleTrelloCard LossyClone<TCard>(TCard card) where TCard : SimpleTrelloCard => new (card);
        public SimpleTrelloCard SetStart(DateTimeOffset? val) => new SimpleTrelloCard(Name, Id, val, Due, DueComplete);
        public SimpleTrelloCard SetDue(DateTimeOffset? val) => new SimpleTrelloCard(Name, Id, Start, val, DueComplete);
        public SimpleTrelloCard SetComplete(bool val) => new SimpleTrelloCard(Name, Id, Start, Due, val);

    }

    public record class LabeledTrelloCard(string Name, string Id, DateTimeOffset? Start, DateTimeOffset? Due, bool DueComplete, TrelloLabel[] Labels) : SimpleTrelloCard(Name, Id, Start, Due, DueComplete);
    public record class FullTrelloCard(string Name, string Id, DateTimeOffset? Start, DateTimeOffset? Due, bool DueComplete, TrelloLabel[] Labels, CheckList[] Checklists) : LabeledTrelloCard(Name, Id, Start, Due, DueComplete, Labels), IQueryableRecord
    {
        public static new string QueryInfo => "checklists=all&";
    }
    public record class TrelloLabel(string Name, string Id, string Color) : SimpleTrelloRecord(Name, Id)
    {
        public record struct TimeUnit(string Name, int DayCount)
        {
            public static implicit operator string(TimeUnit timeUnit) => timeUnit.Name;
            public static implicit operator int(TimeUnit timeUnit) => timeUnit.DayCount;
        }

        public const string Morning = nameof(Morning);
        public const string Night = nameof(Night);

        public const string Task = nameof(Task);
        public const string Reverse = nameof(Reverse);
        public const string Static = nameof(Static);

        public const string Strict = nameof(Strict);
        public const string Inconsistent = nameof(Inconsistent);

        public static readonly TimeUnit Daily = new (nameof(Daily), 1);
        public static readonly TimeUnit Weekly = new (nameof(Weekly), 7);
        public static readonly TimeUnit Biweekly = new (nameof(Biweekly), 14);
        public static readonly TimeUnit Monthly = new (nameof(Monthly), 30);
    }
    public record class CheckList(string Name, string Id, List<CheckItem> CheckItems) : SimpleTrelloRecord(Name, Id);
    public record class CheckItem(string Name, string Id, string State, float Pos) : SimpleTrelloRecord(Name, Id)
    {
        public static readonly string Complete = nameof(Complete).ToLower();
        public static readonly string Incomplete = nameof(Incomplete).ToLower();

        public CheckItem SetState(string state) => new(Name, Id, state, Pos);
    }
    public record class TrelloListPosition(string IdList, string Pos = "top") : IHasId
    {
        string IHasId.Id => IdList;

        public static implicit operator TrelloListPosition(SimpleTrelloRecord record) => new (record.Id);
    }

    record WebhookResponse(Action Action, Model Model, Webhook Webhook);
    record Action(ActionData Data, string Type);
    record ActionData(SimpleTrelloRecord Card);
    record Webhook(); //Placeholder
    record Model(); //Placeholder
}
