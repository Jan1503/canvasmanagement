using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using CanvasManagement;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;
using SkiaSharp;

// ExtensionPreview <ExtensionName> [options]
//   --size 256x128        canvas size
//   --seconds 20          how long to run (real time)
//   --every 500           ms between captured frames
//   --cols 6              frames per row on the contact sheet
//   --zoom 2              sheet zoom (frames are 1:1 pixels, nearest neighbour)
//   --set Name=Value      set an [ExtensionParameter] (property or display name), repeatable
//   --script "1.5:GoLeft,3:GoUp"  call [ExtensionMethod]s at times (s), method or display name
//   --out dir             output folder (default: %TEMP%\ExtensionPreview\<name>)
//   --wav                 record the extension's sound to sound.wav (48 kHz stereo)
//   --shots 3.0,7.5       also save these moments as single big PNGs
var argList = args.ToList();
if (argList.Count == 0)
{
    Console.WriteLine("usage: ExtensionPreview <ExtensionName> [--seconds n] [--every ms] [--script t:Method,...] [--set Name=Value] [--wav]");
    return 1;
}

var root = FindRoot();
if (argList[0] == "music")
{
    // Render every built-in song to WAV for listening.
    var dirOut = argList.Count > 1 ? argList[1] : Path.Combine(Path.GetTempPath(), "ExtensionPreview", "music");
    Directory.CreateDirectory(dirOut);
    foreach (var song in Enum.GetValues<GameSong>())
    {
        var sw = Stopwatch.StartNew();
        var buf = ChipMusic.Get(song);
        var path = Path.Combine(dirOut, $"song-{song.ToString().ToLowerInvariant()}.wav");
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write("RIFF"u8); bw.Write(36 + buf.Length * 4); bw.Write("WAVEfmt "u8); bw.Write(16); bw.Write((short)1);
        bw.Write((short)1); bw.Write(SoundSynth.Rate); bw.Write(SoundSynth.Rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(buf.Length * 2);
        // Two passes so the loop point can be heard.
        for (var pass = 0; pass < 2; pass++)
            foreach (var v in buf) bw.Write((short)Math.Clamp((int)(v * 32767), short.MinValue, short.MaxValue));
        bw.Seek(4, SeekOrigin.Begin); bw.Write(36 + buf.Length * 4); bw.Seek(40, SeekOrigin.Begin); bw.Write(buf.Length * 4);
        Console.WriteLine($"{song,-10} {buf.Length / (float)SoundSynth.Rate:0.0}s rendered in {sw.ElapsedMilliseconds} ms -> {path}");
    }
    return 0;
}
var name = argList[0];
int w = 256, h = 128, cols = 6, zoom = 2;
double seconds = 20, every = 500;
var sets = new List<(string, string)>();
var script = new List<(double T, string Method)>();
var shots = new List<double>();
var wav = false;
string? outDir = null;
for (var i = 1; i < argList.Count; i++)
{
    string Next() => argList[++i];
    switch (argList[i])
    {
        case "--size":
            var p = Next().Split('x');
            w = int.Parse(p[0]);
            h = int.Parse(p[1]);
            break;
        case "--seconds": seconds = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--every": every = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--cols": cols = int.Parse(Next()); break;
        case "--zoom": zoom = int.Parse(Next()); break;
        case "--out": outDir = Next(); break;
        case "--wav": wav = true; break;
        case "--set":
            var kv = Next().Split('=', 2);
            sets.Add((kv[0], kv[1]));
            break;
        case "--shots":
            shots.AddRange(Next().Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)));
            break;
        case "--script":
            foreach (var part in Next().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var tm = part.Split(':', 2);
                script.Add((double.Parse(tm[0], CultureInfo.InvariantCulture), tm[1].Trim()));
            }
            break;
    }
}

outDir ??= Path.Combine(Path.GetTempPath(), "ExtensionPreview", name);
Directory.CreateDirectory(outDir);
foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);

BdfFontRegistry.LoadFontsFromDirectory(Path.Combine(root, "Fonts"), true);

var builds = Directory.GetFiles(Path.Combine(root, "Extensions"), "CanvasManagement*.Extension*.dll", SearchOption.AllDirectories)
    .Where(f => f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
    .OrderByDescending(File.GetLastWriteTimeUtc)
    .ToList();
// By project name (TurboKart), else by extension class name (Matrix, MatrixExtension, GameOfLife).
var dll = builds.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == $"CanvasManagement.Extension.{name}");
string? typeName = null;
if (dll == null)
{
    foreach (var f in builds.Where(f => Path.GetFileName(Path.GetDirectoryName(f)!) is var d && d.StartsWith("net")))
    {
        var stem = Path.GetFileNameWithoutExtension(f);
        var projDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(f)!)!)!)!;
        var src = Directory.GetFiles(projDir, "*.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(c => Path.GetFileNameWithoutExtension(c) is var cn &&
                                 (cn.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                  cn.Equals(name + "Extension", StringComparison.OrdinalIgnoreCase)));
        if (src == null) continue;
        dll = f;
        typeName = Path.GetFileNameWithoutExtension(src);
        break;
    }
}
if (dll == null)
{
    Console.WriteLine($"no extension '{name}' under Extensions/*/bin (project or class name)");
    return 1;
}
Console.WriteLine("loading " + dll);
var dir = Path.GetDirectoryName(dll)!;
// Class-library builds do not copy NuGet dependencies (Svg.Skia, ...) next to the plugin; the smoke-test
// project references every extension, so its output has them all.
var depDirs = new[]
{
    dir,
    Path.Combine(root, "CanvasManagement.Extensions.Tests", "bin", "Release", "net10.0"),
    Path.Combine(root, "CanvasManagement.Extensions.Tests", "bin", "Debug", "net10.0")
};
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var name = new AssemblyName(e.Name).Name + ".dll";
    var file = depDirs.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
    return file != null ? Assembly.LoadFrom(file) : null;
};
var asm = Assembly.LoadFrom(dll);
var type = asm.GetTypes().First(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<ExtensionInfoAttribute>() != null &&
                                     (typeName == null || t.Name == typeName));

using var mgr = new CanvasManager(w, h);
var recorder = new SoundRecorder();
mgr.UseExtensionAudio(recorder.Voices);
var canvas = mgr.GetCanvas(0, "preview"); // concrete Canvas: the harness reads its pixels
var ext = Activator.CreateInstance(type,
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { canvas }, null)!;

foreach (var (key, value) in sets)
{
    var prop = type.GetProperties().FirstOrDefault(pp =>
        pp.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pp.GetCustomAttribute<ExtensionParameterAttribute>()?.DisplayName, key, StringComparison.OrdinalIgnoreCase));
    if (prop == null) { Console.WriteLine("no parameter " + key); continue; }
    object v = prop.PropertyType.IsEnum ? Enum.Parse(prop.PropertyType, value, true)
        : prop.PropertyType == typeof(SKColor) ? SKColor.Parse(value)
        : Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
    prop.SetValue(ext, v);
}

MethodInfo? FindMethod(string key) => type.GetMethods().FirstOrDefault(m =>
    m.GetParameters().Length == 0 &&
    (m.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
     string.Equals(m.GetCustomAttribute<ExtensionMethodAttribute>()?.DisplayName, key, StringComparison.OrdinalIgnoreCase)));

recorder.Source = ext as ICanvasAudioSource;
if (wav) recorder.Start();
var proc = Process.GetCurrentProcess();
var cpu0 = proc.TotalProcessorTime;
var alloc0 = GC.GetTotalAllocatedBytes();
var gc0 = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
var wall = Stopwatch.StartNew();
type.GetMethod("Start", Type.EmptyTypes)!.Invoke(ext, null);
var clock = Stopwatch.StartNew();
var frames = new List<(double T, SKBitmap Bmp)>();
var nextShot = 0.0;
var queue = new Queue<(double T, string Method)>(script.OrderBy(s => s.T));
var shotQueue = new Queue<double>(shots.OrderBy(s => s));
while (clock.Elapsed.TotalSeconds < seconds)
{
    var t = clock.Elapsed.TotalSeconds;
    while (queue.Count > 0 && queue.Peek().T <= t)
    {
        var (_, m) = queue.Dequeue();
        var mi = FindMethod(m);
        if (mi == null) Console.WriteLine("no method " + m);
        else
        {
            try { mi.Invoke(ext, null); } catch (Exception ex) { Console.WriteLine($"{m}: {ex.InnerException?.Message ?? ex.Message}"); }
        }
    }
    if (t * 1000 >= nextShot)
    {
        frames.Add((t, Grab(canvas)));
        nextShot += every;
    }
    if (shotQueue.Count > 0 && shotQueue.Peek() <= t)
    {
        var at = shotQueue.Dequeue();
        using var big = Grab(canvas);
        Save(big, Path.Combine(outDir, $"shot_{at:00.0}.png"), 4);
    }
    Thread.Sleep(5);
}

// Performance of this run, measured before stopping (grabbing frames costs a little, included).
proc.Refresh();
var secs = wall.Elapsed.TotalSeconds;
var cpu = (proc.TotalProcessorTime - cpu0).TotalSeconds / secs * 100;
var allocMb = (GC.GetTotalAllocatedBytes() - alloc0) / 1024.0 / 1024.0 / secs;
var stats = canvas.Stats?.Snapshot();
var perf = $"perf {name}: fps {stats?.Fps:0.0}, render avg {(stats?.AvgRenderMs is { } av ? av.ToString("0.00") : "n/a")} ms max {(stats?.MaxRenderMs is { } mx ? mx.ToString("0.0") : "n/a")} ms, " +
           $"cpu {cpu:0}% of one core, alloc {allocMb:0.0} MB/s, gc {GC.CollectionCount(0) - gc0.Item1}/{GC.CollectionCount(1) - gc0.Item2}/{GC.CollectionCount(2) - gc0.Item3}";
type.GetMethod("Stop", Type.EmptyTypes)?.Invoke(ext, null);
Console.WriteLine(perf);
File.WriteAllText(Path.Combine(outDir, "perf.txt"), perf);
(ext as IDisposable)?.Dispose();
recorder.Stop();

// Contact sheet: frames in a grid, each labelled with its time.
var rows = (frames.Count + cols - 1) / cols;
const int gap = 4, label = 10;
var sheet = new SKBitmap((w * zoom + gap) * cols + gap, (h * zoom + gap + label) * rows + gap);
using (var sk = new SKCanvas(sheet))
{
    sk.Clear(new SKColor(40, 40, 48));
    using var font = new SKFont(SKTypeface.Default, 10);
    using var paint = new SKPaint { Color = SKColors.White };
    for (var i = 0; i < frames.Count; i++)
    {
        var x = gap + i % cols * (w * zoom + gap);
        var y = gap + i / cols * (h * zoom + gap + label);
        sk.DrawText($"{frames[i].T:0.0}s", x, y + 9, font, paint);
        using var img = SKImage.FromBitmap(frames[i].Bmp);
        sk.DrawImage(img, new SKRect(x, y + label, x + w * zoom, y + label + h * zoom),
            new SKSamplingOptions(SKFilterMode.Nearest));
    }
}
Save(sheet, Path.Combine(outDir, "sheet.png"), 1);
Console.WriteLine($"{frames.Count} frames -> {Path.Combine(outDir, "sheet.png")}");
foreach (var f in frames) f.Bmp.Dispose();

if (wav)
{
    var path = Path.Combine(outDir, "sound.wav");
    recorder.Write(path);
    Console.WriteLine(recorder.Report());
    Console.WriteLine("sound -> " + path);
}
return 0;

static SKBitmap Grab(Canvas canvas)
{
    var bmp = new SKBitmap(new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
    var bytes = canvas.Width * canvas.Height * 4;
    var tmp = new byte[bytes];
    Marshal.Copy(canvas.GetPixels(), tmp, 0, bytes);
    Marshal.Copy(tmp, 0, bmp.GetPixels(), bytes);
    // The wall ignores alpha; show it opaque.
    using (var sk = new SKCanvas(bmp))
    using (var p = new SKPaint { BlendMode = SKBlendMode.DstOver, Color = SKColors.Black })
        sk.DrawRect(0, 0, bmp.Width, bmp.Height, p);
    return bmp;
}

static void Save(SKBitmap bmp, string path, int zoom)
{
    using var big = new SKBitmap(bmp.Width * zoom, bmp.Height * zoom);
    using (var c = new SKCanvas(big))
    using (var img = SKImage.FromBitmap(bmp))
        c.DrawImage(img, new SKRect(0, 0, big.Width, big.Height), new SKSamplingOptions(SKFilterMode.Nearest));
    using var data = big.Encode(SKEncodedImageFormat.Png, 90);
    File.WriteAllBytes(path, data.ToArray());
}

static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "CanvasManagement.sln"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("CanvasManagement.sln not found above the tool");
}

/// <summary>Renders the shared voice mixer in real time (like the host does) and keeps the samples.</summary>
internal sealed class SoundRecorder
{
    private const int Frames = 360;
    public readonly ExtensionVoiceMixer Voices = new(48000);
    public ICanvasAudioSource? Source;
    private readonly List<float> _samples = new();
    private Thread? _thread;
    private volatile bool _run;

    public void Start()
    {
        _run = true;
        _thread = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            long blocks = 0;
            var buf = new float[Frames * 2];
            while (_run)
            {
                var due = blocks * Frames / 48000.0;
                if (clock.Elapsed.TotalSeconds < due) { Thread.Sleep(1); continue; }
                Array.Clear(buf);
                var src = Source;
                Voices.Render(buf, s => { if (src is { IsAudible: true }) src.Mix(s, 48000); });
                lock (_samples) _samples.AddRange(buf);
                blocks++;
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
        _thread?.Join();
    }

    public void Write(string path)
    {
        float[] s;
        lock (_samples) s = _samples.ToArray();
        using var bw = new BinaryWriter(File.Create(path));
        bw.Write("RIFF"u8);
        bw.Write(36 + s.Length * 2);
        bw.Write("WAVEfmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(48000);
        bw.Write(48000 * 4);
        bw.Write((short)4);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(s.Length * 2);
        foreach (var v in s) bw.Write((short)Math.Clamp((int)(v * 32767), short.MinValue, short.MaxValue));
    }

    public string Report()
    {
        float[] s;
        lock (_samples) s = _samples.ToArray();
        if (s.Length == 0) return "no sound";
        double sum = 0;
        float peak = 0, jump = 0;
        var hot = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var a = Math.Abs(s[i]);
            sum += a * a;
            if (a > peak) peak = a;
            if (a > 0.95f) hot++;
            if (i >= 2)
            {
                var j = Math.Abs(s[i] - s[i - 2]);
                if (j > jump) jump = j;
            }
        }
        var active = s.Count(v => Math.Abs(v) > 0.001f) / (double)s.Length;
        return $"sound: {s.Length / 2 / 48000.0:0.0}s rms {Math.Sqrt(sum / s.Length):0.000} peak {peak:0.00} near-clip {hot} max-step {jump:0.00} active {active:P0}";
    }
}
