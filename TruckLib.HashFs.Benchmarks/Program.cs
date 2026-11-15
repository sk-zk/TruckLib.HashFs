using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TruckLib;
using TruckLib.HashFs;
using TruckLib.Sii;

try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("Usage: --archives ARCHIVE_LIST --output JSON [--baseline OLD_DLL] [--mode read|sii] [--directory /def/world/] [--entries ENTRY_LIST] [--iterations 5]");
        return 0;
    }
    string[] allowed = ["--archives", "--output", "--baseline", "--mode", "--directory", "--entries", "--iterations"];
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
        if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Unknown, repeated or incomplete option.");
    string mode = options.GetValueOrDefault("--mode", "sii");
    if (mode is not ("read" or "sii")) throw new ArgumentException("Choose --mode read or sii.");
    int iterations = int.Parse(options.GetValueOrDefault("--iterations", "5"), CultureInfo.InvariantCulture);
    if (iterations is < 1 or > 100) throw new ArgumentOutOfRangeException("--iterations");
    string archiveList = Path.GetFullPath(options["--archives"]);
    string[] archives = Lines(archiveList).Select(p => Path.GetFullPath(p, Path.GetDirectoryName(archiveList)!)).ToArray();
    using var current = new Mounted(typeof(HashFsReader).Assembly, archives);
    using var baseline = options.TryGetValue("--baseline", out var oldPath)
        ? new Mounted(new BaselineContext().LoadFromAssemblyPath(Path.GetFullPath(oldPath)), archives) : null;
    string directory = options.GetValueOrDefault("--directory", "/def/world/");
    string[] entries = options.TryGetValue("--entries", out var entryList) ? Lines(entryList).Distinct(StringComparer.Ordinal).ToArray()
        : current.Files.GetFiles(directory).Where(p => p.EndsWith(".sii", StringComparison.Ordinal)
            && (mode == "read" || p.Contains("road_look", StringComparison.Ordinal)
                || p.Contains("model", StringComparison.Ordinal) && !p.Contains("curve", StringComparison.Ordinal)
                || p.StartsWith("/def/world/prefab.", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    if (entries.Length == 0) throw new InvalidDataException("No entries selected.");
    if (mode == "sii" && entries.Any(p => !p.EndsWith(".sii", StringComparison.Ordinal)))
        throw new ArgumentException("SII mode requires SII root documents.");

    var coverage = Run(current.Files, entries, mode);
    var reference = baseline is null ? coverage : Run(baseline.Files, entries, mode);
    Check(reference, coverage);
    var samples = new List<object>();
    for (int round = 1; round <= iterations; round++)
    {
        foreach (bool old in round % 2 == 1 ? new[] { true, false } : new[] { false, true })
        {
            if (old && baseline is null) continue;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var result = Run(old ? baseline!.Files : current.Files, entries, mode);
            Check(reference, result);
            samples.Add(new { Round = round, Version = old ? "baseline" : "current", result.Milliseconds,
                result.ReadCalls, result.ResourceFiles, result.ResourceBytes, result.Units, result.Digest });
            Console.WriteLine($"Round {round} {(old ? "baseline" : "current")}: {result.Milliseconds:F3} ms");
        }
    }
    var report = new { Mode = mode, Entries = entries, Archives = archives.Select(Path.GetFileName).ToArray(),
        Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(), Environment.ProcessorCount,
        CurrentAssembly = Identity(typeof(HashFsReader).Assembly), BaselineAssembly = baseline is null ? null : Identity(baseline.Assembly),
        ParserAssembly = mode == "sii" ? Identity(typeof(SiiFile).Assembly) : null, Samples = samples };
    string output = Path.GetFullPath(options["--output"]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

static string[] Lines(string path) => File.ReadAllLines(path).Select(p => p.Trim())
    .Where(p => p.Length > 0 && !p.StartsWith('#')).ToArray();

static object Identity(Assembly assembly) => new { Name = assembly.GetName().Name,
    Version = assembly.GetName().Version?.ToString(), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };

static Batch Run(IFileSystem files, string[] entries, string mode)
{
    var capture = new CapturingFiles(files);
    int units = 0;
    var watch = Stopwatch.StartNew();
    foreach (string path in entries)
        if (mode == "sii") units += SiiFile.Open(path, capture).Units.Count;
        else _ = capture.ReadAllBytes(path);
    watch.Stop();
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var pair in capture.Buffers.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        byte[] name = Encoding.UTF8.GetBytes(pair.Key);
        digest.AppendData(BitConverter.GetBytes(name.Length)); digest.AppendData(name);
        digest.AppendData(BitConverter.GetBytes(pair.Value.Length)); digest.AppendData(pair.Value);
    }
    return new(watch.Elapsed.TotalMilliseconds, capture.ReadCalls, capture.Buffers.Count,
        capture.Buffers.Values.Sum(b => (long)b.Length), units, Convert.ToHexString(digest.GetHashAndReset()));
}

static void Check(Batch expected, Batch actual)
{
    if (expected.Digest != actual.Digest || expected.Units != actual.Units || expected.ReadCalls != actual.ReadCalls)
        throw new InvalidDataException("Resource contents, read counts or parsed unit counts differ.");
}

sealed record Batch(double Milliseconds, int ReadCalls, int ResourceFiles, long ResourceBytes, int Units, string Digest);

sealed class Mounted : IDisposable
{
    private readonly List<IDisposable> readers = [];
    public Assembly Assembly { get; }
    public IFileSystem Files { get; }
    public Mounted(Assembly assembly, string[] archives)
    {
        Assembly = assembly;
        try
        {
            var open = assembly.GetType("TruckLib.HashFs.HashFsReader")!.GetMethod("Open", [typeof(string)])!;
            var files = archives.Select(path =>
            {
                var reader = (IFileSystem)open.Invoke(null, [path])!;
                readers.Add((IDisposable)reader); return reader;
            }).ToArray();
            Files = (IFileSystem)Activator.CreateInstance(assembly.GetType("TruckLib.HashFs.AssetLoader")!, new object[] { files })!;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { foreach (var reader in readers) reader.Dispose(); }
}

sealed class BaselineContext() : AssemblyLoadContext(isCollectible: false)
{
    protected override Assembly? Load(AssemblyName name) => Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
}

sealed class CapturingFiles(IFileSystem files) : IFileSystem
{
    public Dictionary<string, byte[]> Buffers { get; } = new(StringComparer.Ordinal);
    public int ReadCalls { get; private set; }
    public char DirectorySeparator => files.DirectorySeparator;
    public bool FileExists(string path) => files.FileExists(path);
    public bool DirectoryExists(string path) => files.DirectoryExists(path);
    public IList<string> GetFiles(string path) => files.GetFiles(path);
    public string GetParent(string path) => files.GetParent(path);
    public byte[] ReadAllBytes(string path)
    {
        byte[] bytes = files.ReadAllBytes(path); Buffers[path] = bytes; ReadCalls++; return bytes;
    }
    public Stream Open(string path) => new MemoryStream(ReadAllBytes(path), false);
    public string ReadAllText(string path) => ReadAllText(path, Encoding.UTF8);
    public string ReadAllText(string path, Encoding encoding)
    { using var reader = new StreamReader(Open(path), encoding, true); return reader.ReadToEnd(); }
}
