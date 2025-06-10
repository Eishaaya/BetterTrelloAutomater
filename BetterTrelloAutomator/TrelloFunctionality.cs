using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

#pragma warning disable IDE0060 // Remove unused parameter warnings for API params


namespace BetterTrelloAutomator
{
    public class TrelloFunctionality
    {      
        static int Instances = 0;

        private readonly ILogger<TrelloFunctionality> logger;

        readonly TrelloClient client;
        readonly TrelloBoardInfo boardInfo;

        SimplifiedTrelloRecord[] Lists => boardInfo.Lists;

        readonly bool timersEnabled;
        public TrelloFunctionality(TrelloClient client, TrelloBoardInfo boardInfo, ILogger<TrelloFunctionality> logger, IConfiguration config)
        {
            this.client = client;
            this.boardInfo = boardInfo;
            this.logger = logger;

            logger.LogInformation("INIT TO FUNCTIONALITY # {InstanceCount}", Instances++);

            string? timersEnabled = config["ENABLE_TRELLO_TIMERS"];
            if (!bool.TryParse(timersEnabled, out this.timersEnabled))
            {
                logger.LogWarning("FAILED TO READ TIMER ENABLING CONFIG, DEFAULTING TO FALSE");
            }
        }      

        async Task TransitionDays()
        {
            //Shifting cards over

            logger.LogInformation("CYCLE: {CycleStart} - {cycleEnd}", boardInfo.CycleStart, boardInfo.CycleEnd);
            for (int i = boardInfo.CycleEnd - 1; i >= boardInfo.CycleStart; i--)
            {
                logger.LogInformation("Moving from {firstCard} to {secondCard}", Lists[i].Name, Lists[i + 1].Name);
                await client.MoveCards(Lists[i], Lists[i + 1]);
            }

            await MoveFromFuture();
        }

        async Task MoveFromFuture()
        {
            //Finding cards due within the next week and moving them to corresponding lists

            logger.LogInformation("Moving cards out of future list");

            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, boardInfo.MyTimeZoneInfo);
            now -= new TimeSpan(now.Hour, now.Minute + 1, now.Second); //getting the beginning of the day

            var cards = await client.GetCards(Lists[boardInfo.FirstTodo]);
            foreach (var card in cards)
            {
                string date = card.Start ?? card.Due;

                if (date == null) continue;

                var utcTime = DateTime.Parse(date, null, DateTimeStyles.AdjustToUniversal);
                DateTime dateTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, boardInfo.MyTimeZoneInfo);

                int daysFromNow = (int)(dateTime - now).TotalDays;

                if (daysFromNow <= boardInfo.CycleEnd - boardInfo.CycleStart && daysFromNow >= 0)
                {
                    var movingList = Lists[boardInfo.TodayIndex - daysFromNow];
                    logger.LogInformation("Moving card {cardName} to list {newList} since it is due in {daysFromNow} days", card.Name, movingList.Name, daysFromNow);
                    await client.MoveCard(card, new TrelloListPosition(movingList.Id));
                }
            }
        }
        [Function("ManuallyMoveFromFuture")]
        public async Task ManuallyMoveFromFuture([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req) => await MoveFromFuture();

        async Task SeparateNightTasks()
        {
            var cards = await client.GetCards(Lists[boardInfo.TodayIndex]);

            foreach (var card in cards)
            {

            }
        }

        [Function("TransitionDays")]
        public async Task TransitionDays([TimerTrigger("0 30 10 * * *")] TimerInfo info)
        {
            if (!timersEnabled)
            {
                logger.LogCritical($"TIMERS ARE DISABLED, SKIPPING {nameof(TransitionDays)}");
                return;
            }

            logger.LogInformation("Automatically transitioning Days {timerInfo}", info);
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
            logger.LogInformation($"De-transitioning Days MANUALLY");
            for (int i = boardInfo.CycleStart; i < boardInfo.CycleEnd; i++)
            {
                logger.LogInformation("Moving from {secondList} to {firstList}", Lists[i + 1], Lists[i]);
                await client.MoveCards(Lists[i + 1], Lists[i]);
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
