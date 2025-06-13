using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
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
            await ResolveTickedCard(card);
            return req.CreateResponse(HttpStatusCode.OK);
        }

        public async Task<HttpStatusCode> ResolveTickedCard(FullTrelloCard card)
        {
            if (card.Labels.ContainsTime(out var time, TrelloLabel.Monthly, TrelloLabel.Biweekly, TrelloLabel.Weekly))
            {

            }
            else if (card.Labels.ContainsName(out _, TrelloLabel.Daily, TrelloLabel.Reverse, TrelloLabel.Static))
            {

            }
            else if (card.Labels.ContainsName(TrelloLabel.Task))
            {
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
