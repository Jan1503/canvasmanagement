using System.Numerics;

namespace CanvasManagement.Extension.TurboKart;

internal enum Screen { Boot, Demo, Title, CupSelect, TrackSelect, Loading, Race, Paused, Standings, Podium }

/// <summary>Logical buttons; keyboard and gamepad both map onto these in the extension.</summary>
internal enum Btn { Up, Down, Left, Right, A, B, Item, Start, Back }

internal struct Confetti
{
    public Vector2 Pos, Vel;
    public int Color;
}

/// <summary>App flow: demo attract loop, menus, Grand Prix (4 races per cup) and single races.</summary>
internal sealed partial class Game
{
    public Screen Screen = Screen.Boot;
    public Track? Track;
    public Race? Race;
    public Renderer? Renderer;
    public bool PlayerControl;
    public int TitleSel, CupSel, TrackSel, PauseSel;
    public bool GrandPrix;
    public int GpIndex;
    public readonly int[] Points = new int[Art.Racers.Length];
    public readonly int[] Gain = new int[Art.Racers.Length];
    public float ScreenTime, Idle, Clock;
    public int Laps = 3, Cpu = 7, Difficulty = 5;
    public bool AutoPilot = true;
    public TurboKartWeather Weather = TurboKartWeather.Auto;
    public bool DayNight = true, Replay = true;
    public int W, H;
    public readonly List<Confetti> ConfettiList = new();

    private Task<(Track, Renderer)>? _loading;
    private TrackDef? _loadingDef;
    private Screen _afterLoad;
    private int _demoIndex;
    private readonly Random _rnd = new();

    public static readonly Dictionary<TrackDef, Vector2[]> Thumbs = Circuits.All.ToDictionary(d => d, BuildThumb);

    public Cup CurrentCup => Circuits.Cups[CupSel];
    public TrackDef? LoadingDef => _loadingDef;

    private static Vector2[] BuildThumb(TrackDef def)
    {
        var pts = Track.Centerline(def.Control, out _).Where((_, i) => i % 6 == 0).ToArray();
        var minX = pts.Min(p => p.X);
        var minY = pts.Min(p => p.Y);
        var size = MathF.Max(pts.Max(p => p.X) - minX, pts.Max(p => p.Y) - minY);
        var offX = (size - (pts.Max(p => p.X) - minX)) / 2;
        var offY = (size - (pts.Max(p => p.Y) - minY)) / 2;
        return pts.Select(p => new Vector2((p.X - minX + offX) / size, (p.Y - minY + offY) / size)).ToArray();
    }

    // ------------------------------------------------------------------ loading

    private void StartLoad(TrackDef def, Screen after)
    {
        _loadingDef = def;
        _afterLoad = after;
        var w = W;
        var h = H;
        _loading = Task.Run(() =>
        {
            var t = new Track(def);
            return (t, new Renderer(w, h, t));
        });
    }

    private bool PollLoad()
    {
        if (_loading is not { IsCompleted: true }) return false;
        if (_loading.IsFaulted)
        {
            Console.WriteLine($"[TurboKart] track load failed: {_loading.Exception?.GetBaseException().Message}");
            _loading = null;
            return false;
        }

        (Track, Renderer) = _loading.Result;
        _loading = null;
        return true;
    }

    public void EnsureRenderer()
    {
        if (Track != null && (Renderer == null || Renderer.W != W || Renderer.H != H))
            Renderer = new Renderer(W, H, Track);
    }

    private List<int>? GridOrder()
    {
        if (!GrandPrix || GpIndex == 0) return null;
        var count = Math.Clamp(Cpu, 0, Art.Racers.Length - 1) + 1;
        return Enumerable.Range(0, count).OrderBy(l => Points[l]).ThenByDescending(l => l).ToList();
    }

    private void NewRace(bool human)
    {
        Race = new Race(Track!) { WeatherSetting = Weather, DayCycle = DayNight, ReplayEnabled = Replay };
        Race.Reset(Laps, Cpu, Difficulty, human ? GridOrder() : null);
        PlayerControl = human;
    }

    private void Go(Screen s)
    {
        Screen = s;
        ScreenTime = 0;
    }

    // ------------------------------------------------------------------ update

    public void Update(float dt, DriveInput hold, List<Btn> presses)
    {
        Clock += dt;
        ScreenTime += dt;
        _lastDt = dt;
        Idle = presses.Count > 0 || hold.Up || hold.Down || hold.Left || hold.Right ? 0 : Idle + dt;
        bool Pressed(params Btn[] b) => presses.Any(b.Contains);
        var confirm = Pressed(Btn.A, Btn.Item, Btn.Start);
        var back = Pressed(Btn.B, Btn.Back);

        switch (Screen)
        {
            case Screen.Boot:
                if (_loading == null) StartLoad(Circuits.All.First(), Screen.Demo);
                if (PollLoad())
                {
                    NewRace(false);
                    Go(AutoPilot ? Screen.Demo : Screen.Title);
                }

                break;

            case Screen.Demo:
                if (presses.Count > 0) { Go(Screen.Title); TitleSel = 0; break; }
                CycleDemo();
                break;

            case Screen.Title:
                if (Pressed(Btn.Up, Btn.Down)) TitleSel ^= 1;
                if (confirm) { GrandPrix = TitleSel == 0; Go(Screen.CupSelect); }
                else if (back && AutoPilot) Go(Screen.Demo);
                CycleDemo();
                IdleToDemo();
                break;

            case Screen.CupSelect:
                if (Pressed(Btn.Left)) CupSel = (CupSel + Circuits.Cups.Length - 1) % Circuits.Cups.Length;
                if (Pressed(Btn.Right)) CupSel = (CupSel + 1) % Circuits.Cups.Length;
                if (confirm)
                {
                    if (GrandPrix)
                    {
                        Array.Clear(Points);
                        Array.Clear(Gain);
                        GpIndex = 0;
                        StartLoad(CurrentCup.Tracks[0], Screen.Race);
                        Go(Screen.Loading);
                    }
                    else
                    {
                        TrackSel = 0;
                        Go(Screen.TrackSelect);
                    }
                }
                else if (back) Go(Screen.Title);

                CycleDemo();
                IdleToDemo();
                break;

            case Screen.TrackSelect:
                if (Pressed(Btn.Left, Btn.Right)) TrackSel ^= 1;
                if (Pressed(Btn.Up, Btn.Down)) TrackSel ^= 2;
                if (confirm)
                {
                    StartLoad(CurrentCup.Tracks[TrackSel], Screen.Race);
                    Go(Screen.Loading);
                }
                else if (back) Go(Screen.CupSelect);

                CycleDemo();
                IdleToDemo();
                break;

            case Screen.Loading:
                if (ScreenTime > 1.4f && PollLoad())
                {
                    NewRace(true);
                    Go(_afterLoad);
                }

                break;

            case Screen.Race:
            {
                var race = Race!;
                if (Pressed(Btn.Start, Btn.Back) && race.Phase != RacePhase.Results)
                {
                    PauseSel = 0;
                    Go(Screen.Paused);
                    break;
                }

                hold.UseItem = Pressed(Btn.Item);
                race.Update(dt, hold, PlayerControl);
                if (race.Phase == RacePhase.Results && (race.PhaseTime > 1.5f && confirm || race.PhaseTime > 14f))
                {
                    if (GrandPrix)
                    {
                        AwardPoints(race);
                        Go(Screen.Standings);
                    }
                    else Go(Screen.TrackSelect);
                }

                break;
            }

            case Screen.Paused:
                if (Pressed(Btn.Up)) PauseSel = (PauseSel + 2) % 3;
                if (Pressed(Btn.Down)) PauseSel = (PauseSel + 1) % 3;
                if (back || Pressed(Btn.Start) && PauseSel == 0) { Go(Screen.Race); break; }
                if (Pressed(Btn.A, Btn.Item))
                {
                    if (PauseSel == 0) Go(Screen.Race);
                    else if (PauseSel == 1) { NewRace(true); Go(Screen.Race); }
                    else { PlayerControl = false; Go(Screen.Title); }
                }

                break;

            case Screen.Standings:
                Race?.Update(dt, default, false);
                if (ScreenTime > 1f && confirm || ScreenTime > 16f)
                {
                    if (GpIndex < CurrentCup.Tracks.Length - 1)
                    {
                        GpIndex++;
                        StartLoad(CurrentCup.Tracks[GpIndex], Screen.Race);
                        Go(Screen.Loading);
                    }
                    else
                    {
                        ConfettiList.Clear();
                        Go(Screen.Podium);
                    }
                }

                break;

            case Screen.Podium:
                UpdateConfetti(dt);
                if (ScreenTime > 2f && confirm || ScreenTime > 25f)
                {
                    PlayerControl = false;
                    NewRace(false);
                    Go(Screen.Title);
                }

                break;
        }
    }

    private void IdleToDemo()
    {
        if (AutoPilot && Idle > 60f) Go(Screen.Demo);
    }

    /// <summary>Background race keeps running behind menus; after each demo race the next track loads.</summary>
    private void CycleDemo()
    {
        var race = Race;
        if (race == null) return;
        race.Update(_lastDt, default, false);
        if (_loading == null && race.Phase == RacePhase.Results && race.PhaseTime > 6f)
        {
            _demoIndex = (_demoIndex + 1) % Circuits.All.Count();
            StartLoad(Circuits.All.ElementAt(_demoIndex), Screen.Demo);
        }

        if (_loading != null && _afterLoad == Screen.Demo && PollLoad())
            NewRace(false);
    }

    private float _lastDt = 1f / 30f;

    private void AwardPoints(Race race)
    {
        Array.Clear(Gain);
        foreach (var k in race.Karts)
        {
            var pts = k.Place - 1 < Circuits.Points.Length ? Circuits.Points[k.Place - 1] : 0;
            Gain[k.Look] = pts;
            Points[k.Look] += pts;
        }
    }

    public List<int> StandingsOrder()
    {
        var count = Race?.Karts.Count ?? Math.Clamp(Cpu, 0, Art.Racers.Length - 1) + 1;
        return Race?.Karts.Select(k => k.Look).OrderByDescending(l => Points[l]).ThenBy(l => l).ToList()
               ?? Enumerable.Range(0, count).ToList();
    }

    private void UpdateConfetti(float dt)
    {
        for (var n = 0; n < 4 && ConfettiList.Count < 220; n++)
            ConfettiList.Add(new Confetti
            {
                Pos = new Vector2((float)_rnd.NextDouble() * W, -2),
                Vel = new Vector2(((float)_rnd.NextDouble() - 0.5f) * 20, 18 + (float)_rnd.NextDouble() * 30) * (H / 128f),
                Color = Gfx.Hsv(_rnd.Next(360), 0.7f, 1f)
            });
        for (var i = ConfettiList.Count - 1; i >= 0; i--)
        {
            var c = ConfettiList[i];
            c.Pos += c.Vel * dt;
            c.Vel.X += MathF.Sin(Clock * 3 + i) * 10 * dt;
            if (c.Pos.Y > H) { ConfettiList.RemoveAt(i); continue; }
            ConfettiList[i] = c;
        }
    }
}
