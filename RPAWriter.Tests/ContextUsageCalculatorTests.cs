using NUnit.Framework;
using RPAWriter.Core.Context;
using RPAWriter.Core.Models;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ContextUsageCalculatorTests
    {
        [Test]
        public void Compute_NoWindow_ReturnsIdlePlaceholder()
        {
            var info = ContextUsageCalculator.Compute(0, 0, 0, 0, null);

            Assert.That(info.IsAvailable, Is.False);
            Assert.That(info.Status, Is.EqualTo(ContextUsageStatus.Idle));
            Assert.That(info.FillText, Is.EqualTo("Context window: —"));
            Assert.That(info.StatsText, Is.Empty);
        }

        [Test]
        public void Compute_FillText_ShowsUsedWindowAndPercent()
        {
            var info = ContextUsageCalculator.Compute(12345, 409600, 0, 0, null);

            Assert.That(info.IsAvailable, Is.True);
            Assert.That(info.RatioPercent, Is.EqualTo(100.0 * 12345 / 409600).Within(0.001));
            Assert.That(info.FillText, Does.Contain("12,345"));
            Assert.That(info.FillText, Does.Contain("409,600"));
            Assert.That(info.FillText, Does.Contain("3%"));
        }

        [Test]
        public void Compute_Ratio_ClampsAtHundred()
        {
            var info = ContextUsageCalculator.Compute(999999, 1000, 0, 0, null);

            Assert.That(info.RatioPercent, Is.EqualTo(100.0));
            Assert.That(info.Status, Is.EqualTo(ContextUsageStatus.Normal));
        }

        [Test]
        public void Compute_Status_ProgressesWarningThenOverflow()
        {
            var warning = ContextUsageCalculator.Compute(850, 1000, 850, 880, null);
            Assert.That(warning.Status, Is.EqualTo(ContextUsageStatus.Warning));

            var overflow = ContextUsageCalculator.Compute(880, 1000, 850, 880, null);
            Assert.That(overflow.Status, Is.EqualTo(ContextUsageStatus.Overflow));
        }

        [Test]
        public void Compute_StatsText_IncludesOnlyNonZeroParts()
        {
            var usage = new TokenUsage
            {
                TotalTokens = 45000,
                CacheHitTokens = 8000,
                EstimatedCostUsd = 0.1234
            };

            var info = ContextUsageCalculator.Compute(1000, 409600, 0, 0, usage);

            Assert.That(info.StatsText, Does.Contain("Total 45,000"));
            Assert.That(info.StatsText, Does.Contain("Cache hit 8,000"));
            Assert.That(info.StatsText, Does.Contain("$0.1234"));
        }

        [Test]
        public void Compute_StatsText_EmptyWhenNoUsage()
        {
            var info = ContextUsageCalculator.Compute(1000, 409600, 0, 0, null);

            Assert.That(info.StatsText, Is.Empty);
        }

        [Test]
        public void Compute_StatsText_OmitsZeroCacheAndCost()
        {
            var usage = new TokenUsage
            {
                TotalTokens = 10,
                CacheHitTokens = 0,
                EstimatedCostUsd = 0
            };

            var info = ContextUsageCalculator.Compute(1, 100, 0, 0, usage);

            Assert.That(info.StatsText, Is.EqualTo("Total 10"));
        }

        [Test]
        public void Compute_CacheHitRatio_IsProportionalToWindow()
        {
            var info = ContextUsageCalculator.Compute(30000, 100000, 0, 0, null, cacheHitTokens: 20000);

            Assert.That(info.CacheHitRatioPercent, Is.EqualTo(20.0).Within(0.001));
        }

        [Test]
        public void Compute_CacheHitRatio_ClampsToUsedRatio()
        {
            var info = ContextUsageCalculator.Compute(10000, 100000, 0, 0, null, cacheHitTokens: 50000);

            Assert.That(info.RatioPercent, Is.EqualTo(10.0).Within(0.001));
            Assert.That(info.CacheHitRatioPercent, Is.EqualTo(10.0).Within(0.001));
        }

        [Test]
        public void Compute_CacheHitRatio_ZeroWithoutWindow()
        {
            var info = ContextUsageCalculator.Compute(1000, 0, 0, 0, null, cacheHitTokens: 500);

            Assert.That(info.CacheHitRatioPercent, Is.Zero);
        }

        [Test]
        public void Compute_CacheHitRatio_ZeroWhenNoCacheHit()
        {
            var info = ContextUsageCalculator.Compute(30000, 100000, 0, 0, null, cacheHitTokens: 0);

            Assert.That(info.CacheHitRatioPercent, Is.Zero);
        }
    }
}
