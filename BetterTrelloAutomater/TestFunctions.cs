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
    class TestFunctions
    {
        TrelloClient client;
        ILogger log;
        public TestFunctions(TrelloClient client, ILogger<TestFunctions> log)
        {
            this.client = client;
            this.log = log;
        }

        [Function("GetListArray")]
        [OpenApiOperation("Gets all the list IDs", ["Utility"])]

        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req,
            ILogger log)
        {
            log.LogInformation("Getting list IDs");

            throw new NotImplementedException();
            //return new OkObjectResult(responseMessage);
        }

        [Function("GetPersonalID")]
        public async Task<IActionResult> GetPersonalID([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            log.LogInformation("Getting ID");

            return new OkObjectResult(await client.GetPersonalBoardID());
        }

        [Function("GetLists")]
        public async Task<IActionResult> GetLists([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            return new OkObjectResult(await client.GetLists(2, 9));
        }

    }
}
