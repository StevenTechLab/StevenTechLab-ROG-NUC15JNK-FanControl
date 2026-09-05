using System;
using System.IO;
using FanControl.Plugins;

namespace FanControl.ROGNUC15JNK;

internal static class CurveBackup
{
    internal static void SaveIfMissing(string fan, byte[] data, IPluginLogger? logger)
    {
        if (data.Length == 0) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FanControl");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ROG-NUC15JNK-" + fan + "-bios-curve.bin");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, data);
                logger?.Log($"ROG NUC {fan} BIOS curve backup created: {path}");
            }
        }
        catch (Exception ex)
        {
            logger?.Log($"ROG NUC {fan} BIOS curve backup failed: {ex.Message}");
        }
    }
}
