using System;
using CaseDesk.Plugins.Domain;
using Xunit;

namespace CaseDesk.Plugins.Tests
{
    public class SlaCalculatorTests
    {
        private static readonly DateTime Created = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Due_date_is_created_plus_resolution_hours_24x7()
        {
            var result = SlaCalculator.Calculate(Created, 72, 2);
            Assert.Equal(new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc), result.DueDate);
        }

        [Fact]
        public void Warning_is_due_minus_warning_hours()
        {
            var result = SlaCalculator.Calculate(Created, 8, 2);
            Assert.Equal(Created.AddHours(8), result.DueDate);
            Assert.Equal(Created.AddHours(6), result.WarningOn);
        }

        [Fact]
        public void Crosses_weekend_without_pausing()
        {
            var friday = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);
            Assert.Equal(friday.AddHours(24), SlaCalculator.Calculate(friday, 24, 2).DueDate);
        }

        [Fact]
        public void Warning_longer_than_sla_starts_warning_at_creation()
        {
            var result = SlaCalculator.Calculate(Created, 4, 10);
            Assert.Equal(Created, result.WarningOn);
        }

        [Fact]
        public void Zero_warning_hours_warns_at_due_date()
        {
            var result = SlaCalculator.Calculate(Created, 4, 0);
            Assert.Equal(result.DueDate, result.WarningOn);
        }

        [Fact]
        public void Non_positive_resolution_hours_is_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SlaCalculator.Calculate(Created, 0, 0));
        }
    }
}
