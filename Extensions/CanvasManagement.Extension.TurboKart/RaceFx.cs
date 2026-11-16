using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

internal enum WeatherKind { Clear, Rain, Snow, Embers }

internal struct Skid
{
    public Vector2 Pos;
    public float Age, Size;
    public int Color;
}

internal struct KartSnap
{
    public Vector2 Pos;
    public float Heading, MoveAngle, Z, SpinAngle, Boost, Steer, DriftCharge, Speed;
    public bool Drifting;
    public int DriftDir;
}

/// <summary>Weather, day/night, skid marks, ramps, camera shake, finish replay and ambient effects.</summary>
internal sealed partial class Race
{
    public const float RampHeight = 4f;
    public const float SkidLife = 10f;

    public TurboKartWeather WeatherSetting = TurboKartWeather.Auto;
    public bool DayCycle = true;
    public bool ReplayEnabled = true;

    public WeatherKind Weather;
    public float Darkness;
    public float Shake, ShakeX, ShakeY, CamPitch;
    public float Lightning;

    public readonly Skid[] Skids = new Skid[2400];
    private int _skidHead;

    public bool Replaying;
    private float _replayT, _crossClock;
    private Vector2 _replayCam;
    private KartSnap[]? _live;
    private readonly List<(float T, KartSnap[] S)> _snaps = new();
    private float _fireworkTimer, _lavaTimer, _lightningTimer;

    private float WeatherGrip => Weather switch { WeatherKind.Rain => 0.72f, WeatherKind.Snow => 0.85f, _ => 1f };

    public bool CycleTheme => T.Theme.Kind is ThemeKind.Stadium or ThemeKind.Beach or ThemeKind.Canyon
        or ThemeKind.Autumn or ThemeKind.Snow;

    /// <summary>How strongly lamps / headlights should glow (city is always night).</summary>
    public float NightLight => MathF.Max(Darkness, T.Theme.Kind switch
    {
        ThemeKind.City => 0.85f,
        ThemeKind.Lava => 0.45f,
        ThemeKind.Space => 0.6f,
        _ => 0f
    });

    private void ResetFx()
    {
        Weather = PickWeather();
        Darkness = 0;
        Shake = ShakeX = ShakeY = CamPitch = Lightning = 0;
        Array.Clear(Skids);
        _skidHead = 0;
        Replaying = false;
        _live = null;
        _snaps.Clear();
        _crossClock = 0;
        _fireworkTimer = _lavaTimer = 0;
        _lightningTimer = 6f + (float)_rnd.NextDouble() * 8f;
    }

    private WeatherKind PickWeather()
    {
        var kind = T.Theme.Kind;
        switch (WeatherSetting)
        {
            case TurboKartWeather.Clear:
                return WeatherKind.Clear;
            case TurboKartWeather.Rain:
                return kind == ThemeKind.Space ? WeatherKind.Clear : kind == ThemeKind.Lava ? WeatherKind.Embers : WeatherKind.Rain;
            case TurboKartWeather.Snow:
                return kind == ThemeKind.Space ? WeatherKind.Clear : kind == ThemeKind.Lava ? WeatherKind.Embers : WeatherKind.Snow;
        }

        var r = _rnd.NextDouble();
        return kind switch
        {
            ThemeKind.Stadium => r < 0.3 ? WeatherKind.Rain : WeatherKind.Clear,
            ThemeKind.Autumn => r < 0.45 ? WeatherKind.Rain : WeatherKind.Clear,
            ThemeKind.City => r < 0.55 ? WeatherKind.Rain : WeatherKind.Clear,
            ThemeKind.Snow => r < 0.85 ? WeatherKind.Snow : WeatherKind.Clear,
            ThemeKind.Lava => WeatherKind.Embers,
            _ => WeatherKind.Clear
        };
    }

    // ------------------------------------------------------------------ per-frame

    private void UpdateFx(float dt)
    {
        var target = 0f;
        if (DayCycle && CycleTheme && Phase != RacePhase.Intro && Phase != RacePhase.Countdown)
        {
            var p = Math.Clamp((Player.Lap - 1 + Player.Idx / (float)T.Count) / Laps, 0f, 1f);
            if (Player.Finished) p = 1f;
            var t = Math.Clamp((p - 0.12f) / 0.88f, 0f, 1f);
            target = t * t * (3 - 2 * t) * 0.92f;
        }

        Darkness += Math.Clamp(target - Darkness, -0.15f * dt, 0.15f * dt);

        Shake *= MathF.Exp(-7f * dt);
        if (Shake < 0.05f) Shake = 0;
        ShakeX = ((float)_rnd.NextDouble() * 2 - 1) * Shake;
        ShakeY = ((float)_rnd.NextDouble() * 2 - 1) * Shake;
        var pitch = Player.Airborne ? Math.Clamp(Player.Vz * 0.045f, -4f, 5f) : 0f;
        CamPitch += (pitch - CamPitch) * (1 - MathF.Exp(-8f * dt));

        for (var i = 0; i < Skids.Length; i++)
            if (Skids[i].Size > 0) Skids[i].Age += dt;

        if (Lightning > 0) Lightning = MathF.Max(0, Lightning - dt * 2.5f);
        if (Weather == WeatherKind.Rain)
        {
            _lightningTimer -= dt;
            if (_lightningTimer <= 0)
            {
                Lightning = 1f;
                _lightningTimer = 7f + (float)_rnd.NextDouble() * 12f;
            }
        }

        if (Phase is RacePhase.Finished or RacePhase.Results && Player.Finished) Fireworks(dt);
        if (T.Theme.Kind == ThemeKind.Lava && Phase != RacePhase.Intro) LavaBursts(dt);
    }

    // ------------------------------------------------------------------ skid marks

    private void AddSkid(Vector2 p, int color, float size)
    {
        Skids[_skidHead] = new Skid { Pos = p, Color = color, Size = size, Age = 0 };
        _skidHead = (_skidHead + 1) % Skids.Length;
    }

    private int RutColor => T.Theme.Kind switch
    {
        ThemeKind.Beach => Gfx.Rgb(176, 148, 100),
        ThemeKind.Canyon => Gfx.Rgb(128, 64, 36),
        ThemeKind.Autumn => Gfx.Rgb(86, 104, 40),
        ThemeKind.Snow => Gfx.Rgb(150, 170, 214),
        ThemeKind.Stadium => Gfx.Rgb(40, 96, 34),
        _ => 0
    };

    private void EmitSkids(Kart k)
    {
        if (k.Z > 0.3f || k.Fall > 0) return;
        var rearL = k.Pos - k.Dir * 2.6f - k.Right * 3f;
        var rearR = k.Pos - k.Dir * 2.6f + k.Right * 3f;
        var road = Gfx.Rgb(26, 26, 32);
        if (k.Drifting || (k.Brake && k.Speed > 90f) || k.Spin > 0)
        {
            AddSkid(rearL, road, 1.1f);
            AddSkid(rearR, road, 1.1f);
        }
        else if (k.Offroad && MathF.Abs(k.Speed) > 40f && RutColor != 0 && ((int)(Clock * 30) & 1) == 0)
        {
            AddSkid(rearL, RutColor, 1.3f);
            AddSkid(rearR, RutColor, 1.3f);
        }

        if (Weather == WeatherKind.Rain && MathF.Abs(k.Speed) > 60f && !k.Offroad && Particles.Count < 800)
            Particles.Add(new Particle
            {
                Pos = new Vector3((((int)(Clock * 60) & 1) == 0 ? rearL : rearR) - k.Dir * 1.5f, 0.8f),
                Vel = new Vector3(-k.Dir * 22 + Rand2(10), 18), Gravity = 70, Life = 0.4f, MaxLife = 0.4f,
                Size = 1.4f, Color = Gfx.Rgb(210, 220, 240), Color2 = Gfx.Rgb(150, 170, 200), Smoke = true
            });
    }

    // ------------------------------------------------------------------ ramps

    private void RampPhysics(Kart k)
    {
        if (k.Fall > 0) return;
        var on = false;
        foreach (var r in T.Ramps)
        {
            var di = T.Wrap(k.Idx - r);
            if (di > Track.RampSamples + 1 || MathF.Abs(k.Lat) > Track.HalfWidth + 1) continue;
            var along = Vector2.Dot(k.Pos - T.P[r], T.T[r]) / (Track.RampSamples * Track.Step);
            if (along < 0 || along > 1) continue;
            var surface = along * RampHeight;
            if (k.Vz <= 0 && k.Z <= surface + 0.6f)
            {
                k.Z = surface;
                k.Vz = 0;
                on = true;
            }
        }

        if (k.OnRamp && !on && k.Speed > 50f)
        {
            k.Vz = 34f + k.Speed * 0.38f;
            k.Z = MathF.Max(k.Z, RampHeight);
            k.Airborne = true;
            k.Drifting = false;
        }

        k.OnRamp = on;
    }

    private void Land(Kart k, float vz)
    {
        k.Airborne = false;
        var hard = Math.Clamp(-vz / 40f, 1f, 4f);
        if (k.IsPlayer) Shake = MathF.Max(Shake, hard);
        AddSkid(k.Pos - k.Right * 3f, Gfx.Rgb(26, 26, 32), 1.4f);
        AddSkid(k.Pos + k.Right * 3f, Gfx.Rgb(26, 26, 32), 1.4f);
        for (var i = 0; i < 8 && Particles.Count < 900; i++)
            Particles.Add(new Particle
            {
                Pos = new Vector3(k.Pos + Rand2(3), 0.5f), Vel = new Vector3(Rand2(22), 10), Life = 0.6f,
                MaxLife = 0.6f, Size = 2f, Color = Gfx.Rgb(220, 214, 200), Color2 = Gfx.Rgb(150, 144, 130),
                Smoke = true
            });
        if (k.Speed > 100f) k.Boost = MathF.Max(k.Boost, 0.45f);
    }

    // ------------------------------------------------------------------ ambient bursts

    private void Fireworks(float dt)
    {
        _fireworkTimer -= dt;
        if (_fireworkTimer > 0) return;
        _fireworkTimer = 0.3f + (float)_rnd.NextDouble() * 0.6f;
        var at = T.P[0] + T.N[0] * ((float)_rnd.NextDouble() * 100 - 50) + T.T[0] * (20 + (float)_rnd.NextDouble() * 80);
        var hue = _rnd.Next(360);
        var pos = new Vector3(at, 40 + (float)_rnd.NextDouble() * 35);
        for (var i = 0; i < 36 && Particles.Count < 1000; i++)
        {
            var a = i / 36f * MathF.Tau;
            var e = (float)_rnd.NextDouble() * 0.8f - 0.4f;
            var v = 38f + (float)_rnd.NextDouble() * 14f;
            Particles.Add(new Particle
            {
                Pos = pos,
                Vel = new Vector3(MathF.Cos(a) * v * MathF.Cos(e), MathF.Sin(a) * v * MathF.Cos(e), MathF.Sin(e) * v + 8),
                Gravity = 30, Life = 1.2f, MaxLife = 1.2f, Size = 2.2f, Glow = true,
                Color = Gfx.Hsv(hue, 0.35f, 1f), Color2 = Gfx.Hsv(hue + 30, 0.9f, 0.9f)
            });
        }
    }

    private void LavaBursts(float dt)
    {
        _lavaTimer -= dt;
        if (_lavaTimer > 0) return;
        _lavaTimer = 0.5f + (float)_rnd.NextDouble() * 1.1f;
        var idx = T.Wrap(Player.Idx + 12 + _rnd.Next(70));
        var side = _rnd.Next(2) == 0 ? -1f : 1f;
        var at = T.At(idx, side * (T.HazardDist + 12 + (float)_rnd.NextDouble() * 60));
        for (var i = 0; i < 22 && Particles.Count < 1000; i++)
            Particles.Add(new Particle
            {
                Pos = new Vector3(at, 0.5f),
                Vel = new Vector3(Rand2(18), 60 + (float)_rnd.NextDouble() * 60),
                Gravity = 150, Life = 1.3f, MaxLife = 1.3f, Size = 2.2f, Glow = true,
                Color = Gfx.Rgb(255, 236, 140), Color2 = Gfx.Rgb(220, 50, 10)
            });
    }

    // ------------------------------------------------------------------ finish replay

    private KartSnap Snap(Kart k) => new()
    {
        Pos = k.Pos, Heading = k.Heading, MoveAngle = k.MoveAngle, Z = k.Z, SpinAngle = k.SpinAngle,
        Boost = k.Boost, Steer = k.Steer, DriftCharge = k.DriftCharge, Speed = k.Speed, Drifting = k.Drifting,
        DriftDir = k.DriftDir
    };

    private static void Apply(Kart k, KartSnap s)
    {
        k.Pos = s.Pos;
        k.Heading = s.Heading;
        k.MoveAngle = s.MoveAngle;
        k.Z = s.Z;
        k.SpinAngle = s.SpinAngle;
        k.Boost = s.Boost;
        k.Steer = s.Steer;
        k.DriftCharge = s.DriftCharge;
        k.Speed = s.Speed;
        k.Drifting = s.Drifting;
        k.DriftDir = s.DriftDir;
    }

    private void Record()
    {
        if (!ReplayEnabled || Phase is not (RacePhase.Racing or RacePhase.Finished)) return;
        var frame = new KartSnap[Karts.Count];
        for (var i = 0; i < Karts.Count; i++) frame[i] = Snap(Karts[i]);
        _snaps.Add((Clock, frame));
        while (_snaps.Count > 0 && _snaps[0].T < Clock - 7f) _snaps.RemoveAt(0);
        if (Player.Finished && _crossClock <= 0) _crossClock = Clock;
    }

    private bool StartReplay()
    {
        if (_snaps.Count < 30 || _crossClock <= 0) return false;
        _live = Karts.Select(Snap).ToArray();
        Replaying = true;
        _replayT = MathF.Max(_snaps[0].T, _crossClock - 2.4f);
        Particles.Clear();
        var c = T.Wrap(6);
        _replayCam = T.At(c, -(Track.HalfWidth + 22f));
        Banner = "";
        BannerTime = 0;
        return true;
    }

    private void UpdateReplay(float dt)
    {
        Clock += dt;
        _replayT += dt * 0.65f;
        var end = MathF.Min(_crossClock + 1.1f, _snaps[^1].T);
        if (_replayT >= end)
        {
            EndReplay();
            return;
        }

        var j = 1;
        while (j < _snaps.Count - 1 && _snaps[j].T < _replayT) j++;
        var a = _snaps[j - 1];
        var b = _snaps[j];
        var t = Math.Clamp((_replayT - a.T) / MathF.Max(1e-4f, b.T - a.T), 0f, 1f);
        for (var i = 0; i < Karts.Count && i < a.S.Length; i++)
        {
            var s = a.S[i];
            var e = b.S[i];
            s.Pos = Vector2.Lerp(s.Pos, e.Pos, t);
            s.Heading = Gfx.LerpAngle(s.Heading, e.Heading, t);
            s.MoveAngle = Gfx.LerpAngle(s.MoveAngle, e.MoveAngle, t);
            s.Z += (e.Z - s.Z) * t;
            Apply(Karts[i], s);
            EmitKartParticles(Karts[i], dt * 0.65f);
        }

        UpdateParticles(dt * 0.65f);
        Fireworks(dt);
        CamPos = _replayCam;
        CamYaw = MathF.Atan2(Player.Pos.Y - CamPos.Y, Player.Pos.X - CamPos.X);
        CamH = 7f;
        CamPitch = 0;
        ShakeX = ShakeY = 0;
    }

    private void EndReplay()
    {
        if (_live != null)
            for (var i = 0; i < Karts.Count && i < _live.Length; i++) Apply(Karts[i], _live[i]);
        _live = null;
        Replaying = false;
        Phase = RacePhase.Results;
        PhaseTime = 0;
        CamH = CamHeight;
        UpdateCamera(0, true);
    }
}
