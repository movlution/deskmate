using System;

namespace Deskmate
{
    public static class WeatherRetryCheck
    {
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static string Run()
        {
            Assert(Island.WeatherRetryDelay(0, false) == TimeSpan.FromMinutes(30), "Success without data fallback should keep 30m cadence");
            Assert(Island.WeatherRetryDelay(0, true) == TimeSpan.FromMinutes(30), "Success should refresh every 30m");

            Assert(Island.WeatherRetryDelay(1, false) == TimeSpan.FromSeconds(15), "First failure with no data should retry quickly (15s)");
            Assert(Island.WeatherRetryDelay(2, false) == TimeSpan.FromSeconds(30), "Second failure with no data should back off to 30s");
            Assert(Island.WeatherRetryDelay(3, false) == TimeSpan.FromSeconds(60), "Third failure with no data should back off to 60s");
            Assert(Island.WeatherRetryDelay(4, false) == TimeSpan.FromSeconds(120), "Fourth failure with no data should back off to 120s");
            Assert(Island.WeatherRetryDelay(5, false) == TimeSpan.FromSeconds(300), "Fifth failure with no data should cap at 300s");
            Assert(Island.WeatherRetryDelay(9, false) == TimeSpan.FromSeconds(300), "Repeated failures with no data should stay capped at 300s");

            Assert(Island.WeatherRetryDelay(1, true) == TimeSpan.FromMinutes(2), "First refresh failure with cached data should retry in 2m");
            Assert(Island.WeatherRetryDelay(2, true) == TimeSpan.FromMinutes(5), "Second refresh failure with cached data should retry in 5m");
            Assert(Island.WeatherRetryDelay(3, true) == TimeSpan.FromMinutes(10), "Third refresh failure with cached data should cap at 10m");
            Assert(Island.WeatherRetryDelay(8, true) == TimeSpan.FromMinutes(10), "Repeated refresh failures with cached data should stay capped at 10m");

            return "weather retry backoff passed";
        }
    }
}
