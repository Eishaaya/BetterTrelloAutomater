using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterTrelloAutomator
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var builder = FunctionsApplication.CreateBuilder(args);

            builder.ConfigureFunctionsWebApplication();

            builder.Configuration
                .AddEnvironmentVariables();

            builder.Services
                .AddApplicationInsightsTelemetryWorkerService()
                .ConfigureFunctionsApplicationInsights()
                .AddHttpClient()
                .AddSingleton<TrelloClient>()
                .AddSingleton<TrelloFunctionality>();

            builder.Logging
                .AddConsole()
                .SetMinimumLevel(LogLevel.Information);

            builder.Build().Run();

        }
    }
}
