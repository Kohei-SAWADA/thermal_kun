using System.Text;
using System.Text.Json;

namespace ThermalKun.App;

// Send each frame directly: disposing a buffered writer can flush after the GUI has closed its pipe.
internal static class SensorPipeWriter
{
    internal static bool TryWrite(Stream output, CpuTemperatureFrame frame)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame) + "\n");
        try { output.Write(bytes); return true; }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 109 or 232 or 233)
        {
            // ERROR_BROKEN_PIPE / ERROR_NO_DATA / ERROR_PIPE_NOT_CONNECTED: the GUI stopped reading.
            return false;
        }
    }
}
