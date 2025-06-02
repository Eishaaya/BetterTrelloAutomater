using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net.Http;

namespace BetterTrelloAutomater
{
    public static class TestFunctions
    {
        [Function("GetListArray")]
        [OpenApiOperation("Gets all the list IDs", ["Utility"])]

        public static async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req,
            ILogger log)
        {
            log.LogInformation("Getting list IDs");

            throw new NotImplementedException();
            //return new OkObjectResult(responseMessage);
        }
    }
}
