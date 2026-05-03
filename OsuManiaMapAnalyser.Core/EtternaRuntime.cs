using System.Collections.Concurrent;
using System.Text;
using Wasmtime;

namespace OsuManiaMapAnalyser.Core;

internal static class EtternaRuntime
{
    private static readonly ConcurrentDictionary<string, MinaCalcModule> Modules = new(StringComparer.Ordinal);

    private static readonly string[] OutputOrder =
    [
        "Overall",
        "Stream",
        "Jumpstream",
        "Handstream",
        "Stamina",
        "JackSpeed",
        "Chordjack",
        "Technical",
    ];

    public static IReadOnlyDictionary<string, double> Analyze(BeatmapChart chart, double musicRate, string version)
    {
        var rows = BuildRows(chart);
        if (rows.Masks.Length <= 1)
        {
            return OutputOrder.ToDictionary(static name => name, static _ => 0.0, StringComparer.Ordinal);
        }

        var module = Modules.GetOrAdd(version, static value => new MinaCalcModule(value));
        var result = module.Compute(chart.ColumnCount, (float)musicRate, 0.93f, rows.Masks, rows.Times);

        var mapped = new Dictionary<string, double>(OutputOrder.Length, StringComparer.Ordinal);
        for (var i = 0; i < OutputOrder.Length; i += 1)
        {
            mapped[OutputOrder[i]] = result[i];
        }

        return mapped;
    }

    private static (uint[] Masks, float[] Times) BuildRows(BeatmapChart chart)
    {
        var byTime = new SortedDictionary<int, uint>();
        foreach (var hitObject in chart.HitObjects)
        {
            if (hitObject.Column < 0 || hitObject.Column > 31)
            {
                continue;
            }

            var existing = byTime.TryGetValue(hitObject.StartTime, out var mask) ? mask : 0U;
            byTime[hitObject.StartTime] = existing | (1U << hitObject.Column);
        }

        var masks = new uint[byTime.Count];
        var times = new float[byTime.Count];
        var index = 0;
        foreach (var pair in byTime)
        {
            masks[index] = pair.Value;
            times[index] = pair.Key / 1000.0f;
            index += 1;
        }

        return (masks, times);
    }

    private sealed class MinaCalcModule
    {
        private readonly Engine _engine;
        private readonly Module _module;
        private readonly Linker _linker;

        public MinaCalcModule(string version)
        {
            var wasmPath = Path.Combine(AppContext.BaseDirectory, "Assets", GetWasmFileName(version));
            _engine = new Engine();
            _module = Module.FromFile(_engine, wasmPath);
            _linker = new Linker(_engine);
            DefineHostFunctions(_linker);
        }

        public float[] Compute(int keyCount, float musicRate, float scoreGoal, uint[] rowMasks, float[] rowTimes)
        {
            using var store = new Store(_engine);
            var instance = _linker.Instantiate(store, _module);
            var init = instance.GetAction("n")
                ?? throw new InvalidOperationException("Wasm init export 'n' is unavailable.");
            var malloc = instance.GetFunction<int, int>("q")
                ?? throw new InvalidOperationException("Wasm malloc export 'q' is unavailable.");
            var free = instance.GetAction<int>("r")
                ?? throw new InvalidOperationException("Wasm free export 'r' is unavailable.");
            var compute = instance.GetFunction<int, float, float, int, int, int, int, int>("o")
                ?? throw new InvalidOperationException("Wasm compute export 'o' is unavailable.");
            var memory = instance.GetMemory("m")
                ?? throw new InvalidOperationException("Wasm memory export 'm' is unavailable.");

            init();

            var ptrMasks = malloc(rowMasks.Length * sizeof(uint));
            var ptrTimes = malloc(rowTimes.Length * sizeof(float));
            var ptrOut = malloc(OutputOrder.Length * sizeof(float));

            try
            {
                for (var i = 0; i < rowMasks.Length; i += 1)
                {
                    memory.WriteInt32(ptrMasks + (i * sizeof(uint)), unchecked((int)rowMasks[i]));
                }

                for (var i = 0; i < rowTimes.Length; i += 1)
                {
                    memory.WriteSingle(ptrTimes + (i * sizeof(float)), rowTimes[i]);
                }

                var ok = compute(keyCount, musicRate, scoreGoal, ptrMasks, ptrTimes, rowMasks.Length, ptrOut);
                if (ok == 0)
                {
                    throw new InvalidOperationException("MinaCalc compute returned failure.");
                }

                var output = new float[OutputOrder.Length];
                for (var i = 0; i < output.Length; i += 1)
                {
                    output[i] = memory.ReadSingle(ptrOut + (i * sizeof(float)));
                }

                return output;
            }
            finally
            {
                free(ptrMasks);
                free(ptrTimes);
                free(ptrOut);
            }
        }

        private static string GetWasmFileName(string version)
        {
            return version switch
            {
                "0.72.3" => "minaclac-72.3.wasm",
                "0.74.0" => "minaclac-74.0.wasm",
                _ => throw new InvalidOperationException($"Unsupported Etterna version: {version}"),
            };
        }

        private static void DefineHostFunctions(Linker linker)
        {
            static Memory GetMemory(Caller caller)
            {
                return caller.GetMemory("m")
                    ?? throw new InvalidOperationException("Wasm memory export 'm' is unavailable.");
            }

            linker.DefineFunction("a", "a", (Caller caller, int fd, int iov, int iovcnt, int pnum) =>
            {
                var memory = GetMemory(caller);
                var written = 0;

                for (var i = 0; i < iovcnt; i += 1)
                {
                    var ptr = memory.ReadInt32(iov + (i * 8));
                    var len = memory.ReadInt32(iov + (i * 8) + 4);
                    written += len;

                    if (fd == 1 || fd == 2)
                    {
                        var text = memory.ReadString(ptr, len, Encoding.UTF8);
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (fd == 2)
                            {
                                Console.Error.Write(text);
                            }
                            else
                            {
                                Console.Write(text);
                            }
                        }
                    }
                }

                memory.WriteInt32(pnum, written);
                return 0;
            });
            linker.DefineFunction("a", "b", () =>
            {
                throw new InvalidOperationException("MinaCalc called abort().");
            });
            linker.DefineFunction("a", "c", (int ptr, int type, int destructor) =>
            {
                throw new InvalidOperationException($"MinaCalc threw an exception ptr={ptr}, type={type}, destructor={destructor}.");
            });
            linker.DefineFunction("a", "d", (Caller caller, int size) =>
            {
                var malloc = caller.GetFunction("q")?.WrapFunc<int, int>()
                    ?? throw new InvalidOperationException("Wasm malloc export 'q' is unavailable.");
                return malloc(size + 16) + 16;
            });
            linker.DefineFunction("a", "e", (int environ, int environBuffer) => 0);
            linker.DefineFunction("a", "f", (Caller caller, int environCount, int environBufferSize) =>
            {
                var memory = GetMemory(caller);
                memory.WriteInt32(environCount, 0);
                memory.WriteInt32(environBufferSize, 0);
                return 0;
            });
            linker.DefineFunction("a", "g", (int fd) => 0);
            linker.DefineFunction("a", "h", (Caller caller, int fd, int iov, int iovcnt, int pnum) =>
            {
                var memory = GetMemory(caller);
                memory.WriteInt32(pnum, 0);
                return 0;
            });
            linker.DefineFunction("a", "i", (Caller caller, int requestedSize) =>
            {
                var memory = GetMemory(caller);
                var currentSize = memory.GetSize();
                if (requestedSize <= currentSize)
                {
                    return 1;
                }

                var deltaBytes = requestedSize - currentSize;
                var pages = (deltaBytes + 65535L) / 65536L;
                memory.Grow(pages);
                return 1;
            });
            linker.DefineFunction("a", "j", (Caller caller, int dest, int src, int num) =>
            {
                var memory = GetMemory(caller);
                var temp = memory.GetSpan(src, num).ToArray();
                temp.CopyTo(memory.GetSpan(dest, num));
                return dest;
            });
            linker.DefineFunction("a", "k", (Caller caller, int fd, int offsetLow, int offsetHigh, int whence, int newOffset) =>
            {
                var memory = GetMemory(caller);
                memory.WriteInt32(newOffset, 0);
                memory.WriteInt32(newOffset + 4, 0);
                return 0;
            });
            linker.DefineFunction("a", "l", (int s, int maxSize, int format, int tm, int locale) => 0);
        }
    }
}
