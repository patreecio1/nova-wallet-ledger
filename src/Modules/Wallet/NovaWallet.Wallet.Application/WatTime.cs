namespace NovaWallet.Wallet.Application;

/// <summary>West Africa Time — fixed UTC+1, no daylight saving, ever. Used only for the "reset at midnight WAT" daily-limit window.</summary>
public static class WatTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    /// <summary>The [fromUtc, toUtc) window covering "today" in WAT, for the instant <paramref name="utcNow"/> falls in.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) GetCurrentDayWindowUtc(DateTime utcNow)
    {
        var watNow = utcNow + Offset;
        var watMidnightAsUtc = DateTime.SpecifyKind(watNow.Date - Offset, DateTimeKind.Utc);

        return (watMidnightAsUtc, watMidnightAsUtc.AddDays(1));
    }
}
