using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FanControl.Plugins;

namespace FanControl.ROGNUC15JNK;

internal sealed class AsusModeSnapshot
{
    internal static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GHelper", "config.json");

    private AsusModeSnapshot(int selectedMode, int baseMode, bool customFansEnabled,
        byte[]? cpuCurve, byte[]? gpuCurve)
    {
        SelectedMode = selectedMode;
        BaseMode = baseMode;
        CustomFansEnabled = customFansEnabled;
        CpuCurve = cpuCurve;
        GpuCurve = gpuCurve;
    }

    internal int SelectedMode { get; }
    internal int BaseMode { get; }
    internal int CurveSelector => BaseMode == 1 ? 2 : BaseMode == 2 ? 1 : 0;
    internal bool CustomFansEnabled { get; }
    internal byte[]? CpuCurve { get; }
    internal byte[]? GpuCurve { get; }

    internal static AsusModeSnapshot? TryCapture(IPluginLogger? logger, bool logSuccess = true)
    {
        try
        {
            var json = File.ReadAllText(ConfigPath);
            if (!TryReadInt(json, "performance_mode", out var selectedMode))
            {
                logger?.Log("ROG NUC control unavailable: G-Helper performance_mode is missing");
                return null;
            }

            var baseMode = selectedMode;
            if (selectedMode > 2 && !TryReadInt(json, "mode_base_" + selectedMode, out baseMode))
            {
                logger?.Log("ROG NUC control unavailable: custom mode base is missing");
                return null;
            }
            if (baseMode < 0 || baseMode > 2)
            {
                logger?.Log("ROG NUC control unavailable: invalid base mode " + baseMode);
                return null;
            }

            var customFans = TryReadInt(json, "auto_apply_" + selectedMode, out var autoApply) && autoApply == 1;
            var cpuCurve = TryReadCurve(json, "fan_profile_cpu_" + selectedMode);
            var gpuCurve = TryReadCurve(json, "fan_profile_gpu_" + selectedMode);
            if (customFans && (!IsValidCurve(cpuCurve) || !IsValidCurve(gpuCurve)))
            {
                logger?.Log("ROG NUC control unavailable: active custom curves cannot be restored safely");
                return null;
            }

            if (logSuccess)
                logger?.Log($"ROG NUC initial mode captured: selected={selectedMode}, base={baseMode}, customFans={customFans}");
            return new AsusModeSnapshot(selectedMode, baseMode, customFans, cpuCurve, gpuCurve);
        }
        catch (Exception ex)
        {
            logger?.Log("ROG NUC control unavailable: G-Helper config read failed: " + ex.Message);
            return null;
        }
    }

    internal static DateTime? TryGetConfigWriteUtc()
    {
        try { return File.GetLastWriteTimeUtc(ConfigPath); }
        catch { return null; }
    }

    internal bool Restore(AsusAcpiReadOnlyFanSource source, IPluginLogger? logger)
    {
        var mode = source.SetPerformanceMode(BaseMode);
        logger?.Log("ROG NUC restore base mode: " + mode);
        if (!mode.Succeeded) return false;
        if (!CustomFansEnabled) return true;

        var curves = source.WriteLinkedCurve(CpuCurve!, GpuCurve!);
        logger?.Log("ROG NUC restore custom curves: " + curves);
        return curves.Succeeded;
    }

    private static bool TryReadInt(string json, string key, out int value)
    {
        value = 0;
        var match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(-?\\d+)",
            RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out value);
    }

    private static byte[]? TryReadCurve(string json, string key)
    {
        var match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        try
        {
            var curve = match.Groups[1].Value.Split('-').Select(x => Convert.ToByte(x, 16)).ToArray();
            return curve.Length == 16 ? curve : null;
        }
        catch { return null; }
    }

    private static bool IsValidCurve(byte[]? curve) => curve is { Length: 16 } &&
        curve.Take(8).All(x => x <= 125) && curve.Skip(8).All(x => x <= 100) &&
        curve.Any(x => x != 0);
}
