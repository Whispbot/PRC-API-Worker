using PRC_API_Worker;

Logger.Initialize();

Caching.Init();

Thread APIThread = new(() =>
{
    API.Start();
})
{
    Name = "API Thread"
};
APIThread.Start();

PRC.MainLoop();

Logger.Shutdown();