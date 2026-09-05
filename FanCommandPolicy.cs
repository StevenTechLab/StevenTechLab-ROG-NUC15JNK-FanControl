using System;

namespace FanControl.ROGNUC15JNK;

// Pure decision logic: no hardware access. Time comes from a monotonic clock.
internal sealed class FanCommandPolicy
{
    // Firmware curve writes are comparatively heavy: issuing small changes
    // repeatedly can make the embedded controller reinitialize the fans. Fan
    // Control already handles temperature hysteresis; this window coalesces the
    // resulting hardware writes while the requested level is merely wobbling.
    private const double SmallChangeSettleSeconds = 3.0;
    private const double NormalDecreaseSettleSeconds = 6.0;
    // Fan Control already has temperature hysteresis. Adding another wait here
    // makes a short benchmark finish before its final lower command is sent.
    private const double HighSpeedCooldownSettleSeconds = 0.0;
    private const int ImmediateIncreaseDelta = 15;
    private const int HighSpeedMinorChangeDeadband = 5;
    private int? _pendingTarget;
    private double? _pendingSinceSeconds;
    private double? _pendingSettleSeconds;

    internal int? Evaluate(float requested, int? applied, double seconds, bool force)
    {
        // Invalid commands must not become fan-stop or enter numeric conversions.
        if (float.IsNaN(requested) || float.IsInfinity(requested))
        {
            Clear();
            return null;
        }

        var target = Quantize(requested, applied);
        if (force)
        {
            Clear();
            return Math.Max(target, applied ?? target);
        }

        if (!applied.HasValue)
        {
            Clear();
            return target;
        }

        if (target == applied.Value)
        {
            // Returning to the current level cancels a transient change.
            Clear();
            return null;
        }

        // A one-step change such as 40% -> 45% causes the ASUS controller to
        // restart its curve even though it makes no useful cooling difference.
        // Ignore it completely rather than issuing a delayed second rewrite.
        if (applied.Value >= 35 &&
            Math.Abs(target - applied.Value) <= HighSpeedMinorChangeDeadband)
        {
            Clear();
            return null;
        }

        // A deliberate request for the passive zone must remain responsive.
        // Fan Control's own curve hysteresis protects this boundary.
        if (target == 0)
        {
            Clear();
            return 0;
        }

        // The 20/25/30% range is a useful quiet operating band on this NUC
        // (roughly 1,000 RPM around 20%).  Do not fold those deliberate
        // adjustments into the general coalescing delay: in Fan Control's
        // manual mode a slider movement can otherwise remain at 17%.
        if (target <= 30 && applied.Value <= 30)
        {
            Clear();
            return target;
        }

        // A substantial rise is a real cooling demand, so do not delay it.
        if (target > applied.Value &&
            (target - applied.Value >= ImmediateIncreaseDelta || applied.Value >= 60))
        {
            Clear();
            return target;
        }

        // Keep the latest requested value during one settling period. A BIOS fan
        // curve rewrite restarts the controller's ramp. Let Fan Control's own
        // hysteresis choose the lower value, then write it without another delay.
        _pendingTarget = target;
        if (!_pendingSinceSeconds.HasValue)
        {
            _pendingSinceSeconds = seconds;
            _pendingSettleSeconds = target < applied.Value
                ? (applied.Value >= 60 ? HighSpeedCooldownSettleSeconds : NormalDecreaseSettleSeconds)
                : SmallChangeSettleSeconds;
        }
        if (seconds - _pendingSinceSeconds.Value < _pendingSettleSeconds!.Value)
            return null;

        var decision = _pendingTarget.Value;
        Clear();
        return decision;
    }

    internal void Clear()
    {
        _pendingTarget = null;
        _pendingSinceSeconds = null;
        _pendingSettleSeconds = null;
    }

    private static int Quantize(float requested, int? applied)
    {
        // A stopped fan must reach 17% before starting.  Below that reliable
        // floor, preserve the stop deadband.  The quiet range offers individual
        // 1% steps so roughly 800/900/1000/1100/1200 RPM can be selected where
        // the firmware exposes those distinct physical speeds.  Higher values
        // retain 5% steps to avoid needless curve rewrites.
        var running = applied.GetValueOrDefault() > 0;
        if (requested < (running ? 15f : 17f)) return 0;
        if (requested <= 17f) return 17;
        if (requested <= 30f) return (int)Math.Ceiling(requested);
        return (int)Math.Min(100, Math.Ceiling(requested / 5.0) * 5);
    }
}
