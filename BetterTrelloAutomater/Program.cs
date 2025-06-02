using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomater
{
    internal class Program
    {
        public static async Task Main()
        {
            var host = new HostBuilder()
            .ConfigureAppConfiguration(m =>
            {
                m.AddJsonFile("local.settings.json");
                m.AddEnvironmentVariables();
            })
            //  .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices(services =>
            {
                services.AddHttpClient();
                services.AddSingleton<TrelloClient>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

            var trello = host.Services.GetRequiredService<TrelloClient>();
            var id = await trello.GetPersonalBoardID();

        }
    }
}
