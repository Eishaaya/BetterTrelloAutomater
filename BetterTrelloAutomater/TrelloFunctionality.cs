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
using System.Globalization;

namespace BetterTrelloAutomator
{
    class TrelloFunctionality
    {
        string timeZone = "Pacific Standard Time"; //TODO: ping my phone or laptop to get its actual location
        TimeZoneInfo myTimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        SimplifiedTrelloRecord[] lists;
        int firstTodo;
        int todayIndex;
        int cycleStart => firstTodo + 1;
        int cycleEnd;

        async Task GetLists()
        {
            if (lists != null) return;

            lists = await client.GetLists();

            for (int i = 0; i < lists.Length; i++)
            {
                if (lists[i].Name.Contains("TODO"))
                {
                    firstTodo = i;
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
            
            for (int i = cycleEnd; i >= cycleStart; i--)
            {
                if (lists[i].Name.Contains("today", StringComparison.OrdinalIgnoreCase))
                {
                    todayIndex = i;
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
            //Shifting cards over

            await GetLists();
            log.LogInformation($"CYCLE: {cycleStart} - {cycleEnd}");
            for (int i = cycleEnd - 1; i >= cycleStart; i--)
            {
                log.LogInformation($"Moving from {lists[i].Name} to {lists[i + 1].Name}");
                await client.MoveCards(lists[i], lists[i + 1]);
            }

            //Finding cards due within the next week and moving them to corresponding lists

            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, myTimeZoneInfo);
            now -= new TimeSpan(now.Hour, now.Minute + 1, now.Second); //getting the beginning of the day

            var cards = await client.GetCards(lists[firstTodo]);
            foreach (var card in cards)
            {
                string date = card.Start ?? card.Due;
                
                if (date == null) continue;

                var utcTime = DateTime.Parse(date, null, DateTimeStyles.AdjustToUniversal);
                DateTime dateTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, myTimeZoneInfo);                

                int daysFromNow = (dateTime - now).Days;

                if (daysFromNow <= cycleEnd - cycleStart && daysFromNow >= 0)
                {
                    var movingList = lists[todayIndex - daysFromNow];
                    log.LogInformation($"Moving card {card.Name} to list {movingList.Name} since it is due in {daysFromNow} days");
                    await client.MoveCard(card, new ListPosition(movingList.Id));
                }
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
                await client.MoveCards(lists[i + 1], lists[i]);
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
