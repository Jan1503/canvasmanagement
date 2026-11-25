using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Arcade-style sound made in code: a background siren that climbs as the dots run out, a two-note chomp,
///     a warbling loop while ghosts are blue, a fast whine while eyes run home, and short jingles. All tunes
///     are original. Slot 0 carries the background loop.
/// </summary>
internal sealed class PacSound(ICanvas canvas)
{
    private const int R = SoundSynth.Rate;
    private static Clips? _clips;
    private bool _chompAlt;
    private float[]? _loop;
    private float _lastChomp;
    private float _clock;
    private bool _missing;

    private sealed class Clips
    {
        public readonly float[] ChompA, ChompB, Power, EatGhost, Fruit, Death, Life, Clear, Start, Over, FruitIn;
        public readonly float[][] Siren;
        public readonly float[] Fright, Eyes;

        public Clips()
        {
            ChompA = SoundSynth.Tone(Wave.Triangle, 520, 230, 0.085f, 0.9f, 0.002f, 0.015f);
            ChompB = SoundSynth.Tone(Wave.Triangle, 240, 500, 0.085f, 0.9f, 0.002f, 0.015f);
            Power = SoundSynth.Concat(
                SoundSynth.Tone(Wave.Square, 300, 900, 0.12f, 0.45f),
                SoundSynth.Tone(Wave.Square, 450, 1300, 0.12f, 0.4f));
            EatGhost = SoundSynth.Tone(Wave.Square, 180, 1800, 0.42f, 0.5f, 0.003f, 0.06f);
            FruitIn = SoundSynth.Concat(
                SoundSynth.Tone(Wave.Sine, 1046, 1046, 0.05f, 0.5f),
                SoundSynth.Tone(Wave.Sine, 1568, 1568, 0.08f, 0.5f));

            Fruit = new float[(int)(0.36f * R)];
            SoundSynth.Add(Fruit, 0.00f, Wave.Square, 784, 784, 0.07f, 0.4f);
            SoundSynth.Add(Fruit, 0.08f, Wave.Square, 1175, 1175, 0.07f, 0.4f);
            SoundSynth.Add(Fruit, 0.16f, Wave.Square, 1568, 1568, 0.18f, 0.4f, decay: 6);

            // Death: a spiral of falling sweeps, then two pops.
            Death = new float[(int)(1.75f * R)];
            for (var i = 0; i < 9; i++)
            {
                var top = 900f - i * 70;
                SoundSynth.Add(Death, i * 0.13f, Wave.Square, top, top * 0.62f, 0.13f, 0.42f, 0.002f, 0.02f);
            }
            SoundSynth.Add(Death, 1.25f, Wave.Noise, 3000, 400, 0.12f, 0.5f);
            SoundSynth.Add(Death, 1.45f, Wave.Noise, 3000, 400, 0.12f, 0.5f);

            Life = new float[(int)(0.7f * R)];
            for (var i = 0; i < 4; i++)
                SoundSynth.Add(Life, i * 0.16f, Wave.Sine, 1568, 1568, 0.15f, 0.5f, decay: 10);

            Clear = SoundSynth.Melody(new (int, float)[] { (72, 0.5f), (76, 0.5f), (79, 0.5f), (84, 0.5f), (88, 0.5f), (91, 1.5f) },
                300, Wave.Square, 0.4f);
            Over = SoundSynth.Melody(new (int, float)[] { (67, 1), (64, 1), (60, 1), (55, 2) }, 220, Wave.Square, 0.4f);

            // Start jingle: an original bouncy tune, lead over a walking bass, about four seconds.
            var lead = new (int, float)[]
            {
                (76, 0.5f), (79, 0.5f), (84, 0.5f), (79, 0.5f), (81, 0.5f), (77, 0.5f), (72, 1),
                (74, 0.5f), (77, 0.5f), (81, 0.5f), (77, 0.5f), (79, 0.5f), (76, 0.5f), (72, 1),
                (76, 0.5f), (79, 0.5f), (84, 0.5f), (86, 0.5f), (88, 0.5f), (86, 0.5f), (84, 0.5f), (79, 0.5f),
                (81, 0.5f), (83, 0.5f), (84, 1.5f), (0, 0.5f)
            };
            var bass = new (int, float)[]
            {
                (48, 1), (55, 1), (53, 1), (48, 1), (50, 1), (57, 1), (55, 1), (48, 1),
                (48, 1), (52, 1), (55, 1), (52, 1), (53, 1), (55, 1), (48, 2)
            };
            Start = SoundSynth.Melody(lead, 230, Wave.Square, 0.32f, legato: 0.8f, decay: 3);
            SoundSynth.Melody(bass, 230, Wave.Triangle, 0.5f, Start, 0.9f, 1.5f);

            // Sirens: five steps, each a whole number of wobbles so the loop is seamless.
            Siren = new float[5][];
            for (var i = 0; i < Siren.Length; i++)
                Siren[i] = SoundSynth.Loop(Wave.Triangle, 380 + i * 55, 0.5f, 2.6f + i * 0.35f, 0.16f, 0.55f);
            Fright = SoundSynth.Loop(Wave.Square, 210, 0.3f, 6.6f, 0.35f, 0.42f, lfoSaw: true);
            Eyes = SoundSynth.Loop(Wave.Square, 1150, 0.1f, 20f, 0.3f, 0.32f, lfoSaw: true);
        }
    }

    private readonly GameSound _music = new(canvas, GameSong.Arcade);

    public void Update(Game g, GameEvent ev, bool on, float level, float dt, bool demo, bool music, float musicLevel)
    {
        _music.Music = music;
        _music.Volume = level;
        _music.MusicVolume = musicLevel;
        // The start jingle and the death tune play alone.
        _music.Duck(g.Phase is Phase.Ready or Phase.Dying or Phase.GameOver ? 0f : g.Phase == Phase.Paused ? 0.3f : 1f);
        _music.Update();
        var audio = canvas.Audio;
        if (audio == null || !on || level <= 0)
        {
            if (on && audio == null && !_missing)
            {
                _missing = true;
                Console.WriteLine("[PacMan] sound is on, but this canvas has no audio output");
            }
            // Effects off; the music (if on) keeps going.
            audio?.StopLoop(0);
            _loop = null;
            return;
        }

        _clips ??= new Clips();
        var c = _clips;
        _clock += dt;
        // The demo is quieter; it is wallpaper until someone picks up the pad.
        var v = level * (demo ? 0.75f : 1f) * 1.3f;

        if (ev.HasFlag(GameEvent.GameStart)) Shot(audio, c.Start, 0.55f * v);
        if (ev.HasFlag(GameEvent.Dot) && _clock - _lastChomp > 0.05f)
        {
            _lastChomp = _clock;
            Shot(audio, _chompAlt ? c.ChompB : c.ChompA, 0.42f * v);
            _chompAlt = !_chompAlt;
        }
        if (ev.HasFlag(GameEvent.Power)) Shot(audio, c.Power, 0.35f * v);
        if (ev.HasFlag(GameEvent.GhostEaten)) Shot(audio, c.EatGhost, 0.5f * v);
        if (ev.HasFlag(GameEvent.Fruit)) Shot(audio, c.Fruit, 0.5f * v);
        if (ev.HasFlag(GameEvent.FruitAppears)) Shot(audio, c.FruitIn, 0.3f * v);
        if (ev.HasFlag(GameEvent.ExtraLife)) Shot(audio, c.Life, 0.5f * v);
        if (ev.HasFlag(GameEvent.LevelClear)) Shot(audio, c.Clear, 0.45f * v);
        if (ev.HasFlag(GameEvent.GameOver)) Shot(audio, c.Over, 0.45f * v);
        if (ev.HasFlag(GameEvent.Death)) Shot(audio, c.Death, 0.5f * v);

        // Background loop: eyes beat blue beats siren.
        float[]? loop = null;
        var gain = 0f;
        if (g.Phase is Phase.Playing or Phase.GhostEaten)
        {
            if (g.Ghosts.Any(x => x.State is GhostState.Eyes or GhostState.Entering))
            {
                loop = c.Eyes;
                gain = 0.22f;
            }
            else if (g.Fright > 0)
            {
                loop = c.Fright;
                gain = 0.26f;
            }
            else
            {
                var done = 1f - g.Maze.DotsLeft / (float)Math.Max(1, g.Maze.DotsTotal);
                loop = c.Siren[Math.Min(c.Siren.Length - 1, (int)(done * c.Siren.Length))];
                gain = music ? 0.07f : 0.15f;
            }
            if (g.Phase == Phase.GhostEaten) gain *= 0.5f;
        }

        if (loop == null)
        {
            if (_loop != null) audio.StopLoop(0);
            _loop = null;
        }
        else
        {
            audio.LoopSample(0, loop, R, gain * v, 1f);
            _loop = loop;
        }
    }

    private static void Shot(IExtensionAudio audio, float[] clip, float gain) => audio.Play(clip, R, gain, 0, 1f);

    public void Silence()
    {
        _music.Stop();
        canvas.Audio?.StopLoop(0);
        _loop = null;
    }
}
