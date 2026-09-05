using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FanControl.Plugins;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;

namespace FanControl.ROGNUC15JNK;

internal sealed class AsusAcpiReadOnlyFanSource : IDisposable
{
    private const string DevicePath = @"\\.\ATKACPI";
    private const uint IoctlControl = 0x0022240C;
    private const uint Dsts = 0x53545344;
    private const uint Devs = 0x53564544;
    private const uint CpuFan = 0x00110013;
    private const uint GpuFan = 0x00110014;
    private const uint MidFan = 0x00110031;
    private const uint CpuTemperature = 0x00120094;
    private const uint GpuTemperature = 0x00120097;
    private const uint CpuFanCurve = 0x00110024;
    private const uint GpuFanCurve = 0x00110025;
    private const uint MidFanCurve = 0x00110032;
    private const uint PerformanceMode = 0x00120075;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 1;
    private const uint ShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private static readonly IntPtr InvalidHandle = new(-1);

    private readonly object _ioLock = new();
    private readonly Dictionary<string, uint> _endpoints = new()
    {
        ["asus:cpu"] = CpuFan,
        ["asus:gpu"] = GpuFan,
        ["asus:mid"] = MidFan
    };
    private readonly Dictionary<string, uint> _temperatureEndpoints = new()
    {
        ["asus:cpu-temperature"] = CpuTemperature,
        ["asus:gpu-temperature"] = GpuTemperature
    };
    private readonly IPluginLogger? _logger;
    private IntPtr _readHandle = InvalidHandle;
    private DateTime _lastTemperatureLogUtc = DateTime.MinValue;
    private readonly object _nvidiaLock = new();
    private PhysicalGPU? _nvidiaGpu;
    private bool _nvidiaGpuDiscoveryAttempted;
    private Task<float?>? _nvidiaTemperatureTask;
    private float? _lastNvidiaTemperature;
    private DateTime _lastNvidiaTemperatureUtc = DateTime.MinValue;
    private string? _nvidiaGpuName;
    private bool _nvidiaSourceLogged;

    internal AsusAcpiReadOnlyFanSource(IPluginLogger? logger = null) => _logger = logger;

    internal IEnumerable<FanReading> Discover() => _endpoints.Select(x => new FanReading(x.Key,
        x.Key switch
        {
            "asus:cpu" => "CPU-Lüfter",
            "asus:gpu" => "GPU-Lüfter",
            _ => "Mittlerer Lüfter"
        }, 0));

    internal Dictionary<string, float> Read()
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        var rawTemperatures = new Dictionary<string, int>(StringComparer.Ordinal);

        lock (_ioLock)
        {
            if (!OpenRead()) return result;
            foreach (var endpoint in _endpoints)
            {
                var raw = ReadDevice(endpoint.Value);
                var units = raw & 0xFFFF;
                if (raw >= 0 && units <= 120) result[endpoint.Key] = units * 100;
            }

            foreach (var endpoint in _temperatureEndpoints)
            {
                var raw = ReadDevice(endpoint.Value);
                rawTemperatures[endpoint.Key] = raw;
                var temperature = NormalizeTemperature(raw);
                if (temperature.HasValue) result[endpoint.Key] = temperature.Value;
            }
        }

        // NVIDIA calls are intentionally outside the ATKACPI lock. A slow or wedged
        // display driver must never block a fan-control Set/Reset operation.
        if (!result.ContainsKey("asus:gpu-temperature"))
        {
            var nvidiaTemperature = TryReadNvidiaGpuTemperatureNonBlocking();
            if (nvidiaTemperature.HasValue)
                result["asus:gpu-temperature"] = nvidiaTemperature.Value;
        }

        if (DateTime.UtcNow - _lastTemperatureLogUtc >= TimeSpan.FromSeconds(10))
        {
            foreach (var endpoint in _temperatureEndpoints)
            {
                rawTemperatures.TryGetValue(endpoint.Key, out var raw);
                var normalized = result.TryGetValue(endpoint.Key, out var temperature)
                    ? temperature.ToString("0.0")
                    : "unsupported";
                _logger?.Log($"ROG NUC {endpoint.Key} temperature raw={raw} normalized={normalized}");
            }
            _lastTemperatureLogUtc = DateTime.UtcNow;
        }

        return result;
    }

    internal byte[] ReadCurve(string fan, int selector = 0)
    {
        lock (_ioLock)
        {
            if (!OpenRead()) return Array.Empty<byte>();
            var endpoint = fan switch
            {
                "cpu" => CpuFanCurve,
                "gpu" => GpuFanCurve,
                "mid" => MidFanCurve,
                _ => 0u
            };
            return endpoint == 0 ? Array.Empty<byte>() : ReadDeviceBuffer(endpoint, selector);
        }
    }

    internal AcpiWriteResult SetPerformanceMode(int mode)
    {
        if (mode < 0 || mode > 2) return AcpiWriteResult.Invalid("mode outside 0..2");
        lock (_ioLock)
        {
            var handle = OpenWrite();
            if (handle == InvalidHandle) return AcpiWriteResult.OpenFailed(Marshal.GetLastWin32Error());
            try { return WriteInt(handle, PerformanceMode, mode); }
            finally { CloseHandle(handle); }
        }
    }

    internal LinkedFanWriteResult WriteLinkedCurve(byte[] cpuCurve, byte[] gpuCurve)
    {
        if (!ValidCurve(cpuCurve) || !ValidCurve(gpuCurve))
            return LinkedFanWriteResult.Invalid("curve must contain 16 plausible bytes");

        lock (_ioLock)
        {
            var handle = OpenWrite();
            if (handle == InvalidHandle)
                return LinkedFanWriteResult.OpenFailed(Marshal.GetLastWin32Error());
            try
            {
                var cpu = WriteBuffer(handle, CpuFanCurve, cpuCurve);
                var gpu = WriteBuffer(handle, GpuFanCurve, gpuCurve);
                return new LinkedFanWriteResult(cpu, gpu);
            }
            finally { CloseHandle(handle); }
        }
    }

    internal bool WriteIdenticalCpuCurve(byte[] curve)
    {
        lock (_ioLock)
        {
            var handle = OpenWrite();
            if (handle == InvalidHandle) return false;
            try { return WriteBuffer(handle, CpuFanCurve, curve).Succeeded; }
            finally { CloseHandle(handle); }
        }
    }

    internal bool WriteIdenticalGpuCurve(byte[] curve)
    {
        lock (_ioLock)
        {
            var handle = OpenWrite();
            if (handle == InvalidHandle) return false;
            try { return WriteBuffer(handle, GpuFanCurve, curve).Succeeded; }
            finally { CloseHandle(handle); }
        }
    }

    private AcpiWriteResult WriteInt(IntPtr handle, uint endpoint, int value)
    {
        var args = new byte[8];
        BitConverter.GetBytes(endpoint).CopyTo(args, 0);
        BitConverter.GetBytes(value).CopyTo(args, 4);
        return CallWrite(handle, args);
    }

    private AcpiWriteResult WriteBuffer(IntPtr handle, uint endpoint, byte[] payload)
    {
        var args = new byte[4 + payload.Length];
        BitConverter.GetBytes(endpoint).CopyTo(args, 0);
        payload.CopyTo(args, 4);
        return CallWrite(handle, args);
    }

    private AcpiWriteResult CallWrite(IntPtr handle, byte[] args)
    {
        var packet = new byte[8 + args.Length];
        BitConverter.GetBytes(Devs).CopyTo(packet, 0);
        BitConverter.GetBytes((uint)args.Length).CopyTo(packet, 4);
        args.CopyTo(packet, 8);
        var output = new byte[16];
        var transport = DeviceIoControl(handle, IoctlControl, packet, (uint)packet.Length,
            output, (uint)output.Length, out var returned, IntPtr.Zero);
        if (!transport) return AcpiWriteResult.TransportFailed(Marshal.GetLastWin32Error(), returned);
        var firmware = returned >= 4 ? BitConverter.ToInt32(output, 0) : int.MinValue;
        return new AcpiWriteResult(true, firmware, 0, returned, null);
    }

    private bool OpenRead()
    {
        if (_readHandle != InvalidHandle) return true;
        _readHandle = CreateFile(DevicePath, GenericRead, ShareRead | ShareWrite,
            IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        return _readHandle != InvalidHandle;
    }

    private static IntPtr OpenWrite() => CreateFile(DevicePath, GenericRead | GenericWrite,
        ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);

    private int ReadDevice(uint endpoint)
    {
        var packet = ReadPacket(endpoint, 0);
        var output = new byte[16];
        return DeviceIoControl(_readHandle, IoctlControl, packet, (uint)packet.Length,
            output, (uint)output.Length, out _, IntPtr.Zero)
            ? BitConverter.ToInt32(output, 0) - 65536
            : -1;
    }

    private byte[] ReadDeviceBuffer(uint endpoint, int selector)
    {
        var packet = ReadPacket(endpoint, selector);
        var output = new byte[16];
        return DeviceIoControl(_readHandle, IoctlControl, packet, (uint)packet.Length,
            output, (uint)output.Length, out _, IntPtr.Zero) ? output : Array.Empty<byte>();
    }

    private static byte[] ReadPacket(uint endpoint, int selector)
    {
        var args = new byte[8];
        BitConverter.GetBytes(endpoint).CopyTo(args, 0);
        BitConverter.GetBytes(selector).CopyTo(args, 4);
        var packet = new byte[16];
        BitConverter.GetBytes(Dsts).CopyTo(packet, 0);
        BitConverter.GetBytes(8u).CopyTo(packet, 4);
        args.CopyTo(packet, 8);
        return packet;
    }

    private static bool ValidCurve(byte[] curve) => curve.Length == 16 &&
        curve.Take(8).All(x => x <= 125) && curve.Skip(8).All(x => x <= 100) &&
        curve.Any(x => x != 0);

    private static float? NormalizeTemperature(int raw)
    {
        if (raw < 0) return null;
        if (raw <= 125) return raw;
        if (raw <= 1250) return raw / 10f;
        if (raw <= 12500) return raw / 100f;
        return null;
    }

    private float? TryReadNvidiaGpuTemperatureNonBlocking()
    {
        string? sourceToLog = null;
        float? result;
        lock (_nvidiaLock)
        {
            if (_nvidiaTemperatureTask?.IsCompleted == true)
            {
                try { _lastNvidiaTemperature = _nvidiaTemperatureTask.Result; }
                catch { _lastNvidiaTemperature = null; }
                _lastNvidiaTemperatureUtc = DateTime.UtcNow;
                _nvidiaTemperatureTask = null;

                if (!_nvidiaSourceLogged && !string.IsNullOrWhiteSpace(_nvidiaGpuName))
                {
                    _nvidiaSourceLogged = true;
                    sourceToLog = _nvidiaGpuName;
                }
            }

            if (_nvidiaTemperatureTask == null)
                _nvidiaTemperatureTask = Task.Run(ReadNvidiaGpuTemperatureCore);

            result = DateTime.UtcNow - _lastNvidiaTemperatureUtc <= TimeSpan.FromSeconds(10)
                ? _lastNvidiaTemperature
                : null;
        }

        if (sourceToLog != null)
            _logger?.Log($"ROG NUC NVIDIA temperature source: {sourceToLog}");
        return result;
    }

    private float? ReadNvidiaGpuTemperatureCore()
    {
        try
        {
            if (!_nvidiaGpuDiscoveryAttempted)
            {
                _nvidiaGpu = PhysicalGPU.GetPhysicalGPUs().FirstOrDefault();
                _nvidiaGpuDiscoveryAttempted = true;
                _nvidiaGpuName = _nvidiaGpu?.FullName;
            }

            if (_nvidiaGpu == null) return null;

            // This call fails with NVAPI_GPU_NOT_POWERED while the dGPU sleeps. In that
            // case do not request thermal data and let Fan Control show the sensor as n/a.
            GPUApi.GetCurrentPerformanceState(_nvidiaGpu.Handle);
            var sensor = _nvidiaGpu.ThermalInformation.ThermalSensors
                .FirstOrDefault(item => item.Target == ThermalSettingsTarget.GPU);
            var value = sensor?.CurrentTemperature;
            return value is >= 0 and <= 125 ? value : null;
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        lock (_ioLock)
        {
            if (_readHandle != InvalidHandle) CloseHandle(_readHandle);
            _readHandle = InvalidHandle;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr device, uint code, byte[] input, uint inputSize,
        byte[] output, uint outputSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class AcpiWriteResult
{
    internal AcpiWriteResult(bool transportSucceeded, int firmwareResult, int win32Error,
        uint bytesReturned, string? error)
    {
        TransportSucceeded = transportSucceeded;
        FirmwareResult = firmwareResult;
        Win32Error = win32Error;
        BytesReturned = bytesReturned;
        Error = error;
    }

    internal bool TransportSucceeded { get; }
    internal int FirmwareResult { get; }
    internal int Win32Error { get; }
    internal uint BytesReturned { get; }
    internal string? Error { get; }
    internal bool Succeeded => TransportSucceeded && FirmwareResult == 1;

    internal static AcpiWriteResult Invalid(string error) => new(false, int.MinValue, 0, 0, error);
    internal static AcpiWriteResult OpenFailed(int win32) => new(false, int.MinValue, win32, 0, "open failed");
    internal static AcpiWriteResult TransportFailed(int win32, uint bytes) =>
        new(false, int.MinValue, win32, bytes, "DeviceIoControl failed");

    public override string ToString() => Succeeded
        ? $"transport=OK firmware=1 bytes={BytesReturned}"
        : $"transport={(TransportSucceeded ? "OK" : "failed")} firmware={FirmwareResult} " +
          $"win32={Win32Error} bytes={BytesReturned} error={Error ?? "none"}";
}

internal sealed class LinkedFanWriteResult
{
    internal LinkedFanWriteResult(AcpiWriteResult cpu, AcpiWriteResult gpu)
    {
        Cpu = cpu;
        Gpu = gpu;
    }

    internal AcpiWriteResult Cpu { get; }
    internal AcpiWriteResult Gpu { get; }
    internal bool Succeeded => Cpu.Succeeded && Gpu.Succeeded;

    internal static LinkedFanWriteResult Invalid(string error) =>
        new(AcpiWriteResult.Invalid(error), AcpiWriteResult.Invalid(error));
    internal static LinkedFanWriteResult OpenFailed(int win32) =>
        new(AcpiWriteResult.OpenFailed(win32), AcpiWriteResult.OpenFailed(win32));

    public override string ToString() => $"CPU[{Cpu}] GPU[{Gpu}]";
}
