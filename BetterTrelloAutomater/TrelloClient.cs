using Microsoft.Azure.Functions.Worker.Extensions.OpenApi;
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
    internal class TrelloClient
    {
        readonly string key;
        readonly string token;
        readonly string authString;
        const string boardID = "660328145c642e3b4fc66006"; //ID for personal board
        HttpClient client;
        ILogger<TrelloClient> logger;

        JsonSerializerOptions caseInsensitive = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

        public TrelloClient(HttpClient httpClient, IConfiguration config, ILogger<TrelloClient> myLogger)
        {
            key = config["TRELLO_KEY"] ?? throw new ArgumentNullException("FAILED TO LOAD KEY");
            token = config["TRELLO_TOKEN"] ?? throw new ArgumentNullException("FAILED TO LOAD TOKEN");

            authString = $"&key={key}&token={token}";

            client = httpClient;
            client.BaseAddress = new Uri("https://api.trello.com/1/");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            logger = myLogger;
            logger.LogInformation("Trello client made");
        }

        public async Task<string> GetPersonalBoardID()
        {
            var url = "members/me/boards?fields=name,id" + authString;
            logger.LogInformation("CALLING @:" + client.BaseAddress + url);
            var response = await client.GetAsync(url);
            
            response.EnsureSuccessStatusCode();

            var boards = await response.Content.ReadAsStringAsync();

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

        public async Task<SimplifiedTrelloRecord[]> GetLists (int startingIndex = 0, int endingIndex = int.MaxValue)
        {
            var url = $"boards/{boardID}/lists?fields=name,id" + authString;
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            var lists = JsonSerializer.Deserialize<SimplifiedTrelloRecord[]>(body, caseInsensitive).Where((_, i) => i >= startingIndex && i <= endingIndex);

            return lists.ToArray();
        }

        public async Task MoveCards(string fromID, string toID)
        {
            var url = $"lists/{fromID}/moveAllCards?idBoard={boardID}&idList={toID}" + authString;
            var response = await client.PostAsync(url, null); 
            response.EnsureSuccessStatusCode();
        }

    }

    record class SimplifiedTrelloRecord(string Name, string Id);
}
