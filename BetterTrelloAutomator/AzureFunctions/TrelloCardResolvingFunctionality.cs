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

            var todayDate = DateTime.UtcNow - new TimeSpan(Constants.DayStartHour, Constants.DayStartMinute, 0); //Counting anytime before "morning" as the day before. This is means that midnight of the 16th is still treated as the 15th

            string? start = card.Start;
            string due = card.Due!;

            string? newStart = null;
            string newDue = null!;

            string dateString = start ?? (due ??= DateTime.UtcNow.ToString());

            bool isStrict = card.Labels.ContainsName(TrelloLabel.Strict);

            bool isTask = false;
            async Task<bool> HandleRoutineTask() //Returns if further action should be skipped
            {
                if (card.Labels.ContainsName(TrelloLabel.Task))
                {
                    isTask = true;
                    logger.LogInformation("Periodic task detected");

                    if (DateTime.TryParse(card.Due, out var taskDue))
                    {
                        newDue = card.Due;
                        if (taskDue < DateTime.UtcNow || DateTime.Parse(newStart ?? newDue!) > taskDue)
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
                        var date = DateTime.Parse(dateString, null, DateTimeStyles.AdjustToUniversal);

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
                    newStart = start.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                    newDue = due.TrySetDate(todayDate + TimeSpan.FromDays(dayChange))!;
                }

                if (await HandleRoutineTask()) return HttpStatusCode.OK;

                await client.MoveCard(card, Lists[boardInfo.RoutineIndex]);

                var newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue);

                int correctIndex = GetDaysToMove(newCard, out _);
                if (correctIndex == -1) //If the card were to be moved somewhere invalid, our date is sometime far in the past
                {
                    //Resetting date to now
                    newStart = start.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                    if (!isTask)
                    {
                        newDue = due.TrySetDate(todayDate + TimeSpan.FromDays(dayChange))!;
                    }
                    newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue);
                    correctIndex = GetDaysToMove(newCard, out _);
                }
                correctIndex = boardInfo.TodayIndex - correctIndex;

                logger.LogInformation("Moving cloned card {newCard} to list at index {correctIndex}", newCard, correctIndex);

                await client.CloneCard(newCard, Lists[correctIndex]);

                #endregion
            }
            else if (card.Labels.ContainsName(out _, TrelloLabel.Daily, TrelloLabel.Reverse/*, TrelloLabel.Static*/))
            {
                bool isDivided = card.Labels.ContainsName(TrelloLabel.Morning) && card.Labels.ContainsName(TrelloLabel.Night);

                bool complete = true;

                foreach (var checkList in card.Checklists)
                {
                    int incompleteCount = 0;
                    DayOfWeek? previousDay = null;

                    foreach (var currItem in checkList.CheckItems)
                    {
                        if (currItem.State != CheckItem.Incomplete)
                        {
                            previousDay = null;
                            continue;
                        }

                        var checkItemDay = currItem.Name.AsDayOfWeek();

                        if (checkItemDay == null) continue;

                        if (checkItemDay == previousDay && !isDivided) //Allowing side-by-side checkItems marked for the same day to be checked together, except when they're representing two separate occasions
                        {
                            incompleteCount--;
                        }
                        previousDay = checkItemDay;

                        if (isStrict || boardInfo.TodayDay <= checkItemDay) //Only allowed to mark previous days if we're in a strict task like stretching, which carries over
                        {
                            if (incompleteCount == 0) //If we haven't completed more than one "bunch" of cards
                            {
                                await client.CompleteCheckItem(card, currItem);
                            }
                            incompleteCount++;
                        }
                    }

                    logger.LogInformation("Checklist {checklist} had {incompleteCount} incomplete items/bunches", checkList.Name, incompleteCount);
                    complete &= incompleteCount <= 1;
                }

                newStart = start.TrySetDate(todayDate + TimeSpan.FromDays(1));
                newDue = due.TrySetDate(todayDate + TimeSpan.FromDays(1))!;

                bool isDaily = card.Labels.ContainsName(TrelloLabel.Daily);

                if (!complete)
                {
                    logger.LogInformation("Card is not complete");

                    await client.UpdateCard(new SimpleTrelloCard(card.Name, card.Id, newStart, newDue));

                    if (isDivided && boardInfo.Now < boardInfo.TonightStart)
                    {
                        logger.LogInformation("Moving incomplete divided task to tonight");
                        await client.MoveCard(card, Lists[boardInfo.TonightIndex]);
                    }
                    else if (isDaily)
                    {
                        logger.LogInformation("Moving incomplete daily task to tomorrow");
                        await client.MoveCard(card, Lists[boardInfo.TomorrowIndex]);
                    }
                }
                else
                {
                    if (await HandleRoutineTask()) return HttpStatusCode.OK;

                    await client.MoveCard(card, Lists[boardInfo.RoutineIndex]);

                    var newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue);

                    int movingIndex = isDaily ? boardInfo.TomorrowIndex : boardInfo.WindDownIndex;

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
