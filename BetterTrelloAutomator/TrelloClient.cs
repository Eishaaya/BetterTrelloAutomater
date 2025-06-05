using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

            authString = $"key={key}&token={token}";

            client = httpClient;
            client.BaseAddress = new Uri("https://api.trello.com/1/");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            logger = myLogger;
            logger.LogInformation("Trello client made");
        }

        #region utility
        public async Task<string> GetPersonalBoardID()
        {
            var url = "members/me/boards?fields=name,id&";
            var boards = await GetResponse(url);

            var usableBoards = JsonSerializer.Deserialize<SimplifiedTrelloRecord[]>(boards, caseInsensitive);
            foreach (var board in usableBoards)
            {
                if (board.Name == "Personal")
                {
                    return board.Id;
                }
            }

            throw new InvalidOperationException("Failed to find personal board!");
        }

        public async Task<SimplifiedTrelloRecord[]> GetLists(int startingIndex = 0, int endingIndex = int.MaxValue)
        {
            var url = $"boards/{boardID}/lists?fields=name,id&";
            var body = await GetResponse(url);

            var lists = JsonSerializer.Deserialize<SimplifiedTrelloRecord[]>(body, caseInsensitive).Where((_, i) => i >= startingIndex && i <= endingIndex);

            return [.. lists];
        }

        async Task<string> GetResponse(string uri)
        {
            var response = await client.GetAsync(uri + authString);
            response.EnsureSuccessStatusCode();
            return await response?.Content.ReadAsStringAsync();
        }

        #endregion

        public async Task MoveCards(SimplifiedTrelloRecord from, SimplifiedTrelloRecord to)
        {
            var uri = $"lists/{from.Id}/moveAllCards?idBoard={boardID}&idList={to.Id}&" + authString;
            var response = await client.PostAsync(uri, null);
            response.EnsureSuccessStatusCode();
        }

        public async Task<SimplifiedTrelloCard[]> GetCards(SimplifiedTrelloRecord list)
        {
            var response = await GetResponse($"lists/{list.Id}/cards?fields=name,id,start,due&");
            return JsonSerializer.Deserialize<SimplifiedTrelloCard[]>(response, caseInsensitive);
        }

        public async Task MoveCard(SimplifiedTrelloCard card, ListPosition position)
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

    public record class SimplifiedTrelloRecord(string Name, string Id);
    public record class SimplifiedTrelloCard(string Name, string Id, string Start, string Due);
    public record class ListPosition(string ListId, string Pos = "top");
}
