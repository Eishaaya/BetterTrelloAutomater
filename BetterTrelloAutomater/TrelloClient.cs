using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterTrelloAutomater
{
    internal class TrelloClient
    {
        readonly string key;
        readonly string token;
        readonly string authString;
        HttpClient client;
        ILogger<TrelloClient> logger;

        public TrelloClient(HttpClient httpClient, IConfiguration config, ILogger<TrelloClient> myLogger)
        {
            key = config["TRELLO_KEY"];
            token = config["TRELLO_TOKEN"];
            authString = $"&key={key}&token={token}";

            client = httpClient;
            client.BaseAddress = new Uri("https://api.trello.com/1/boards");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            logger = myLogger;
            logger.LogInformation("Trello client made");
        }

        public async Task<string> GetPersonalBoardID()
        {
            var url = "?fields=name,id" + authString;
            logger.LogInformation("Call: " + url);
            var response = await client.GetAsync(url);
            
            response.EnsureSuccessStatusCode();

            var boards = await response.Content.ReadAsStringAsync();

            var usableBoards = JsonSerializer.Deserialize<SimplifiedTrelloBoard[]>(boards);
            foreach (var board in usableBoards)
            {
                if (board.Name == "Personal")
                {
                    return board.Id;
                }
            }

            throw new InvalidOperationException("Failed to find personal board!");
        }

        public string[] GetListIds (int startingIndex, int endingIndex)
        {
            throw new NotImplementedException();
        }


    }

    record class SimplifiedTrelloBoard(string Name, string Id);
}
