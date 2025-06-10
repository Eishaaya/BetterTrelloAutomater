using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomator
{
    public class TrelloBoardInfo
    {
        public class StartupService : IHostedService
        {
            TrelloBoardInfo boardInfo;
            public StartupService(TrelloBoardInfo boardInfo) => this.boardInfo = boardInfo;
            
            public Task StartAsync(CancellationToken cancellationToken) => boardInfo.Setup();
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        readonly ILogger logger;
        readonly TrelloClient client;

        internal readonly string TimeZone = "Pacific Standard Time"; //TODO: ping my phone or laptop to get its actual location
        internal TimeZoneInfo MyTimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        internal SimpleTrelloRecord[] Lists { get; set; } = null!;
        internal int FirstTodo { get; set; }
        internal int TodayIndex { get; set; }
        internal int TonightIndex => TodayIndex + 1;
        internal int CycleStart => FirstTodo + 1;
        internal int CycleEnd { get; set; }

        public TrelloBoardInfo(TrelloClient client, ILogger<TrelloBoardInfo> logger)
        {
            this.logger = logger;
            this.client = client;

            logger.LogInformation("Initializing Board Information");
        }

        public async Task Setup()
        {
            Lists = await client.GetLists();

            for (int i = 0; i < Lists.Length; i++)
            {
                if (Lists[i].Name.Contains("TODO"))
                {
                    FirstTodo = i;
                    break;
                }
            }

            for (int i = Lists.Length - 1; i >= 0; i--)
            {
                if (Lists[i].Name.Contains("TODO"))
                {
                    CycleEnd = i;
                    break;
                }
            }

            for (int i = CycleEnd; i >= CycleStart; i--)
            {
                if (Lists[i].Name.Contains("today", StringComparison.OrdinalIgnoreCase))
                {
                    TodayIndex = i;
                    break;
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    }
}
