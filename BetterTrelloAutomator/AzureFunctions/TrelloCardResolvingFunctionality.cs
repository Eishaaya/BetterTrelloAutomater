using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

using System.Net;
using System.Text.Json;

namespace BetterTrelloAutomator.AzureFunctions
{
    public partial class TrelloFunctionality
    {
        [Function("ManuallyResolveCard")]
        [OpenApiOperation("ResolveCard")]
        [OpenApiRequestBody("application/json", typeof(FullTrelloCard), Description = "Card to resolve")]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully took steps to resolve the specific card")] //TODO, look into multiple responses
        public async Task<HttpResponseData> ManuallyResolveCard([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            var card = await JsonSerializer.DeserializeAsync<FullTrelloCard>(req.Body) ?? throw new ArgumentException($"Invalid card format");
            return req.CreateResponse(await ResolveTickedCard(card));
        }

        [Function("ManuallyResolveTickedCards")]
        [OpenApiOperation("ResolveTickedCards")]
        [OpenApiRequestBody("application/json", typeof(int), Description = "List index to search in")]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully took steps to resolve all cards marked complete in the specified list")] //TODO, look into multiple responses
        public async Task<HttpResponseData> ManuallyResolveTickedCards([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            var listIndex = await JsonSerializer.DeserializeAsync<int>(req.Body);

            var cards = await client.GetCards<FullTrelloCard>(Lists[listIndex]);
            foreach (var card in cards)
            {
                if (!card.DueComplete) continue;

                var response = await ResolveTickedCard(card);
                if (response != HttpStatusCode.OK) return req.CreateResponse(response);
            }
            return req.CreateResponse(HttpStatusCode.OK);
        }

        public async Task<HttpStatusCode> ResolveTickedCard(FullTrelloCard card)
        {
            #region Setup

            //boardInfo.StringMovingCardIDs.Add(card.Id);

            logger.LogInformation("Received card {card} to resolve", card);

            var todayDate = boardInfo.Now - new TimeSpan(Constants.DayStartHour, Constants.DayStartMinute, 0); //Counting anytime before "morning" as the day before. This is means that midnight of the 16th is still treated as the 15th

            DateTimeOffset? start = card.Start?.ToOffset(boardInfo.TimeZoneOffset);
            DateTimeOffset due = (card.Due ?? start ?? DateTimeOffset.UtcNow).ToOffset(boardInfo.TimeZoneOffset);

            DateTimeOffset knownDate = start ?? due;

            bool isStrict = card.Labels.ContainsName(TrelloLabel.Strict);
            bool isTask = card.Labels.ContainsName(TrelloLabel.Task);

            foreach (var checklist in card.Checklists)
            {
                checklist.CheckItems.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            }

            List<Task> tasksToDo = [];



            #endregion

            #region Helper Functions

            async Task<bool> HandleRoutineTask() //Returns if further action should be skipped
            {
                if (isTask)
                {
                    logger.LogInformation("Periodic task detected");

                    if (card.Due != null)
                    {
                        if (card.Due.Value < DateTimeOffset.UtcNow || (start ?? due) > card.Due)
                        {
                            logger.LogInformation("Completing periodic task");
                            await client.MoveCard(card, Lists[boardInfo.DoneIndex]);
                            return true;
                        }
                    }
                }
                return false;
            }

            Task TryCompleteItems(FullTrelloCard? changingCard = null) //Complete all items until reaching already completed ones. This reduces API calls & leaves intentional blanks alive
            {
                changingCard ??= card;
                List<Task> checks = [];
                foreach (var checklist in changingCard.Checklists)
                {
                    for (int i = checklist.CheckItems.Count - 1; i >= 0; i--)
                    {
                        if (checklist.CheckItems[i].State == CheckItem.Complete) break;

                        checks.Add(client.CompleteCheckItem(changingCard, checklist.CheckItems[i]));
                    }
                }
                return Task.WhenAll(checks);
            }

            void TrySetDates()
            {
                start = start?.TrySetDate(todayDate);
                if (!isTask)
                {
                    due = due.TrySetDate(todayDate)!;
                }
            }
            void TryPushDates(double dayChange)
            {
                start = start?.AddDays(dayChange);
                if (!isTask)
                {
                    due = due.AddDays(dayChange);
                }
            }

            #endregion

            #region Main Logic

            if (card.Labels.ContainsTime(out var time, TrelloLabel.Monthly, TrelloLabel.Biweekly, TrelloLabel.Weekly))
            {

                #region Periodic Tasks

                #region Setup

                logger.LogInformation("Resolving {TimeType} card", time.Name);
                int dayChange = time.DayCount;

                #endregion

                #region Handle Checklist

                tasksToDo.Add(TryCompleteItems());

                #endregion

                #region Dates

                if (isStrict)
                {
                    logger.LogInformation("Card is strict");

                    if (time == TrelloLabel.Monthly)
                    {
                        var date = knownDate;

                        var newDate = date + TimeSpan.FromDays(dayChange = TrelloLabel.Weekly.DayCount * 4);

                        var beginningOfDueWeek = (newDate.Day / TrelloLabel.Weekly.DayCount) * TrelloLabel.Weekly.DayCount;

                        if (newDate.Day <= beginningOfDueWeek)
                        {
                            dayChange += TrelloLabel.Weekly.DayCount;
                        }
                    }

                    //Getting new times from originals
                    TryPushDates(dayChange);
                }
                else
                {
                    logger.LogInformation("Card is not strict");

                    //Getting new times from now
                    TrySetDates();
                    TryPushDates(dayChange);
                }

                #endregion

                #region Movement/Cloning

                if (await HandleRoutineTask()) return HttpStatusCode.OK;

                tasksToDo.Add(client.MoveCard(card, Lists[boardInfo.RoutineIndex]));

                var newCard = new SimpleTrelloCard(card.Name, card.Id, start, due, false);

                var correctIndex = GetIndexToMoveTo(newCard);
                logger.LogInformation("Moving cloned card {newCard} to list at index {correctIndex}", newCard, correctIndex);
                tasksToDo.Add(client.CloneCard(newCard, Lists[correctIndex]));

                #endregion

                #endregion
            }
            else if (card.Labels.ContainsName(out var cardType, TrelloLabel.Daily, TrelloLabel.Reverse))
            {
                #region Daily Tasks

                #region Setup

                logger.LogInformation("Resolving {cardType} card", cardType);
                bool isDivided = card.Labels.ContainsName(TrelloLabel.Morning) && card.Labels.ContainsName(TrelloLabel.Night);

                bool complete = true;

                int totalIncompleteCount = 0;

                var endingSaturday = due + TimeSpan.FromDays(DayOfWeek.Saturday - due.DayOfWeek);
                bool simplyEndTask = todayDate.Date > endingSaturday;


                #endregion

                #region Handle Checklist

                DayOfWeek cardDay = boardInfo.Now >= knownDate ? todayDate.DayOfWeek : knownDate.DayOfWeek; //Doing day-based judgements based off today or the card's startdate (if it begins after today)

                double extraDayOffset = 0;
                if (!simplyEndTask)
                {
                    foreach (var checkList in card.Checklists)
                    {
                        int incompleteCount = totalIncompleteCount;
                        DayOfWeek? releventPreviousDay = null;

                        DayOfWeek? lastSkippedDay = null;

                        foreach (var currItem in checkList.CheckItems)
                        {
                            var checkItemDay = currItem.Name.AsDayOfWeek();

                            if (currItem.State != CheckItem.Incomplete)
                            {
                                lastSkippedDay = checkItemDay;
                                releventPreviousDay = null;
                                continue;
                            }


                            //Only allowed to mark previous days if we're in a strict task like stretching, which carries over, otherwise we are allowed to mark current or future days (if all current days are checked)
                            if (checkItemDay == null || checkItemDay < cardDay)
                            {
                                if (!isStrict) continue; //Skipping non-weekday marked items except on strict cards which are allowed to handle them
                            }
                            else if (checkItemDay == releventPreviousDay && !isDivided) //Allowing side-by-side checkItems marked for the same day to be checked together, except when they're representing two separate occasions
                            {
                                incompleteCount--;
                            }
                            releventPreviousDay = checkItemDay; //Null days will never be counted even if stored, so this is harmless

                            if (incompleteCount <= 0) //If we haven't completed more than one "bunch" of cards
                            {
                                if (isDivided && todayDate.TimeOfDay >= boardInfo.TonightStart.TimeOfDay && lastSkippedDay != checkItemDay)
                                {
                                    lastSkippedDay = checkItemDay;
                                    extraDayOffset = .5;
                                    continue; // Skipping the first card for the set day if it's nighttime
                                }
                                tasksToDo.Add(client.CompleteCheckItem(card, currItem));
                            }
                            incompleteCount++;
                        }


                        if (isDivided)
                        {
                            totalIncompleteCount += incompleteCount; //Making it so divided tasks only check one item on all lists
                        }

                        logger.LogInformation("Checklist {checklist} had {incompleteCount} incomplete items/bunches", checkList.Name, incompleteCount);
                        complete &= incompleteCount <= 1;
                    }


                    #endregion

                    #region Dates
                }
                bool isDividing = isDivided && todayDate.TimeOfDay < boardInfo.TonightStart.TimeOfDay && totalIncompleteCount > 1;

                if (!isStrict)
                {
                    TrySetDates();
                }
                if (isDivided)
                {
                    TryPushDates(.5 + extraDayOffset);
                }
                else
                {
                    TryPushDates(1);
                }


                #endregion

                #region Movement/Cloning

                var newCard = new SimpleTrelloCard(card.Name, card.Id, start, due, false);

                bool isDaily = card.Labels.ContainsName(TrelloLabel.Daily);
                bool isReverse = card.Labels.ContainsName(TrelloLabel.Reverse);
                int movingIndex = isDaily ? GetIndexToMoveTo(newCard) : boardInfo.WindDownIndex;

                if (!complete)
                {
                    logger.LogInformation("Card is not complete");

                    if (isDividing) //If a divided task is incomplete  
                    {
                        logger.LogInformation("Moving incomplete divided task to tonight");
                        tasksToDo.Add(client.UpdateCard(newCard, new(Lists[boardInfo.TonightIndex].Id, "bottom"))); //Moving to tonight, but not applying any date changes
                    }
                    else if (isDaily && due > boardInfo.TomorrowStart)
                    {
                        logger.LogInformation("Moving extant card {newCard} to {movingIndex}", newCard, movingIndex);
                        tasksToDo.Add(client.UpdateCard(newCard, Lists[movingIndex]));
                    }
                    else
                    {
                        logger.LogInformation("Updating extant card {newCard}", newCard);
                        tasksToDo.Add(client.UpdateCard(newCard));
                    }
                }
                else
                {
                    //Being completed means all lists are filled OR the last day of the week is filled
                    //Any remaining unticked checkItems findable through the back are descriptive for the daily task and should be ticked:

                    if (await HandleRoutineTask()) return HttpStatusCode.OK;

                    tasksToDo.Add(client.MoveCard(card, Lists[boardInfo.RoutineIndex]));

                    logger.LogInformation("Moving cloned card {newCard} to {movingIndex}", newCard, movingIndex);

                    //Completing items when we're not resolving a card that was left until the next week
                    if (!simplyEndTask)
                    {
                        tasksToDo = [TryCompleteItems()];
                    }

                    var cloningCard = client.CloneCard(newCard, Lists[movingIndex]);
                    //Filling up reversed cards so user must untick them instead of tick
                    if (isReverse)
                    {
                        var clonedCard = await cloningCard;
                        tasksToDo.Add(TryCompleteItems(clonedCard));
                    }
                    else
                    {
                        tasksToDo.Add(cloningCard);
                    }

                    await Task.WhenAll(tasksToDo);
                }
                #endregion

                #endregion
            }
            else if (isTask)
            {
                #region Setup

                logger.LogInformation("Completing standard task");

                #endregion

                #region Handle Checklist

                tasksToDo.Add(TryCompleteItems());

                #endregion

                #region Movement

                tasksToDo.Add(client.MoveCard(card, Lists[boardInfo.DoneIndex]));

                #endregion
            }
            else if (card.Labels.ContainsName(TrelloLabel.Static))
            {
                #region Setup

                logger.LogInformation("Completing static task");

                #endregion

                #region Handle Checklist

                tasksToDo.Add(TryCompleteItems());

                #endregion

                #region Movement/Cloning

                tasksToDo.Add(client.MoveCard(card, Lists[boardInfo.RoutineIndex]));
                tasksToDo.Add(client.CloneCard(card.Simpify(), Lists[boardInfo.WindDownIndex]));

                #endregion
            }
            else
            {
                logger.LogCritical("Received unhandleable card");
                return HttpStatusCode.BadRequest;
            }

            #endregion

            #region Finish

            await Task.WhenAll(tasksToDo);

            return HttpStatusCode.OK;

            #endregion
        }

        [Function("AutoResolveCard")]
        [OpenApiOperation("ResolveTickedCard")]
        [OpenApiRequestBody("application/json", typeof(WebhookResponse), Description = "Webhook trigger info")]
        public async Task<HttpResponseData> ResolveTickedCardFromWebhook([HttpTrigger(AuthorizationLevel.Anonymous, "post", "head")] HttpRequestData req)
        {
            logger.LogInformation("Webhook triggered");
            var input = await req.ReadAsStringAsync();


            if (req.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)) return req.CreateResponse(HttpStatusCode.OK);

            logger.LogInformation($"RAW INPUT: {input}");
            var response = JsonSerializer.Deserialize<WebhookResponse>(input, client.CaseInsensitive);


            var basicCard = response.Action.Data.Card;
            logger.LogInformation($"NOW VALIDATING: {response}");


            try
            {
                if (response.Action.Type != "updateCard")
                {
                    logger.LogInformation("Skipping Unimportant action");
                    return req.CreateResponse(HttpStatusCode.PreconditionFailed);
                }
                if (basicCard.DueComplete == false)
                {
                    logger.LogInformation("Skipping unticked card");
                    return req.CreateResponse(HttpStatusCode.PreconditionFailed);
                }
                if (response.Action.Data.Old?.DueComplete == true)
                {
                    logger.LogInformation("Skipping unchanged or already ticked card");
                    return req.CreateResponse(HttpStatusCode.PreconditionFailed);
                }

                //if (response!.Action.MemberCreator.FullName.Contains("Bot", StringComparison.OrdinalIgnoreCase)) return req.CreateResponse(HttpStatusCode.PreconditionFailed);

            }
            catch (Exception ex)
            {
                logger.LogError($"PROBLEM VALIDATING {response}, \nThrew: {ex}");
            }



            FullTrelloCard fullCard;
            try
            {

                fullCard = await client.GetCard<FullTrelloCard>(basicCard.Id);

            }
            catch (Exception ex)
            {
                logger.LogError("FAILLED TO GET CARD " + ex);
                throw ex;
            }

            var output = await ResolveTickedCard(fullCard);

            //boardInfo.StringMovingCardIDs.Remove(basicCard.Id);
            try
            {
                return req.CreateResponse(output);
            }
            catch (Exception ex)
            {
                logger.LogError($"FAILED TO PROCESS {fullCard}");
                logger.LogError($"EXCEPTION: {ex}");
                throw ex;
            }
        }

        [Function("CreateResolutionHook")]
        [OpenApiOperation("CreateResolutionHook")]
        public async Task<HttpResponseData> CreateResolutionHook([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            string url = cardResolutionURL ?? throw new ArgumentNullException("Desired URL null or nonexistant");

            var hookOutput = await client.CreateBoardHook(url, "Webhook to trigger card resolution logic");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(hookOutput);
            return response;
        }
    }
}
