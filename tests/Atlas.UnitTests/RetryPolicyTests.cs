using System;
using Atlas.Worker.Execution;
using Xunit;

namespace Atlas.UnitTests;

public class RetryPolicyTests
{
    [Fact]
    public void CalculateDelay_ShouldBeWithinJitterBoundaries()
    {
        // Arrange
        var options = new RetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(10)
        };

        // Act & Assert
        // Attempt 1: backoff is 2s * 2^0 = 2s. Jittered value must be between 0 and 2s.
        for (int i = 0; i < 50; i++)
        {
            var delay = RetryPolicy.CalculateDelay(1, options);
            Assert.True(delay >= TimeSpan.Zero, "Delay cannot be negative");
            Assert.True(delay <= TimeSpan.FromSeconds(2), "Delay exceeded Max backoff bound");
        }

        // Attempt 2: backoff is 2s * 2^1 = 4s. Jittered value must be between 0 and 4s.
        for (int i = 0; i < 50; i++)
        {
            var delay = RetryPolicy.CalculateDelay(2, options);
            Assert.True(delay >= TimeSpan.Zero, "Delay cannot be negative");
            Assert.True(delay <= TimeSpan.FromSeconds(4), "Delay exceeded Max backoff bound");
        }
    }

    [Fact]
    public void CalculateDelay_ShouldRespectMaxDelay()
    {
        // Arrange
        var options = new RetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(5)
        };

        // Act & Assert
        // Attempt 5: backoff would be 2 * 2^4 = 32s, but MaxDelay is 5s.
        // Under full jitter, the value should be in [0, 5s].
        for (int i = 0; i < 100; i++)
        {
            var delay = RetryPolicy.CalculateDelay(5, options);
            Assert.True(delay >= TimeSpan.Zero);
            Assert.True(delay <= TimeSpan.FromSeconds(5), "Delay exceeded MaxDelay cap");
        }
    }

    [Fact]
    public void CalculateDelay_WithInvalidAttempt_ShouldFallbackToAttemptOne()
    {
        // Arrange
        var options = new RetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(10)
        };

        // Act
        var delay = RetryPolicy.CalculateDelay(-5, options);

        // Assert
        Assert.True(delay >= TimeSpan.Zero);
        Assert.True(delay <= TimeSpan.FromSeconds(2));
    }
}
