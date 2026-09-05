using System;
using System.Collections.Generic;
using System.Management;

namespace FanControl.ROGNUC15JNK;

/// <summary>
/// Conservative WMI reader. Win32_Fan does not standardize a measured RPM property;
/// therefore it only discovers values labelled as a speed by the provider and ignores
/// unsupported or implausible values. Add vendor-specific adapters only after Gate 1.
/// </summary>
internal sealed class WmiReadOnlyFanSource
{
    internal IEnumerable<FanReading> Discover()
    {
        foreach (var pair in Read())
            yield return new FanReading(pair.Key, "WMI fan " + pair.Key, pair.Value);
    }

    internal Dictionary<string, float> Read()
    {
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        using var searcher = new ManagementObjectSearcher("SELECT DeviceID, DesiredSpeed FROM Win32_Fan");
        using var results = searcher.Get();
        foreach (ManagementObject fan in results)
        {
            var deviceId = fan["DeviceID"]?.ToString();
            if (string.IsNullOrWhiteSpace(deviceId)) continue;
            if (!TryRpm(fan["DesiredSpeed"], out var rpm)) continue;
            values["wmi:Win32_Fan:" + deviceId] = rpm;
        }
        return values;
    }

    private static bool TryRpm(object? raw, out float rpm)
    {
        rpm = 0;
        return raw != null && float.TryParse(raw.ToString(), out rpm) && rpm is >= 100 and <= 20000;
    }
}

internal sealed class FanReading
{
    internal FanReading(string id, string name, float rpm)
    {
        Id = id;
        Name = name;
        Rpm = rpm;
    }

    internal string Id { get; }
    internal string Name { get; }
    internal float Rpm { get; }
}
