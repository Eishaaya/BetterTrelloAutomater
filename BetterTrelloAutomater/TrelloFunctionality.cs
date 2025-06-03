using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net.Http;

namespace BetterTrelloAutomator
{
    class TrelloFunctionality
    {
        SimplifiedTrelloRecord[] lists;
        int cycleStart;
        int cycleEnd;

        async Task GetLists()
        {
            if (lists != null) return;

            lists = await client.GetLists();

            for (int i = 0; i < lists.Length; i++)
            {
                if (lists[i].Name.Contains("TODO"))
                {
                    cycleStart = i;
                    break;
                }
            }

            for (int i = lists.Length - 1; i >= 0; i--)
            {
                if (lists[i].Name.Contains("TODO"))
                {
                    cycleEnd = i;
                    break;
                }
            }
        }

        TrelloClient client;
        ILogger log;
        public TrelloFunctionality(TrelloClient client, ILogger<TrelloFunctionality> log)
        {
            this.client = client;
            this.log = log;
        }

        async Task TransitionDays()
        {
            await GetLists();
            log.LogInformation($"CYCLE: {cycleStart} - {cycleEnd}");
            for (int i = cycleEnd - 1; i >= cycleStart; i--)
            {
                log.LogInformation($"Moving from {lists[i].Name} to {lists[i + 1].Name}");
                await client.MoveCards(lists[i].Id, lists[i + 1].Id);
            }
        }

        [Function("TransitionDays")]
        public async Task TransitionDays([TimerTrigger("0 30 10 * * *")] TimerInfo info)
        {
            log.LogInformation($"Automatically transitioning Days {info}");
            await TransitionDays();
        }

        [Function("ManuallyTransitionDays")]
        public async Task ManuallyTransitionDays([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            log.LogInformation($"Transitioning Days MANUALLY");
            await TransitionDays();
        }
        [Function("DetransitionDays")]
        public async Task DetransitionDays([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            await GetLists();
            log.LogInformation($"De-transitioning Days MANUALLY");
            for (int i = cycleStart; i < cycleEnd; i++)
            {
                log.LogInformation($"Moving from {lists[i + 1].Name} to {lists[i].Name}");
                await client.MoveCards(lists[i + 1].Id, lists[i].Id);
            }
        }

        [Function("GetPersonalID")]
        public async Task<IActionResult> GetPersonalID([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            log.LogInformation("Getting ID");
            return new OkObjectResult(await client.GetPersonalBoardID());
        }

        [Function("GetLists")]
        public async Task<IActionResult> GetLists([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            log.LogInformation("User requesting to get lists");
            return new OkObjectResult(await client.GetLists());
        }



    }
}
