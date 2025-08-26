using PRC_API_Worker;
using Serilog;

Logger.Initialize();

Caching.Init();
Sync.Init();


Thread APIThread = new(() =>
{
    API.Start();
})
{
    Name = "API Thread"
};
APIThread.Start();


AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    // SIGTERM/SIGINT

    Log.Information("Shutting down...");

    PRC.stopping = true;
    Redis.Close();
};

PRC.MainLoop();

Logger.Shutdown();