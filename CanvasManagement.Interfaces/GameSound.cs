using System.Diagnostics;

namespace CanvasManagement.Interfaces;

/// <summary>Stock game effects, made in code once and shared by every game.</summary>
public static class GameSfx
{
    private const int R = SoundSynth.Rate;
    private static readonly Lazy<GameSfxSet> Set = new(() => new GameSfxSet());

    public static float[] Jump => Set.Value.Jump;
    public static float[] Coin => Set.Value.Coin;
    public static float[] PowerUp => Set.Value.PowerUp;
    public static float[] Hit => Set.Value.Hit;
    public static float[] Explode => Set.Value.Explode;
    public static float[] Shoot => Set.Value.Shoot;
    public static float[] Bounce => Set.Value.Bounce;
    public static float[] Blip => Set.Value.Blip;
    public static float[] Select => Set.Value.Select;
    public static float[] Pop => Set.Value.Pop;
    public static float[] Splash => Set.Value.Splash;
    public static float[] Hop => Set.Value.Hop;
    public static float[] Thud => Set.Value.Thud;
    public static float[] Whoosh => Set.Value.Whoosh;
    public static float[] Stomp => Set.Value.Stomp;
    public static float[] Rotate => Set.Value.Rotate;
    public static float[] Drop => Set.Value.Drop;
    public static float[] Clear => Set.Value.Clear;
    public static float[] BigClear => Set.Value.BigClear;
    public static float[] Die => Set.Value.Die;
    public static float[] LevelUp => Set.Value.LevelUp;
    public static float[] GameOver => Set.Value.GameOver;
    public static float[] Start => Set.Value.Start;
    public static float[] Score => Set.Value.Score;
    public static float[] Eat => Set.Value.Eat;
    public static float[] Flap => Set.Value.Flap;
    public static float[] Bomb => Set.Value.Bomb;
    public static float[] Fuse => Set.Value.Fuse;

    private sealed class GameSfxSet
    {
        public readonly float[] Jump, Coin, PowerUp, Hit, Explode, Shoot, Bounce, Blip, Select, Pop, Splash, Hop, Thud,
            Whoosh, Stomp, Rotate, Drop, Clear, BigClear, Die, LevelUp, GameOver, Start, Score, Eat, Flap, Bomb, Fuse;

        public GameSfxSet()
        {
            Jump = SoundSynth.Tone(Wave.Square, 300, 760, 0.16f, 0.5f, 0.002f, 0.04f);
            Coin = Buf(0.3f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Square, 988, 988, 0.06f, 0.45f);
                SoundSynth.Add(b, 0.06f, Wave.Square, 1319, 1319, 0.22f, 0.45f, decay: 9);
            });
            PowerUp = Buf(0.6f, b =>
            {
                var notes = new[] { 60, 64, 67, 72, 76, 79, 84 };
                for (var i = 0; i < notes.Length; i++)
                {
                    var hz = SoundSynth.Midi(notes[i]);
                    SoundSynth.Add(b, i * 0.06f, Wave.Square, hz, hz, 0.08f, 0.4f);
                }
            });
            Hit = Buf(0.25f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Noise, 3000, 300, 0.2f, 0.7f, 0.001f, 0.06f);
                SoundSynth.Add(b, 0, Wave.Square, 220, 80, 0.15f, 0.4f);
            });
            Explode = Buf(0.9f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Noise, 2500, 90, 0.85f, 0.9f, 0.001f, 0.3f, decay: 3);
                SoundSynth.Add(b, 0, Wave.Sine, 90, 35, 0.4f, 0.8f, 0.001f, 0.15f);
            });
            Shoot = SoundSynth.Tone(Wave.Square, 1500, 300, 0.13f, 0.4f, 0.001f, 0.03f);
            Bounce = SoundSynth.Tone(Wave.Square, 440, 440, 0.05f, 0.45f, 0.001f, 0.02f);
            Blip = SoundSynth.Tone(Wave.Triangle, 880, 880, 0.04f, 0.5f, 0.001f, 0.015f);
            Select = Buf(0.15f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Square, 660, 660, 0.05f, 0.35f);
                SoundSynth.Add(b, 0.05f, Wave.Square, 990, 990, 0.08f, 0.35f);
            });
            Pop = SoundSynth.Tone(Wave.Sine, 900, 1800, 0.07f, 0.6f, 0.001f, 0.03f);
            Splash = Buf(0.5f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Noise, 5000, 600, 0.45f, 0.6f, 0.005f, 0.2f, decay: 4);
                SoundSynth.Add(b, 0, Wave.Sine, 400, 120, 0.2f, 0.4f);
            });
            Hop = SoundSynth.Tone(Wave.Triangle, 420, 640, 0.07f, 0.5f, 0.002f, 0.025f);
            Thud = Buf(0.2f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Sine, 140, 50, 0.16f, 0.8f, 0.001f, 0.06f);
                SoundSynth.Add(b, 0, Wave.Noise, 1200, 200, 0.08f, 0.4f, 0.001f, 0.04f);
            });
            Whoosh = SoundSynth.Tone(Wave.Noise, 600, 3000, 0.25f, 0.4f, 0.04f, 0.1f);
            Stomp = Buf(0.22f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Square, 600, 150, 0.12f, 0.45f);
                SoundSynth.Add(b, 0.02f, Wave.Noise, 2000, 300, 0.1f, 0.4f);
            });
            Rotate = SoundSynth.Tone(Wave.Triangle, 700, 900, 0.04f, 0.4f, 0.001f, 0.015f);
            Drop = Buf(0.18f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Square, 260, 120, 0.08f, 0.4f);
                SoundSynth.Add(b, 0, Wave.Noise, 1500, 300, 0.06f, 0.3f);
            });
            Clear = Buf(0.45f, b =>
            {
                foreach (var (t, n) in new[] { (0f, 72), (0.07f, 76), (0.14f, 79) })
                {
                    var hz = SoundSynth.Midi(n);
                    SoundSynth.Add(b, t, Wave.Square, hz, hz, 0.12f, 0.4f, decay: 6);
                }
            });
            BigClear = Buf(0.9f, b =>
            {
                var notes = new[] { 72, 76, 79, 84, 79, 84, 88 };
                for (var i = 0; i < notes.Length; i++)
                {
                    var hz = SoundSynth.Midi(notes[i]);
                    SoundSynth.Add(b, i * 0.08f, Wave.Square, hz, hz, 0.12f, 0.4f, decay: 4);
                }
            });
            Die = Buf(1.0f, b =>
            {
                for (var i = 0; i < 5; i++)
                {
                    var top = 700f - i * 100;
                    SoundSynth.Add(b, i * 0.15f, Wave.Square, top, top * 0.6f, 0.15f, 0.4f);
                }
                SoundSynth.Add(b, 0.75f, Wave.Noise, 2000, 200, 0.2f, 0.4f);
            });
            LevelUp = SoundSynth.Melody(new (int, float)[] { (67, 0.5f), (72, 0.5f), (76, 0.5f), (79, 0.5f), (84, 1.5f) },
                280, Wave.Square, 0.4f);
            GameOver = SoundSynth.Melody(new (int, float)[] { (67, 1), (66, 1), (65, 1), (64, 3) }, 200, Wave.Square, 0.4f);
            Start = SoundSynth.Melody(new (int, float)[] { (60, 0.5f), (64, 0.5f), (67, 0.5f), (72, 1) }, 260, Wave.Square,
                0.4f);
            Score = Buf(0.35f, b =>
            {
                SoundSynth.Add(b, 0, Wave.Sine, 1047, 1047, 0.1f, 0.5f);
                SoundSynth.Add(b, 0.09f, Wave.Sine, 1568, 1568, 0.25f, 0.5f, decay: 8);
            });
            Eat = SoundSynth.Tone(Wave.Square, 500, 900, 0.07f, 0.4f, 0.001f, 0.02f);
            Flap = SoundSynth.Tone(Wave.Noise, 1200, 2600, 0.09f, 0.35f, 0.005f, 0.04f);
            Bomb = Buf(0.15f, b => SoundSynth.Add(b, 0, Wave.Square, 200, 260, 0.1f, 0.35f));
            Fuse = SoundSynth.Tone(Wave.Noise, 7000, 7000, 0.06f, 0.2f, 0.005f, 0.02f);
        }

        private static float[] Buf(float seconds, Action<float[]> fill)
        {
            var b = new float[(int)(seconds * R)];
            fill(b);
            // Layered sounds can add up past full scale; keep every stock clip under 0.9.
            var peak = 0f;
            foreach (var v in b) peak = Math.Max(peak, Math.Abs(v));
            if (peak > 0.9f)
                for (var i = 0; i < b.Length; i++) b[i] *= 0.9f / peak;
            return b;
        }
    }
}

/// <summary>
///     What a game needs for sound: effects with a volume, an optional looping background song on slot 7,
///     and no spam (the same clip is not restarted within a few frames). Silent when the canvas has no audio.
///     Call <see cref="Update"/> once per frame and <see cref="Stop"/> in the extension's Stop.
/// </summary>
public sealed class GameSound(ICanvas canvas, GameSong song)
{
    public const int MusicSlot = 7;
    private readonly Dictionary<float[], long> _last = new(ReferenceEqualityComparer.Instance);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float[]? _playing;
    private float _duck = 1f;

    public bool Effects { get; set; } = true;
    public bool Music { get; set; } = true;

    /// <summary>0..1 for everything.</summary>
    public float Volume { get; set; } = 0.7f;

    /// <summary>0..1, on top of <see cref="Volume"/>.</summary>
    public float MusicVolume { get; set; } = 0.5f;

    public GameSong Song { get; set; } = song;

    /// <summary>Plays a clip made with <see cref="SoundSynth"/> (or <see cref="GameSfx"/>).</summary>
    public void Play(float[] clip, float gain = 1f, float pan = 0f, float rate = 1f, int minGapMs = 45)
    {
        var audio = canvas.Audio;
        if (audio == null || !Effects || Volume <= 0 || clip.Length < 2) return;
        var now = _clock.ElapsedMilliseconds;
        if (_last.TryGetValue(clip, out var t) && now - t < minGapMs) return;
        _last[clip] = now;
        audio.Play(clip, SoundSynth.Rate, Math.Clamp(gain * Volume, 0, 1), Math.Clamp(pan, -1, 1), rate);
    }

    /// <summary>Lower the music for a moment (game over, pause) — 1 is full, 0 silent.</summary>
    public void Duck(float level) => _duck = Math.Clamp(level, 0, 1);

    /// <summary>Keeps the background song running (or stopped) to match the settings.</summary>
    public void Update()
    {
        var audio = canvas.Audio;
        if (audio == null) return;
        var gain = Music ? Volume * MusicVolume * _duck * 0.9f : 0f;
        if (gain <= 0.002f)
        {
            if (_playing != null) audio.StopLoop(MusicSlot);
            _playing = null;
            return;
        }
        var buf = ChipMusic.TryGet(Song);
        if (buf == null) return; // still rendering, starts next frame or so
        audio.LoopSample(MusicSlot, buf, SoundSynth.Rate, gain, 1f);
        _playing = buf;
    }

    public void Stop()
    {
        canvas.Audio?.StopLoop(MusicSlot);
        _playing = null;
    }
}
