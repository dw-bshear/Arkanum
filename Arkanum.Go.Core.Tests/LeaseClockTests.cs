using Arkanum.Go.Core;

namespace Arkanum.Go.Core.Tests;

public class LeaseClockTests
{
    internal static readonly DateTimeOffset Start = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    internal static LeaseState Initial => new("boot-A", Start, TimeSpan.FromHours(3), Start, TimeSpan.FromHours(3));

    [Fact]
    public void Same_boot_uses_elapsed_time_even_when_wall_clock_is_frozen()
    {
        var result = LeaseClock.Advance(Initial, new("boot-A", Start, TimeSpan.FromHours(4)));
        Assert.True(result.Allowed);
        Assert.Equal(Start.AddHours(1), result.EffectiveUtc);
        Assert.Equal(Start, result.NextState.HighWaterUtc);
        Assert.Equal(TimeSpan.FromHours(4), result.NextState.HighWaterElapsed);
    }

    [Fact]
    public void Repeated_opens_do_not_reset_monotonic_anchor()
    {
        var first = LeaseClock.Advance(Initial, new("boot-A", Start.AddMinutes(10), TimeSpan.FromHours(4)));
        var next = LeaseClock.Advance(first.NextState, new("boot-A", Start.AddHours(2), TimeSpan.FromHours(5)));
        Assert.Equal(Start.AddHours(2), next.EffectiveUtc);
        Assert.Equal(Initial.AnchorUtc, next.NextState.AnchorUtc);
        Assert.Equal(Initial.AnchorElapsed, next.NextState.AnchorElapsed);
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(1, 2)]
    public void Wall_or_monotonic_rollback_denies_and_preserves_high_water(int wallHours, int elapsedHours)
    {
        var result = LeaseClock.Advance(Initial, new("boot-A", Start.AddHours(wallHours), TimeSpan.FromHours(elapsedHours)));
        Assert.False(result.Allowed);
        Assert.True(result.NextState.HighWaterUtc >= Initial.HighWaterUtc);
        Assert.True(result.NextState.HighWaterElapsed >= Initial.HighWaterElapsed);
        Assert.Equal(Initial.AnchorUtc, result.NextState.AnchorUtc);
    }

    [Fact]
    public void Failed_advance_is_not_a_new_lease()
    {
        var failed = LeaseClock.Advance(Initial, new("boot-A", Start.AddHours(-1), TimeSpan.FromHours(5)));
        Assert.False(failed.Allowed);
        Assert.Equal(Start, failed.NextState.HighWaterUtc);
        Assert.Equal(TimeSpan.FromHours(5), failed.NextState.HighWaterElapsed);
        var retry = LeaseClock.Advance(failed.NextState, new("boot-A", Start.AddMinutes(30), TimeSpan.FromHours(5)));
        Assert.False(retry.Allowed);
    }

    [Fact]
    public void Ordinary_offline_reboot_preserves_remaining_original_window()
    {
        var result = LeaseClock.Advance(Initial, new("boot-B", Start.AddHours(1), TimeSpan.Zero));
        Assert.True(result.Allowed);
        Assert.Equal(Start.AddHours(1), result.EffectiveUtc);
        Assert.Equal("boot-B", result.NextState.BootId);
        Assert.Equal(TimeSpan.Zero, result.NextState.AnchorElapsed);
        var next = LeaseClock.Advance(result.NextState, new("boot-B", Start.AddHours(2), TimeSpan.FromHours(1)));
        Assert.True(next.Allowed);
        Assert.Equal(Start.AddHours(2), next.EffectiveUtc);
    }

    [Fact]
    public void Invalid_anchor_or_overflow_fails_closed()
    {
        Assert.False(LeaseClock.Advance(Initial with { HighWaterUtc = Start.AddSeconds(-1) }, new("boot-A", Start, TimeSpan.FromHours(3))).Allowed);
        Assert.False(LeaseClock.Advance(Initial, new("boot-A", Start, TimeSpan.MaxValue)).Allowed);
    }

    [Fact]
    public void Invalid_persisted_state_cannot_be_silently_repaired_by_a_failed_open()
    {
        var corrupt = Initial with { HighWaterUtc = Start.AddTicks(-1) };
        var first = LeaseClock.Advance(corrupt, new("boot-A", Start, TimeSpan.FromHours(3)));
        Assert.False(first.Allowed);
        Assert.False(LeaseClock.Advance(first.NextState, new("boot-A", Start.AddSeconds(1), TimeSpan.FromHours(4))).Allowed);
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("monotonic")]
    [InlineData("invalidBoot")]
    [InlineData("overflow")]
    public void Clock_failure_stays_denied_even_when_later_readings_look_valid(string failure)
    {
        var reading = failure switch
        {
            "wall" => new ClockReading("boot-A", Start.AddSeconds(-1), TimeSpan.FromHours(3)),
            "monotonic" => new ClockReading("boot-A", Start, TimeSpan.FromHours(2)),
            "invalidBoot" => new ClockReading("", Start, TimeSpan.Zero),
            _ => new ClockReading("boot-A", Start, TimeSpan.MaxValue)
        };
        var denied = LeaseClock.Advance(Initial, reading);
        Assert.False(denied.Allowed);
        var recoveredLookingReading = new ClockReading("boot-A", Start.AddHours(1), TimeSpan.FromHours(4));
        Assert.False(LeaseClock.Advance(denied.NextState, recoveredLookingReading).Allowed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Nondecreasing_wall_behind_monotonic_time_is_not_rollback(int wallStepSeconds)
    {
        var state = Initial;
        for (var step = 1; step <= 3; step++)
        {
            var reading = new ClockReading("boot-A", Start.AddSeconds(step * wallStepSeconds),
                Initial.AnchorElapsed + TimeSpan.FromSeconds(step * 2));
            var result = LeaseClock.Advance(state, reading);
            Assert.True(result.Allowed);
            Assert.Equal(Start.AddSeconds(step * 2), result.EffectiveUtc);
            state = result.NextState;
        }
    }
}
