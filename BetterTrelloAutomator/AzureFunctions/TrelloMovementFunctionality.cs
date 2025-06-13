using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace BetterTrelloAutomator.AzureFunctions
{
    public partial class TrelloFunctionality
    {
        static int Instances = 0;

        private readonly ILogger<TrelloFunctionality> logger;

        readonly TrelloClient client;
        readonly TrelloBoardInfo boardInfo;

        SimpleTrelloRecord[] Lists => boardInfo.Lists;

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
            await SeparateNightTasks();
        }

        [Function("ManuallyTransitionDays")]
        [OpenApiOperation("TransitionDays", tags: ["MovementTesting"])]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully transitioned days forwards and out of future (if necessaru), and separated any night tasks")]
        public async Task<HttpResponseData> ManuallyTransitionDays([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            await TransitionDays();
            return req.CreateResponse(HttpStatusCode.OK);
        }

        [Function("DetransitionDays")]
        [OpenApiOperation("DetransitionDays", tags: ["MovementTesting"])]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully moved tasks one day backwards")]
        public async Task<HttpResponseData> DetransitionDays([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            logger.LogInformation($"De-transitioning Days MANUALLY");
            for (int i = boardInfo.CycleStart; i < boardInfo.CycleEnd; i++)
            {
                logger.LogInformation("Moving from {secondList} to {firstList}", Lists[i + 1], Lists[i]);
                await client.MoveCards(Lists[i + 1], Lists[i]);
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }

        async Task MoveFromFuture()
        {
            //Finding cards due within the next week and moving them to corresponding lists

            logger.LogInformation("Moving cards out of future list");

            var cards = await client.GetCards<SimpleTrelloCard>(Lists[boardInfo.FirstTodo]);

            foreach (var card in cards)
            {
                await MoveToCorrectList(card);
            }
        }

        int GetDaysToMove<TCard>(TCard card, out int maxDays) where TCard : SimpleTrelloCard
        {
            maxDays = boardInfo.CycleEnd - boardInfo.FirstTodo;
            
            string? date = card.Start ?? card.Due;
            if (date == null) return -1;

            var utcTime = DateTime.Parse(date, null, DateTimeStyles.AdjustToUniversal);
            DateTime dateTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, boardInfo.MyTimeZoneInfo); //Getting due datetime in my timezone

            int daysFromNow = (int)(dateTime - boardInfo.TodayStart).TotalDays; //How many days from now this card is due
            
            return Math.Min(maxDays, daysFromNow); //Finding the list to move to by time from today, capping it so it overflows into the general "future" list, or whichever is first
        }

        async Task MoveToCorrectList<TCard>(TCard card) where TCard : SimpleTrelloCard
        {
            int daysFromNow = GetDaysToMove(card, out int maxDays);

            if (daysFromNow >= 0 && daysFromNow < maxDays) //Checking if the card's due date isn't already expired or otherwise inapplicable
            {
                var movingList = Lists[boardInfo.TodayIndex - daysFromNow];
                logger.LogInformation("Moving card {cardName} to list {newList} since it is due in {daysFromNow} days", card.Name, movingList.Name, daysFromNow);
                await client.MoveCard(card, new TrelloListPosition(movingList.Id));

            }
        }

        [Function("ManuallyMoveFromFuture")]
        [OpenApiOperation("MoveFromFuture", ["MovementTesting"])]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully moved any relevant tasks out of future, and into the weekly TODO cycle")]
        public async Task<HttpResponseData> ManuallyMoveFromFuture([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            await MoveFromFuture();
            return req.CreateResponse(HttpStatusCode.OK);
        }

        async Task SeparateNightTasks()
        {
            var cards = await client.GetCards<LabeledTrelloCard>(Lists[boardInfo.TodayIndex]);

            foreach (var card in cards)
            {
                if (!card.Labels.ContainsName(TrelloLabel.Morning) && card.Labels.ContainsName(TrelloLabel.Night))
                {
                    await client.MoveCard(card, Lists[boardInfo.TonightIndex]);
                }
            }
        }

        [Function("ManuallySeparateNightTasks")]
        [OpenApiOperation("SeparateNightTasks", ["MovementTesting"])]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully moved all of today's night tasks to tonight")]
        public async Task<HttpResponseData> ManuallySeparateNightTasks([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            await SeparateNightTasks();
            return req.CreateResponse(HttpStatusCode.OK);
        }
        Task MergeNightTasks() => client.MoveCards(Lists[boardInfo.TonightIndex], Lists[boardInfo.TodayIndex]);

        [Function("MergeNightTasks")]
        public async Task MergeNightTasks([TimerTrigger("0 5 2 * * *")] TimerInfo info)
        {
            if (!timersEnabled)
            {
                logger.LogCritical($"TIMERS ARE DISABLED, SKIPPING {nameof(MergeNightTasks)}");
                return;
            }

            logger.LogInformation("Automatically merging night tasks {timerInfo}", info);
            await MergeNightTasks();
        }
        [Function("ManuallyMergeNightTasks")]
        [OpenApiOperation("MergeNightTasks", ["MovementTesting"])]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Moved tasks from tonight to today")]
        public async Task<HttpResponseData> ManuallyMergeNightTasks([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            await MergeNightTasks();
            return req.CreateResponse(HttpStatusCode.OK);
        }


        [Function("TransitionDays")]
        public async Task TransitionDays([TimerTrigger($"0 30 10 * * *")] TimerInfo info)
        {
            if (!timersEnabled)
            {
                logger.LogCritical($"TIMERS ARE DISABLED, SKIPPING {nameof(TransitionDays)}");
                return;
            }

            logger.LogInformation("Automatically transitioning Days {timerInfo}", info);
            await TransitionDays();
        }
    }
}
