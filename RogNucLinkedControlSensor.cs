using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FanControl.Plugins;

namespace FanControl.ROGNUC15JNK;

internal sealed class RogNucLinkedControlSensor : IPluginControlSensor
{
    private readonly FanCommandPolicy _policy = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AsusAcpiReadOnlyFanSource _source;
    private readonly AsusModeSnapshot _initialState;
    private readonly IPluginLogger? _logger;
    private DateTime _lastConflictLogUtc = DateTime.MinValue;
    private DateTime? _gHelperConfigWriteUtc;
    private DateTime? _gHelperSettleUntilUtc;
    private AsusModeSnapshot? _gHelperState;
    private bool _gHelperWasRunning;
    private bool _gHelperReapplyPending;
    private int? _lastWrittenPercent;
    private bool _active;
    private bool _writeDisabled;
    private float? _value;
    private readonly object _writeQueueLock = new();
    private PendingWrite? _pendingWrite;
    private bool _writeWorkerRunning;
    private int? _lastRequestedPercent;

    internal RogNucLinkedControlSensor(AsusAcpiReadOnlyFanSource source,
        AsusModeSnapshot initialState, IPluginLogger? logger)
    {
        _source = source;
        _initialState = initialState;
        _logger = logger;
        _logger?.Log("ROG NUC control policy v17: queued ASUS writes; direct 17-30% quiet-band steps; immediate rise and final cool-down");
    }

    public string Id => "asus:linked-cpu-gpu-control";
    public string Name => "CPU- & GPU-Lüfter (gemeinsam)";
    public string Origin => "ROG NUC 2025";
    public float? Value => _value;
    public void Update() { }

    public void Set(float val)
    {
        if (_writeDisabled) return;
        if (float.IsNaN(val) || float.IsInfinity(val))
        {
            _policy.Clear();
            return;
        }
        if (!TryPrepareGHelperCoexistence(out var forceReapply, out var baseMode)) return;

        // The ASUS ACPI write may take a long time.  Base policy decisions on
        // the latest requested value as well as the last completed write, so a
        // slider drag remains responsive without queueing every intermediate
        // point for the firmware.
        var currentPercent = _lastRequestedPercent ?? _lastWrittenPercent;
        var decision = _policy.Evaluate(val, currentPercent,
            _clock.Elapsed.TotalSeconds, forceReapply);
        if (!decision.HasValue) return;
        var percent = decision.Value;

        _lastRequestedPercent = percent;
        QueueWrite(new PendingWrite(val, percent, baseMode, forceReapply));
    }

    private void QueueWrite(PendingWrite request)
    {
        lock (_writeQueueLock)
        {
            // Retain only the newest value.  The intermediate positions of a
            // slider drag have no cooling benefit, but each would restart the
            // ASUS curve controller.
            if (_pendingWrite != null && _pendingWrite.ForceReapply)
                request = new PendingWrite(request.Requested, request.Percent,
                    request.BaseMode, true);
            _pendingWrite = request;
            if (_writeWorkerRunning) return;
            _writeWorkerRunning = true;
        }

        _ = Task.Run(ProcessWriteQueue);
    }

    private void ProcessWriteQueue()
    {
        while (true)
        {
            PendingWrite? request;
            lock (_writeQueueLock)
            {
                request = _pendingWrite;
                _pendingWrite = null;
                if (request == null)
                {
                    _writeWorkerRunning = false;
                    return;
                }
            }

            ApplyWrite(request);
        }
    }

    private void ApplyWrite(PendingWrite request)
    {
        if (_writeDisabled) return;
        var percent = request.Percent;

        if (request.ForceReapply)
            _logger?.Log("ROG NUC coexistence: reapplying Fan Control curve after G-Helper change");
        _logger?.Log($"ROG NUC linked control requested={request.Requested:0.##}% " +
            $"quantized={percent}% previous={_lastWrittenPercent?.ToString() ?? "none"}%");
        if (!_active || request.ForceReapply)
        {
            var mode = _source.SetPerformanceMode(request.BaseMode);
            _logger?.Log("ROG NUC linked control activate mode: " + mode);
            if (!mode.Succeeded)
            {
                DisableAfterFailure("performance mode was rejected");
                return;
            }
        }

        var curve = CreateFailSafeCurve(percent);
        var result = _source.WriteLinkedCurve(curve, curve);
        _logger?.Log("ROG NUC linked control curve result: " + result);
        if (!result.Succeeded)
        {
            DisableAfterFailure("CPU/GPU curve was rejected");
            RestoreInitialState();
            return;
        }

        _active = true;
        _value = percent;
        _lastWrittenPercent = percent;
    }

    public void Reset()
    {
        if (!_active)
        {
            ClearActiveState();
            return;
        }

        if (IsGHelperRunning())
        {
            var currentState = AsusModeSnapshot.TryCapture(_logger, false);
            if (currentState == null)
            {
                _logger?.Log("ROG NUC linked control reset skipped: G-Helper state could not be read safely");
                ClearActiveState();
                return;
            }

            var restored = currentState.Restore(_source, _logger);
            _logger?.Log("ROG NUC linked control reset to current G-Helper mode=" +
                (restored ? "confirmed" : "failed"));
            ClearActiveState();
            if (!restored) _writeDisabled = true;
            return;
        }

        RestoreInitialState();
    }

    private void RestoreInitialState()
    {
        var restored = _initialState.Restore(_source, _logger);
        _logger?.Log("ROG NUC linked control reset result=" + (restored ? "confirmed" : "failed"));
        ClearActiveState();
        if (!restored) _writeDisabled = true;
    }

    private void ClearActiveState()
    {
        _active = false;
        _value = null;
        _lastWrittenPercent = null;
        _lastRequestedPercent = null;
        ClearPendingChange();
    }

    private void ClearPendingChange()
    {
        _policy.Clear();
    }

    private bool TryPrepareGHelperCoexistence(out bool forceReapply, out int baseMode)
    {
        forceReapply = false;
        baseMode = _initialState.BaseMode;
        var running = IsGHelperRunning();

        if (!running)
        {
            if (_gHelperWasRunning)
            {
                baseMode = _gHelperState?.BaseMode ?? _initialState.BaseMode;
                forceReapply = true;
                _logger?.Log("ROG NUC coexistence: G-Helper closed; reclaiming fan control");
            }

            _gHelperWasRunning = false;
            _gHelperConfigWriteUtc = null;
            _gHelperSettleUntilUtc = null;
            _gHelperState = null;
            _gHelperReapplyPending = false;
            return true;
        }

        var configWriteUtc = AsusModeSnapshot.TryGetConfigWriteUtc();
        if (!configWriteUtc.HasValue)
        {
            LogGHelperConflict("G-Helper config timestamp could not be read");
            return false;
        }

        if (!_gHelperWasRunning || _gHelperConfigWriteUtc != configWriteUtc)
        {
            var state = AsusModeSnapshot.TryCapture(_logger, false);
            if (state == null)
            {
                LogGHelperConflict("G-Helper config could not be read safely");
                return false;
            }

            _gHelperWasRunning = true;
            _gHelperConfigWriteUtc = configWriteUtc;
            _gHelperState = state;
            _gHelperSettleUntilUtc = DateTime.UtcNow.AddSeconds(3);
            _gHelperReapplyPending = true;
            _logger?.Log($"ROG NUC coexistence: G-Helper change detected, mode={state.SelectedMode}, " +
                $"base={state.BaseMode}, customFans={state.CustomFansEnabled}; waiting 3 seconds");
        }

        if (_gHelperState == null)
        {
            LogGHelperConflict("G-Helper state is unavailable");
            return false;
        }

        baseMode = _gHelperState.BaseMode;
        if (_gHelperState.CustomFansEnabled)
        {
            LogGHelperConflict("G-Helper custom fan curve is enabled for mode " +
                _gHelperState.SelectedMode);
            return false;
        }

        if (_gHelperSettleUntilUtc.HasValue && DateTime.UtcNow < _gHelperSettleUntilUtc.Value)
            return false;

        if (_gHelperReapplyPending)
        {
            _gHelperReapplyPending = false;
            _gHelperSettleUntilUtc = null;
            forceReapply = true;
        }

        return true;
    }

    private void LogGHelperConflict(string reason)
    {
        _policy.Clear();
        if (DateTime.UtcNow - _lastConflictLogUtc < TimeSpan.FromSeconds(30)) return;
        _logger?.Log("ROG NUC linked control blocked: " + reason);
        _lastConflictLogUtc = DateTime.UtcNow;
    }

    private void DisableAfterFailure(string reason)
    {
        _writeDisabled = true;
        _value = null;
        _logger?.Log("ROG NUC linked control disabled: " + reason);
    }

    private static byte[] CreateFailSafeCurve(int percent)
    {
        var requested = (byte)percent;
        return new byte[]
        {
            20, 30, 40, 50, 60, 70, 85, 95,
            requested, requested, requested, requested, requested,
            (byte)Math.Max(percent, 17),
            (byte)Math.Max(percent, 60),
            100
        };
    }

    private static bool IsGHelperRunning()
    {
        Process[]? processes = null;
        try
        {
            processes = Process.GetProcessesByName("GHelper");
            return processes.Any();
        }
        catch { return true; }
        finally
        {
            if (processes != null)
                foreach (var process in processes) process.Dispose();
        }
    }

    private sealed class PendingWrite
    {
        internal PendingWrite(float requested, int percent, int baseMode, bool forceReapply)
        {
            Requested = requested;
            Percent = percent;
            BaseMode = baseMode;
            ForceReapply = forceReapply;
        }

        internal float Requested { get; }
        internal int Percent { get; }
        internal int BaseMode { get; }
        internal bool ForceReapply { get; }
    }
}
