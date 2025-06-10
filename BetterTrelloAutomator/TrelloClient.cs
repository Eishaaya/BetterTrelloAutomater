using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System.Text.Json;

namespace BetterTrelloAutomator
{
    public class TrelloClient
    {
        readonly string key;
        readonly string token;
        readonly string authString;
        const string boardID = "660328145c642e3b4fc66006"; //ID for personal board
        readonly HttpClient client;
        readonly ILogger<TrelloClient> logger;

        readonly JsonSerializerOptions caseInsensitive = new() { PropertyNameCaseInsensitive = true };

        public TrelloClient(HttpClient httpClient, IConfiguration config, ILogger<TrelloClient> myLogger)
        {
            key = config["TRELLO_KEY"] ?? throw new InvalidOperationException("FAILED TO LOAD TRELLO_KEY");
            token = config["TRELLO_TOKEN"] ?? throw new InvalidOperationException("FAILED TO LOAD TRELLO_TOKEN");

            authString = $"&key={key}&token={token}";

            client = httpClient;
            client.BaseAddress = new Uri("https://api.trello.com/1/");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            logger = myLogger;
            logger.LogInformation("Trello client made");
        }

        #region utility
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

        async Task<string> GetResponse(string uri)
        {
            var response = await client.GetAsync(uri + authString);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        async Task<TRecord> GetValue<TRecord>(string uri)
        {
            var response = await GetResponse($"{uri}?fields={RecordHelpers.GetFields<TRecord>()}");
            return JsonSerializer.Deserialize<TRecord>(response, caseInsensitive)!;
        }
        async Task<TRecord[]> GetValues<TRecord>(string uri)
        {
            var response = await GetResponse($"{uri}?fields={RecordHelpers.GetFields<TRecord>()}");
            
            return JsonSerializer.Deserialize<TRecord[]>(response, caseInsensitive)!;
        }

        #endregion

        public async Task MoveCards(SimpleTrelloRecord from, SimpleTrelloRecord to)
        {
            var uri = $"lists/{from.Id}/moveAllCards?idBoard={boardID}&idList={to.Id}" + authString;
            var response = await client.PostAsync(uri, null);
            response.EnsureSuccessStatusCode();
        }

        public async Task<TrelloCard[]> GetCards(SimpleTrelloRecord list)
        {
            return await GetValues<TrelloCard>($"lists/{list.Id}/cards");           
        }

        public async Task MoveCard(TrelloCard card, TrelloListPosition position)
        {
            var uri = $"cards/{card.Id}?" + authString;
            var content = new FormUrlEncodedContent([
                new ("idList", position.ListId),
                new ("pos", position.Pos)
            ]);

            var response = await client.PutAsync(uri, content);
            response.EnsureSuccessStatusCode();
        }
    }

}
