using System.Text.Json;
using Arkanum.Go.Core;

namespace Arkanum.Go.Core.Tests;

public class CrossBootLeaseTests
{
    private static DateTimeOffset Start => LeaseClockTests.Start;
    private static LeaseState Initial => LeaseClockTests.Initial;
    private static LeaseState Persist(LeaseState state) => JsonSerializer.Deserialize<LeaseState>(JsonSerializer.Serialize(state))!;

    [Fact]
    public void Repeated_boots_and_service_restarts_do_not_renew_original_expiry()
    {
        var expiry = Start.AddHours(48);
        var state = Initial;
        for (var hour = 1; hour <= 49; hour++)
        {
            var result = LeaseClock.Advance(Persist(state), new($"boot-{hour}", Start.AddHours(hour), TimeSpan.Zero));
            Assert.True(result.Allowed);
            Assert.Equal(Start.AddHours(hour), result.EffectiveUtc);
            Assert.Equal(hour < 48, result.EffectiveUtc < expiry);
            var restarted = LeaseClock.Advance(Persist(result.NextState), new($"boot-{hour}", Start.AddHours(hour), TimeSpan.Zero));
            Assert.True(restarted.Allowed);
            Assert.Equal(result.EffectiveUtc, restarted.EffectiveUtc);
            state = restarted.NextState;
        }
    }

    [Theory]
    [InlineData(48)]
    [InlineData(60)]
    public void Powered_off_time_reaches_original_expiry(int hours)
    {
        var beforeShutdown = LeaseClock.Advance(Initial, new("boot-A", Start.AddHours(1), TimeSpan.FromHours(4)));
        var after = LeaseClock.Advance(Persist(beforeShutdown.NextState), new("boot-B", Start.AddHours(hours), TimeSpan.FromMinutes(1)));
        Assert.True(after.Allowed); // Clock validity is separate from the signed grant's expiry gate.
        Assert.Equal(Start.AddHours(hours), after.EffectiveUtc);
        Assert.False(after.EffectiveUtc < Start.AddHours(48));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Frozen_or_slow_wall_cannot_erase_elapsed_time_across_chained_boots(int wallSecondsPerHour)
    {
        var state = Initial;
        DateTimeOffset previous = Start;
        for (var boot = 0; boot < 25; boot++)
        {
            var bootId = boot == 0 ? "boot-A" : $"new-{boot}";
            var baseline = boot == 0 ? TimeSpan.FromHours(3) : TimeSpan.Zero;
            if (boot > 0)
            {
                var transition = LeaseClock.Advance(Persist(state), new(bootId, Start.AddSeconds(boot * 2 * wallSecondsPerHour), baseline));
                Assert.True(transition.Allowed);
                Assert.True(transition.EffectiveUtc >= previous);
                state = transition.NextState;
            }
            var result = LeaseClock.Advance(Persist(state), new(bootId, Start.AddSeconds((boot + 1) * 2 * wallSecondsPerHour), baseline + TimeSpan.FromHours(2)));
            Assert.True(result.Allowed);
            Assert.True(result.EffectiveUtc >= Start.AddHours((boot + 1) * 2));
            Assert.Equal(Start.AddSeconds((boot + 1) * 2 * wallSecondsPerHour), result.NextState.HighWaterUtc);
            previous = result.EffectiveUtc;
            state = result.NextState;
        }
        Assert.True(previous >= Start.AddHours(48));
    }

    [Fact]
    public void New_boot_uptime_is_not_discarded_when_wall_is_frozen()
    {
        var first = LeaseClock.Advance(Initial, new("boot-A", Start, TimeSpan.FromHours(8)));
        var second = LeaseClock.Advance(Persist(first.NextState), new("boot-B", Start, TimeSpan.FromHours(2)));
        Assert.True(second.Allowed);
        Assert.Equal(Start.AddHours(7), second.EffectiveUtc);
    }

    [Fact]
    public void Monotonic_rollback_after_reboot_latches_across_further_boots()
    {
        var boot = LeaseClock.Advance(Initial, new("boot-B", Start.AddHours(1), TimeSpan.FromHours(2)));
        Assert.True(boot.Allowed);
        var denied = LeaseClock.Advance(Persist(boot.NextState), new("boot-B", Start.AddHours(2), TimeSpan.FromHours(1)));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
        Assert.False(LeaseClock.Advance(Persist(denied.NextState), new("boot-C", Start.AddHours(3), TimeSpan.Zero)).Allowed);
    }

    [Fact]
    public void Cross_boot_wall_rollback_latches_even_after_wall_catches_up()
    {
        var prior = LeaseClock.Advance(Initial, new("boot-A", Start.AddHours(2), TimeSpan.FromHours(5)));
        var denied = LeaseClock.Advance(Persist(prior.NextState), new("boot-B", Start.AddHours(1), TimeSpan.Zero));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
        Assert.Equal(Start.AddHours(2), denied.NextState.HighWaterUtc);
        Assert.False(LeaseClock.Advance(Persist(denied.NextState), new("boot-C", Start.AddHours(3), TimeSpan.Zero)).Allowed);
    }

    [Fact]
    public void Forward_wall_jump_then_frozen_wall_still_consumes_monotonic_time()
    {
        var jump = LeaseClock.Advance(Initial, new("boot-A", Start.AddHours(10), TimeSpan.FromHours(4)));
        var later = LeaseClock.Advance(Persist(jump.NextState), new("boot-A", Start.AddHours(10), TimeSpan.FromHours(5)));
        Assert.True(later.Allowed);
        Assert.Equal(Start.AddHours(11), later.EffectiveUtc);
    }

    [Theory]
    [InlineData("negativeAnchor")]
    [InlineData("negativeHighWater")]
    [InlineData("elapsedOrder")]
    [InlineData("wallOrder")]
    [InlineData("blankBoot")]
    public void Invalid_legacy_state_is_not_repaired_by_boot_transition(string invalid)
    {
        var state = invalid switch
        {
            "negativeAnchor" => Initial with { AnchorElapsed = TimeSpan.FromTicks(-1) },
            "negativeHighWater" => Initial with { HighWaterElapsed = TimeSpan.FromTicks(-1) },
            "elapsedOrder" => Initial with { HighWaterElapsed = TimeSpan.Zero },
            "wallOrder" => Initial with { HighWaterUtc = Start.AddTicks(-1) },
            _ => Initial with { BootId = " " }
        };
        var denied = LeaseClock.Advance(state, new("boot-B", Start.AddHours(1), TimeSpan.Zero));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
        Assert.False(LeaseClock.Advance(Persist(denied.NextState), new("boot-C", Start.AddHours(2), TimeSpan.Zero)).Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Missing_reading_boot_identity_latches(string? bootId)
    {
        var denied = LeaseClock.Advance(Initial, new(bootId!, Start.AddHours(1), TimeSpan.Zero));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("monotonic")]
    public void Invalid_durable_effective_floor_requires_online_recovery(string invalid)
    {
        var state = invalid == "wall"
            ? Initial with { HighWaterUtc = Start.AddHours(2), EffectiveHighWaterUtc = Start.AddHours(1) }
            : Initial with { HighWaterElapsed = TimeSpan.FromHours(5), EffectiveHighWaterUtc = Start.AddHours(1) };
        var denied = LeaseClock.Advance(Persist(state), new("boot-B", Start.AddHours(3), TimeSpan.Zero));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
        Assert.False(LeaseClock.Advance(Persist(denied.NextState), new("boot-C", Start.AddHours(4), TimeSpan.Zero)).Allowed);
    }

    [Fact]
    public void Durable_floor_is_separate_from_observed_wall_after_serialized_reboot()
    {
        var first = LeaseClock.Advance(Initial, new("boot-A", Start, TimeSpan.FromHours(8)));
        Assert.Equal(Start.AddHours(5), first.NextState.EffectiveHighWaterUtc);
        Assert.Equal(Start, first.NextState.HighWaterUtc);
        var boot = LeaseClock.Advance(Persist(first.NextState), new("boot-B", Start, TimeSpan.Zero));
        Assert.True(boot.Allowed);
        Assert.Equal(Start.AddHours(5), boot.NextState.AnchorUtc);
        Assert.Equal(Start.AddHours(5), boot.NextState.EffectiveHighWaterUtc);
        Assert.Equal(Start, boot.NextState.HighWaterUtc);
        Assert.True(LeaseClock.Advance(Persist(boot.NextState), new("boot-B", Start, TimeSpan.FromHours(1))).Allowed);
    }

    [Fact]
    public void Cross_boot_floor_overflow_latches_without_reanchoring()
    {
        var state = new LeaseState("boot-A", DateTimeOffset.MaxValue.AddHours(-1), TimeSpan.Zero,
            DateTimeOffset.MaxValue.AddHours(-1), TimeSpan.Zero);
        var denied = LeaseClock.Advance(state, new("boot-B", state.HighWaterUtc, TimeSpan.FromHours(2)));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
        Assert.Equal("boot-A", denied.NextState.BootId);
        Assert.False(LeaseClock.Advance(Persist(denied.NextState), new("boot-C", state.HighWaterUtc, TimeSpan.Zero)).Allowed);
    }

    [Fact]
    public void Negative_new_boot_elapsed_latches()
    {
        var denied = LeaseClock.Advance(Initial, new("boot-B", Start.AddHours(1), TimeSpan.FromTicks(-1)));
        Assert.False(denied.Allowed);
        Assert.True(denied.NextState.NeedsOnlineRevalidation);
    }
}
