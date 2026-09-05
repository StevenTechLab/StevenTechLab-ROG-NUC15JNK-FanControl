using FanControl.Plugins;

namespace FanControl.ROGNUC15JNK;

public sealed class RogNucTemperatureSensor : IPluginSensor
{
    public RogNucTemperatureSensor(string id, string name) { Id = id; Name = name; }
    public string Id { get; }
    public string Name { get; }
    public float? Value { get; internal set; }
    public void Update() { }
}
