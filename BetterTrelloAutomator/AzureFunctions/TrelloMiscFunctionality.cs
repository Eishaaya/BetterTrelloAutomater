using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;

using System.Net;

namespace BetterTrelloAutomator.AzureFunctions;

public partial class TrelloFunctionality
{
    [Function("GetPersonalID")]
    [OpenApiOperation("GetPersonalID", ["Misc"])]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(string), Description = "the ID of the user who supplied the trello key and token in ENV (that's probably me)")]
    public async Task<HttpResponseData> GetPersonalID([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
    {
        logger.LogInformation("Getting ID");
        var id = await client.GetPersonalBoardID();

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString(id);
        return response;
    }

    [Function("GetLists")]
    [OpenApiOperation("GetLists", ["Misc"])]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(SimpleTrelloRecord[]), Description = "All the lists of the client's board")]
    public async Task<HttpResponseData> GetLists([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
    {
        logger.LogInformation("User requesting to get lists");
        var lists = await client.GetLists();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(lists);
        return response;
    }
}