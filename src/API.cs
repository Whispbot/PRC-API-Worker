using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using static System.Net.Http.HttpMethod;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using System.ComponentModel.Design.Serialization;

namespace PRC_API_Worker
{
    public class API
    {
        public static void Start()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            string port = Environment.GetEnvironmentVariable("PORT") ?? "5001";

            if (!Config.isDev) builder.Logging.ClearProviders();

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(int.Parse(port));
            });

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.IncludeFields = true;
            });

            WebApplication app = builder.Build();

            foreach (var route in routes)
            {
                foreach (var method in route.Value)
                {
                    if (method.Key == Get)
                    {
                        app.MapGet($"{route.Key}", method.Value);
                    }
                    else if (method.Key == Post)
                    {
                        app.MapPost($"{route.Key}", method.Value);
                    }
                }
            }

            foreach (var endpointKVP in PRC.endpoints)
            {
                var endpoint = endpointKVP.Value;
                if (endpoint.Item2 == Get)
                {
                    app.MapGet($"{endpoint.Item1}", GetDelegate(endpointKVP.Key));
                }
                else if (endpoint.Item2 == Post)
                {
                    app.MapPost($"{endpoint.Item1}", GetDelegate(endpointKVP.Key));
                }
            }

            app.MapFallback(context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return context.Response.WriteAsJsonAsync(new { code = 404, message = "Not Found" });
            });

            Log.Information($"API started on http://localhost:{port}.");
            app.Run();
        }

        public static readonly bool requireAuth = Environment.GetEnvironmentVariable("USE_AUTHORIZATION")?.ToUpper() == "TRUE" && !Config.isDev;
        public static readonly string? authToken = Environment.GetEnvironmentVariable("AUTHORIZATION_KEY");
        public static async Task<bool> CheckAuth(HttpContext context)
        {
            if (!requireAuth) return true;

            var headers = context.Request.Headers;
            var authHeader = headers.Authorization.FirstOrDefault();

            if (authToken is null)
            {
                Log.Error("Authorization is required but authorization key is not set.");
                throw new Exception("Authorization is required but authorization key is not set.");
            }

            if (authHeader is null || authHeader != authToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code = 401, message = "Unauthorized" });
                return false;
            }

            return true;
        }

        public static RequestDelegate GetDelegate(PRC.Endpoint endpoint)
        {
            return async context =>
            {
                if (!await CheckAuth(context)) return; // Ignores during dev

                var endpointData = PRC.endpoints[endpoint];
                string? serverKey = context.Request.Headers["Server-Key"].FirstOrDefault();

                if (serverKey is null && endpointData.Item4) // Makes sure that server key is provided if required by endpoint
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { code = 400, message = "Server key not provided when required" });
                    return;
                }

                // Check cache for pre-existing data
                string? useCacheHeader = context.Request.Headers["Use-Cache"].FirstOrDefault();
                bool useCache = useCacheHeader is null || bool.Parse(useCacheHeader);

                string? cacheDurationHeader = context.Request.Headers["Cache-Duration"].FirstOrDefault();
                TimeSpan cacheDuration = cacheDurationHeader is not null ? TimeSpan.FromSeconds(int.Parse(cacheDurationHeader)) : TimeSpan.FromMinutes(1);

                string cacheKey = $"prcapiworker:{endpoint}:{serverKey ?? "unauthenticated"}";
                if (context.Request.Method == Get.ToString() && useCache)
                {
                    // Only need to cache get requests

                    object? cachedResponse = Caching.GetCache(cacheKey);

                    if (cachedResponse is not null)
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(new { code = 304, message = "Item Cached", data = cachedResponse });
                        return;
                    }
                }

                // API keys are encrypted when sent
                string? unencryptedKey = serverKey is not null ? Encryption.DecryptApiKey(serverKey) : null;

                // Get the request body if it exists
                string? requestBody = null;
                if (context.Request.HasJsonContentType())
                {
                    using var reader = new StreamReader(context.Request.Body);
                    requestBody = await reader.ReadToEndAsync();
                }

                // If a run at timestamp is provided, parse it into a DTO
                string? runAt = context.Request.Headers["Run-At"].FirstOrDefault();
                DateTimeOffset? runAtDTO = runAt is not null ? DateTimeOffset.Parse(runAt) : null;

                // Enqueue the item and wait for completion
                PRC.QueueItem item = (context.Request.Method == Get.ToString() ? PRC.requestQueue.Find(q => !q.complete && q.endpoint == endpoint && q.serverKey == unencryptedKey) : null) ?? PRC.Enqueue(endpoint, unencryptedKey, requestBody, runAtDTO);
                PRC.WaitForCompletion(item);

                if (item.complete)
                {
                    if (item.success) // We gucci
                    {
                        if (context.Request.Method == Get.ToString() && useCache && item.result is not null)
                        {
                            // Run caching in the background to avoid blocking response
                            _ = Task.Run(() => Caching.SetCache(cacheKey, item.result, cacheDuration.TotalMilliseconds));
                        }

                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(new { code = 200, data = item.result });
                    }
                    else // Request failed for whatever reason
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new { code = item.failureCode, message = item.failureReason });
                    }
                }
                else // Request never completed within expected duration
                {
                    context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                    await context.Response.WriteAsJsonAsync(new { code = 408, message = "Request Timeout" });
                }
            };
        }

        [StringSyntax("Route")]
        public static readonly Dictionary<string, Dictionary<HttpMethod, RequestDelegate>> routes = new() {
            {
                "/health",
                new() {
                    { Get, async context =>
                    {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await context.Response.WriteAsJsonAsync(new {status = "ok"});
                    }
                    }
                }
            },
            {
                "/encrypt-key",
                new() {
                    { Post, async context =>
                    {
                        CacheKeyJson? body = context.Request.HasJsonContentType() ? await context.Request.ReadFromJsonAsync<CacheKeyJson>() : null;

                        if (body is null)
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsJsonAsync(new { code = 400, message = "Invalid request body" });
                            return;
                        }

                        string? key = body.key;

                        if (key is null)
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsJsonAsync(new { code = 400, message = "Invalid key" });
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(new { encryptedKey = Encryption.EncryptApiKey(key) });
                        return;
                    }
                    }
                }
            }
        };
    }

    public class CacheKeyJson
    {
        public string? key;
    }
}
