using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.TurboKart;

/// <summary>
///     The race's sounds. Recorded: three engine loops, the tyre skid, the hit and the boost. Everything else
///     (countdown, lap chime, fanfare, item and drift cues, bumps, jumps) is made in code with
///     <see cref="SoundSynth"/>, so it is clean and in tune.
/// </summary>
internal static class RaceSounds
{
    private const int R = SoundSynth.Rate;
    public static float[]? Low, Mid, High, Skid, Hit, Boost, Click;
    public static int EngineRate = 44100, SkidRate = 32000, SfxRate = 44100;

    // Fundamental of each engine loop (Hz), measured from the recordings; pitch is set relative to these.
    public const float LowHz = 44, MidHz = 66, HighHz = 76;

    public static float[] Beep = [], Go = [], Lap = [], FinalLap = [], Finish = [], Roll = [], Got = [], Oil = [],
        Orb = [], Shield = [], Spin = [], Bump = [], Jump = [], Land = [], Fall = [], Gem = [], Wrong = [];

    public static float[][] Drift = [];
    public static float[] Rumble = [], Jet = [];

    private static bool _tried;

    public static bool Ready
    {
        get
        {
            Ensure();
            return Low != null && Mid != null && High != null;
        }
    }

    public static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var low = WavClip.FromResource("engine-low.wav");
            var mid = WavClip.FromResource("engine-mid.wav");
            var high = WavClip.FromResource("engine-high.wav");
            if (low != null && mid != null && high != null)
            {
                EngineRate = low.Value.Rate;
                Low = WavClip.Prepare(low.Value.Mono, EngineRate, 0.30f, true);
                Mid = WavClip.Prepare(mid.Value.Mono, EngineRate, 0.30f, true);
                High = WavClip.Prepare(high.Value.Mono, EngineRate, 0.30f, true);
            }
            var skid = WavClip.FromResource("skid.wav");
            if (skid != null)
            {
                SkidRate = skid.Value.Rate;
                Skid = WavClip.Prepare(Soften(skid.Value.Mono, 0.35f), SkidRate, 0.22f, true);
            }
            Hit = One("hit.wav", 0.25f);
            Boost = One("boost.wav", 0.22f);
            Click = One("click.wav", 0.2f);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TurboKart] sounds: " + ex.Message);
        }

        try
        {
            Synth();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TurboKart] synth: " + ex.Message);
        }
    }

    private static float[]? One(string name, float rms)
    {
        var c = WavClip.FromResource(name);
        if (c == null) return null;
        SfxRate = c.Value.Rate;
        return WavClip.Prepare(c.Value.Mono, c.Value.Rate, rms, false);
    }

    /// <summary>A gentle low-pass: the raw squeal is mostly hiss on small speakers.</summary>
    private static float[] Soften(float[] x, float k)
    {
        var y = new float[x.Length];
        var s = 0f;
        for (var i = 0; i < x.Length; i++)
        {
            s += (x[i] - s) * k;
            y[i] = s;
        }
        return y;
    }

    private static void Synth()
    {
        Beep = SoundSynth.Tone(Wave.Square, 440, 440, 0.2f, 0.55f, 0.003f, 0.04f);
        Go = SoundSynth.Tone(Wave.Square, 880, 880, 0.65f, 0.55f, 0.003f, 0.15f, decay: 1.5f);

        Lap = new float[(int)(0.42f * R)];
        SoundSynth.Add(Lap, 0f, Wave.Triangle, 1046, 1046, 0.12f, 0.6f);
        SoundSynth.Add(Lap, 0.11f, Wave.Triangle, 1568, 1568, 0.3f, 0.6f, decay: 6);

        FinalLap = SoundSynth.Melody(new (int, float)[] { (67, 0.5f), (72, 0.5f), (76, 0.5f), (79, 1.5f) }, 260,
            Wave.Square, 0.4f);

        // Finish fanfare: original, lead over a bass.
        var lead = new (int, float)[] { (72, 0.5f), (72, 0.5f), (72, 0.5f), (76, 1), (74, 0.5f), (76, 0.5f), (79, 2.5f) };
        var bass = new (int, float)[] { (48, 1.5f), (52, 1), (55, 1), (48, 2.5f) };
        Finish = SoundSynth.Melody(lead, 220, Wave.Square, 0.38f, decay: 1.2f);
        SoundSynth.Melody(bass, 220, Wave.Triangle, 0.5f, Finish, 0.95f, 1f);

        Roll = SoundSynth.Tone(Wave.Triangle, 1400, 1400, 0.03f, 0.45f, 0.001f, 0.01f);
        Got = new float[(int)(0.3f * R)];
        SoundSynth.Add(Got, 0f, Wave.Sine, 1318, 1318, 0.08f, 0.6f);
        SoundSynth.Add(Got, 0.07f, Wave.Sine, 1760, 1760, 0.08f, 0.6f);
        SoundSynth.Add(Got, 0.14f, Wave.Sine, 2093, 2093, 0.16f, 0.6f, decay: 10);

        Oil = new float[(int)(0.3f * R)];
        SoundSynth.Add(Oil, 0f, Wave.Noise, 1800, 200, 0.28f, 0.7f, 0.002f, 0.08f);
        SoundSynth.Add(Oil, 0f, Wave.Sine, 140, 60, 0.18f, 0.6f);

        Orb = SoundSynth.Tone(Wave.Square, 1800, 320, 0.28f, 0.45f, 0.002f, 0.05f, vibratoHz: 30, vibratoDepth: 0.05f);
        Shield = SoundSynth.Tone(Wave.Sine, 900, 1500, 0.45f, 0.5f, 0.01f, 0.12f, vibratoHz: 14, vibratoDepth: 0.06f);
        Spin = SoundSynth.Tone(Wave.Square, 700, 150, 0.7f, 0.35f, 0.003f, 0.12f, vibratoHz: 11, vibratoDepth: 0.12f);

        Bump = new float[(int)(0.16f * R)];
        SoundSynth.Add(Bump, 0f, Wave.Noise, 900, 120, 0.14f, 0.8f, 0.001f, 0.05f);
        SoundSynth.Add(Bump, 0f, Wave.Sine, 110, 60, 0.12f, 0.7f, 0.001f, 0.05f);

        Jump = SoundSynth.Tone(Wave.Noise, 300, 2400, 0.3f, 0.45f, 0.03f, 0.1f);
        Land = new float[(int)(0.22f * R)];
        SoundSynth.Add(Land, 0f, Wave.Noise, 700, 90, 0.2f, 0.8f, 0.001f, 0.08f);
        SoundSynth.Add(Land, 0f, Wave.Sine, 90, 45, 0.2f, 0.8f, 0.001f, 0.08f);

        Fall = SoundSynth.Tone(Wave.Sine, 1500, 260, 1.1f, 0.45f, 0.01f, 0.2f);
        Gem = new float[(int)(0.17f * R)];
        SoundSynth.Add(Gem, 0f, Wave.Square, 1318, 1318, 0.05f, 0.35f);
        SoundSynth.Add(Gem, 0.05f, Wave.Square, 1976, 1976, 0.12f, 0.35f, decay: 14);
        Wrong = SoundSynth.Tone(Wave.Pulse, 140, 130, 0.35f, 0.5f, 0.005f, 0.05f);

        Drift = new[] { 1046f, 1318f, 1760f }
            .Select(hz => SoundSynth.Tone(Wave.Sine, hz, hz, 0.14f, 0.5f, 0.002f, 0.05f, decay: 12)).ToArray();

        // Gravel rumble and the boost flame: filtered noise with a seamless wrap.
        Rumble = NoiseLoop(0.5f, 0.12f, 22f);
        Jet = NoiseLoop(0.4f, 0.5f, 0f);
    }

    private static float[] NoiseLoop(float seconds, float lowpass, float flutterHz)
    {
        var n = (int)(seconds * R);
        var raw = new float[n + n / 4];
        uint rng = 0x1234567u;
        var s = 0f;
        for (var i = 0; i < raw.Length; i++)
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            var white = (rng & 0xFFFF) / 32767.5f - 1f;
            s += (white - s) * lowpass;
            var am = flutterHz > 0 ? 0.6f + 0.4f * MathF.Sin(MathF.Tau * flutterHz * i / R) : 1f;
            raw[i] = s * am;
        }
        return WavClip.Prepare(raw, R, 0.25f, true);
    }
}

/// <summary>
///     Race audio through <see cref="ICanvas.Audio"/>. Slots: 0–2 engine bands, 3 skid, 4 nearest rival,
///     5 gravel, 6 boost flame. Engine pitch follows speed relative to the kart's top speed.
/// </summary>
internal sealed class KartAudio(ICanvas canvas)
{
    private readonly GameSound _music = new(canvas, GameSong.Racing);

    /// <summary>Background music: full in the menus, under the engine while racing.</summary>
    public void Music(Game game, bool on, float level, float musicLevel)
    {
        _music.Music = on && level > 0;
        _music.Volume = level;
        _music.MusicVolume = musicLevel;
        var racing = game.Race != null && game.Screen is Screen.Race or Screen.Paused or Screen.Demo;
        _music.Duck(racing ? 0.45f : 1f);
        _music.Update();
    }

    private const int R = SoundSynth.Rate;
    private RacePhase _phase;
    private int _beep = -1;
    private int _lap;
    private float _spin, _boost, _spd, _bump, _fall, _rollTick;
    private bool _drifting, _finished, _air, _shield, _inRace, _heard, _missing, _wrong;
    private int _driftLevel, _gems;
    private ItemKind _item;
    private float _rival;

    public void Update(Game game, bool sound, float level, bool clicked)
    {
        var audio = canvas.Audio;
        level = Math.Clamp(level, 0f, 1f);
        if (audio == null || !sound || level <= 0f)
        {
            if (sound && audio == null && !_missing)
            {
                _missing = true;
                Console.WriteLine("[TurboKart] sound is on, but this canvas has no audio output");
            }
            Silence(audio);
            _inRace = false;
            _heard = false;
            return;
        }

        RaceSounds.Ensure();
        if (!_heard)
        {
            _heard = true;
            Clip(audio, RaceSounds.Click, RaceSounds.SfxRate, 0.5f * level);
        }

        if (clicked && game.Screen is Screen.Title or Screen.CupSelect or Screen.TrackSelect or Screen.Paused)
            Clip(audio, RaceSounds.Click, RaceSounds.SfxRate, 0.45f * level, 1.1f);

        var race = game.Race;
        var player = race?.Player;
        var showRace = player != null &&
                       game.Screen is not (Screen.Boot or Screen.Loading or Screen.Podium);
        if (!showRace || race == null || player == null)
        {
            if (_inRace) Silence(audio);
            _inRace = false;
            _beep = -1;
            return;
        }

        if (!_inRace)
        {
            _inRace = true;
            Remember(player);
            _beep = -1;
        }

        var replay = race.Replaying;
        if (race.Phase == RacePhase.Countdown)
        {
            var step = (int)race.PhaseTime;
            if (step != _beep && step is >= 0 and <= 2)
            {
                _beep = step;
                Synth(audio, RaceSounds.Beep, 0.6f * level);
            }
        }
        else if (_phase == RacePhase.Countdown && race.Phase == RacePhase.Racing)
        {
            Synth(audio, RaceSounds.Go, 0.65f * level);
        }

        var paused = game.Screen == Screen.Paused;
        if (race.Phase is RacePhase.Racing or RacePhase.Finished)
            Engine(audio, race, player, level * (replay ? 0.6f : 1f), paused);
        else
            StopEngine(audio);

        if (!paused && !replay)
            Effects(audio, race, player, level);

        _phase = race.Phase;
        Remember(player);
    }

    private void Remember(Kart p)
    {
        _lap = p.Lap;
        _spin = p.Spin;
        _boost = p.Boost;
        _drifting = p.Drifting;
        _driftLevel = p.DriftLevel;
        _finished = p.Finished;
        _item = p.Item;
        _bump = p.Bump;
        _air = p.Airborne;
        _fall = p.Fall;
        _shield = p.Shield > 0;
        _gems = p.Gems;
        _wrong = p.WrongWay > 0.5f;
    }

    private void Effects(IExtensionAudio audio, Race race, Kart p, float level)
    {
        if (p.Boost > 0.2f && _boost <= 0.2f)
        {
            Clip(audio, RaceSounds.Boost, RaceSounds.SfxRate, 0.55f * level);
        }
        if (p.Spin > 0.35f && _spin <= 0.35f)
        {
            Clip(audio, RaceSounds.Hit, RaceSounds.SfxRate, 0.7f * level);
            Synth(audio, RaceSounds.Spin, 0.45f * level);
        }
        if (p.Bump > 0 && _bump <= 0 && p.Spin <= 0) Synth(audio, RaceSounds.Bump, 0.6f * level);
        if (p.Airborne && !_air) Synth(audio, RaceSounds.Jump, 0.4f * level);
        if (!p.Airborne && _air) Synth(audio, RaceSounds.Land, 0.6f * level);
        if (p.Fall > 0 && _fall <= 0) Synth(audio, RaceSounds.Fall, 0.5f * level);
        if (p.Shield > 0 && !_shield) Synth(audio, RaceSounds.Shield, 0.45f * level);
        if (p.Gems > _gems) Synth(audio, RaceSounds.Gem, 0.35f * level);
        if (p.WrongWay > 0.5f && !_wrong) Synth(audio, RaceSounds.Wrong, 0.45f * level);

        if (p.DriftLevel > _driftLevel && p.DriftLevel is >= 1 and <= 3)
            Synth(audio, RaceSounds.Drift[p.DriftLevel - 1], 0.4f * level);

        if (p.Lap > _lap && p.Lap >= 2 && !p.Finished)
            Synth(audio, p.Lap == race.Laps ? RaceSounds.FinalLap : RaceSounds.Lap, 0.5f * level);
        if (p.Finished && !_finished) Synth(audio, RaceSounds.Finish, 0.55f * level);

        // Item roulette ticks while it spins, a chime when it lands, a cue per item when it is used.
        if (p.RollTime > 0)
        {
            _rollTick -= 1 / 30f;
            if (_rollTick <= 0)
            {
                _rollTick = 0.075f;
                Synth(audio, RaceSounds.Roll, 0.3f * level, 0.9f + p.RollTime * 0.2f);
            }
        }
        if (p.Item != ItemKind.None && _item == ItemKind.None) Synth(audio, RaceSounds.Got, 0.4f * level);
        if (p.Item == ItemKind.None && _item != ItemKind.None)
        {
            switch (_item)
            {
                case ItemKind.Oil: Synth(audio, RaceSounds.Oil, 0.55f * level); break;
                case ItemKind.Orb: Synth(audio, RaceSounds.Orb, 0.45f * level); break;
                // Nitro is the boost clip (Boost rises); Shield has its own cue above.
            }
        }
    }

    public void Silence()
    {
        Silence(canvas.Audio);
        _music.Stop();
    }

    private void Engine(IExtensionAudio audio, Race race, Kart player, float level, bool paused)
    {
        // Pitch follows speed against this kart's own top speed; boost pushes past the top.
        var top = MathF.Max(60f, race.MaxSpeed(player));
        _spd += (player.Speed - _spd) * 0.25f;
        var t = Math.Clamp(_spd / top, 0f, 1.3f);
        var hz = 38f + 92f * MathF.Pow(Math.Min(t, 1.3f), 0.85f);
        if (!player.Throttle && t > 0.2f) hz *= 0.93f;

        if (RaceSounds.Ready)
        {
            var wLow = 1 - Smooth(0.12f, 0.42f, t);
            var wHigh = Smooth(0.5f, 0.82f, t);
            var wMid = Math.Max(0, 1 - wLow - wHigh);
            // The engine is the bed of the race: clearly louder than before, effects sit on top of it.
            var gain = (0.62f + 0.18f * Math.Min(t, 1f) + (player.Throttle ? 0.08f : 0f)) * level;
            if (player.Finished) gain *= 0.45f;
            if (paused) gain *= 0.3f;
            // Equal-power crossfade between the three recordings.
            Band(audio, 0, RaceSounds.Low!, hz / RaceSounds.LowHz, gain * MathF.Sqrt(wLow));
            Band(audio, 1, RaceSounds.Mid!, hz / RaceSounds.MidHz, gain * MathF.Sqrt(wMid));
            Band(audio, 2, RaceSounds.High!, hz / RaceSounds.HighHz, gain * MathF.Sqrt(wHigh));
        }
        else
        {
            var synth = (player.Finished ? 0.12f : 0.3f + 0.15f * Math.Min(t, 1f)) * level * (paused ? 0.3f : 1f);
            audio.Loop(0, hz * 1.5f, synth);
        }

        var calm = paused || race.Replaying ? 0f : 1f;

        // Tyres.
        if (player.Drifting && RaceSounds.Skid != null && !player.Airborne)
            audio.LoopSample(3, RaceSounds.Skid, RaceSounds.SkidRate, (0.2f + 0.05f * player.DriftLevel) * level * calm,
                0.95f + 0.07f * player.DriftLevel);
        else audio.StopLoop(3);

        // Gravel.
        if (player.Offroad && !player.Airborne && _spd > 15)
            audio.LoopSample(5, RaceSounds.Rumble, R, Math.Min(1f, _spd / 120f) * 0.4f * level * calm,
                0.8f + Math.Min(t, 1f) * 0.5f);
        else audio.StopLoop(5);

        // Boost flame.
        if (player.Boost > 0.05f)
            audio.LoopSample(6, RaceSounds.Jet, R, Math.Min(1f, player.Boost) * 0.28f * level * calm, 1.1f);
        else audio.StopLoop(6);

        Rival(audio, race, player, level * calm);
    }

    /// <summary>The nearest CPU kart within earshot: its engine, louder as it nears, pitched by the Doppler shift.</summary>
    private void Rival(IExtensionAudio audio, Race race, Kart player, float level)
    {
        if (RaceSounds.Mid == null) return;
        Kart? near = null;
        var best = 110f;
        foreach (var k in race.Karts)
        {
            if (k == player || k.Fall > 0) continue;
            var d = (k.Pos - player.Pos).Length();
            if (d < best)
            {
                best = d;
                near = k;
            }
        }
        var target = 0f;
        var rate = 1f;
        if (near != null)
        {
            var closeness = 1 - best / 110f;
            target = closeness * closeness * 0.32f * level;
            var toMe = player.Pos - near.Pos;
            var len = MathF.Max(1, toMe.Length());
            var dir = toMe / len;
            var vRel = System.Numerics.Vector2.Dot(near.Dir * near.Speed - player.Dir * player.Speed, dir);
            var doppler = Math.Clamp(1 + vRel / 500f, 0.8f, 1.25f);
            var top = MathF.Max(60f, race.MaxSpeed(near));
            var hz = 38f + 92f * MathF.Pow(Math.Clamp(near.Speed / top, 0, 1.3f), 0.85f);
            rate = hz / RaceSounds.MidHz * doppler * 1.06f;
        }
        _rival += (target - _rival) * 0.3f;
        if (_rival > 0.004f) audio.LoopSample(4, RaceSounds.Mid, RaceSounds.EngineRate, _rival, Math.Clamp(rate, 0.4f, 2.8f));
        else audio.StopLoop(4);
    }

    private static void Band(IExtensionAudio audio, int slot, float[] clip, float rate, float gain)
    {
        if (gain < 0.003f) audio.StopLoop(slot);
        else audio.LoopSample(slot, clip, RaceSounds.EngineRate, gain, Math.Clamp(rate, 0.45f, 2.8f));
    }

    private static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private static void Synth(IExtensionAudio audio, float[] clip, float gain, float rate = 1f)
    {
        if (clip.Length > 1) audio.Play(clip, R, gain * 0.8f, 0, rate);
    }

    private static void Clip(IExtensionAudio audio, float[]? clip, int rate, float gain, float playback = 1f)
    {
        if (clip != null) audio.Play(clip, rate, gain * 0.8f, 0, playback);
    }

    private static void StopEngine(IExtensionAudio audio)
    {
        for (var i = 0; i <= 6; i++) audio.StopLoop(i);
    }

    private static void Silence(IExtensionAudio? audio)
    {
        if (audio == null) return;
        StopEngine(audio);
    }
}
