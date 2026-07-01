using System;

namespace Atlas.Worker.Execution;

public class RetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);
    public double BackoffMultiplier { get; set; } = 2.0;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
}

public static class RetryPolicy
{
    private static readonly Random _random = new();

    public static TimeSpan CalculateDelay(int attempt, RetryOptions options)
    {
        if (attempt <= 0)
        {
            attempt = 1;
        }

        // Exponential backoff calculation: InitialDelay * (BackoffMultiplier ^ (attempt - 1))
        var rawBackoffMs = options.InitialDelay.TotalMilliseconds * Math.Pow(options.BackoffMultiplier, attempt - 1);
        var backoffMs = Math.Min(options.MaxDelay.TotalMilliseconds, rawBackoffMs);

        // Apply AWS-style Full Jitter: random value between 0 and backoffMs
        lock (_random)
        {
            var jitteredMs = _random.NextDouble() * backoffMs;
            return TimeSpan.FromMilliseconds(jitteredMs);
        }
    }
}
