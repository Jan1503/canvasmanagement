namespace CanvasManagement.Interfaces;

/// <summary>Built-in background tunes. All are original, composed in code from a chord plan and a seed.</summary>
public enum GameSong
{
    Arcade,
    Puzzle,
    Adventure,
    Action,
    Chill,
    Racing,
    Retro,
    Space
}

/// <summary>
///     A tiny tracker that renders a looping chiptune: drums, a bass line, a chord arpeggio and a lead melody
///     whose A section repeats so it is catchy. The loop is seamless (note tails are folded onto the start).
///     Rendering takes a few tens of milliseconds; results are cached per song.
/// </summary>
public static class ChipMusic
{
    private const int R = SoundSynth.Rate;
    private static readonly Dictionary<GameSong, float[]> Cache = new();
    private static readonly object Gate = new();

    private sealed record Style(
        int Bpm,
        int Root,
        int[] Scale,
        int[][] Chords,
        string Kick,
        string Snare,
        string Hat,
        string Bass,
        Wave Lead,
        Wave Arp,
        int Seed,
        float Swing = 0f,
        bool Busy = true);

    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
    private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };

    // Chord plans: one row per section (A, A, B, A'), four bars each; numbers are scale degrees (0 = I).
    private static Style StyleOf(GameSong song) => song switch
    {
        GameSong.Arcade => new Style(138, 60, Major,
            new[] { new[] { 0, 5, 3, 4 }, new[] { 0, 5, 3, 4 }, new[] { 3, 4, 2, 5 }, new[] { 0, 5, 4, 0 } },
            "x...x...x...x...", "....x.......x...", "..x...x...x...x.", "R.R.O.R.R.R.O.5.", Wave.Square, Wave.Pulse, 11),
        GameSong.Puzzle => new Style(118, 57, Minor,
            new[] { new[] { 0, 5, 2, 6 }, new[] { 0, 5, 2, 4 }, new[] { 3, 0, 3, 4 }, new[] { 0, 5, 6, 0 } },
            "x.......x.x.....", "....x.......x...", "x.x.x.x.x.x.x.x.", "R...5...O...5...", Wave.Pulse, Wave.Triangle, 23, Busy: false),
        GameSong.Adventure => new Style(150, 62, Major,
            new[] { new[] { 0, 3, 4, 0 }, new[] { 0, 3, 4, 4 }, new[] { 5, 3, 0, 4 }, new[] { 0, 3, 4, 0 } },
            "x.....x.x.......", "....x.......x..x", "x.x.x.x.x.x.x.x.", "R.O.R.O.R.O.5.O.", Wave.Square, Wave.Pulse, 37, 0.12f),
        GameSong.Action => new Style(162, 52, Minor,
            new[] { new[] { 0, 0, 5, 6 }, new[] { 0, 0, 5, 4 }, new[] { 3, 4, 5, 6 }, new[] { 0, 5, 6, 0 } },
            "x..x..x.x..x..x.", "....x.......x...", "xxxxxxxxxxxxxxxx", "RRORRRORRRORRR5R", Wave.Saw, Wave.Square, 41),
        GameSong.Chill => new Style(98, 55, Dorian,
            new[] { new[] { 0, 3, 0, 3 }, new[] { 0, 3, 4, 3 }, new[] { 5, 4, 3, 1 }, new[] { 0, 3, 0, 3 } },
            "x.........x.....", "....x.......x...", "..x...x...x...x.", "R.....5.R.....5.", Wave.Triangle, Wave.Sine, 53, 0.18f, false),
        GameSong.Racing => new Style(172, 57, Minor,
            new[] { new[] { 0, 5, 6, 4 }, new[] { 0, 5, 6, 4 }, new[] { 3, 4, 0, 5 }, new[] { 0, 5, 6, 0 } },
            "x.x...x.x.x...x.", "....x.......x...", "x.xxx.xxx.xxx.xx", "R.RRR.RRO.OOR.55", Wave.Saw, Wave.Pulse, 67),
        GameSong.Retro => new Style(128, 65, Major,
            new[] { new[] { 0, 4, 5, 3 }, new[] { 0, 4, 5, 3 }, new[] { 1, 4, 0, 5 }, new[] { 0, 4, 3, 0 } },
            "x...x...x...x...", "....x.......x...", "x.x.x.x.x.x.x.x.", "R...R.5.R...O.5.", Wave.Square, Wave.Triangle, 79),
        _ => new Style(110, 50, Minor,
            new[] { new[] { 0, 6, 5, 6 }, new[] { 0, 6, 5, 4 }, new[] { 2, 3, 5, 4 }, new[] { 0, 6, 5, 0 } },
            "x.......x.......", "........x.......", "x...x...x...x...", "R.......5.......", Wave.Triangle, Wave.Sine, 97, 0f, false)
    };

    /// <summary>The song if it is rendered already; otherwise starts rendering it in the background.</summary>
    public static float[]? TryGet(GameSong song)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(song, out var buf)) return buf;
            if (!Pending.Add(song)) return null;
        }
        Task.Run(() =>
        {
            try { Get(song); }
            catch (Exception ex) { Console.WriteLine("[ChipMusic] " + ex.Message); }
        });
        return null;
    }

    private static readonly HashSet<GameSong> Pending = new();

    /// <summary>The song as one seamless mono loop at <see cref="SoundSynth.Rate"/>.</summary>
    public static float[] Get(GameSong song)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(song, out var buf)) return buf;
            buf = Render(StyleOf(song));
            Cache[song] = buf;
            return buf;
        }
    }

    private static float[] Render(Style s)
    {
        var step = 60f / s.Bpm / 4f; // sixteenth note
        var bars = s.Chords.Length * 4;
        var length = (int)(bars * 16 * step * R);
        var buf = new float[length + 3 * R];
        var rng = new Random(s.Seed);

        int Note(int degree, int octave = 0)
        {
            var d = ((degree % 7) + 7) % 7;
            var o = (degree - d) / 7 + octave;
            return s.Root + s.Scale[d] + 12 * o;
        }

        float T(int bar, int st) => (bar * 16 + st) * step + (st % 2 == 1 ? s.Swing * step : 0);

        // The A motif is written once and reused, so the tune comes back like a chorus.
        var motifA = Motif(rng, s.Busy);
        var motifB = Motif(rng, s.Busy);

        for (var section = 0; section < s.Chords.Length; section++)
        {
            var motif = section == 2 ? motifB : motifA;
            var variation = section == 3;
            for (var b = 0; b < 4; b++)
            {
                var bar = section * 4 + b;
                var chord = s.Chords[section][b];
                var lastBar = bar == bars - 1;

                // Drums.
                for (var st = 0; st < 16; st++)
                {
                    var t = T(bar, st);
                    if (s.Kick[st] == 'x')
                        SoundSynth.Add(buf, t, Wave.Sine, 130, 42, 0.14f, 0.9f, 0.001f, 0.03f);
                    if (s.Snare[st] == 'x' || (lastBar && st >= 12 && st % 2 == 0))
                    {
                        SoundSynth.Add(buf, t, Wave.Noise, 9000, 2500, 0.12f, 0.35f, 0.001f, 0.05f, decay: 18);
                        SoundSynth.Add(buf, t, Wave.Triangle, 190, 150, 0.06f, 0.3f, 0.001f, 0.02f);
                    }
                    if (s.Hat[st] == 'x')
                        SoundSynth.Add(buf, t, Wave.Noise, 16000, 14000, 0.03f, st % 4 == 0 ? 0.12f : 0.08f, 0.001f, 0.015f);
                }

                // Bass: R root, 5 fifth, O octave, in the second octave below the root.
                for (var st = 0; st < 16; st++)
                {
                    var c = s.Bass[st];
                    if (c == '.') continue;
                    var len = 1;
                    while (st + len < 16 && s.Bass[st + len] == '.' && len < 4) len++;
                    var n = c switch
                    {
                        '5' => Note(chord + 4, -2),
                        'O' => Note(chord, -1),
                        _ => Note(chord, -2)
                    };
                    var hz = SoundSynth.Midi(n);
                    SoundSynth.Add(buf, T(bar, st), Wave.Triangle, hz, hz, len * step * 0.9f, 0.55f, 0.003f, 0.02f);
                }

                // Arpeggio over the chord, quiet.
                var arp = new[] { 0, 2, 4, 7 };
                for (var st = 0; st < 16; st += s.Busy ? 1 : 2)
                {
                    var n = Note(chord + arp[(st / (s.Busy ? 1 : 2)) % 4], 0);
                    var hz = SoundSynth.Midi(n);
                    SoundSynth.Add(buf, T(bar, st), s.Arp, hz, hz, step * 0.8f, 0.09f, 0.002f, 0.02f, decay: 6);
                }

                // Lead.
                foreach (var (st, len, deg) in motif[b])
                {
                    var d = deg + chord + (variation && b == 3 && st >= 8 ? 2 : 0);
                    var n = Note(d, 1);
                    var hz = SoundSynth.Midi(n);
                    SoundSynth.Add(buf, T(bar, st), s.Lead, hz, hz, len * step * 0.92f, 0.2f, 0.004f, 0.03f,
                        len >= 4 ? 5.5f : 0, len >= 4 ? 0.012f : 0, decay: 1.2f);
                }
            }
        }

        // Fold the tails past the end back onto the start, then trim: the loop is seamless.
        for (var i = 0; i < buf.Length - length; i++) buf[i] += buf[length + i];
        var loop = new float[length];
        Array.Copy(buf, loop, length);

        // Gentle level: music sits under the effects.
        double sum = 0;
        var peak = 0f;
        foreach (var v in loop)
        {
            sum += v * v;
            peak = Math.Max(peak, Math.Abs(v));
        }
        var rms = (float)Math.Sqrt(sum / loop.Length);
        var k = Math.Min(0.16f / Math.Max(rms, 1e-4f), 0.9f / Math.Max(peak, 1e-4f));
        for (var i = 0; i < loop.Length; i++) loop[i] *= k;
        return loop;
    }

    /// <summary>Four bars of melody: (step, length in sixteenths, scale degree above the chord root).</summary>
    private static List<(int Step, int Len, int Degree)>[] Motif(Random rng, bool busy)
    {
        var rhythms = busy
            ? new[] { new[] { 2, 2, 2, 2, 4, 4 }, new[] { 4, 2, 2, 4, 4 }, new[] { 2, 2, 4, 2, 2, 4 }, new[] { 3, 3, 2, 4, 4 } }
            : new[] { new[] { 4, 4, 8 }, new[] { 6, 2, 8 }, new[] { 4, 2, 2, 8 }, new[] { 8, 4, 4 } };
        var chordTones = new[] { 0, 2, 4, 7 };
        var bars = new List<(int, int, int)>[4];
        var prev = 4;
        for (var b = 0; b < 4; b++)
        {
            var r = rhythms[rng.Next(rhythms.Length)];
            if (b == 3) r = busy ? new[] { 2, 2, 4, 8 } : new[] { 4, 12 };
            var list = new List<(int, int, int)>();
            var st = 0;
            for (var i = 0; i < r.Length && st < 16; i++)
            {
                int deg;
                if (st % 4 == 0)
                {
                    // Strong beats land on chord tones near the previous note.
                    deg = chordTones.OrderBy(c => Math.Abs(c - prev) + rng.NextDouble() * 2).First();
                }
                else
                {
                    deg = Math.Clamp(prev + rng.Next(-2, 3), -2, 9);
                }
                if (b == 3 && i == r.Length - 1) deg = 0; // resolve home
                // An occasional rest keeps it breathing.
                if (!(i > 0 && rng.NextDouble() < 0.12)) list.Add((st, r[i], deg));
                prev = deg;
                st += r[i];
            }
            bars[b] = list;
        }
        return bars;
    }
}
