namespace Arkanum.Go.Core;

public sealed record ClockReading(string BootId, DateTimeOffset Utc, TimeSpan Elapsed);
public sealed record LeaseState(string BootId, DateTimeOffset AnchorUtc, TimeSpan AnchorElapsed,
    DateTimeOffset HighWaterUtc, TimeSpan HighWaterElapsed, bool NeedsOnlineRevalidation = false,
    DateTimeOffset? EffectiveHighWaterUtc = null);
public sealed record ClockDecision(bool Allowed, DateTimeOffset EffectiveUtc, LeaseState NextState);

public static class LeaseClock
{
    // Trusted broker inputs only. Persist NextState atomically before releasing data, even on denial.
    // HighWaterUtc is OBSERVED wall time, never the monotonic-derived effective UTC floor.
    public static ClockDecision Advance(LeaseState state, ClockReading reading)
    {
        var effective = state.EffectiveHighWaterUtc ?? state.HighWaterUtc;
        ClockDecision Deny() => new(false, effective, state with { NeedsOnlineRevalidation = true });
        if (state.NeedsOnlineRevalidation || string.IsNullOrWhiteSpace(state.BootId) ||
            state.AnchorElapsed < TimeSpan.Zero || state.HighWaterElapsed < state.AnchorElapsed)
            return Deny();

        try
        {
            var anchorFloor = state.AnchorUtc + (state.HighWaterElapsed - state.AnchorElapsed);
            if (state.EffectiveHighWaterUtc is { } persistedFloor)
            {
                if (persistedFloor < state.HighWaterUtc || persistedFloor < anchorFloor)
                    return Deny();
            }
            else
            {
                // Compatible legacy initialization: reconstruct elapsed progress, not a fresh lease.
                if (state.HighWaterUtc < state.AnchorUtc) return Deny();
                if (anchorFloor > effective) effective = anchorFloor;
            }

            var sameBoot = string.Equals(state.BootId, reading.BootId, StringComparison.Ordinal);
            var validReading = !string.IsNullOrWhiteSpace(reading.BootId) && reading.Elapsed >= TimeSpan.Zero;
            var allowed = validReading && reading.Utc >= state.HighWaterUtc &&
                (!sameBoot || reading.Elapsed >= state.HighWaterElapsed);
            var observedWall = reading.Utc > state.HighWaterUtc ? reading.Utc : state.HighWaterUtc;

            if (!allowed)
            {
                // Retain the historical same-boot high-water behavior without repairing a bad boot.
                var elapsed = sameBoot && reading.Elapsed > state.HighWaterElapsed ? reading.Elapsed : state.HighWaterElapsed;
                if (sameBoot) effective += elapsed - state.HighWaterElapsed;
                if (observedWall > effective) effective = observedWall;
                return new(false, effective, state with
                {
                    HighWaterUtc = observedWall, HighWaterElapsed = elapsed,
                    EffectiveHighWaterUtc = effective, NeedsOnlineRevalidation = true
                });
            }

            // Same boot: consume every newly observed elapsed tick on top of the durable floor.
            // New boot: RTC delta includes downtime; uptime is an independent conservative lower
            // bound. Add the larger to the OLD floor so a lagging wall cannot erase prior progress.
            var consumed = sameBoot ? reading.Elapsed - state.HighWaterElapsed :
                Max(reading.Utc - state.HighWaterUtc, reading.Elapsed);
            effective += consumed;
            if (observedWall > effective) effective = observedWall;
            return new(true, effective, state with
            {
                BootId = reading.BootId,
                AnchorUtc = sameBoot ? state.AnchorUtc : effective,
                AnchorElapsed = sameBoot ? state.AnchorElapsed : reading.Elapsed,
                HighWaterUtc = observedWall,
                HighWaterElapsed = reading.Elapsed,
                EffectiveHighWaterUtc = effective
            });
        }
        catch (ArgumentOutOfRangeException) { return Deny(); }
        catch (OverflowException) { return Deny(); }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
