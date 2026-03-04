using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRC_API_Worker
{
    public static class Analytics
    {
        public static readonly TimeSpan spanDuration = TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("ANALYTICS_TIMESPAN_DURATION_S") is string s ? double.Parse(s) : 10);
        public static readonly int maxSpans = Environment.GetEnvironmentVariable("ANALYTICS_MAX_SPANS") is string max ? int.Parse(max) : 20;
        public static readonly double maxRequestTime = 30;

        public static readonly List<Span> requests = [];

        private static DateTimeOffset _currentSpanEnd = DateTimeOffset.FromUnixTimeSeconds((long)(Math.Ceiling(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / spanDuration.TotalSeconds) * spanDuration.TotalSeconds));

        private static double _currentMin = double.MaxValue;
        private static double _currentMax = double.MinValue;
        private static double _currentTotal = 0;
        private static int _currentCount = 0;
        private static int _currentErrors = 0;

        private static ISubscriber? _subscriber = Redis.GetSubscriber();

        public static void CheckSpan()
        {
            if (DateTimeOffset.UtcNow > _currentSpanEnd)
            {
                if (_currentMin == double.MaxValue) _currentMin = -1;
                if (_currentMax == double.MinValue) _currentMax = -1;

                var data = new Span(_currentSpanEnd.ToUnixTimeSeconds(), _currentCount, _currentErrors, _currentCount > 0 ? _currentTotal / _currentCount : 0, _currentMin, _currentMax);

                requests.Add(data);
                _currentSpanEnd = DateTimeOffset.FromUnixTimeSeconds((long)(Math.Ceiling(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / spanDuration.TotalSeconds) * spanDuration.TotalSeconds));

                _currentMin = double.MaxValue;
                _currentMax = double.MinValue;
                _currentTotal = 0;
                _currentCount = 0;
                _currentErrors = 0;

                while (requests.Count > maxSpans)
                {
                    requests.RemoveAt(0);
                }

                _subscriber?.PublishAsync("prcapiworker:analytics", JsonConvert.SerializeObject(data));
            }
        }

        public static void AddRequest(double requestDuration, bool error)
        {
            CheckSpan();

            _currentCount++;
            if (error) _currentErrors++;
            _currentTotal += requestDuration;

            if (requestDuration < _currentMin) _currentMin = requestDuration;
            if (requestDuration > _currentMax) _currentMax = requestDuration;
        }
    }

    public record struct Span(long Timestamp, int Count, int Errors, double Mean, double Min, double Max);
}
