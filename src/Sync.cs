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
    public static class Sync
    {
        private static ISubscriber? _pubSub = null;

        private static readonly Random _random = new();

        public static bool otherInstanceAlive = false;
        public static int syncKey = _random.Next(100_000, 999_999);

        public static readonly int checkAliveChance = 5;

        public static void Init()
        {
            if (!Caching.usingRedis) return; // Cannot sync without redis

            int attempts = 0;
            do
            {
                _pubSub = Redis.GetSubscriber();

                if (_pubSub is null)
                {
                    Log.Warning($"Redis connection unavailable for sync, retrying in {attempts * 2} seconds.");
                    attempts++;
                    Thread.Sleep(attempts * 2000);
                }
            }
            while (_pubSub is null && attempts < 10);

            if (_pubSub is null) // Cannot operate properly without pubsub
            {
                Log.Fatal("Failed to get pubsub after multiple attempts, cannot sync.");
                Environment.Exit(1);
            }

            _pubSub.Subscribe("prcapiworker-alive", (channel, value) => // Check if other instances are alive, no point shouting if nobody there to hear ya
            {
                if (value.HasValue && value.ToString() != syncKey.ToString())
                {
                    otherInstanceAlive = true;
                }
            });

            _pubSub.Publish("prcapiworker-alive", syncKey.ToString()); // Announce presence

            _pubSub.Subscribe("prcapiworker-sync", (channel, value) =>
            {
                string[] parts = value.ToString().Split(":", 2);
                if (parts[0] == syncKey.ToString()) return; // Don't process our own messages

                otherInstanceAlive = true;

                PRC.Bucket? bucket = JsonConvert.DeserializeObject<PRC.Bucket>(parts[1]);

                if (bucket is null) return; // Invalid data

                PRC.Bucket? currentBucket = PRC.buckets.GetValueOrDefault(bucket.key);

                if (currentBucket is null) // No existing bucket, add it
                {
                    PRC.buckets[bucket.key] = bucket;
                }
                else
                {
                    if (bucket.reset > currentBucket.reset) // Incoming bucket is newer, replace ours entirely
                    {
                        PRC.buckets[bucket.key] = currentBucket;
                    }
                    else if (bucket.reset == currentBucket.reset) // Same age
                    {
                        if (currentBucket.remaining > bucket.remaining) // We have more so go for the lower value to be safe
                        {
                            currentBucket.remaining = bucket.remaining;
                        }
                    }
                }
            });
        }

        public static void SyncBucket(PRC.Bucket bucket)
        {
            if (_pubSub is null) return;
            if (!otherInstanceAlive && _random.Next(0,100) < (100 - checkAliveChance)) return;
            long recievers = _pubSub.Publish("prcapiworker-sync", $"{syncKey}:{JsonConvert.SerializeObject(bucket)}");
            if (recievers > 0) otherInstanceAlive = true;
        }
    }
}
