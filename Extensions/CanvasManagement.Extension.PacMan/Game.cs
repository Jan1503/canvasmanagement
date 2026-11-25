namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Rules and timing. Distances are tiles, times seconds; <see cref="Update"/> takes the frame time so the
///     game runs the same at any frame rate. Ghost behaviour follows the arcade recipe: a global scatter/chase
///     wave, one personality per ghost, no reversing except when the wave flips, frightened ghosts wander, eaten
///     ghosts return as eyes and are revived in the pen.
/// </summary>
internal sealed class Game
{
    private static readonly float[] Waves = { 7, 20, 7, 20, 5, 20, 5 };
    private static readonly int[] FruitPoints = { 100, 300, 500, 700, 1000, 2000, 3000, 5000 };

    private readonly Random _rng;
    private readonly HashSet<int>? _simPower;
    private readonly int _cols, _rows;
    private int[,] _dist = new int[1, 1];
    private int[,] _exitDist = new int[1, 1];
    private int _seed = Environment.TickCount;
    private int _wave;
    private float _waveTime;
    private int _combo;
    private int _dotsEaten;
    private int _fruitsShown;
    private bool _extraGiven;
    private (int X, int Y) _roam;
    private float _roamTime;

    public Game(int cols, int rows, int ghosts, int difficulty)
    {
        _rng = new Random();
        _cols = cols;
        _rows = rows;
        GhostCount = Math.Clamp(ghosts, 1, 8);
        Difficulty = Math.Clamp(difficulty, 1, 10);
        NewGame();
    }

    /// <summary>
    ///     A throwaway copy for the autopilot to look ahead with. It shares the maze read-only: dots are not
    ///     eaten (power pellets are, in a private set), and nothing touches the score or the high score.
    /// </summary>
    public Game CloneForSim() => new(this);

    private Game(Game src)
    {
        Sim = true;
        _rng = new Random(src._seed);
        _simPower = src._simPower != null ? new HashSet<int>(src._simPower) : new HashSet<int>();
        _cols = src._cols;
        _rows = src._rows;
        Maze = src.Maze;
        _dist = new int[Maze.W, Maze.H];
        _exitDist = src._exitDist;
        _wave = src._wave;
        _waveTime = src._waveTime;
        _roam = src._roam;
        _roamTime = src._roamTime;
        Mode = src.Mode;
        Fright = src.Fright;
        FrightTotal = src.FrightTotal;
        Level = src.Level;
        Lives = 1;
        Difficulty = src.Difficulty;
        SpeedFactor = src.SpeedFactor;
        GhostCount = src.GhostCount;
        Phase = Phase.Playing;
        Pac.X = src.Pac.X;
        Pac.Y = src.Pac.Y;
        Pac.Dir = src.Pac.Dir;
        Pac.Off = src.Pac.Off;
        Pac.Arrived = OnPacCentre;
        foreach (var o in src.Ghosts)
            Ghosts.Add(new Ghost(o.Index)
            {
                X = o.X, Y = o.Y, Dir = o.Dir, Off = o.Off, State = o.State, Frightened = o.Frightened,
                HomeTimer = o.HomeTimer, Bob = o.Bob, PenX = o.PenX, PenY = o.PenY, Brain = Think
            });
    }

    public bool Sim { get; }

    /// <summary>Seconds of unpaused play since this game started.</summary>
    public float Clock { get; private set; }
    public int GhostsEatenInSim { get; private set; }

    /// <summary>Power pellet still on the board (in a simulation: not eaten in it yet).</summary>
    public bool PowerAt(int x, int y) => Maze.HasPower(x, y) && (_simPower == null || !_simPower.Contains(y * Maze.W + x));

    public Maze Maze { get; private set; } = null!;
    public Pac Pac { get; } = new();
    public List<Ghost> Ghosts { get; } = new();
    public Phase Phase { get; private set; }
    public float PhaseTime { get; private set; }
    public float ReadyLength { get; private set; }
    public int Score { get; private set; }
    public static int HiScore { get; private set; }
    public int Lives { get; private set; }
    public int Level { get; private set; }
    public GhostMode Mode { get; private set; }
    public float Fright { get; private set; }
    public float FrightTotal { get; private set; }
    public int GhostCount { get; private set; }
    public int Difficulty { get; set; }
    public float SpeedFactor { get; set; } = 1f;
    public GameEvent Events { get; private set; }

    /// <summary>Events since the last call.</summary>
    public GameEvent TakeEvents()
    {
        var e = Events;
        Events = GameEvent.None;
        return e;
    }
    public int DotsEaten => _dotsEaten;

    /// <summary>Active fruit: tile, kind (0..7) and seconds left; null when none is out.</summary>
    public (int X, int Y, int Kind, float Left)? Fruit { get; private set; }

    /// <summary>Points shown where something was just eaten.</summary>
    public (float X, float Y, int Points, float Left)? Popup { get; private set; }

    public int FruitKind => Math.Min(Level - 1, FruitPoints.Length - 1);

    /// <summary>Called at every tile centre Pac-Man reaches; returns the direction it should take there.</summary>
    public Func<Game, Direction>? Pilot { get; set; }

    /// <summary>Called every frame between tiles; true reverses Pac-Man on the spot.</summary>
    public Func<Game, bool>? Panic { get; set; }

    public float PacSpeed => Base * Math.Min(0.92f, 0.80f + 0.03f * Math.Min(Level - 1, 4)) * (Fright > 0 ? 1.08f : 1f);
    public float GhostSpeed => Base * (0.70f + 0.025f * Difficulty + 0.03f * Math.Min(Level - 1, 4));
    public float FrightSpeed => Base * 0.5f;
    public float EyesSpeed => Base * 1.7f;
    private float Base => 7.5f * SpeedFactor;
    private bool Elroy => Maze.DotsLeft <= Math.Max(8, Maze.DotsTotal / 10);

    // ------------------------------------------------------------------ setup

    public void NewGame()
    {
        Score = 0;
        Lives = 3;
        Level = 1;
        _extraGiven = false;
        BuildLevel();
        Begin(4.2f);
        Events |= GameEvent.GameStart;
    }

    public void SetGhostCount(int n)
    {
        n = Math.Clamp(n, 1, 8);
        if (n == GhostCount) return;
        GhostCount = n;
        PlaceActors();
    }

    private void BuildLevel()
    {
        Maze = new Maze(_cols, _rows, _seed++ * 7919 + Level);
        _dist = new int[Maze.W, Maze.H];
        _exitDist = new int[Maze.W, Maze.H];
        Maze.Distances(Maze.Exit.X, Maze.Exit.Y, _exitDist);
        _dotsEaten = 0;
        _fruitsShown = 0;
        Fruit = null;
    }

    private void Begin(float ready)
    {
        PlaceActors();
        Phase = Phase.Ready;
        PhaseTime = 0;
        ReadyLength = ready;
    }

    private void PlaceActors()
    {
        Pac.Reset(Maze);
        Pac.Arrived = OnPacCentre;
        Ghosts.Clear();
        var gap = Math.Max(0.5f, 2.4f - 0.25f * (Level - 1) - 0.08f * Difficulty);
        for (var i = 0; i < GhostCount; i++)
        {
            var g = new Ghost(i) { Brain = Think, Bob = i * 0.7f };
            if (i == 0)
            {
                g.State = GhostState.Active;
                g.X = Maze.Exit.X;
                g.Y = Maze.Exit.Y;
                g.Dir = Direction.Left;
            }
            else
            {
                g.State = GhostState.Home;
                var slot = (i - 1) % 4;
                g.PenX = Maze.HouseX + new[] { 0, -2, 2, -1 }[slot] + (i > 4 ? 1 : 0);
                g.PenY = Maze.HouseY;
                g.HomeTimer = gap * i;
            }
            Ghosts.Add(g);
        }
        Mode = GhostMode.Scatter;
        _wave = 0;
        _waveTime = 0;
        Fright = 0;
        Popup = null;
    }

    // ------------------------------------------------------------------ control

    public void Pause(bool on)
    {
        if (on && Phase is Phase.Playing or Phase.Ready) { _resume = Phase; Phase = Phase.Paused; }
        else if (!on && Phase == Phase.Paused) Phase = _resume;
    }

    private Phase _resume = Phase.Playing;

    // ------------------------------------------------------------------ update

    public void Update(float dt)
    {
        if (Phase == Phase.Paused) return;
        PhaseTime += dt;
        Clock += dt;
        if (Popup is { } pop)
            Popup = pop.Left - dt > 0 ? pop with { Left = pop.Left - dt } : null;

        switch (Phase)
        {
            case Phase.Ready:
                if (PhaseTime >= ReadyLength) SetPhase(Phase.Playing);
                BobHome(dt);
                break;
            case Phase.Playing:
                Play(dt);
                break;
            case Phase.GhostEaten:
                if (PhaseTime >= 0.65f) SetPhase(Phase.Playing);
                break;
            case Phase.Dying:
                if (PhaseTime >= 2.8f)
                {
                    if (Lives <= 0)
                    {
                        SetPhase(Phase.GameOver);
                        Events |= GameEvent.GameOver;
                    }
                    else Begin(1.8f);
                }
                break;
            case Phase.LevelClear:
                if (PhaseTime >= 2.6f)
                {
                    Level++;
                    BuildLevel();
                    Begin(2.0f);
                }
                break;
        }
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Play(float dt)
    {
        // Scatter / chase waves (the clock stops while ghosts are frightened).
        if (Fright > 0)
        {
            Fright -= dt;
            if (Fright <= 0)
            {
                Fright = 0;
                foreach (var g in Ghosts) g.Frightened = false;
            }
        }
        else if (_wave < Waves.Length)
        {
            _waveTime += dt;
            if (_waveTime >= Waves[_wave])
            {
                _waveTime = 0;
                _wave++;
                Mode = _wave % 2 == 1 ? GhostMode.Chase : GhostMode.Scatter;
                if (_wave >= Waves.Length) Mode = GhostMode.Chase;
                foreach (var g in Ghosts)
                    if (g.State == GhostState.Active) g.Reverse(Maze);
            }
        }

        if (Fruit is { } f)
            Fruit = f.Left - dt > 0 ? f with { Left = f.Left - dt } : null;

        _roamTime -= dt;
        if (_roamTime <= 0)
        {
            _roamTime = 5;
            _roam = Maze.Snap(_rng.Next(Maze.W), _rng.Next(Maze.H));
        }

        // Pac-Man.
        if (Pac.Off > 0 && Panic?.Invoke(this) == true) Pac.Reverse(Maze);
        Pac.Update(Maze, PacSpeed * dt);
        if (Phase != Phase.Playing) return;
        if (Fruit is { } fr && Pac.Near(Maze) == (fr.X, fr.Y))
        {
            var pts = FruitPoints[fr.Kind];
            AddScore(pts);
            Popup = (fr.X, fr.Y, pts, 1.6f);
            Fruit = null;
            Events |= GameEvent.Fruit;
        }

        // Ghosts.
        BobHome(dt);
        foreach (var g in Ghosts) MoveGhost(g, dt);

        Collide();
        if (Phase == Phase.Playing && Maze.DotsLeft <= 0)
        {
            SetPhase(Phase.LevelClear);
            Fruit = null;
            Events |= GameEvent.LevelClear;
        }
    }

    private void OnPacCentre(int x, int y)
    {
        if (Sim)
        {
            if (PowerAt(x, y))
            {
                _simPower!.Add(y * Maze.W + x);
                Frighten();
            }
        }
        else if (Maze.EatDot(x, y))
        {
            AddScore(10);
            _dotsEaten++;
            Events |= GameEvent.Dot;
            var at = new[] { Maze.DotsTotal * 3 / 10, Maze.DotsTotal * 7 / 10 };
            if (_fruitsShown < 2 && _dotsEaten >= at[_fruitsShown])
            {
                _fruitsShown++;
                Fruit = (Maze.PacSpawn.X, Maze.PacSpawn.Y, FruitKind, 9.5f);
                Events |= GameEvent.FruitAppears;
            }
        }
        else if (Maze.EatPower(x, y))
        {
            AddScore(50);
            _dotsEaten++;
            Events |= GameEvent.Power;
            Frighten();
        }

        if (Pilot != null)
        {
            var d = Pilot(this);
            if (d != Direction.None) Pac.Want = d;
        }
    }

    private void Frighten()
    {
        FrightTotal = Math.Clamp(7f - (Level - 1) * 0.8f - (Difficulty - 3) * 0.25f, 1.5f, 8f);
        Fright = FrightTotal;
        _combo = 0;
        foreach (var g in Ghosts)
        {
            if (g.State is GhostState.Eyes or GhostState.Entering) continue;
            g.Frightened = true;
            if (g.State == GhostState.Active) g.Reverse(Maze);
        }
    }

    private void AddScore(int pts)
    {
        if (Sim) return;
        Score += pts;
        if (Score > HiScore) HiScore = Score;
        if (!_extraGiven && Score >= 10000)
        {
            _extraGiven = true;
            Lives++;
            Events |= GameEvent.ExtraLife;
        }
    }

    private void BobHome(float dt)
    {
        foreach (var g in Ghosts)
            if (g.State == GhostState.Home)
                g.Bob += dt * 5;
    }

    private void MoveGhost(Ghost g, float dt)
    {
        var m = Maze;
        switch (g.State)
        {
            case GhostState.Home:
                g.HomeTimer -= dt;
                // Release one at a time, and only if no one is walking out right now.
                if (g.HomeTimer <= 0 && Ghosts.All(o => o.State != GhostState.Leaving))
                    g.State = GhostState.Leaving;
                break;

            case GhostState.Leaving:
            {
                var v = (g.Frightened ? FrightSpeed : GhostSpeed * 0.6f) * dt;
                if (MathF.Abs(g.PenX - m.HouseX) > 0.01f)
                    g.PenX += Math.Clamp(m.HouseX - g.PenX, -v, v);
                else if (g.PenY > m.Exit.Y)
                    g.PenY = Math.Max(m.Exit.Y, g.PenY - v);
                else
                {
                    g.State = GhostState.Active;
                    g.X = m.Exit.X;
                    g.Y = m.Exit.Y;
                    g.Off = 0;
                    g.Dir = _rng.Next(2) == 0 ? Direction.Left : Direction.Right;
                }
                break;
            }

            case GhostState.Active:
            {
                float speed;
                if (g.Frightened) speed = FrightSpeed;
                else if (m.InTunnel(g.X, g.Y)) speed = Base * 0.45f;
                else speed = GhostSpeed * (g.Index == 0 && Elroy ? 1.1f : 1f);
                g.Step(m, speed * dt);
                break;
            }

            case GhostState.Eyes:
                g.Step(m, EyesSpeed * dt);
                if (g.X == m.Exit.X && g.Y == m.Exit.Y && g.Off < 0.35f)
                {
                    g.State = GhostState.Entering;
                    g.PenX = m.Exit.X;
                    g.PenY = m.Exit.Y;
                }
                break;

            case GhostState.Entering:
                g.PenY += EyesSpeed * 0.6f * dt;
                if (g.PenY >= m.HouseY)
                {
                    g.PenY = m.HouseY;
                    g.Frightened = false;
                    g.State = GhostState.Leaving;
                }
                break;
        }
    }

    private void Collide()
    {
        var px = Pac.Fx(Maze);
        var py = Pac.Fy;
        foreach (var g in Ghosts)
        {
            if (g.State != GhostState.Active) continue;
            var dx = MathF.Abs(g.Fx(Maze) - px);
            dx = MathF.Min(dx, Maze.W - dx);
            var dy = MathF.Abs(g.Fy - py);
            if (dx + dy > 0.6f) continue;
            if (g.Frightened)
            {
                var pts = 200 << Math.Min(_combo, 4);
                _combo++;
                AddScore(pts);
                Popup = (g.Fx(Maze), g.Fy, pts, 0.65f);
                g.Frightened = false;
                g.State = GhostState.Eyes;
                if (Sim)
                {
                    GhostsEatenInSim++;
                    continue;
                }
                SetPhase(Phase.GhostEaten);
                Events |= GameEvent.GhostEaten;
                return;
            }

            Lives--;
            SetPhase(Phase.Dying);
            Fruit = null;
            Events |= GameEvent.Death;
            return;
        }
    }

    // ------------------------------------------------------------------ ghost brains

    /// <summary>Called at each tile centre a ghost reaches.</summary>
    private Direction Think(Ghost g, Maze m)
    {
        var back = g.Dir.Opposite();
        Span<Direction> options = stackalloc Direction[4];
        var n = 0;
        foreach (var d in Dirs.All)
            if (d != back && m.Step(g.X, g.Y, d, out _, out _))
                options[n++] = d;
        if (n == 0) return back;
        if (n == 1) return options[0];

        if (g.State == GhostState.Eyes) return Best(g, m, options[..n], _exitDist, false);

        if (g.Frightened)
        {
            // Wander, but drift away from Pac-Man.
            var best = options[0];
            var bestScore = float.MinValue;
            foreach (var d in options[..n])
            {
                m.Step(g.X, g.Y, d, out var nx, out var ny);
                var away = MathF.Abs(nx - Pac.X) + MathF.Abs(ny - Pac.Y);
                var s = away * 0.4f + (float)_rng.NextDouble() * 4f;
                if (s > bestScore) { bestScore = s; best = d; }
            }
            return best;
        }

        var (tx, ty) = Target(g, m);
        m.Distances(tx, ty, _dist);
        return Best(g, m, options[..n], _dist, true);
    }

    private Direction Best(Ghost g, Maze m, ReadOnlySpan<Direction> options, int[,] dist, bool spread)
    {
        var best = options[0];
        var bestCost = float.MaxValue;
        foreach (var d in options)
        {
            m.Step(g.X, g.Y, d, out var nx, out var ny);
            float cost = dist[nx, ny] < 0 ? 999 : dist[nx, ny];
            if (spread)
                // Do not queue up behind another hunter on the same path: take the other way round instead.
                foreach (var o in Ghosts)
                {
                    if (o == g || !o.Dangerous) continue;
                    var (ox, oy) = o.Next(m);
                    if ((o.X == nx && o.Y == ny) || (ox == nx && oy == ny)) cost += 3;
                }
            if (cost < bestCost) { bestCost = cost; best = d; }
        }
        return best;
    }

    /// <summary>Each ghost's idea of where Pac-Man will be. Ghosts 5–8 get their own habits.</summary>
    private (int X, int Y) Target(Ghost g, Maze m)
    {
        if (Mode == GhostMode.Scatter && !(g.Index == 0 && Elroy))
        {
            var corner = (g.Index % 8) switch
            {
                0 => (m.W - 2, 0),
                1 => (1, 0),
                2 => (m.W - 2, m.H - 1),
                3 => (1, m.H - 1),
                4 => (m.W / 2, 0),
                5 => (m.W / 2, m.H - 1),
                6 => (0, m.H / 2),
                _ => (m.W - 1, m.H / 2)
            };
            return m.Snap(corner.Item1, corner.Item2);
        }

        int px = Pac.X, py = Pac.Y, dx = Pac.Dir.Dx(), dy = Pac.Dir.Dy();
        switch (g.Index % 8)
        {
            case 0: // straight at him
                return (px, py);
            case 1: // four tiles ahead of him
                return m.Snap(px + dx * 4, py + dy * 4);
            case 2: // flank: mirror the first ghost around a point two tiles ahead
            {
                var lead = Ghosts[0];
                int ax = px + dx * 2, ay = py + dy * 2;
                return m.Snap(2 * ax - lead.X, 2 * ay - lead.Y);
            }
            case 3: // shy: hunts from afar, backs off up close
            {
                var far = Math.Abs(g.X - px) + Math.Abs(g.Y - py) > 8;
                return far ? (px, py) : m.Snap(1, m.H - 1);
            }
            case 4: // cuts off the way he came
                return m.Snap(px - dx * 4, py - dy * 4);
            case 5: // guards the power pellet nearest to him
            {
                (int X, int Y) best = (px, py);
                var bd = int.MaxValue;
                for (var x = 0; x < m.W; x++)
                for (var y = 0; y < m.H; y++)
                    if (m.HasPower(x, y) && Math.Abs(x - px) + Math.Abs(y - py) < bd)
                    {
                        bd = Math.Abs(x - px) + Math.Abs(y - py);
                        best = (x, y);
                    }
                return best;
            }
            case 6:
                return (px, py);
            default:
                return _roam;
        }
    }
}
