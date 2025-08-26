namespace Commitments.Domain.Entities;

public enum CommitmentStatus
{
    Active = 0,
    DecisionNeeded = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Deleted = 5
}

[Flags]
public enum WeekdayMask
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
}

public enum SchedulePatternType
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}
