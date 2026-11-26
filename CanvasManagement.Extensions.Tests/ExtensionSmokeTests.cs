using System.Reflection;
using System.Text;
using CanvasManagement.BdfFontManager;
using CanvasManagement.Interfaces;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CanvasManagement.Extensions.Tests;

/// <summary>
///     Every extension, at the two wall sizes we ship (one NAS panel, the Pi wall): it must construct with
///     only an <see cref="ICanvas"/>, start, deliver frames, log no errors, and really stop — no frames after
///     <c>Stop()</c>. Extensions that need a file, stream, device or service to show anything only have to
///     start and stop cleanly.
/// </summary>
public class ExtensionSmokeTests
{
    /// <summary>Need media, a URL, native audio or a service before they draw anything.</summary>
    private static readonly HashSet<string> NeedsInput = new(StringComparer.Ordinal)
    {
        "AnimatedGifPlayerExtension",
        "BassAudioPlayerExtension",
        "AudioPlayerExtension",
        "TrailerExtension",
        "Lav1StreamPlayerExtension",
        "NetworkStreamPlayerExtension",
        "NetworkStreamPlayer",
        "SlideShowPlayerExtension",
        "VLCMediaPlayerExtension",
        "YouTubePlayerExtension"
    };

    /// <summary>Talk to the internet or Home Assistant; their error logs depend on the network, not on the code.</summary>
    private static readonly string[] OnlinePrefixes = { "HomeAssistant", "Weather", "NewsTicker", "NightSky", "NowPlaying" };

    private static readonly Lazy<IReadOnlyList<Type>> Types = new(DiscoverTypes);

    public static IEnumerable<object[]> Cases() =>
        Types.Value.SelectMany(t => new[] { new object[] { t.FullName!, 256, 128 }, new object[] { t.FullName!, 384, 192 } });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Starts_renders_and_stops(string typeName, int width, int height)
    {
        var type = Types.Value.Single(t => t.FullName == typeName);
        var log = new StringWriter(new StringBuilder());
        var oldOut = Console.Out;
        Console.SetOut(log);
        try
        {
            using var mgr = new CanvasManager(width, height);
            mgr.UseExtensionAudio(new ExtensionVoiceMixer());
            var canvas = mgr.GetCanvas(0, "smoke");
            var ext = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { canvas }, null)!;
            var start = type.GetMethod("Start", Type.EmptyTypes)!;
            var stop = type.GetMethod("Stop", Type.EmptyTypes)!;

            start.Invoke(ext, null);
            Thread.Sleep(2500);
            var framesRunning = canvas.Stats.Snapshot().Frames;
            stop.Invoke(ext, null);
            Thread.Sleep(150);
            var framesAfterStop = canvas.Stats.Snapshot().Frames;
            Thread.Sleep(500);
            var framesLater = canvas.Stats.Snapshot().Frames;
            (ext as IDisposable)?.Dispose();

            var shortName = type.Name;
            var output = log.ToString();
            if (!NeedsInput.Contains(shortName))
                Assert.True(framesRunning >= 1, $"{shortName} delivered only {framesRunning} frames in 2.5 s.\n{Tail(output)}");
            Assert.True(framesLater == framesAfterStop,
                $"{shortName} still delivered {framesLater - framesAfterStop} frames after Stop().");

            var online = OnlinePrefixes.Any(p => shortName.StartsWith(p, StringComparison.Ordinal));
            if (!online && !NeedsInput.Contains(shortName))
            {
                var errors = output.Split('\n').Where(IsErrorLine).ToList();
                Assert.True(errors.Count == 0, $"{shortName} logged errors:\n{string.Join("\n", errors.Take(10))}");
            }
        }
        finally
        {
            Console.SetOut(oldOut);
        }
    }

    private static bool IsErrorLine(string line) =>
        line.Contains("[LOOP]", StringComparison.Ordinal) ||
        line.Contains("Exception", StringComparison.Ordinal) ||
        line.Contains("Render error", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("tick error", StringComparison.OrdinalIgnoreCase);

    private static string Tail(string s) => s.Length > 1500 ? s[^1500..] : s;

    private static IReadOnlyList<Type> DiscoverTypes()
    {
        BdfFontRegistry.LoadFontsFromDirectory(FindFonts(), true);
        // Extension assemblies are named CanvasManagement.Extension.X, CanvasManagement.Extensions.Default and
        // (Game of Life) CanvasManagement.Canvas.Extension.X — match on the ".Extension" part.
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "CanvasManagement*.Extension*.dll"))
            if (!dll.EndsWith(".Tests.dll", StringComparison.OrdinalIgnoreCase))
                Assembly.LoadFrom(dll);
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is { } n && n.Contains(".Extension", StringComparison.Ordinal) &&
                        !n.EndsWith(".Tests", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetCustomAttribute<ExtensionInfoAttribute>() != null)
            .Where(t => t.GetCustomAttribute<ObsoleteAttribute>() == null)
            .OrderBy(t => t.FullName)
            .ToList();
    }

    private static string FindFonts()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Fonts"))) d = d.Parent;
        return d == null ? "Fonts" : Path.Combine(d.FullName, "Fonts");
    }
}
