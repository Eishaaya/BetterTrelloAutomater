using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomater
{
    internal class TrelloClient
    {
        readonly string key;
        readonly string token;
        readonly string authString;
        HttpClient client;

        public TrelloClient(HttpClient httpClient, IConfiguration config)
        {
            key = config["TRELLO_KEY"];
            token = config["TRELLO_TOKEN"];
            authString = $"key={key}&token={token}";

            client = httpClient;
            client.BaseAddress = new Uri("https://api.trello.com/1/boards/");
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));
        }


    }
}
