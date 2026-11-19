using CanvasManagement.Interfaces;

namespace CanvasManagement.Extension.TurboKart;

/// <summary>Recorded clips. The host only sees float buffers, so any extension can bring its own sounds.</summary>
internal static class RaceSounds
{
    public static float[]? Low, Mid, High, Skid, Hit, Boost, Click, Tick, Go, Lap, Item, Finish;
    public static int EngineRate = 44100, SkidRate = 32000, SfxRate = 44100;
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
            if (low == null || mid == null || high == null) return;
            Low = low.Value.Mono;
            Mid = mid.Value.Mono;
            High = high.Value.Mono;
            EngineRate = low.Value.Rate;
            var skid = WavClip.FromResource("skid.wav");
            if (skid != null) { Skid = skid.Value.Mono; SkidRate = skid.Value.Rate; }
            Hit = One("hit.wav");
            Boost = One("boost.wav");
            Click = One("click.wav");
            Tick = One("tick.wav");
            Go = One("go.wav");
            Lap = One("lap.wav");
            Item = One("item.wav");
            Finish = One("finish.wav");
            SfxRate = 44100;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TurboKart] sounds: " + ex.Message);
        }
    }

    private static float[]? One(string name) => WavClip.FromResource(name)?.Mono;
}

/// <summary>
///     Race audio through <see cref="ICanvas.Audio"/>. Engine speed crossfades three recorded loops.
///     One-shots are the countdown, boost, hit, item, lap and finish clips.
/// </summary>
internal sealed class KartAudio(ICanvas canvas)
{
    private RacePhase _phase;
    private int _beep = -1;
    private int _lap;
    private float _spin;
    private float _boost;
    private float _spd;
    private bool _drifting;
    private bool _finished;
    private ItemKind _item;
    private bool _inRace;
    private bool _heard;
    private bool _missing;

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
            Shot(audio, RaceSounds.Click, 0.45f * level, 1f, 880, 0.12f);
        }

        if (clicked && game.Screen is Screen.Title or Screen.CupSelect or Screen.TrackSelect or Screen.Paused)
            Shot(audio, RaceSounds.Click, 0.35f * level, 1.1f, 660, 0.05f);

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
            _lap = player.Lap;
            _finished = player.Finished;
            _item = player.Item;
            _beep = -1;
        }

        if (race.Phase == RacePhase.Countdown)
        {
            var step = (int)race.PhaseTime;
            if (step != _beep && step is >= 0 and <= 2)
            {
                _beep = step;
                Shot(audio, RaceSounds.Tick, 0.6f * level, 0.9f + step * 0.16f, 392 + step * 98, 0.12f);
            }
        }
        else if (_phase == RacePhase.Countdown && race.Phase == RacePhase.Racing)
        {
            Shot(audio, RaceSounds.Go, 0.7f * level, 1f, 784, 0.18f);
        }

        if (race.Phase is RacePhase.Racing or RacePhase.Finished)
            Engine(audio, player, level, game.Screen == Screen.Paused);
        else
            StopEngine(audio);

        if (player.Drifting && !_drifting && RaceSounds.Skid == null)
            audio.Noise(0.07f, 0.3f * level, 0);
        if (player.Boost > 0.2f && _boost <= 0.2f)
            Shot(audio, RaceSounds.Boost, 0.55f * level, 1f, 494, 0.22f);
        if (player.Spin > 0.35f && _spin <= 0.35f)
        {
            Shot(audio, RaceSounds.Hit, 0.75f * level, 1f, 130, 0.1f);
            if (RaceSounds.Hit == null) audio.Noise(0.16f, 0.55f * level, 0);
        }
        if (player.Lap > _lap && player.Lap >= 2)
            Shot(audio, RaceSounds.Lap, 0.5f * level, 1f, 698, 0.09f);
        if (player.Finished && !_finished)
            Shot(audio, RaceSounds.Finish, 0.65f * level, 1f, 523, 0.35f);
        if (player.Item == ItemKind.None && _item != ItemKind.None)
            Shot(audio, RaceSounds.Item, 0.5f * level, 1f, 988, 0.07f);

        _phase = race.Phase;
        _lap = player.Lap;
        _spin = player.Spin;
        _boost = player.Boost;
        _drifting = player.Drifting;
        _finished = player.Finished;
        _item = player.Item;
    }

    public void Silence() => Silence(canvas.Audio);

    private void Engine(IExtensionAudio audio, Kart player, float level, bool paused)
    {
        _spd += (player.Speed - _spd) * 0.18f;
        if (RaceSounds.Ready)
        {
            var t = Math.Clamp(_spd / 72f, 0f, 1f);
            var low = Band(t, 0f);
            var mid = Band(t, 0.5f);
            var high = Band(t, 1f);
            var sum = low + mid + high;
            if (sum > 0.001f) { low /= sum; mid /= sum; high /= sum; }
            var gain = (0.28f + Math.Clamp(_spd / 150f, 0f, 0.18f) + (player.Throttle ? 0.06f : 0f)) * level;
            if (player.Finished) gain *= 0.4f;
            if (paused) gain *= 0.35f;
            var rate = 0.96f + t * 0.12f;
            audio.LoopSample(0, RaceSounds.Low!, RaceSounds.EngineRate, gain * low, rate);
            audio.LoopSample(1, RaceSounds.Mid!, RaceSounds.EngineRate, gain * mid, rate);
            audio.LoopSample(2, RaceSounds.High!, RaceSounds.EngineRate, gain * high, rate);
            if (player.Drifting && RaceSounds.Skid != null)
                audio.LoopSample(3, RaceSounds.Skid, RaceSounds.SkidRate, 0.42f * level, 0.92f + Math.Clamp(player.DriftCharge / 80f, 0f, 0.35f));
            else
                audio.StopLoop(3);
            return;
        }

        var hz = 46f + _spd * 2.1f + (player.Boost > 0.05f ? 36f : 0f);
        var synth = (player.Finished ? 0.12f : 0.34f + Math.Clamp(_spd / 180f, 0f, 0.28f)) * level;
        if (player.Throttle) synth += 0.08f * level;
        if (paused) synth *= 0.35f;
        audio.Loop(0, hz, synth);
        if (player.Drifting) audio.Loop(1, 70f + player.DriftCharge * 6f, 0.28f * level);
        else audio.StopLoop(1);
    }

    private static float Band(float t, float center) => Math.Clamp(1f - MathF.Abs(t - center) / 0.5f, 0f, 1f);

    private static void Shot(IExtensionAudio audio, float[]? clip, float gain, float rate, float hz, float seconds)
    {
        if (clip != null) audio.Play(clip, RaceSounds.SfxRate, gain, 0, rate);
        else audio.Tone(hz, seconds, gain, 0);
    }

    private static void StopEngine(IExtensionAudio audio)
    {
        audio.StopLoop(0);
        audio.StopLoop(1);
        audio.StopLoop(2);
        audio.StopLoop(3);
    }

    private static void Silence(IExtensionAudio? audio)
    {
        if (audio == null) return;
        StopEngine(audio);
    }
}
