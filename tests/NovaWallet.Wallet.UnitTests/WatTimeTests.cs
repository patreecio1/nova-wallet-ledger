using FluentAssertions;
using NovaWallet.Wallet.Application;

namespace NovaWallet.Wallet.UnitTests;

public class WatTimeTests
{
    [Fact]
    public void Midday_utc_falls_within_the_same_wat_calendar_day()
    {
        var utcNow = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var (fromUtc, toUtc) = WatTime.GetCurrentDayWindowUtc(utcNow);

        // 2026-03-10 12:00 UTC = 2026-03-10 13:00 WAT, so the window is the UTC instants
        // corresponding to 2026-03-10 00:00 WAT through 2026-03-11 00:00 WAT.
        fromUtc.Should().Be(new DateTime(2026, 3, 9, 23, 0, 0, DateTimeKind.Utc));
        toUtc.Should().Be(new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Just_before_wat_midnight_falls_in_the_earlier_wat_day()
    {
        // 23:30 UTC = 00:30 WAT the next day.
        var utcNow = new DateTime(2026, 3, 10, 22, 59, 0, DateTimeKind.Utc);

        var (fromUtc, toUtc) = WatTime.GetCurrentDayWindowUtc(utcNow);

        fromUtc.Should().Be(new DateTime(2026, 3, 9, 23, 0, 0, DateTimeKind.Utc));
        toUtc.Should().Be(new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Just_after_wat_midnight_rolls_into_the_next_window()
    {
        // 23:00 UTC = 00:00 WAT the next day, exactly the boundary.
        var utcNow = new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc);

        var (fromUtc, toUtc) = WatTime.GetCurrentDayWindowUtc(utcNow);

        fromUtc.Should().Be(new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc));
        toUtc.Should().Be(new DateTime(2026, 3, 11, 23, 0, 0, DateTimeKind.Utc));
    }
}
