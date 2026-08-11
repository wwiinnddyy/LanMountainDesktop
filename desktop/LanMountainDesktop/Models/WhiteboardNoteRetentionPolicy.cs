namespace LanMountainDesktop.Models;

public static class WhiteboardNoteRetentionPolicy
{
    public const int MinimumDays = 7;
    public const int MaximumDays = 15;
    public const int DefaultDays = MaximumDays;

    public static int NormalizeDays(int days)
    {
        if (days < MinimumDays)
        {
            return MinimumDays;
        }

        if (days > MaximumDays)
        {
            return MaximumDays;
        }

        return days;
    }
}
