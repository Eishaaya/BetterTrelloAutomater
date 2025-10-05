using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterTrelloAutomator.Dependencies
{
    public class TrelloClient
    {
        readonly string key;
        readonly string token;
        readonly string authString;

        const string boardID = "660328145c642e3b4fc66006"; //ID for personal board
        readonly HttpClient client;
        readonly ILogger<TrelloClient> logger;

        public readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        public TrelloClient(HttpClient httpClient, IConfiguration config, ILogger<TrelloClient> myLogger)
        {
            key = config["TRELLO_KEY"] ?? throw new InvalidOperationException("FAILED TO LOAD TRELLO_KEY");
            token = config["TRELLO_TOKEN"] ?? throw new InvalidOperationException("FAILED TO LOAD TRELLO_TOKEN");

            authString = $"key={key}&token={token}";

            client = httpClient;
            client.BaseAddress = new Uri("https://api.trello.com/1/");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            logger = myLogger;
            logger.LogInformation("Trello client made");
        }

        #region boardInfo

        public async Task<string> GetPersonalBoardID()
        {
            var uri = $"members/me/boards";
            var usableBoards = await GetValues<SimpleTrelloRecord>(uri);

            foreach (var board in usableBoards!)
            {
                if (board.Name == "Personal")
                {
                    return board.Id;
                }
            }

            throw new InvalidOperationException("Failed to find personal board!");
        }

        public async Task<SimpleTrelloRecord[]> GetLists(int startingIndex = 0, int endingIndex = int.MaxValue)
        {
            var lists = await GetValues<SimpleTrelloRecord>($"boards/{boardID}/lists");

            return [.. lists.Where((_, i) => i >= startingIndex && i <= endingIndex)];
        }

        #endregion

        #region internalHelpers


        async Task<string> GetResponse(string uri)
        {
            var response = await client.GetAsync($"{uri}&{authString}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        async Task<TRecord> GetValue<TRecord>(string uri)
        {
            var response = await GetResponse($"{uri}fields={RecordHelpers.GetFields<TRecord>()}");
            return JsonSerializer.Deserialize<TRecord>(response, CaseInsensitive)!;
        }
        async Task<TRecord[]> GetValues<TRecord>(string uri) where TRecord : IQueryableRecord
        {
            string fullUri = $"{uri}?{TRecord.QueryInfo}fields={RecordHelpers.GetFields<TRecord>()}";
            var response = await GetResponse(fullUri);

            return JsonSerializer.Deserialize<TRecord[]>(response, CaseInsensitive)!;
        }

        async Task PutValue<TRecord>(string uri, TRecord contentRecord, bool ignoreContent, IEnumerable<StringPair> otherChanges) where TRecord : SimpleTrelloRecord
        {
            uri.EnsureUriFormat();

            IEnumerable<StringPair> baseChanges = ignoreContent ? [] : RecordHelpers.GetFields(contentRecord);

            var content = new FormUrlEncodedContent([..baseChanges,..otherChanges]);
            var response = await client.PutAsync($"{uri}{authString}", content);
            response.EnsureSuccessStatusCode();
        }
        Task PutValue<TRecord>(string uri, TRecord contentRecord, bool ignoreContent = false, params StringPair[] otherChanges) where TRecord : SimpleTrelloRecord => PutValue(uri, contentRecord, ignoreContent, otherChanges.AsEnumerable());


        async Task<HttpResponseMessage> PostValue(string uri, IEnumerable<StringPair> content)
        {
            uri.EnsureUriFormat();

            var response = await client.PostAsync($"{uri}{authString}", new FormUrlEncodedContent(content));
            response.EnsureSuccessStatusCode();
            return response;
        }
        Task<HttpResponseMessage> PostValue(string uri, params StringPair[] content) => PostValue(uri, content.AsEnumerable());


        #endregion

        #region movement

        public async Task MoveCards(SimpleTrelloRecord from, SimpleTrelloRecord to)
        {
            var uri = $"lists/{from.Id}/moveAllCards?idBoard={boardID}&idList={to.Id}&";
            await PostValue(uri);
        }

        public async Task UpdateCard<TCard>(TCard card) where TCard : SimpleTrelloCard
        {
            var uri = $"cards/{card.Id}?";
            await PutValue(uri, card);
        }

        public async Task MoveCard<TCard>(TCard card, TrelloListPosition position) where TCard : SimpleTrelloCard
        {
            var uri = $"cards/{card.Id}?";
            await PutValue(uri, card, true, RecordHelpers.GetFields(position));
        }
        public async Task UpdateCard<TCard>(TCard card, TrelloListPosition position) where TCard : SimpleTrelloCard
        {
            var uri = $"cards/{card.Id}?";
            await PutValue(uri, card, false, RecordHelpers.GetFields(position));
        }

        #endregion

        #region utility
        public Task<TCard[]> GetCards<TCard>(SimpleTrelloRecord list) where TCard : SimpleTrelloCard
            => GetValues<TCard>($"lists/{list.Id}/cards");


        internal Task<TCard> GetCard<TCard>(string cardId) where TCard : SimpleTrelloCard
            => GetValue<TCard>($"cards/{cardId}?checklists=all&");
        

        #endregion

        #region checkLists

        public async Task CompleteCheckItem<TCard>(TCard card, CheckItem item) where TCard : SimpleTrelloCard
        {
            logger.LogInformation("Completing item {checkItem} in card {card}", item, card);
            var uri = $"cards/{card.Id}/checkItem/{item.Id}?";
            await PutValue(uri, item.SetState(CheckItem.Complete));
        }

        public async Task CompleteAllCheckedItems(FullTrelloCard card)
        {
            foreach (var list in card.Checklists)
            {
                foreach (var item in list.CheckItems)
                {                            
                    await CompleteCheckItem(card, item);
                }
            }
        }

        internal async Task<FullTrelloCard> CloneCard(SimpleTrelloCard card, TrelloListPosition pos)
        {
            var message = await PostValue($"cards?idList={pos.IdList}&idCardSource={card.Id}&dueComplete=false&due={card.Due}&start={card.Start}&" +
                $"keepFromSource=attachments,checklists,customFields,comments,labels,members,stickers&");

            return await message.Content.ReadFromJsonAsync<FullTrelloCard>() ?? throw new Exception("BUG: Should never be null, means template card does not exist.");
        }



        #endregion
        internal async Task<string> CreateBoardHook(string callbackURL, string description = "")
        {
            var response = await client.PostAsync($"tokens/{token}/webhooks/?key={key}", new FormUrlEncodedContent([
                new (nameof(description), description),
                new (nameof(callbackURL), callbackURL),
                new ("idModel", boardID)
                ]));

            return await response.Content.ReadAsStringAsync();
        }
    }

}
