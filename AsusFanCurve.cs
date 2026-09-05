using System;

namespace FanControl.ROGNUC15JNK;

internal sealed class AsusFanCurve
{
    internal readonly byte[] Temperatures;
    internal readonly byte[] Percentages;

    private AsusFanCurve(byte[] temperatures, byte[] percentages)
    {
        Temperatures = temperatures;
        Percentages = percentages;
    }

    internal static AsusFanCurve? TryParse(byte[] buffer)
    {
        if (buffer.Length < 16) return null;
        var temperatures = new byte[8];
        var percentages = new byte[8];
        Array.Copy(buffer, 0, temperatures, 0, 8);
        Array.Copy(buffer, 8, percentages, 0, 8);
        for (var i = 0; i < 8; i++)
            if (temperatures[i] > 125 || percentages[i] > 100) return null;
        return new AsusFanCurve(temperatures, percentages);
    }
}
