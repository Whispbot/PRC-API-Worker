using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using static System.Net.Http.HttpMethod;

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

            Log.Information($"API started on http://localhost:{port}.");
            app.Run();
        }

        [StringSyntax("Route")]
        public static Dictionary<string, Dictionary<HttpMethod, RequestDelegate>> routes = new() {
            { "/health", new() {
                { Get, async context => {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(new {status = "ok"});
                    }
                }
            } }
        };
    }
}
