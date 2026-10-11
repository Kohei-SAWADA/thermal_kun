using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ThermalKun.App;

internal static class SensorPipeChecks
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: Windows pipe regression checks require Windows."); return; }
        var frame = new CpuTemperatureFrame(DateTimeOffset.UtcNow, 48, "/intelcpu/0/temperature/6", "CPU Package", "pipe regression test only");
        var name = "thermal-kun-pipe-check-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.Out);
        var connect = server.WaitForConnectionAsync();
        client.Connect(5000);
        connect.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
        var read = reader.ReadLineAsync();
        if (!SensorPipeWriter.TryWrite(client, frame)) throw new Exception("Connected pipe rejected the frame.");
        var line = read.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        if (JsonSerializer.Deserialize<CpuTemperatureFrame>(line!) != frame) throw new Exception("Pipe frame or newline framing changed.");
        server.Dispose();
        if (SensorPipeWriter.TryWrite(client, frame)) throw new Exception("A closed GUI pipe was reported as writable.");
        client.Dispose(); // Must not flush a buffered writer into the disconnected pipe.
        try
        {
            SensorPipeWriter.TryWrite(new FailingStream(), frame);
            throw new Exception("An unrelated I/O error was hidden.");
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x8007001D)) { }
        Console.WriteLine("PASS: Windows pipe regression checks (live frame, newline framing, GUI disconnect, disposal, unrelated I/O failure).");
    }

    private sealed class FailingStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Unrelated write failure", unchecked((int)0x8007001D));
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Unrelated write failure", unchecked((int)0x8007001D));
    }
}
