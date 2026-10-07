using System.Buffers.Binary;

namespace BleHidProbe;

internal static class SelfTests
{
    private sealed class RecordingTransport : IReportTransport
    {
        public List<(byte Id, byte[] Payload)> Sent { get; } = [];
        public bool FailFirst { get; init; }
        public Task SendAsync(byte id, byte[] payload, CancellationToken token)
        {
            Sent.Add((id, payload));
            if (FailFirst && Sent.Count == 1) { throw new IOException("simulated press failure"); }
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    public static async Task<int> RunAsync()
    {
        int passed = 0;
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Report Map lengths/IDs", () => Check(() =>
            {
                Dictionary<int, int> bits = ParseReportBits(HidSchema.ReportMap);
                Require(bits.Count == 2 && bits[1] == 64 && bits[2] == 48);
            })),
            ("Keyboard payload excludes ID", () => Check(() => Require(HidSchema.Keyboard(4).SequenceEqual(new byte[] { 0,0,4,0,0,0,0,0 })))),
            ("Modifier usage maps to modifier byte", () => Check(() => Require(HidSchema.Keyboard(0xE1).SequenceEqual(new byte[] { 2,0,0,0,0,0,0,0 })))),
            ("Signed mouse/wheel layout", () => Check(() =>
            {
                byte[] report = HidSchema.Mouse(-32767, 32767, -127, 1);
                Require(report.Length == 6 && report[0] == 1 && BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(1)) == -32767 &&
                    BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(3)) == 32767 && unchecked((sbyte)report[5]) == -127);
            })),
            ("Range rejection, no silent truncation", () => Check(() => Expect<ArgumentOutOfRangeException>(() => HidSchema.Mouse(32768, 0)))),
            ("ASCII shift mapping", () => Check(() => Require(HidSchema.MapAscii('A')[0] == 2 && HidSchema.MapAscii('A')[2] == 4 && HidSchema.MapAscii('0')[2] == 0x27))),
            ("Invalid Unicode sends nothing", async () =>
            {
                var fake = new RecordingTransport();
                try { await new ManualInput(fake).TypeAsync("abç", CancellationToken.None); throw new Exception("Expected rejection"); }
                catch (ArgumentException) { Require(fake.Sent.Count == 0); }
            }),
            ("Click has neutral release", async () =>
            {
                var fake = new RecordingTransport();
                await new ManualInput(fake).PulseAsync(2, HidSchema.Mouse(0, 0, buttons: 1), CancellationToken.None);
                Require(fake.Sent.Count == 2 && fake.Sent[0].Payload[0] == 1 && fake.Sent[1].Payload.All(value => value == 0));
            }),
            ("Failed press still attempts release", async () =>
            {
                var fake = new RecordingTransport { FailFirst = true };
                try { await new ManualInput(fake).PulseAsync(1, HidSchema.Keyboard(4), CancellationToken.None); throw new Exception("Expected failure"); }
                catch (IOException) { Require(fake.Sent.Count == 2 && fake.Sent[1].Payload.All(value => value == 0)); }
            }),
            ("Cancellation cannot suppress release", async () =>
            {
                var fake = new RecordingTransport();
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                try { await new ManualInput(fake).PulseAsync(1, HidSchema.Keyboard(4), canceled.Token); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException) { Require(fake.Sent.Count == 2 && fake.Sent[1].Payload.All(value => value == 0)); }
            }),
            ("Type releases each key", async () =>
            {
                var fake = new RecordingTransport();
                await new ManualInput(fake).TypeAsync("aA 1", CancellationToken.None);
                Require(fake.Sent.Count == 8 && fake.Sent.Where((_, i) => i % 2 == 1).All(report => report.Payload.All(value => value == 0)));
            })
        };
        foreach ((string name, Func<Task> run) in tests)
        {
            try { await run(); passed++; Console.WriteLine($"[PASS] {name}"); }
            catch (Exception error) { Console.WriteLine($"[FAIL] {name}: {error.GetType().Name}"); }
        }
        Console.WriteLine($"SELF TESTS: {passed}/{tests.Length}; no Bluetooth accessed");
        return passed == tests.Length ? 0 : 1;
    }

    private static Dictionary<int, int> ParseReportBits(byte[] descriptor)
    {
        var bits = new Dictionary<int, int>();
        int size = 0, count = 0, id = 0;
        for (int index = 0; index < descriptor.Length;)
        {
            byte prefix = descriptor[index++];
            if (prefix == 0xFE) { throw new Exception("Unexpected long HID item"); }
            int length = (prefix & 3) == 3 ? 4 : prefix & 3;
            int value = 0;
            for (int part = 0; part < length; part++) { value |= descriptor[index++] << (8 * part); }
            switch (prefix & 0xFC)
            {
                case 0x74: size = value; break;
                case 0x94: count = value; break;
                case 0x84: id = value; break;
                case 0x80: bits[id] = bits.GetValueOrDefault(id) + size * count; break;
            }
        }
        return bits;
    }

    private static Task Check(Action test) { test(); return Task.CompletedTask; }
    private static void Require(bool condition) { if (!condition) { throw new Exception("Assertion failed"); } }
    private static void Expect<T>(Action operation) where T : Exception
    {
        try { operation(); } catch (T) { return; }
        throw new Exception("Expected exception");
    }
}
