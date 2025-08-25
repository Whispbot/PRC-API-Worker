using Microsoft.AspNetCore.Routing.Constraints;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public static class Breaker
    {
        private static Random _random = new();

        public static readonly TimeSpan windowDuration = TimeSpan.FromSeconds(20);

        public static double currentWindow = Math.Floor(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowDuration.TotalSeconds);

        public static int currentWindowErrors = 0;
        public static int lastWindowErrors = 0;
        
        public static int currentWindowRequests = 0;
        public static int lastWindowRequests = 0;

        public static readonly int errorThreshold = 15;
        public static readonly double randomRequirementMultiplier = 1.5;
        public static readonly int maxRequiredPercentage = 95;
        public static readonly int minRequiredPercentage = 75;

        public static readonly int minRequests = 5;

        public static bool aboveThreshold = false;
        private static DateTimeOffset _lastUpdate = DateTimeOffset.UtcNow;

        public static double LastWindowWeight => (windowDuration.TotalSeconds - ((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % (windowDuration.TotalSeconds * 1000))/1000)) / windowDuration.TotalSeconds;

        public static double ApproxErrors => currentWindowErrors + (lastWindowErrors * LastWindowWeight);
        public static double ApproxRequests => currentWindowRequests + (lastWindowRequests * LastWindowWeight);
        public static double ApproxErrorRate => ApproxRequests == 0 ? 0 : ApproxErrors / ApproxRequests;
        public static double ApproxErrorPercentage => ApproxErrorRate * 100;
        public static bool AboveErrorThreshold => ApproxErrorPercentage >= errorThreshold;

        public static bool AllowRequest
        {
            get
            {
                if (ApproxErrorPercentage < errorThreshold || currentWindowRequests < minRequests) // Good rate
                {
                    if (aboveThreshold)
                    {
                        aboveThreshold = false;
                        Task.Run(OnLowErrorRate);
                    }

                    return true;
                }
                else
                {
                    // If error rate is high, generate random number and check against threshold
                    // Threshold = errorPercentage * randomRequirementMultiplier
                    // Threshold is clamped so that not too many or too few requests are allowed through

                    if (!aboveThreshold || _lastUpdate < DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5))
                    {
                        aboveThreshold = true;
                        _lastUpdate = DateTimeOffset.UtcNow;
                        Task.Run(OnHighErrorRate);
                    }

                    return _random.Next(0, 100) > Math.Clamp(ApproxErrorPercentage * randomRequirementMultiplier, minRequiredPercentage, maxRequiredPercentage);
                }
            }
        }

        public static void CheckWindow()
        {
            double thisWindow = Math.Floor(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / windowDuration.TotalSeconds);
            if (thisWindow > currentWindow)
            {
                lastWindowErrors = currentWindowErrors;
                lastWindowRequests = currentWindowRequests;
                currentWindowErrors = 0;
                currentWindowRequests = 0;
                currentWindow = thisWindow;
            }
        }

        public static void RecordRequest(HttpResponseMessage message)
        {
            CheckWindow();
            currentWindowRequests++;
            if (!message.IsSuccessStatusCode)
            {
                currentWindowErrors++;
            }
        }

        public static void RecordRequest(bool isError)
        {
            CheckWindow();
            currentWindowRequests++;
            if (isError)
            {
                currentWindowErrors++;
            }
        }

        public static async Task OnHighErrorRate()
        {
            Log.Warning($"High error rate! {ApproxErrorRate * 100}% of requests fail.");
            await SendDiscordLog();
        }

        public static async Task OnLowErrorRate()
        {
            Log.Information($"Error rate back to normal.");
            await SendDiscordLog();
        }

        private static async Task SendDiscordLog()
        {
            string? webhookUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
            if (string.IsNullOrEmpty(webhookUrl)) return;

            using HttpClient client = new();
            await client.PostAsync(
                webhookUrl,
                new StringContent(
                    JsonConvert.SerializeObject(
                        new {
                            embeds = new[]
                            {
                                new
                                {
                                    title = AboveErrorThreshold ? "High Error Rate" : "Good Error Rate",
                                    color = AboveErrorThreshold ? 0xAA0000 : 0x00AA00,
                                    description = "-# All values are approximated (floating window) to conserve memory.",
                                    fields = new[]
                                    {
                                        new
                                        {
                                            name = "Errors",
                                            value = $"{Math.Round(ApproxErrors)}",
                                            inline = true
                                        },
                                        new
                                        {
                                            name = "Requests",
                                            value = $"{Math.Round(ApproxRequests)}",
                                            inline = true
                                        },
                                        new
                                        {
                                            name = "Error Rate",
                                            value = $"{ApproxErrorPercentage:F2}/{errorThreshold:F2}%",
                                            inline = false
                                        }
                                    }
                                }
                            }
                        }
                    ),
                    Encoding.UTF8,
                    "application/json"
                )
            );
        }
    }
}
