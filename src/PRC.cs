using Newtonsoft.Json;
using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public static class PRC
    {
        private static readonly HttpClient _client = new();
        private static readonly Random _random = new();
        private static readonly ISubscriber? _subscriber = Caching.usingRedis ? Redis.GetSubscriber() : null;

        public static readonly string baseUrl = "https://api.policeroleplay.community";
        public static readonly string? globalApiKey = Environment.GetEnvironmentVariable("PRC_GLOBAL_KEY");
        public static readonly string globalBucketKey = globalApiKey is null ? "unauthenticated-global" : "global";

        public static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(30); // How long a request can stay in the queue before being dropped

        public static readonly int maxRetries = 3;
        public static readonly TimeSpan retryDelay = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan retryJitter = TimeSpan.FromSeconds(5);
        public static TimeSpan GetRetryDelay(int attempt) => retryDelay + TimeSpan.FromSeconds(_random.NextDouble() * retryJitter.TotalSeconds * attempt);

        public static readonly Dictionary<string, Bucket> buckets = [];
        public static readonly List<QueueItem> requestQueue = [];

        public static readonly List<ErrorCode> retryCodes = [ErrorCode.InternalError, ErrorCode.RobloxError, ErrorCode.RateLimited, ErrorCode.Unknown];
        public static readonly List<ErrorCode> invalidKeyCodes = [ErrorCode.KeyBanned, ErrorCode.KeyNotProvided, ErrorCode.IncorrectKey, ErrorCode.InvalidKey];

        public static bool stopping = false;

        public static void MainLoop()
        {
            _client.BaseAddress = new(baseUrl);

            Log.Information("Starting main loop...");

            do
            {
                if (requestQueue.Count == 0) // No need to waste resources if there is nothing to do
                {
                    Thread.Sleep(50);
                    continue;
                }

                lock (requestQueue) // Accessed by multiple threads, just to be safe
                {
                    QueueItem? theChosenOne = null;
                    foreach (QueueItem item in requestQueue)
                    {
                        if (item.ExpiresAt < DateTimeOffset.UtcNow)
                        {
                            // Request has timed out, drop it to avoid backlog
                            theChosenOne = item;
                            break;
                        }

                        if (item.runAt < DateTimeOffset.UtcNow)
                        {
                            Bucket? bucket = buckets.GetValueOrDefault(item.BucketKey);
                            if (bucket is null)
                            {
                                bucket = new() { key = item.BucketKey }; // Create new bucket if it doesn't already exist
                                buckets[item.BucketKey] = bucket;
                            }

                            if (bucket.remaining == 0)
                            {
                                if (bucket.reset.ToUnixTimeSeconds() < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) // Bucket has reset
                                {
                                    bucket.remaining = bucket.limit;
                                }
                                else // Schedule to run when the bucket resets
                                {
                                    item.runAt = bucket.reset.AddSeconds(1);
                                    continue;
                                }
                            }
                            else if (bucket.remaining <= -1 && bucket.inUse) continue; // Unknown limit, but already in use, so wait until limit is known



                            if (!Breaker.AllowRequest) // Circuit breaker is open, requeue the request
                            {
                                if (item.attempts < maxRetries)
                                {
                                    item.runAt = DateTimeOffset.UtcNow + GetRetryDelay(item.attempts);
                                    item.attempts++;
                                }
                                else
                                {
                                    item.complete = true;
                                    item.success = false;
                                    item.failureReason = "Max retries reached (circuit breaker)";
                                    theChosenOne = item; // Drop the request
                                }
                                break;
                            }

                            var endpoint = endpoints[item.endpoint];

                            // Everything looks good, request can be dropped from queue and sent

                            theChosenOne = item;
                            bucket.inUse = true;
                            bucket.remaining--;

                            // Run in thread pool to avoid blocking main loop

                            Task.Run(async () =>
                            {
                                try
                                {
                                    HttpRequestMessage request = new(endpoint.Item2, endpoint.Item1);

                                    if (item.serverKey is not null) request.Headers.Add("Server-Key", item.serverKey);

                                    if (item.body is not null) request.Content = new StringContent(item.body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

                                    HttpResponseMessage result = await _client.SendAsync(request);

                                    var headers = result.Headers;
                                    if (headers.Contains("X-RateLimit-Bucket"))
                                    {
                                        bucket.key = headers.GetValues("X-RateLimit-Bucket").FirstOrDefault() ?? bucket.key;
                                        bucket.limit = int.Parse(headers.GetValues("X-RateLimit-Limit").FirstOrDefault() ?? "-1");
                                        bucket.reset = DateTimeOffset.FromUnixTimeSeconds(long.Parse(headers.GetValues("X-RateLimit-Reset").FirstOrDefault() ?? $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120}"));
                                        if (bucket.remaining <= 0) // Only update if remaining is unknown or could de-sync values if another request is in progress
                                        {
                                            bucket.remaining = int.Parse(headers.GetValues("X-RateLimit-Remaining").FirstOrDefault() ?? "-1");
                                        }

                                        _ = Task.Run(() => Sync.SyncBucket(bucket));
                                    }
                                    bucket.inUse = false;

                                    if (result.IsSuccessStatusCode)
                                    {
                                        Breaker.RecordRequest(false); // Successful request

                                        string body = await result.Content.ReadAsStringAsync();
                                        item.result = JsonConvert.DeserializeObject(body, endpoint.Item3 ?? typeof(PRC_Message), new JsonSerializerSettings() { Error = (o, e) => { e.ErrorContext.Handled = true; } });
                                        item.complete = true;
                                        item.success = true;

                                        if (item.requeueIn is not null) // Requeue the request if requested
                                        {
                                            QueueItem newItem = new()
                                            {
                                                endpoint = item.endpoint,
                                                serverKey = item.serverKey,
                                                runAt = DateTimeOffset.UtcNow + item.requeueIn.Value,
                                                requeueIn = item.requeueIn,
                                                body = item.body
                                            };

                                            lock (requestQueue) requestQueue.Add(newItem);
                                        }

                                        if (Environment.GetEnvironmentVariable("REDIS_PUBLISH_RESULTS")?.ToLower() == "true")
                                        {
                                            _subscriber?.Publish("prcapiworker:update", $"{item.serverKey}:{item.endpoint}:{body}");
                                        }
                                    }
                                    else
                                    {
                                        PRC_Error? error = JsonConvert.DeserializeObject<PRC_Error>(await result.Content.ReadAsStringAsync());
                                        ErrorCode code = error?.code ?? ErrorCode.Unknown;
                                        bool retry = retryCodes.Contains(code);
                                        Breaker.RecordRequest(retry);

                                        if (retry)
                                        {
                                            if (item.attempts < maxRetries)
                                            {
                                                item.attempts++;
                                                item.runAt = DateTimeOffset.UtcNow + GetRetryDelay(item.attempts);
                                                item.queuedAt = DateTimeOffset.UtcNow;
                                                lock (requestQueue) requestQueue.Add(item);

                                                return;
                                            }
                                        }
                                        else
                                        {
                                            item.complete = true;
                                            item.success = false;
                                            item.failureCode = code;
                                            item.failureReason = $"Error {error?.code ?? ErrorCode.Unknown}: {error?.message ?? "Unknown error"}";

                                            if (Environment.GetEnvironmentVariable("REDIS_PUBLISH_RESULTS")?.ToLower() == "true")
                                            {
                                                _subscriber?.Publish("prcapiworker:failure", $"{item.serverKey}:{item.endpoint}:{code}:{error?.message ?? "unknown error"}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    item.complete = true;
                                    item.success = false;
                                    item.failureReason = $"{ex.GetType().Name}: {ex.Message}";
                                }
                            });
                        }
                    }

                    if (theChosenOne is not null)
                    {
                        requestQueue.Remove(theChosenOne);
                    }
                }
            }
            while (!stopping);

            _client.CancelPendingRequests();
            _client.Dispose();
        }

        public static QueueItem Enqueue(QueueItem item)
        {
            lock (requestQueue) requestQueue.Add(item);
            return item;
        }

        public static QueueItem Enqueue(Endpoint endpoint, string? serverKey, string? body = null, DateTimeOffset? runAt = null)
        {
            runAt ??= DateTimeOffset.UtcNow;
            QueueItem item = new()
            {
                endpoint = endpoint,
                serverKey = serverKey,
                runAt = runAt.Value,
                body = body
            };
            return Enqueue(item);
        }

        public static QueueItem? WaitForCompletion(QueueItem item, TimeSpan? timeout = null, TimeSpan? interval = null)
        {
            timeout ??= TimeSpan.FromSeconds(10);
            interval ??= TimeSpan.FromMilliseconds(50);

            while (!item.complete && item.ExpiresAt > DateTimeOffset.UtcNow + timeout) Thread.Sleep(interval.Value);

            return item.complete ? item : null;
        }

        public class Bucket
        {
            public string key = globalBucketKey;
            public int limit = -1;
            public int remaining = -1;
            public DateTimeOffset reset = DateTimeOffset.MinValue;
            public bool inUse = false;
        }

        public class QueueItem
        {
            public Endpoint endpoint;
            public (string, HttpMethod, Type?, bool) EndpointData => endpoints[endpoint];
            /// <summary>
            /// Plain text API key for sending to PRC API and bucket identification
            /// </summary>
            public string? serverKey = null;
            /// <summary>
            /// Hashed version of serverKey for secure logging and caching (security)
            /// </summary>
            public string HashedServerKey => serverKey is not null ? Encryption.HashString(serverKey) : "unauthenticated";
            /// <summary>
            /// Bucket key for rate limiting - uses plain text serverKey to match PRC API bucket headers
            /// </summary>
            public string BucketKey => endpoint == Endpoint.ServerCommand ? $"command-{serverKey}" : globalApiKey ?? "global";

            /// <summary>
            /// When the request should be run. If in the past, should be run immediately.
            /// </summary>
            public DateTimeOffset runAt = DateTimeOffset.UtcNow;
            /// <summary>
            /// When the request was queued at, used to determine if the request has timed out.
            /// </summary>
            public DateTimeOffset queuedAt = DateTimeOffset.UtcNow;
            /// <summary>
            /// After this time, the request is considered expired and will be dropped from the queue.
            /// </summary>
            public DateTimeOffset ExpiresAt => runAt + requestTimeout;
            /// <summary>
            /// For repeated requests after success, runAt should be set to now + requeueIn.
            /// </summary>
            public TimeSpan? requeueIn = null;

            public string? body = null;

            public int attempts = 0;

            public bool complete = false;
            public bool success = true;
            public ErrorCode? failureCode = null;
            public string? failureReason = null;

            public object? result = null;
        }

        //    Endpoint                     Path                      Method          Return Type                         Requires Server Key
        public static readonly Dictionary<Endpoint, (string, HttpMethod, Type?, bool)> endpoints = new() {
            { Endpoint.ServerCommand,     ("/v1/server/command",     HttpMethod.Post, null,                               true ) },
            { Endpoint.ServerInfo,        ("/v1/server",             HttpMethod.Get,  typeof(PRC_Server),                 true ) },
            { Endpoint.ServerPlayers,     ("/v1/server/players",     HttpMethod.Get,  typeof(List<PRC_Player>),           true ) },
            { Endpoint.ServerJoinlogs,    ("/v1/server/joinlogs",    HttpMethod.Get,  typeof(List<PRC_JoinLog>),          true ) },
            { Endpoint.ServerQueue,       ("/v1/server/queue",       HttpMethod.Get,  typeof(List<double>),               true ) },
            { Endpoint.ServerKilllogs,    ("/v1/server/killlogs",    HttpMethod.Get,  typeof(List<PRC_KillLog>),          true ) },
            { Endpoint.ServerCommandlogs, ("/v1/server/commandlogs", HttpMethod.Get,  typeof(List<PRC_CommandLog>),       true ) },
            { Endpoint.ServerModcalls,    ("/v1/server/modcalls",    HttpMethod.Get,  typeof(List<PRC_CallLog>),          true ) },
            { Endpoint.ServerBans,        ("/v1/server/bans",        HttpMethod.Get,  typeof(Dictionary<string, string>), true ) },
            { Endpoint.ServerVehicles,    ("/v1/server/vehicles",    HttpMethod.Get,  typeof(List<PRC_Vehicle>),          true ) },
            { Endpoint.ServerStaff,       ("/v1/server/staff",       HttpMethod.Get,  typeof(PRC_Staff),                  true ) },
            { Endpoint.ResetAPIKey,       ("/v1/api-key/reset",      HttpMethod.Get,  null,                               false) },
        };

        public enum Endpoint
        {
            ServerCommand,
            ServerInfo,
            ServerPlayers,
            ServerJoinlogs,
            ServerQueue,
            ServerKilllogs,
            ServerCommandlogs,
            ServerModcalls,
            ServerBans,
            ServerVehicles,
            ServerStaff,
            ResetAPIKey
        }

        public enum ErrorCode
        {
            Unknown = 0,
            RobloxError = 1001,
            InternalError = 1002,
            KeyNotProvided = 2000,
            IncorrectKey = 2001,
            InvalidKey = 2002,
            InvalidGlobalKey = 2003,
            KeyBanned = 2004,
            InvalidCommand = 3001,
            ServerOffline = 3002,
            RateLimited = 4001,
            CommandRestricted = 4002,
            MessageProhibited = 4003,
            RescourseRestricted = 9998,
            OutOfDate = 9999
        }

        public class PRC_Error
        {
            public ErrorCode code = ErrorCode.Unknown;
            public string message = "An unknown error occurred.";
        }

        public class PRC_Message
        {
            public string message = "Something happened but it failed to translate.";
        }
    }
}
