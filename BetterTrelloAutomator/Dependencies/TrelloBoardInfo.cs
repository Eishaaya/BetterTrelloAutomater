using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterTrelloAutomator.Dependencies
{
    public class TrelloBoardInfo
    {
        public class StartupService(TrelloBoardInfo boardInfo) : IHostedService
        {
            readonly TrelloBoardInfo boardInfo = boardInfo;

            public Task StartAsync(CancellationToken cancellationToken) => boardInfo.Setup();
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        readonly ILogger logger;
        readonly TrelloClient client;

        internal readonly string TimeZone = "Pacific Standard Time"; //TODO: ping my phone or laptop to get its actual location
        internal TimeZoneInfo MyTimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        internal SimpleTrelloRecord[] Lists { get; private set; } = null!;

        internal DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MyTimeZoneInfo); //Getting the time in my timezone
        internal DateTime TodayStart
        {
            get
            {
                return Now - new TimeSpan(Now.Hour, Now.Minute + 1, Now.Second); //getting the beginning of today
            }
        }


        internal int FirstTodo { get; private set; }
        internal int TonightIndex => TodayIndex + 1;
        internal int CycleStart => FirstTodo + 1;
        internal int CycleEnd { get; private set; }
        internal int TodayIndex { get; private set; }
        internal int DoneIndex { get; private set; }
        internal int RoutineIndex { get; private set; }
        internal int WindDownIndex
        {
            get
            {
                int posFromToday = TodayIndex + 2;
                if (posFromToday != DoneIndex - 1)
                {
                    throw new InvalidOperationException($"Invalid Board structure detected, did you insert a list between {Lists[TodayIndex].Name} and {Lists[DoneIndex].Name}?");
                }
                return posFromToday;
            }
        }

        public TrelloBoardInfo(TrelloClient client, ILogger<TrelloBoardInfo> logger)
        {
            logger.LogInformation("Initializing Board Information");

            this.logger = logger;
            this.client = client;
        }

        public async Task Setup()
        {
            Lists = await client.GetLists();

            for (int i = 0; i < Lists.Length; i++) //Finding future TODO (or whatever its equivalent is)
            {
                if (Lists[i].Name.Contains("TODO"))
                {
                    FirstTodo = i;
                    break;
                }
            }

            for (int i = Lists.Length - 1; i >= 0; i--) //Finding today TODO (or any buffers after it)
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

            for (int i = CycleEnd; i < Lists.Length; i++)
            {
                if (Lists[i].Name.Contains("done", StringComparison.OrdinalIgnoreCase))
                {
                    DoneIndex = i;
                }
                if (Lists[i].Name.Contains("routine", StringComparison.OrdinalIgnoreCase))
                {
                    RoutineIndex = i;
                }
            }
        }
    }
}
