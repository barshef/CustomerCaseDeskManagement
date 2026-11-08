using System;

namespace CaseDesk.Plugins.Domain
{
    public struct SlaDates
    {
        public SlaDates(DateTime dueDate, DateTime warningOn)
        {
            DueDate = dueDate;
            WarningOn = warningOn;
        }

        public DateTime DueDate { get; }
        public DateTime WarningOn { get; }
    }

    /// <summary>SLA maths. Hours are 24x7 calendar hours (spec Clarifications Q1).</summary>
    public static class SlaCalculator
    {
        public static SlaDates Calculate(DateTime createdOnUtc, int resolutionHours, int warningHours)
        {
            if (resolutionHours <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolutionHours), "Resolution hours must be positive.");

            var created = DateTime.SpecifyKind(createdOnUtc, DateTimeKind.Utc);
            var due = created.AddHours(resolutionHours);

            // A warning window longer than the SLA itself means "warn from the start".
            var warnHours = Math.Max(0, Math.Min(warningHours, resolutionHours));
            var warningOn = due.AddHours(-warnHours);

            return new SlaDates(due, warningOn);
        }
    }
}
