using BetterTrelloAutomator.AzureFunctions;

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

            builder.Services.AddFunctionsWorkerDefaults();


            builder.Services
                .AddApplicationInsightsTelemetryWorkerService()
                .ConfigureFunctionsApplicationInsights()
                .AddHttpClient()
                .AddSingleton<TrelloClient>()
                .AddSingleton<TrelloBoardInfo>()
                .AddHostedService<TrelloBoardInfo.StartupService>();

            var environment = builder.Configuration["ENVIRONMENT"] ?? "LOCAL";

            builder.Logging
                .AddConsole()
                .SetMinimumLevel(environment == "LOCAL"? LogLevel.Information : LogLevel.Warning);


            builder.Build().Run();

            #region HostBuilder setup for sanity testing

            //var host = new HostBuilder()
            //    .ConfigureAppConfiguration(m =>
            //    {
            //        m.AddEnvironmentVariables();
            //    })
            //    .ConfigureFunctionsWebApplication()
            //    .ConfigureServices(m =>
            //    {
            //        m.AddApplicationInsightsTelemetryWorkerService();
            //        m.ConfigureFunctionsApplicationInsights();

            //        m.AddHttpClient();
            //        m.AddSingleton<TrelloClient>();
            //        m.AddTransient<TrelloBoardInfo>();
            //        m.AddHostedService<TrelloBoardInfo.StartupService>();
            //    })
            //    .Build();

            #endregion
        }
    }
}
