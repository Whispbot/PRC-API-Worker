using Newtonsoft.Json;
using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public static class Caching
    {
        public static bool usingRedis = !Config.isDev && (Environment.GetEnvironmentVariable("REDIS_HOST") is not null);

        public static readonly Dictionary<string, (object, double)> inMemory = [];
        public static IDatabase? redis = null;

        public static void Init()
        {
            if (usingRedis)
            {
                Log.Information("Using redis for caching.");

                int attempts = 0;
                do
                {
                    redis = Redis.GetDatabase();

                    if (redis is null)
                    {
                        Log.Warning($"Redis connection unavailable for caching, retrying in {attempts * 2} seconds.");
                        attempts++;
                        Thread.Sleep(attempts * 2000);
                    }
                }
                while (redis is null && attempts < 10);

                if (redis == null)
                {
                    Log.Fatal("Failed to connect to redis for caching...");
                    usingRedis = false;
                }
            }
            else
            {
                Log.Information("Using memory for caching (not recommended for prod).");
            }
        }

        public static void SetCache(string key, object item, double ttlMs = 60000)
        {
            if (usingRedis)
            {
                if (redis is null) return;
                redis.StringSet(key, JsonConvert.SerializeObject(item), TimeSpan.FromMilliseconds(ttlMs));
            }
            else
            {
                inMemory[key] = (item, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ttlMs);
            }
        }

        public static object? GetCache(string key)
        {
            if (usingRedis)
            {
                if (redis is null) return default;
                var value = redis.StringGet(key);
                return value.HasValue ? JsonConvert.DeserializeObject(value.ToString()) : default;
            }
            else
            {
                (object, double) value = inMemory.GetValueOrDefault(key, (new { },-1));
                if (value.Item2 > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                {
                    return value.Item1;
                }
                else
                {
                    inMemory.Remove(key);
                    return default;
                }
            }
        }
    }
}
