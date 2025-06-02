using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomater
{
    internal class Program
    {
        public static void Main()
        {
            var host = new HostBuilder()
            .ConfigureAppConfiguration(m =>
            {
                m.AddJsonFile("local.settings.json");
                m.AddEnvironmentVariables();
            })
            .ConfigureServices(services =>
            {
                services.AddHttpClient();
            });
        }
    }
}
