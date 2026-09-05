using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FanControl.Plugins;
using Microsoft.Win32;

namespace FanControl.ROGNUC15JNK;

public sealed class ROGNUC15JNKPlugin : IPlugin2
{
    private readonly IPluginLogger? _logger;
    private readonly List<RogNucFanSensor> _fanSensors = new();
    private readonly List<RogNucTemperatureSensor> _temperatureSensors = new();
    private RogNucLinkedControlSensor? _linkedControl;
    private AsusAcpiReadOnlyFanSource _source = new();

    public ROGNUC15JNKPlugin(IPluginLogger logger) => _logger = logger;

    // Keep this stable: Fan Control persists sensor references together with the plugin name.
    // This name is part of Fan Control's persisted sensor identity. Keep it stable so
    // existing controls, curves, and sensor links continue to load after an update.
    public string Name => "ROG NUC 2025 (read-only research)";

    public void Initialize()
    {
        // Initialization itself performs reads only. Hardware writes occur solely in Set/Reset.
        _source.Dispose();
        _source = new AsusAcpiReadOnlyFanSource(_logger);
        _linkedControl = null;
        _fanSensors.Clear();
        _temperatureSensors.Clear();
        foreach (var reading in _source.Discover())
            _fanSensors.Add(new RogNucFanSensor(reading.Id, reading.Name));
        _temperatureSensors.Add(new RogNucTemperatureSensor("asus:cpu-temperature", "CPU-Temperatur"));
        _temperatureSensors.Add(new RogNucTemperatureSensor("asus:gpu-temperature", "NVIDIA GPU temperature"));

        var modeSnapshot = AsusModeSnapshot.TryCapture(_logger);
        var selector = modeSnapshot?.CurveSelector ?? 0;

        // Capture the BIOS curves matching the selected base mode for evidence and recovery.
        foreach (var fan in new[] { "cpu", "gpu", "mid" })
        {
            var curve = _source.ReadCurve(fan, selector);
            _logger?.Log($"ROG NUC {fan} BIOS curve read ({curve.Length} bytes): " +
                string.Join(" ", curve.Select(b => b.ToString("X2"))));
            CurveBackup.SaveIfMissing(fan, curve, _logger);
            var parsed = AsusFanCurve.TryParse(curve);
            if (parsed != null)
                _logger?.Log($"ROG NUC {fan} curve decoded: T=" + string.Join(",", parsed.Temperatures) +
                    " P=" + string.Join(",", parsed.Percentages));
        }

        var marker = Path.Combine(AppContext.BaseDirectory, "ROG-NUC15JNK-ENABLE-CONTROLS.TEST");
        if (File.Exists(marker) && modeSnapshot != null && IsTargetModel())
        {
            _linkedControl = new RogNucLinkedControlSensor(_source, modeSnapshot, _logger);
            _logger?.Log("ROG NUC verified linked CPU/GPU control enabled (0-100%, firmware fail-safe curve)");
        }
        else if (File.Exists(marker))
        {
            _logger?.Log("ROG NUC control marker found, but the model/mode recovery gate did not pass");
        }

        _logger?.Log($"ROG NUC plugin found {_fanSensors.Count} qualified read-only fan source(s).");
    }

    public void Load(IPluginSensorsContainer container)
    {
        foreach (var sensor in _fanSensors)
            container.FanSensors.Add(sensor);
        foreach (var sensor in _temperatureSensors)
            container.TempSensors.Add(sensor);
        if (_linkedControl != null) container.ControlSensors.Add(_linkedControl);
    }

    public void Update()
    {
        try
        {
            var readings = _source.Read();
            foreach (var sensor in _fanSensors)
                sensor.Value = readings.TryGetValue(sensor.Id, out var rpm) ? rpm : null;
            foreach (var sensor in _temperatureSensors)
                sensor.Value = readings.TryGetValue(sensor.Id, out var temperature) ? temperature : null;
        }
        catch (Exception ex)
        {
            foreach (var sensor in _fanSensors) sensor.Value = null;
            foreach (var sensor in _temperatureSensors) sensor.Value = null;
            _logger?.Log($"ROG NUC ACPI update failed: {ex.Message}");
        }
    }

    public void Close()
    {
        _linkedControl?.Reset();
        foreach (var sensor in _fanSensors) sensor.Value = null;
        _fanSensors.Clear();
        _temperatureSensors.Clear();
        _source.Dispose();
    }

    private static bool IsTargetModel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var product = key?.GetValue("SystemProductName")?.ToString() ?? string.Empty;
            return product.IndexOf("NUC15JNK", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }
}
