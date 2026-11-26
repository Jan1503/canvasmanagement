using Xunit.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;
using CanvasManagement.Interfaces;

namespace CanvasManagement.Extensions.Tests;

/// <summary>
///     Every game left to its autopilot for a while, the way it runs on the wall when nobody plays:
///     the picture must keep moving (a frozen game is a bug — Pixel Plumber used to stop for no reason), and
///     where the score is readable it must go up (the autopilot actually plays).
/// </summary>
public class AutopilotEnduranceTests(ITestOutputHelper output)
{
    private const int Seconds = 14;

    /// <summary>The longest the wall may show the identical picture while the game runs.</summary>
    private const double MaxFrozenSeconds = 3.5;

    public static IEnumerable<object[]> Games() =>
        new[]
        {
            "BomberMaze", "BubblePop", "DinoRunner", "FlappyBird", "FruitFall", "PixelPlumber", "Pong",
            "RainbowBreakout", "Snake", "SpaceInvaders", "StreetCrosser", "Tetris", "PacMan", "TurboKart"
        }.Select(g => new object[] { g });

    [Theory]
    [MemberData(nameof(Games))]
    public void Autopilot_keeps_playing(string game)
    {
        var type = ExtensionSmokeTests.AllTypes().Single(t => t.Name == game + "Extension");
        var oldOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            using var mgr = new CanvasManager(256, 128);
            mgr.UseExtensionAudio(new ExtensionVoiceMixer());
            var canvas = mgr.GetCanvas(0, "autopilot");
            var ext = (ICanvasExtension)Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { canvas }, null)!;

            ext.Start();
            var bytes = canvas.Width * canvas.Height * 4;
            var buffer = new byte[bytes];
            ulong lastHash = 0;
            var frozenSince = 0.0;
            var longestFrozen = 0.0;
            var bestScore = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Thread.Sleep(1500); // title / countdown
            while (clock.Elapsed.TotalSeconds < Seconds)
            {
                Thread.Sleep(250);
                var now = clock.Elapsed.TotalSeconds;
                Marshal.Copy(canvas.GetPixels(), buffer, 0, bytes);
                var hash = Hash(buffer);
                if (hash != lastHash)
                {
                    lastHash = hash;
                    frozenSince = now;
                }
                longestFrozen = Math.Max(longestFrozen, now - frozenSince);
                bestScore = Math.Max(bestScore, Score(ext) ?? 0);
            }
            ext.Stop();
            (ext as IDisposable)?.Dispose();

            output.WriteLine($"{game}: longest still picture {longestFrozen:0.0} s, best score {(Score(ext) is null ? "n/a" : bestScore)}");
            Assert.True(longestFrozen <= MaxFrozenSeconds,
                $"{game}: the picture stood still for {longestFrozen:0.0} s");
            if (Score(ext) is not null)
                Assert.True(bestScore > 0, $"{game}: the autopilot scored nothing in {Seconds} s");
        }
        finally
        {
            Console.SetOut(oldOut);
        }
    }

    /// <summary>The game's score if it has a readable one (_score, Score, or Pong's two scores).</summary>
    private static int? Score(object game)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = game.GetType();
        if (t.GetField("_score", all)?.GetValue(game) is int f) return f;
        if (t.GetProperty("Score", all)?.GetValue(game) is int p) return p;
        if (t.GetProperty("Score1", all)?.GetValue(game) is int a && t.GetProperty("Score2", all)?.GetValue(game) is int b)
            return a + b;
        return null;
    }

    private static ulong Hash(byte[] data)
    {
        // FNV-1a over the whole frame
        var h = 14695981039346656037UL;
        for (var i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= 1099511628211UL;
        }
        return h;
    }
}
