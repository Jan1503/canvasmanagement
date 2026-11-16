using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

internal enum ItemKind { None, Nitro, Oil, Orb, Shield }

internal enum RacePhase { Intro, Countdown, Racing, Finished, Results }

internal struct DriveInput
{
    public bool Up, Down, Left, Right, UseItem;
}

internal sealed class Kart
{
    public int Id;
    public int Look;
    public bool IsPlayer;
    public Vector2 Pos;
    public float Heading, MoveAngle, Speed, Z, Vz;
    public float Steer;
    public bool Throttle, Brake;
    public int Idx, Lap;
    public float Lat;
    public bool Offroad;
    public float Boost, Spin, SpinAngle, Shield, Invuln;
    public ItemKind Item = ItemKind.None, Rolling = ItemKind.None;
    public float RollTime;
    public int Gems;
    public bool Drifting;
    public int DriftDir;
    public float DriftCharge;
    public bool Finished;
    public float FinishTime;
    public int Place;
    public float WrongWay;
    public float Fall;
    public bool Human;
    public bool OnRamp, Airborne;
    public float Skill, Seed, LaneOffset, ItemDelay, Bump;

    public Vector2 Dir => new(MathF.Cos(Heading), MathF.Sin(Heading));
    public Vector2 Right => new(-MathF.Sin(Heading), MathF.Cos(Heading));
    public int DriftLevel => !Drifting ? 0 : DriftCharge > 2.6f ? 3 : DriftCharge > 1.6f ? 2 : DriftCharge > 0.75f ? 1 : 0;
}

internal sealed class ItemBox
{
    public Vector2 Pos;
    public float Respawn;
}

internal sealed class GemPickup
{
    public Vector2 Pos;
    public float Respawn;
}

internal sealed class OilSlick
{
    public Vector2 Pos;
    public int Owner;
    public float Life, Age;
}

internal sealed class Orb
{
    public Vector2 Pos;
    public float S, Lat, Speed, Life, Age;
    public int Owner, Target;
}

internal struct Particle
{
    public Vector3 Pos, Vel;
    public float Life, MaxLife, Size, Gravity;
    public int Color, Color2;
    public bool Glow;
    public bool Smoke;
}

internal sealed partial class Race
{
    public const float BaseMax = 205f;
    public const float CamDist = 46f;
    public const float CamHeight = 16f;
    private const float KartRadius = 3.6f;

    public readonly Track T;
    public readonly List<Kart> Karts = new();
    public readonly List<ItemBox> Boxes = new();
    public readonly List<GemPickup> GemPickups = new();
    public readonly List<OilSlick> Oils = new();
    public readonly List<Orb> Orbs = new();
    public readonly List<Particle> Particles = new();
    private readonly Random _rnd = new();

    public Kart Player = null!;
    public RacePhase Phase;
    public float PhaseTime, RaceTime, Clock;
    public int Laps = 3;
    public string Banner = "";
    public float BannerTime;
    public bool PlayerWrongWay;
    private float _timeUp;

    public Vector2 CamPos;
    public float CamYaw, CamH = CamHeight, CamBack = CamDist;

    public Race(Track track) => T = track;

    /// <param name="gridLooks">Racer looks in grid-slot order (0 = player); null = default grid.</param>
    public void Reset(int laps, int aiCount, float difficulty, IReadOnlyList<int>? gridLooks = null)
    {
        Laps = Math.Clamp(laps, 1, 9);
        Karts.Clear();
        Oils.Clear();
        Orbs.Clear();
        Particles.Clear();
        Boxes.Clear();
        GemPickups.Clear();
        foreach (var b in T.ItemBoxes) Boxes.Add(new ItemBox { Pos = b });
        foreach (var g in T.Gems) GemPickups.Add(new GemPickup { Pos = g });

        var count = Math.Clamp(aiCount, 0, Art.Racers.Length - 1) + 1;
        var looks = gridLooks is { Count: > 0 } ? gridLooks.Take(count).ToList() : DefaultGrid(count);
        count = looks.Count;
        for (var slot = 0; slot < count; slot++)
        {
            var isPlayer = looks[slot] == 0;
            var row = slot / 2;
            var col = slot % 2;
            var s = -14f - row * 20f - col * 9f;
            var idx = T.Wrap((int)MathF.Round(s / Track.Step));
            var lat = col == 0 ? -13f : 13f;
            var dir = T.T[idx];
            var k = new Kart
            {
                Id = slot,
                Look = looks[slot],
                IsPlayer = isPlayer,
                Pos = T.At(idx, lat),
                Heading = MathF.Atan2(dir.Y, dir.X),
                Idx = idx,
                Lap = 0,
                Seed = (float)_rnd.NextDouble() * 100f,
                ItemDelay = 2f
            };
            k.MoveAngle = k.Heading;
            var d = Math.Clamp(difficulty, 1f, 10f);
            k.Skill = isPlayer ? 1f : 0.8f + d * 0.017f + (float)_rnd.NextDouble() * 0.05f;
            Karts.Add(k);
            if (isPlayer) Player = k;
        }

        Phase = RacePhase.Intro;
        PhaseTime = 0;
        RaceTime = 0;
        _timeUp = 0;
        Banner = "";
        BannerTime = 0;
        CamYaw = Player.Heading;
        CamBack = 150f;
        CamH = 60f;
        ResetFx();
        UpdateCamera(0, true);
        UpdatePlaces();
    }

    public static List<int> DefaultGrid(int count)
    {
        var looks = Enumerable.Range(1, count - 1).ToList();
        looks.Insert(Math.Min(count - 1, 5), 0);
        return looks;
    }

    // ------------------------------------------------------------------ main step

    public void Update(float dt, DriveInput input, bool human)
    {
        if (Replaying)
        {
            UpdateReplay(dt);
            return;
        }

        Clock += dt;
        PhaseTime += dt;
        if (BannerTime > 0) BannerTime -= dt;

        switch (Phase)
        {
            case RacePhase.Intro:
                if (PhaseTime > 2.2f) { Phase = RacePhase.Countdown; PhaseTime = 0; }
                break;
            case RacePhase.Countdown:
                if (PhaseTime > 3f)
                {
                    Phase = RacePhase.Racing;
                    PhaseTime = 0;
                    Banner = "GO!";
                    BannerTime = 1.0f;
                }

                break;
            case RacePhase.Finished:
                if (ReplayEnabled && Player.Finished && PhaseTime > 1.6f && StartReplay()) return;
                if (PhaseTime > 4f) { Phase = RacePhase.Results; PhaseTime = 0; }
                break;
            case RacePhase.Racing:
                _timeUp = Karts.All(k => k.IsPlayer || k.Finished) ? _timeUp + dt : 0;
                if (_timeUp > 20f)
                {
                    Phase = RacePhase.Finished;
                    PhaseTime = 0;
                    Banner = "TIME UP";
                    BannerTime = 3.5f;
                }

                break;
        }

        var racing = Phase is RacePhase.Racing or RacePhase.Finished or RacePhase.Results;
        if (racing) RaceTime += dt;

        foreach (var k in Karts)
        {
            k.Human = racing && k.IsPlayer && human && !k.Finished;
            if (racing)
            {
                if (k.Human) HumanControl(k, input, dt);
                else AiControl(k, dt);
            }
            else
            {
                k.Throttle = false;
                k.Brake = false;
                k.Steer = 0;
            }

            Physics(k, dt, racing);
        }

        if (racing)
        {
            KartCollisions();
            UpdateBoxes(dt);
            UpdateGems(dt);
            UpdateOils(dt);
            UpdateOrbs(dt);
        }

        UpdateParticles(dt);
        UpdatePlaces();
        UpdateFx(dt);
        UpdateCamera(dt, false);
        Record();
    }

    // ------------------------------------------------------------------ control

    private void HumanControl(Kart k, DriveInput input, float dt)
    {
        var target = (input.Right ? 1f : 0f) - (input.Left ? 1f : 0f);
        k.Steer = Approach(k.Steer, target, 7f * dt);
        k.Throttle = input.Up;
        k.Brake = input.Down;
        if (input.UseItem) UseItem(k);
    }

    private void AiControl(Kart k, float dt)
    {
        var look = 5 + (int)(MathF.Max(0, k.Speed) * 0.045f);
        var ti = T.Wrap(k.Idx + look);

        var laneTarget = 15f * MathF.Sin(Clock * 0.21f + k.Seed);
        foreach (var o in Karts)
        {
            if (o == k) continue;
            var rel = o.Pos - k.Pos;
            var ahead = Vector2.Dot(rel, T.T[k.Idx]);
            if (ahead is > 0 and < 34 && MathF.Abs(o.Lat - k.LaneOffset) < 8f)
                laneTarget = o.Lat + (o.Lat > 0 ? -13f : 13f);
        }

        foreach (var oil in Oils)
        {
            var rel = oil.Pos - k.Pos;
            var ahead = Vector2.Dot(rel, T.T[k.Idx]);
            var lat = Vector2.Dot(oil.Pos - T.P[k.Idx], T.N[k.Idx]);
            if (ahead is > 0 and < 70 && MathF.Abs(lat - k.LaneOffset) < 10f)
                laneTarget = lat + (lat > 0 ? -15f : 15f);
        }

        if (k.Item == ItemKind.None && k.RollTime <= 0)
        {
            foreach (var b in Boxes)
            {
                if (b.Respawn > 0) continue;
                var rel = b.Pos - k.Pos;
                var ahead = Vector2.Dot(rel, T.T[k.Idx]);
                if (ahead is > 10 and < 90)
                {
                    laneTarget = Vector2.Dot(b.Pos - T.P[k.Idx], T.N[k.Idx]);
                    break;
                }
            }
        }

        var hazard = T.Theme.Hazard;
        var laneMax = hazard ? 13f : 24f;
        laneTarget = Math.Clamp(laneTarget, -laneMax, laneMax);
        k.LaneOffset = Approach(k.LaneOffset, laneTarget, 22f * dt);
        var nearEdge = hazard && MathF.Abs(k.Lat) > 24f;
        if (nearEdge) look = Math.Max(3, look / 2);
        var target = T.At(T.Wrap(k.Idx + look), nearEdge ? 0 : k.LaneOffset);
        var want = MathF.Atan2(target.Y - k.Pos.Y, target.X - k.Pos.X);
        var err = Gfx.WrapAngle(want - k.Heading);
        k.Steer = Math.Clamp(err * 3.2f, -1f, 1f);

        var turn = MathF.Abs(T.TurnAhead(k.Idx, hazard ? 30 : 22));
        var vmax = MaxSpeed(k);
        var targetSpeed = hazard
            ? vmax * (1f - Math.Clamp((turn - 0.3f) * 0.5f, 0f, 0.5f))
            : vmax * (1f - Math.Clamp((turn - 0.45f) * 0.32f, 0f, 0.38f));
        if (nearEdge) targetSpeed = MathF.Min(targetSpeed, vmax * 0.6f);
        var outward = Vector2.Dot(k.Dir, T.N[k.Idx]) * MathF.Sign(k.Lat);
        if (hazard && MathF.Abs(k.Lat) > 18f && outward > 0.25f) targetSpeed = MathF.Min(targetSpeed, vmax * 0.35f);
        if (MathF.Abs(err) > 1.2f) targetSpeed = vmax * 0.4f;
        k.Throttle = k.Speed < targetSpeed;
        k.Brake = k.Speed > targetSpeed + 30f;

        if (!k.IsPlayer)
        {
            var gap = Progress(k) - Progress(Player);
            var band = gap > 120 ? 0.92f : gap < -120 ? 1.07f : 1f;
            k.Skill = Approach(k.Skill, Math.Clamp(k.Skill * band, 0.78f, 1.04f), 0.02f * dt);
        }

        if (k.Item != ItemKind.None)
        {
            k.ItemDelay -= dt;
            if (k.ItemDelay <= 0 && AiWantsItem(k, turn))
            {
                UseItem(k);
                k.ItemDelay = 1.5f + (float)_rnd.NextDouble() * 4f;
            }
        }
    }

    private bool AiWantsItem(Kart k, float turn)
    {
        switch (k.Item)
        {
            case ItemKind.Nitro:
                return MathF.Abs(T.TurnAhead(k.Idx, 70)) < 0.45f && MathF.Abs(k.Lat) < 20f;
            case ItemKind.Shield:
                return true;
            case ItemKind.Orb:
                return Karts.Any(o => o.Place == k.Place - 1) || _rnd.NextDouble() < 0.004;
            case ItemKind.Oil:
                return Karts.Any(o => o != k && Vector2.Dot(o.Pos - k.Pos, k.Dir) is < 0 and > -90 &&
                                      Vector2.Distance(o.Pos, k.Pos) < 90) || _rnd.NextDouble() < 0.01;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ physics

    public float MaxSpeed(Kart k) => BaseMax * k.Skill + k.Gems * 2.4f;

    private void Physics(Kart k, float dt, bool racing)
    {
        var theme = T.Theme;
        if (k.Fall > 0)
        {
            k.Fall -= dt;
            k.Z -= 26f * dt;
            k.Speed *= MathF.Exp(-3f * dt);
            k.Pos += new Vector2(MathF.Cos(k.MoveAngle), MathF.Sin(k.MoveAngle)) * k.Speed * dt;
            if (k.Fall <= 0) Respawn(k);
            return;
        }

        var vmax = MaxSpeed(k);
        if (k.Offroad && k.Boost <= 0) vmax *= theme.OffroadSpeed;
        if (k.Boost > 0)
        {
            vmax = MathF.Max(vmax, BaseMax * 1.42f);
            k.Boost -= dt;
        }

        if (k.Shield > 0) k.Shield -= dt;
        if (k.Invuln > 0) k.Invuln -= dt;
        if (k.Bump > 0) k.Bump -= dt;
        if (k.RollTime > 0)
        {
            k.RollTime -= dt;
            if (k.RollTime <= 0) { k.Item = k.Rolling; k.Rolling = ItemKind.None; }
        }

        var turnRate = 0f;
        if (k.Spin > 0)
        {
            k.Spin -= dt;
            k.SpinAngle += 15f * dt;
            k.Speed = Approach(k.Speed, 0, 320f * dt);
            k.Drifting = false;
            if (k.Spin <= 0) k.SpinAngle = 0;
        }
        else
        {
            if (k.Drifting)
            {
                if (!k.Brake || k.Speed < BaseMax * 0.32f) EndDrift(k);
            }
            else if (k.Human && k.Brake && MathF.Abs(k.Steer) > 0.35f && k.Speed > BaseMax * 0.5f && k.Z <= 0)
            {
                k.Drifting = true;
                k.DriftDir = MathF.Sign(k.Steer) >= 0 ? 1 : -1;
                k.DriftCharge = 0;
                k.Vz = 42f;
            }

            if (k.Drifting)
            {
                var into = k.Steer * k.DriftDir;
                turnRate = k.DriftDir * (1.35f + 1.15f * into);
                k.DriftCharge += dt * (0.8f + 0.6f * MathF.Max(0, into));
                k.Speed = Approach(k.Speed, vmax * 0.96f, 120f * dt);
            }
            else
            {
                if (k.Boost > 0) k.Speed = MathF.Min(vmax, k.Speed + 420f * dt);
                else if (k.Throttle && k.Speed < vmax) k.Speed = MathF.Min(vmax, k.Speed + 150f * (1.15f - k.Speed / vmax) * dt);
                else if (k.Speed > vmax) k.Speed = Approach(k.Speed, vmax, 200f * dt);
                else if (!k.Throttle) k.Speed = Approach(k.Speed, 0, 55f * dt);

                if (k.Brake) k.Speed = MathF.Max(-45f, k.Speed - 250f * dt);

                var grip = Math.Clamp(MathF.Abs(k.Speed) / 70f, 0f, 1f) * (1.1f - 0.35f * MathF.Abs(k.Speed) / BaseMax);
                turnRate = k.Steer * 2.35f * grip * MathF.Sign(k.Speed == 0 ? 1 : k.Speed);
            }
        }

        if (k.Airborne && !k.Drifting) turnRate *= 0.5f;
        k.Heading = Gfx.WrapAngle(k.Heading + turnRate * dt);
        var slip = k.Drifting ? 2.6f : k.Offroad ? 6f : 9f * theme.RoadGrip * WeatherGrip;
        k.MoveAngle = Gfx.WrapAngle(Gfx.LerpAngle(k.MoveAngle, k.Heading, 1 - MathF.Exp(-slip * dt)));
        k.Pos += new Vector2(MathF.Cos(k.MoveAngle), MathF.Sin(k.MoveAngle)) * k.Speed * dt;

        var landingVz = k.Vz;
        k.Vz -= 280f * dt;
        k.Z += k.Vz * dt;
        if (k.Z < 0)
        {
            k.Z = 0;
            k.Vz = 0;
            if (k.Airborne) Land(k, landingVz);
        }

        var prevIdx = k.Idx;
        k.Idx = T.Nearest(k.Pos, k.Idx);
        k.Lat = Vector2.Dot(k.Pos - T.P[k.Idx], T.N[k.Idx]);
        RampPhysics(k);
        if (prevIdx > T.Count * 3 / 4 && k.Idx < T.Count / 4) OnCrossLine(k);
        else if (prevIdx < T.Count / 4 && k.Idx > T.Count * 3 / 4) k.Lap--;

        var absLat = MathF.Abs(k.Lat);
        k.Offroad = absLat > Track.CurbOuter + 1.5f && k.Z <= 0;
        if (theme.Hazard && absLat > T.HazardDist && k.Z <= 0)
        {
            k.Fall = 1.3f;
            k.Drifting = false;
            k.Boost = 0;
            k.Airborne = k.OnRamp = false;
            if (k.IsPlayer) Shake = MathF.Max(Shake, 4f);
            Burst(new Vector3(k.Pos, 1), 14,
                theme.Kind == ThemeKind.Lava ? Gfx.Rgb(255, 200, 80) : Gfx.Rgb(200, 160, 255),
                theme.Kind == ThemeKind.Lava ? Gfx.Rgb(255, 60, 10) : Gfx.Rgb(60, 40, 200), 40, 0.6f, true);
            return;
        }

        var limit = Track.WallDist - KartRadius;
        if (theme.Walls && absLat > limit)
        {
            k.Pos -= T.N[k.Idx] * (absLat - limit) * MathF.Sign(k.Lat);
            k.Lat = MathF.Sign(k.Lat) * limit;
            if (k.Speed > 50f && k.Bump <= 0)
            {
                if (k.IsPlayer) Shake = MathF.Max(Shake, 1.5f + k.Speed / 90f);
                k.Speed *= 0.55f;
                k.Bump = 0.3f;
                Burst(new Vector3(k.Pos + T.N[k.Idx] * MathF.Sign(k.Lat) * 3, 2), 10, Gfx.Rgb(255, 230, 120),
                    Gfx.Rgb(255, 140, 40), 40f, 0.3f, true);
            }

            var trackAngle = MathF.Atan2(T.T[k.Idx].Y, T.T[k.Idx].X);
            k.MoveAngle = Gfx.LerpAngle(k.MoveAngle, trackAngle, 0.35f);
        }

        if (k.Z <= 0 && T.OnPad(k.Idx, k.Lat))
        {
            if (k.Boost < 0.3f) Burst(new Vector3(k.Pos, 1), 8, Gfx.Rgb(255, 240, 120), Gfx.Rgb(255, 120, 20), 30, 0.35f, true);
            k.Boost = MathF.Max(k.Boost, 1.0f);
        }

        var forward = Vector2.Dot(k.Dir, T.T[k.Idx]);
        k.WrongWay = forward < -0.3f && k.Speed > 20 ? k.WrongWay + dt : 0;

        EmitSkids(k);
        EmitKartParticles(k, dt);
    }

    private void Respawn(Kart k)
    {
        var idx = T.Wrap(k.Idx - 2);
        k.Pos = T.At(idx, Math.Clamp(k.Lat, -12f, 12f) * 0.3f);
        k.Idx = idx;
        k.Lat = 0;
        var dir = T.T[idx];
        k.Heading = k.MoveAngle = MathF.Atan2(dir.Y, dir.X);
        k.Speed = 0;
        k.Z = 6f;
        k.Vz = 0;
        k.Invuln = 1.6f;
        k.Spin = 0;
        k.SpinAngle = 0;
    }

    private void EndDrift(Kart k)
    {
        var lvl = k.DriftLevel;
        k.Drifting = false;
        if (lvl > 0)
        {
            k.Boost = MathF.Max(k.Boost, lvl switch { 1 => 0.55f, 2 => 0.95f, _ => 1.4f });
            Burst(new Vector3(k.Pos - k.Dir * 3, 2), 14, DriftColor(lvl), Gfx.Rgb(255, 255, 255), 50, 0.3f, true);
        }

        k.DriftCharge = 0;
    }

    public static int DriftColor(int level) => level switch
    {
        1 => Gfx.Rgb(80, 170, 255),
        2 => Gfx.Rgb(255, 150, 30),
        _ => Gfx.Rgb(220, 90, 255)
    };

    private void OnCrossLine(Kart k)
    {
        k.Lap++;
        if (k.Lap > Laps && !k.Finished)
        {
            k.Finished = true;
            k.FinishTime = RaceTime;
            if (k.IsPlayer)
            {
                Phase = RacePhase.Finished;
                PhaseTime = 0;
                Banner = "FINISH!";
                BannerTime = 3.5f;
            }
        }
        else if (k.IsPlayer && k.Lap >= 2 && k.Lap <= Laps)
        {
            Banner = k.Lap == Laps ? "FINAL LAP!" : $"LAP {k.Lap}/{Laps}";
            BannerTime = 2.2f;
        }
    }

    public float Progress(Kart k) => k.Lap * T.Count + k.Idx;

    private void UpdatePlaces()
    {
        var order = Karts
            .OrderByDescending(k => k.Finished)
            .ThenBy(k => k.Finished ? k.FinishTime : 0)
            .ThenByDescending(Progress)
            .ToList();
        for (var i = 0; i < order.Count; i++) order[i].Place = i + 1;
        PlayerWrongWay = Phase == RacePhase.Racing && Player.WrongWay > 0.8f;
    }

    private void KartCollisions()
    {
        for (var i = 0; i < Karts.Count; i++)
        for (var j = i + 1; j < Karts.Count; j++)
        {
            var a = Karts[i];
            var b = Karts[j];
            if (a.Fall > 0 || b.Fall > 0) continue;
            var d = b.Pos - a.Pos;
            var dist = d.Length();
            if (dist >= KartRadius * 2 || dist < 1e-3f) continue;
            var n = d / dist;
            var push = (KartRadius * 2 - dist) * 0.5f;
            a.Pos -= n * push;
            b.Pos += n * push;
            if (a.Shield > 0 && b.Shield <= 0) Hit(b);
            else if (b.Shield > 0 && a.Shield <= 0) Hit(a);
            else
            {
                var faster = a.Speed > b.Speed ? a : b;
                faster.Speed *= 0.9f;
                if (faster.Bump <= 0)
                {
                    faster.Bump = 0.25f;
                    Burst(new Vector3((a.Pos + b.Pos) / 2, 2), 5, Gfx.Rgb(255, 255, 200), Gfx.Rgb(255, 200, 80), 30, 0.2f, true);
                }
            }
        }
    }

    // ------------------------------------------------------------------ items

    private ItemKind RollItem(Kart k)
    {
        var q = Karts.Count <= 1 ? 0.5f : (k.Place - 1) / (float)(Karts.Count - 1);
        var w = new[] { 1f + 2f * q, 2f - 1.3f * q, 0.6f + 2f * q, 0.8f + 0.8f * q };
        var total = w.Sum();
        var r = (float)_rnd.NextDouble() * total;
        for (var i = 0; i < w.Length; i++)
        {
            if (r < w[i]) return (ItemKind)(i + 1);
            r -= w[i];
        }

        return ItemKind.Nitro;
    }

    private void UseItem(Kart k)
    {
        if (k.Item == ItemKind.None || k.Spin > 0 || k.RollTime > 0) return;
        var item = k.Item;
        k.Item = ItemKind.None;
        switch (item)
        {
            case ItemKind.Nitro:
                k.Boost = MathF.Max(k.Boost, 1.5f);
                Burst(new Vector3(k.Pos - k.Dir * 4, 3), 16, Gfx.Rgb(255, 240, 120), Gfx.Rgb(255, 90, 20), 50, 0.4f, true);
                break;
            case ItemKind.Oil:
                Oils.Add(new OilSlick { Pos = k.Pos - k.Dir * 8, Owner = k.Id, Life = 30 });
                break;
            case ItemKind.Shield:
                k.Shield = 8f;
                break;
            case ItemKind.Orb:
                var target = Karts.FirstOrDefault(o => o.Place == k.Place - 1);
                Orbs.Add(new Orb
                {
                    Pos = k.Pos + k.Dir * 6,
                    S = k.Idx * Track.Step + 6,
                    Lat = k.Lat,
                    Speed = MathF.Max(k.Speed + 140, 330),
                    Life = 7,
                    Owner = k.Id,
                    Target = target?.Id ?? -1
                });
                break;
        }
    }

    private void Hit(Kart k)
    {
        if (k.Invuln > 0 || k.Spin > 0) return;
        if (k.Shield > 0)
        {
            k.Shield = 0;
            k.Invuln = 0.8f;
            Burst(new Vector3(k.Pos, 4), 18, Gfx.Rgb(150, 230, 255), Gfx.Rgb(255, 255, 255), 50, 0.4f, true);
            return;
        }

        k.Spin = 1.3f;
        k.Invuln = 2.2f;
        k.Boost = 0;
        if (k.IsPlayer) Shake = MathF.Max(Shake, 4.5f);
        k.Vz = 70f;
        var lost = Math.Min(3, k.Gems);
        k.Gems -= lost;
        Burst(new Vector3(k.Pos, 4), 10 + lost * 4, Gfx.Rgb(255, 255, 160), Gfx.Rgb(120, 255, 230), 60, 0.6f, true);
    }

    private void UpdateBoxes(float dt)
    {
        foreach (var b in Boxes)
        {
            if (b.Respawn > 0) { b.Respawn -= dt; continue; }
            foreach (var k in Karts)
            {
                if (Vector2.DistanceSquared(k.Pos, b.Pos) > 7f * 7f) continue;
                b.Respawn = 2.5f;
                for (var i = 0; i < 18; i++)
                {
                    var c = Gfx.Hsv(i * 20 + Clock * 200, 0.6f, 1f);
                    Burst(new Vector3(b.Pos, 4), 1, c, Gfx.Rgb(255, 255, 255), 55, 0.55f, true);
                }

                if (k.Item == ItemKind.None && k.RollTime <= 0)
                {
                    k.Rolling = RollItem(k);
                    k.RollTime = k.IsPlayer ? 1.3f : 0.8f;
                    k.ItemDelay = 1f + (float)_rnd.NextDouble() * 3f;
                }

                break;
            }
        }
    }

    private void UpdateGems(float dt)
    {
        foreach (var g in GemPickups)
        {
            if (g.Respawn > 0) { g.Respawn -= dt; continue; }
            foreach (var k in Karts)
            {
                if (Vector2.DistanceSquared(k.Pos, g.Pos) > 5.5f * 5.5f) continue;
                g.Respawn = 9f;
                if (k.Gems < 10) k.Gems++;
                Burst(new Vector3(g.Pos, 2), 6, Gfx.Rgb(160, 255, 240), Gfx.Rgb(255, 255, 255), 30, 0.3f, true);
                break;
            }
        }
    }

    private void UpdateOils(float dt)
    {
        for (var i = Oils.Count - 1; i >= 0; i--)
        {
            var o = Oils[i];
            o.Age += dt;
            if (o.Age > o.Life) { Oils.RemoveAt(i); continue; }
            foreach (var k in Karts)
            {
                if (k.Id == o.Owner && o.Age < 0.8f) continue;
                if (k.Z > 1 || Vector2.DistanceSquared(k.Pos, o.Pos) > 6.5f * 6.5f) continue;
                Hit(k);
                Oils.RemoveAt(i);
                break;
            }
        }
    }

    private void UpdateOrbs(float dt)
    {
        for (var i = Orbs.Count - 1; i >= 0; i--)
        {
            var o = Orbs[i];
            o.Age += dt;
            if (o.Age > o.Life) { Orbs.RemoveAt(i); continue; }
            var target = o.Target >= 0 ? Karts.FirstOrDefault(k => k.Id == o.Target) : null;
            if (target != null && Vector2.Distance(target.Pos, o.Pos) < 60f)
            {
                var d = target.Pos - o.Pos;
                var len = d.Length();
                o.Pos += d / MathF.Max(1e-3f, len) * MathF.Min(len, o.Speed * dt);
                o.S = T.Nearest(o.Pos, (int)(o.S / Track.Step)) * Track.Step;
            }
            else
            {
                o.S += o.Speed * dt;
                var idx = (int)(o.S / Track.Step);
                if (target != null) o.Lat = Approach(o.Lat, target.Lat, 30f * dt);
                o.Pos = T.At(idx, o.Lat);
            }

            if (((int)(o.Age * 60) & 1) == 0)
                Particles.Add(new Particle
                {
                    Pos = new Vector3(o.Pos, 2.5f), Life = 0.35f, MaxLife = 0.35f, Size = 1.6f,
                    Color = Gfx.Rgb(120, 240, 255), Color2 = Gfx.Rgb(30, 60, 200), Glow = true
                });

            foreach (var k in Karts)
            {
                if (k.Id == o.Owner && o.Age < 0.5f) continue;
                if (Vector2.DistanceSquared(k.Pos, o.Pos) > 6f * 6f) continue;
                Hit(k);
                Burst(new Vector3(o.Pos, 3), 16, Gfx.Rgb(160, 250, 255), Gfx.Rgb(40, 90, 255), 70, 0.5f, true);
                Orbs.RemoveAt(i);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ particles

    private void EmitKartParticles(Kart k, float dt)
    {
        if (Particles.Count > 700) return;
        var dir = k.Dir;
        var right = k.Right;
        if (k.Boost > 0)
        {
            for (var s = -1; s <= 1; s += 2)
            for (var n = 0; n < 2; n++)
            {
                var p = k.Pos - dir * 4.6f + right * (s * 1.55f);
                Particles.Add(new Particle
                {
                    Pos = new Vector3(p, 3.0f + (float)_rnd.NextDouble() * 0.6f),
                    Vel = new Vector3(-dir * (30 + (float)_rnd.NextDouble() * 30) + Rand2(8), 4),
                    Life = 0.22f, MaxLife = 0.22f, Size = 1.5f,
                    Color = Gfx.Rgb(255, 250, 180), Color2 = Gfx.Rgb(255, 70, 10), Glow = true
                });
            }
        }

        var lvl = k.DriftLevel;
        if (k.Drifting && k.Z <= 0.5f)
            Particles.Add(new Particle
            {
                Pos = new Vector3(k.Pos - dir * 3f - right * (k.DriftDir * 2f), 0.8f),
                Vel = new Vector3(-dir * 8 + Rand2(5), 7), Life = 0.8f, MaxLife = 0.8f, Size = 1.8f,
                Color = Gfx.Rgb(226, 226, 232), Color2 = Gfx.Rgb(160, 160, 172), Smoke = true
            });
        if (k.Drifting)
        {
            var color = lvl > 0 ? DriftColor(lvl) : Gfx.Rgb(220, 220, 220);
            for (var s = -1; s <= 1; s += 2)
            {
                var p = k.Pos - dir * 2.5f + right * (s * 3.4f);
                Particles.Add(new Particle
                {
                    Pos = new Vector3(p, 0.6f),
                    Vel = new Vector3(-dir * 20 + right * (-k.DriftDir * 15) + Rand2(14), 16 + (float)_rnd.NextDouble() * 18),
                    Gravity = 90, Life = 0.2f, MaxLife = 0.2f, Size = lvl > 0 ? 1.0f : 0.7f,
                    Color = Gfx.Rgb(255, 255, 255), Color2 = color, Glow = lvl > 0
                });
            }
        }

        if (k.Offroad && MathF.Abs(k.Speed) > 30 && _rnd.NextDouble() < 0.7)
        {
            var p = k.Pos - dir * 3f + right * ((float)_rnd.NextDouble() * 6 - 3);
            var sand = MathF.Abs(k.Lat) > Track.BandOuter && T.Curv[k.Idx] * k.Lat < 0 &&
                       MathF.Abs(T.Curv[k.Idx]) > 0.004f;
            Particles.Add(new Particle
            {
                Pos = new Vector3(p, 0.5f), Vel = new Vector3(-dir * 15 + Rand2(10), 14), Gravity = 40,
                Life = 0.5f, MaxLife = 0.5f, Size = 1.3f,
                Color = sand ? Gfx.Rgb(236, 210, 150) : Gfx.Rgb(110, 190, 80),
                Color2 = sand ? Gfx.Rgb(190, 160, 100) : Gfx.Rgb(60, 120, 50)
            });
        }

        if (k.Spin > 0 && _rnd.NextDouble() < 0.5)
        {
            var a = Clock * 9 + k.Id;
            var p = k.Pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 4f;
            Particles.Add(new Particle
            {
                Pos = new Vector3(p, 7f), Life = 0.3f, MaxLife = 0.3f, Size = 1.2f,
                Color = Gfx.Rgb(255, 255, 140), Color2 = Gfx.Rgb(255, 200, 40), Glow = true
            });
        }
    }

    private Vector2 Rand2(float s) => new(((float)_rnd.NextDouble() * 2 - 1) * s, ((float)_rnd.NextDouble() * 2 - 1) * s);

    private void Burst(Vector3 at, int count, int c1, int c2, float speed, float life, bool glow)
    {
        for (var i = 0; i < count && Particles.Count < 900; i++)
        {
            var a = (float)_rnd.NextDouble() * MathF.Tau;
            var v = speed * (0.4f + (float)_rnd.NextDouble() * 0.6f);
            Particles.Add(new Particle
            {
                Pos = at,
                Vel = new Vector3(MathF.Cos(a) * v, MathF.Sin(a) * v, 20 + (float)_rnd.NextDouble() * 40),
                Gravity = 120, Life = life, MaxLife = life, Size = 1.2f, Color = c1, Color2 = c2, Glow = glow
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (var i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0) { Particles.RemoveAt(i); continue; }
            p.Vel.Z -= p.Gravity * dt;
            p.Pos += p.Vel * dt;
            if (p.Pos.Z < 0) { p.Pos.Z = 0; p.Vel.Z *= -0.3f; }
            Particles[i] = p;
        }
    }

    // ------------------------------------------------------------------ camera

    private void UpdateCamera(float dt, bool snap)
    {
        var p = Player;
        var intro = Phase == RacePhase.Intro;
        var wantBack = CamDist + (p.Boost > 0 ? 7f : 0f);
        var wantH = CamHeight + p.Z * 0.4f;
        if (intro)
        {
            var t = Math.Clamp(PhaseTime / 2.2f, 0, 1);
            var e = 1 - (1 - t) * (1 - t) * (1 - t);
            CamBack = 150f + (CamDist - 150f) * e;
            CamH = 60f + (CamHeight - 60f) * e;
            CamYaw = p.Heading + (1 - e) * 1.6f;
        }
        else if (!snap)
        {
            var k = 1 - MathF.Exp(-6f * dt);
            CamBack += (wantBack - CamBack) * k;
            CamH += (wantH - CamH) * k;
            if (p.Spin <= 0)
            {
                var yawTarget = Gfx.LerpAngle(p.MoveAngle, p.Heading, 0.35f);
                CamYaw = Gfx.WrapAngle(Gfx.LerpAngle(CamYaw, yawTarget, 1 - MathF.Exp(-5.5f * dt)));
            }
        }

        CamPos = p.Pos - new Vector2(MathF.Cos(CamYaw), MathF.Sin(CamYaw)) * CamBack;
    }

    private static float Approach(float v, float target, float step) =>
        v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);
}
