using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Globalization;
using Microsoft.Extensions.Configuration;

#pragma warning disable IDE0060 // Remove unused parameter warnings for API params


namespace BetterTrelloAutomator
{
    public class TrelloFunctionality
    {
        private readonly ILogger<TrelloFunctionality> logger;

        readonly string timeZone = "Pacific Standard Time"; //TODO: ping my phone or laptop to get its actual location
        TimeZoneInfo MyTimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        SimplifiedTrelloRecord[] lists;
        int firstTodo;
        int todayIndex;
        int CycleStart => firstTodo + 1;
        int cycleEnd;

        readonly TrelloClient client;
        readonly bool timersEnabled;
        public TrelloFunctionality(TrelloClient client, ILogger<TrelloFunctionality> logger, IConfiguration config)
        {
            this.client = client;
            this.logger = logger;

            string? timersEnabled = config["ENABLE_TRELLO_TIMERS"];
            if (!bool.TryParse(timersEnabled, out this.timersEnabled))
            {
                logger.LogWarning("FAILED TO READ TIMER ENABLING CONFIG, DEFAULTING TO FALSE");
            }
        }

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

            for (int i = cycleEnd; i >= CycleStart; i--)
            {
                if (lists[i].Name.Contains("today", StringComparison.OrdinalIgnoreCase))
                {
                    todayIndex = i;
                    break;
                }
            }
        }


        async Task TransitionDays()
        {
            //Shifting cards over

            await GetLists();
            logger.LogInformation($"CYCLE: {CycleStart} - {cycleEnd}");
            for (int i = cycleEnd - 1; i >= CycleStart; i--)
            {
                logger.LogInformation($"Moving from {lists[i].Name} to {lists[i + 1].Name}");
                await client.MoveCards(lists[i], lists[i + 1]);
            }

            //Finding cards due within the next week and moving them to corresponding lists

            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MyTimeZoneInfo);
            now -= new TimeSpan(now.Hour, now.Minute + 1, now.Second); //getting the beginning of the day

            var cards = await client.GetCards(lists[firstTodo]);
            foreach (var card in cards)
            {
                string date = card.Start ?? card.Due;

                if (date == null) continue;

                var utcTime = DateTime.Parse(date, null, DateTimeStyles.AdjustToUniversal);
                DateTime dateTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, MyTimeZoneInfo);

                int daysFromNow = (dateTime - now).Days;

                if (daysFromNow <= cycleEnd - CycleStart && daysFromNow >= 0)
                {
                    var movingList = lists[todayIndex - daysFromNow];
                    logger.LogInformation($"Moving card {card.Name} to list {movingList.Name} since it is due in {daysFromNow} days");
                    await client.MoveCard(card, new ListPosition(movingList.Id));
                }
            }
        }

        [Function("TransitionDays")]
        public async Task TransitionDays([TimerTrigger("0 30 10 * * *")] TimerInfo info)
        {
            if (!timersEnabled)
            {
                logger.LogCritical($"TIMERS ARE DISABLED, SKIPPING {nameof(TransitionDays)}");
            }

            logger.LogInformation($"Automatically transitioning Days {info}");
            await TransitionDays();
        }

        [Function("ManuallyTransitionDays")]
        public async Task ManuallyTransitionDays([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            logger.LogInformation($"Transitioning Days MANUALLY");
            await TransitionDays();
        }
        [Function("DetransitionDays")]
        public async Task DetransitionDays([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            await GetLists();
            logger.LogInformation($"De-transitioning Days MANUALLY");
            for (int i = CycleStart; i < cycleEnd; i++)
            {
                logger.LogInformation($"Moving from {lists[i + 1].Name} to {lists[i].Name}");
                await client.MoveCards(lists[i + 1], lists[i]);
            }
        }

        [Function("GetPersonalID")]
        public async Task<IActionResult> GetPersonalID([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            logger.LogInformation("Getting ID");
            return new OkObjectResult(await client.GetPersonalBoardID());
        }

        [Function("GetLists")]
        public async Task<IActionResult> GetLists([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            logger.LogInformation("User requesting to get lists");
            return new OkObjectResult(await client.GetLists());
        }



    }
}
