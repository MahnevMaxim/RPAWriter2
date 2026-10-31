using System;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Tests of the query the log analysis panel sends to a provider:
    /// level pass-through and the inclusive day boundaries.
    /// </summary>
    [TestFixture]
    public class ProdLogQueryBuilderTests
    {
        [Test]
        public void Build_WithoutFiltersReturnsEmptyQuery()
        {
            var query = ProdLogQueryBuilder.Build(null, null, null);

            Assert.That(query.MinLevel, Is.Null);
            Assert.That(query.FromUtc, Is.Null);
            Assert.That(query.ToUtc, Is.Null);
        }

        [Test]
        public void Build_PassesMinimumLevel()
        {
            Assert.That(ProdLogQueryBuilder.Build(LogLevel.Warning, null, null).MinLevel, Is.EqualTo(LogLevel.Warning));
        }

        [Test]
        public void Build_FromDateIsStartOfDayUtc()
        {
            var query = ProdLogQueryBuilder.Build(null, new DateTime(2024, 5, 6, 17, 45, 0), null);

            Assert.That(query.FromUtc, Is.EqualTo(new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(query.FromUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void Build_ToDateIsLastTickOfDayUtc()
        {
            // The upper bound stays inclusive for a day picker: 23:59:59.9999999 of the chosen day.
            var query = ProdLogQueryBuilder.Build(null, null, new DateTime(2024, 5, 6, 3, 0, 0));

            Assert.That(query.ToUtc!.Value.Date, Is.EqualTo(new DateTime(2024, 5, 6)));
            Assert.That(query.ToUtc!.Value.Hour, Is.EqualTo(23));
            Assert.That(query.ToUtc!.Value.Minute, Is.EqualTo(59));
            Assert.That(query.ToUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(query.ToUtc!.Value.AddTicks(1).Date, Is.EqualTo(new DateTime(2024, 5, 7)));
        }

        [Test]
        public void Build_UsesIndependentBounds()
        {
            var from = new DateTime(2024, 1, 1);
            var to = new DateTime(2024, 1, 31);

            var query = ProdLogQueryBuilder.Build(LogLevel.Error, from, to);

            Assert.That(query.MinLevel, Is.EqualTo(LogLevel.Error));
            Assert.That(query.FromUtc!.Value, Is.EqualTo(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(query.ToUtc!.Value.Date, Is.EqualTo(new DateTime(2024, 1, 31)));
        }
    }
}
