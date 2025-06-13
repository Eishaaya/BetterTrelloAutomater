using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Globalization;
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
            var todayDate = DateTime.UtcNow - new TimeSpan(Constants.DayStartHour, Constants.DayStartMinute, 0); //Counting anytime before "morning" as the day before. This is means that midnight of the 16th is still treated as the 15th

            if (card.Labels.ContainsTime(out var time, TrelloLabel.Monthly, TrelloLabel.Biweekly, TrelloLabel.Weekly))
            {
                #region Periodic Tasks

                await client.CompleteAllCheckedItems(card);
                logger.LogInformation("Resolving {TimeType} card", time.Name);

                int dayChange = time.DayCount;
                string? newStart = null;
                string? newDue = null;

                string? start = card.Start;
                string? due = card.Due;

                string dateString = start ?? (due ??= DateTime.UtcNow.ToString());

                if (card.Labels.ContainsName(TrelloLabel.Strict))
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
                    newDue = due.TryAddDays(dayChange);
                }
                else
                {
                    logger.LogInformation("Card is not strict");
                    //Getting new times from now
                    newStart = start.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                    newDue = due.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                }

                bool isTask = false;
                if (card.Labels.ContainsName(TrelloLabel.Task))
                {
                    isTask = true;
                    logger.LogInformation("Periodic task detected");

                    if (DateTime.TryParse(card.Due, out var taskDue))
                    {
                        newDue = card.Due;
                        if (taskDue < DateTime.UtcNow)
                        {
                            logger.LogInformation("Completing periodic task");

                            await client.MoveCard(card, Lists[boardInfo.DoneIndex]);
                            return HttpStatusCode.OK;
                        }
                    }
                }

                await client.MoveCard(card, Lists[boardInfo.RoutineIndex]);

                var newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue);

                int correctIndex = GetDaysToMove(newCard, out _);
                if (correctIndex == -1) //If the card were to be moved somewhere invalid, our date is sometime far in the past
                {
                    //Resetting date to now
                    newStart = start.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                    if (!isTask)
                    {
                        newDue = due.TrySetDate(todayDate + TimeSpan.FromDays(dayChange));
                    }
                    newCard = new SimpleTrelloCard(card.Name, card.Id, newStart, newDue);
                    correctIndex = GetDaysToMove(newCard, out _);
                }
                correctIndex = boardInfo.TodayIndex - correctIndex;

                logger.LogInformation("Moving cloned card {newCard} to list at index {correctIndex}", newCard, correctIndex);

                await client.CloneCard(newCard, Lists[correctIndex]);
                
                #endregion
            }
            else if (card.Labels.ContainsName(out _, TrelloLabel.Daily, TrelloLabel.Reverse, TrelloLabel.Static))
            {

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
