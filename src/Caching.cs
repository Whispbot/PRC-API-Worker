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
        public static bool usingRedis = !Config.isDev && (Environment.GetEnvironmentVariable("USE_REDIS") != "FALSE");

        public static readonly Dictionary<string, (object, double)> inMemory = [];
        public static IDatabase? redis = null;

        public static void Init()
        {
            if (usingRedis)
            {
                Log.Information("Using redis for caching.");
                redis = Redis.GetDatabase();
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

        public static void SetCache(string token, object item, double ttlMs = 60000)
        {
            
            string key = $"prcworker-{item.GetType().Name.ToLower()}-{token}";

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

        public static T? GetCache<T>(string token)
        {
            string key = $"prcworker-{typeof(T).Name.ToLower()}-{token}";

            if (usingRedis)
            {
                if (redis is null) return default;
                var value = redis.StringGet(key);
                return value.HasValue ? JsonConvert.DeserializeObject<T>(value.ToString()) : default;
            }
            else
            {
                (object, double) value = inMemory.GetValueOrDefault(key, (new { },-1));
                if (value.Item2 > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                {
                    return (T)value.Item1;
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
