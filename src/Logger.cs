using PRC_API_Worker;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public static class Logger
    {
        public static void Initialize()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: $"[{{Timestamp:HH:mm:ss}} {{Level:u3}}] [{Config.replicaId}] {{Message:lj}}{{NewLine}}{{Exception}}")
                .CreateLogger();

            Log.Information("Logger initialized");
        }

        public static void Shutdown()
        {
            Log.CloseAndFlush();
        }
    }
}
