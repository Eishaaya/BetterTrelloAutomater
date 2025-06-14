using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterTrelloAutomator.AzureFunctions
{
    public partial class TrelloFunctionality
    {
        [Function("ManuallyResolveTickedCard")]
        [OpenApiOperation("ResolveTickedCard")]
        [OpenApiRequestBody("application/json", typeof(FullTrelloCard), Description = "Card to resolve")]
        [OpenApiResponseWithoutBody(HttpStatusCode.OK, Description = "Successfully took steps to resolve the specific card")] //TODO, look into multiple responses
        public async Task<HttpResponseData> ManuallyResolveTickedCard([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            var card = await JsonSerializer.DeserializeAsync<FullTrelloCard>(req.Body) ?? throw new ArgumentException($"Invalid card format");
            return req.CreateResponse(await ResolveTickedCard(card));
        }

        public async Task<HttpStatusCode> ResolveTickedCard(FullTrelloCard card)
        {
            logger.LogInformation("Received card {card} to resolve", card);

            var todayDate = boardInfo.Now - new TimeSpan(Constants.DayStartHour, Constants.DayStartMinute, 0); //Counting anytime before "morning" as the day before. This is means that midnight of the 16th is still treated as the 15th

            string? start = card.Start;
            string due = card.Due ?? start ?? DateTimeOffset.UtcNow.ToString();

            string? newStart = null;
            string newDue = null!;

            string dateString = start ?? due;

            bool isStrict = card.Labels.ContainsName(TrelloLabel.Strict);

            bool isTask = false;
            async Task<bool> HandleRoutineTask() //Returns if further action should be skipped
            {
                if (card.Labels.ContainsName(TrelloLabel.Task))
                {
                    isTask = true;
                    logger.LogInformation("Periodic task detected");

                    if (DateTimeOffset.TryParse(card.Due, out var taskDue))
                    {
                        newDue = card.Due;
                        if (taskDue < DateTimeOffset.UtcNow || DateTimeOffset.Parse(newStart ?? newDue!) > taskDue)
                        {
                            logger.LogInformation("Completing periodic task");
                            await client.MoveCard(card, Lists[boardInfo.DoneIndex]);
                            return true;
                        }
                    }
                }
                return false;
            }

            if (card.Labels.ContainsTime(out var time, TrelloLabel.Monthly, TrelloLabel.Biweekly, TrelloLabel.Weekly))
            {
                #region Periodic Tasks

                await client.CompleteAllCheckedItems(card);
                logger.LogInformation("Resolving {TimeType} card", time.Name);

                int dayChange = time.DayCount;

                if (isStrict)
                {
                    logger.LogInformation("Card is strict");

                    if (time == TrelloLabel.Monthly)
                    {
                        var date = DateTimeOffset.Parse(dateString);

                        var newDate = date + TimeSpan.FromDays(dayChange = TrelloLabel.Weekly.DayCount * 4);

                        if (newDate.Day > date.Day)
                        {
                            dayChange += TrelloLabel.Weekly.DayCount;
                        }
                    }

                    //Getting new times from originals
                    newStart = start.TryAddDays(dayChange);
                    newDue = due.TryAddDays(dayChange)!;
                }
                else
                {
                    logger.LogInformation("Card is not strict");
                    //Getting new times from now
                    newStart = start.TrySetDate(todayDate);
                    newDue = due.TrySetDate(todayDate)!;

                    newStart = start.TryAddDays(dayChange);
                    newDue = due.TryAddDays(dayChange)!;
                }

                if (await HandleRoutineTask()) return HttpStatusCode.OK;

                await client.MoveCard(card, Lists[boardInfo.RoutineIndex]);

                var newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue, false);

                //Resetting date to now
                newStart = start.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                if (!isTask)
                {
                    newDue = due.TrySetDate(todayDate + TimeSpan.FromDays(dayChange))!;
                }
                newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue, false);

                var correctIndex = GetIndexToMoveTo(newCard);

                logger.LogInformation("Moving cloned card {newCard} to list at index {correctIndex}", newCard, correctIndex);

                await client.CloneCard(newCard, Lists[correctIndex]);

                #endregion
            }
            else if (card.Labels.ContainsName(out _, TrelloLabel.Daily, TrelloLabel.Reverse/*, TrelloLabel.Static*/))
            {
                bool isDivided = card.Labels.ContainsName(TrelloLabel.Morning) && card.Labels.ContainsName(TrelloLabel.Night);

                bool complete = true;

                int totalIncompleteCount = 0;

                foreach (var checkList in card.Checklists)
                {
                    checkList.CheckItems.Sort((a, b) => a.Pos.CompareTo(b.Pos)); //Sorting items by list position (they default to creation date per ID)

                    int incompleteCount = totalIncompleteCount;
                    DayOfWeek? previousDay = null;

                    foreach (var currItem in checkList.CheckItems)
                    {
                        if (currItem.State != CheckItem.Incomplete)
                        {
                            previousDay = null;
                            continue;
                        }

                        var checkItemDay = currItem.Name.AsDayOfWeek();

                        if (isStrict)
                        {
                            checkItemDay ??= (DayOfWeek)(-1); //Storing a garbage value, so we ignore non-weekdays
                        }

                        if (checkItemDay == null) continue;

                        if (checkItemDay == previousDay && !isDivided) //Allowing side-by-side checkItems marked for the same day to be checked together, except when they're representing two separate occasions
                        {
                            incompleteCount--;
                        }
                        previousDay = checkItemDay;

                        if (isStrict || boardInfo.TodayDay <= checkItemDay) //Only allowed to mark previous days if we're in a strict task like stretching, which carries over, otherwise we are allowed to mark current or future days (if all current days are checked)
                        {
                            if (incompleteCount == 0) //If we haven't completed more than one "bunch" of cards
                            {
                                if (isDivided && totalIncompleteCount == 0 && boardInfo.Now >= boardInfo.TonightStart) continue; // Skipping the first card for the set day if it's nighttime

                                await client.CompleteCheckItem(card, currItem);
                            }
                            incompleteCount++;
                        }
                    }

                    if (isDivided)
                    {
                        totalIncompleteCount += incompleteCount; //Making it so divided tasks only check one item on all lists
                    }

                    logger.LogInformation("Checklist {checklist} had {incompleteCount} incomplete items/bunches", checkList.Name, incompleteCount);
                    complete &= incompleteCount <= 1;
                }

                if (!isStrict)
                {
                    newStart = start.TrySetDate(todayDate);
                    newDue = due.TrySetDate(todayDate)!;
                }
                newStart = start.TryAddDays(1);
                newDue = due.TryAddDays(1)!;


                var newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue, false);

                bool isDaily = card.Labels.ContainsName(TrelloLabel.Daily);
                int movingIndex = isDaily ? GetIndexToMoveTo(newCard) : boardInfo.WindDownIndex;

                if (!complete)
                {
                    logger.LogInformation("Card is not complete");

                    await client.UpdateCard(newCard);

                    if (isDivided && boardInfo.Now < boardInfo.TonightStart && totalIncompleteCount > 1) //If a divided task is incomplete  
                    {
                        logger.LogInformation("Moving incomplete divided task to tonight");
                        await client.MoveCard(card, new(Lists[boardInfo.TonightIndex].Id, "bottom"));
                    }
                    else if (isDaily && DateTimeOffset.Parse(newDue) > boardInfo.TomorrowStart)
                    {
                        logger.LogInformation("Moving extant card {newCard} to {movingIndex}", newCard, movingIndex);
                        await client.MoveCard(card, Lists[GetIndexToMoveTo(card)]);
                    }
                }
                else
                {
                    if (await HandleRoutineTask()) return HttpStatusCode.OK;

                    await client.MoveCard(card, Lists[boardInfo.RoutineIndex]);

                    logger.LogInformation("Moving cloned card {newCard} to {movingIndex}", newCard, movingIndex);
                    await client.CloneCard(newCard, Lists[movingIndex]);
                }




                //if (complete) //Completing any non-day-specific tasks on a completed card
                //{
                //    foreach (var checkList in card.Checklists)
                //    {
                //        foreach (var currItem in checkList.CheckItems)
                //        {
                //            if (currItem.State == CheckItem.Incomplete && currItem.Name.AsDayOfWeek() == null)
                //            {
                //                await client.CompleteCheckItem(card, currItem);
                //            }
                //        }
                //    }
                //}
            }
            else if (card.Labels.ContainsName(TrelloLabel.Task))
            {
                logger.LogInformation("Completing standard task");

                await client.MoveCard(card, Lists[boardInfo.DoneIndex]);
                await client.CompleteAllCheckedItems(card);
            }
            else
            {
                logger.LogCritical("Received unhandleable card");
                return HttpStatusCode.BadRequest;
            }
            return HttpStatusCode.OK;
        }
    }
}
