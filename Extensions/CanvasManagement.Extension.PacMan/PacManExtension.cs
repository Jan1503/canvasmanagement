using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Pac-Man game extension with intelligent AI-controlled Pac-Man.
/// </summary>
[ExtensionInfo("Pac-Man",
    "Classic Pac-Man arcade game with AI-controlled player",
    "Games",
    IconResourceName = "pacman.svg")]
public class PacManExtension(ICanvas canvas) : ICanvasExtension
{
    private readonly ICanvas _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    private readonly object _lock = new();
    private SKColor _backgroundColor = SKColors.Black;
    private int _caution = 5;

    // AI keeps a committed direction so BFS ties don't cause oscillation.
    private Direction _committedDirection = Direction.None;
    private int _gameOverTimer;
    private Timer? _gameTimer;
    private int _lastDifficulty;

    // Track last applied values for dynamic updates
    private int _lastGameSpeed;
    private int _lastGhostCount;
    private bool _lastShowDebugInfo;
    // Animation timers
    private int _levelCompleteTimer;
    private GameRenderer? _renderer;
    private GameState? _state;

    public string Description => "Classic Pac-Man game";

    // ── Gameplay ──
    [ExtensionParameter("Game Speed", "Update interval in milliseconds — lower is faster",
        DefaultValue = 35, MinValue = 16, MaxValue = 200, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 35;

    [ExtensionParameter("Difficulty", "Ghost speed — higher ghosts move quicker (still capped below Pac-Man)",
        DefaultValue = 3, MinValue = 1, MaxValue = 10, Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Ghosts", "How many ghosts hunt Pac-Man",
        DefaultValue = 4, MinValue = 1, MaxValue = 8, Order = 3)]
    public int GhostCount { get; set; } = 4;

    [ExtensionParameter("Caution",
        "How wide a berth Pac-Man gives ghosts when routing to pellets. Higher = bigger detours around " +
        "ghosts, lower = braver shortcuts",
        DefaultValue = 5, MinValue = 1, MaxValue = 12, Order = 4)]
    public int Caution
    {
        get => _caution;
        set => _caution = Math.Clamp(value, 1, 12);
    }

    // ── Appearance ──
    [ExtensionParameter("Background Color", "Background colour for the game area",
        DefaultValue = "#000000", Order = 5)]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            _backgroundColor = value;
            if (_renderer != null) _renderer.BackgroundColor = value;
        }
    }

    [ExtensionParameter("Show Debug Info", "Overlay AI/position debug text",
        DefaultValue = false, Order = 6)]
    public bool ShowDebugInfo { get; set; } = false;

    // ── Live status (read-only) ──
    [ExtensionParameter("Score", "Current game score", ReadOnly = true, Order = 10)]
    public int Score => _state?.Score ?? 0;

    [ExtensionParameter("Lives", "Remaining lives", ReadOnly = true, Order = 11)]
    public int Lives => _state?.Lives ?? 0;

    [ExtensionParameter("Level", "Current level", ReadOnly = true, Order = 12)]
    public int Level => _state?.Level ?? 0;

    public string Name => "Pac-Man";
    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            var width = _canvas.Width;
            var height = _canvas.Height;

            _state = new GameState(width, height, GhostCount, Difficulty);
            _renderer = new GameRenderer(_canvas)
            {
                ShowDebugInfo = ShowDebugInfo,
                BackgroundColor = _backgroundColor
            };

            // Reset AI state
            _committedDirection = Direction.None;

            _lastGameSpeed = GameSpeed;
            _lastGhostCount = GhostCount;
            _lastShowDebugInfo = ShowDebugInfo;
            _lastDifficulty = Difficulty;

            _gameTimer = new Timer(GameSpeed);
            _gameTimer.Elapsed += OnGameTick;
            _gameTimer.AutoReset = true;
            _gameTimer.Start();

            IsRunning = true;

            Console.WriteLine(
                $"[PacMan] Started: {width}x{height}, Grid: {_state.Maze.GridWidth}x{_state.Maze.GridHeight}");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsRunning = false;

            _gameTimer?.Stop();
            _gameTimer?.Dispose();
            _gameTimer = null;

            try
            {
                _canvas.Clear(SKColors.Black);
            }
            catch
            {
            }

            _renderer?.Dispose();
            _renderer = null;
            _state = null;

            Console.WriteLine("[PacMan] Stopped");
        }
    }

    private void OnGameTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _state == null || _renderer == null) return;

            try
            {
                ApplyDynamicParameters();

                // Decide only at tile centres (real decision points). Re-deciding every tick made Pac-Man
                // flip back and forth on the spot; committing to a lane between intersections fixes that.
                if (!_state.IsDeathAnimation && !_state.IsLevelStartAnimation && !_state.GameOver &&
                    !_state.LevelComplete && _state.PacMan.AtCellCenter)
                {
                    var aiDirection = GetAIDirection();
                    _state.PacMan.SetNextDirection(aiDirection);
                }

                _state.Update();

                // Reset AI state on death or level change
                if (_state.IsDeathAnimation || _state.IsLevelStartAnimation)
                    _committedDirection = Direction.None;

                if (_state.LevelComplete && !_state.IsLevelStartAnimation)
                {
                    _levelCompleteTimer++;
                    if (_levelCompleteTimer > 120)
                    {
                        _levelCompleteTimer = 0;
                        _state.NextLevel();
                    }
                }

                if (_state.GameOver)
                {
                    _gameOverTimer++;
                    if (_gameOverTimer > 180)
                    {
                        _gameOverTimer = 0;
                        _state.Reset();
                    }
                }

                _renderer.Render(_state);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PacMan] Error: {ex.Message}");
            }
        }
    }

    private void ApplyDynamicParameters()
    {
        if (_gameTimer != null && GameSpeed != _lastGameSpeed)
        {
            _gameTimer.Interval = GameSpeed;
            _lastGameSpeed = GameSpeed;
        }

        if (_state != null && GhostCount != _lastGhostCount)
        {
            _state.SetGhostCount(GhostCount);
            _lastGhostCount = GhostCount;
        }

        if (_renderer != null && ShowDebugInfo != _lastShowDebugInfo)
        {
            _renderer.ShowDebugInfo = ShowDebugInfo;
            _lastShowDebugInfo = ShowDebugInfo;
        }

        if (_state != null && Difficulty != _lastDifficulty)
        {
            _state.SetDifficulty(Difficulty);
            _lastDifficulty = Difficulty;
        }
    }

    /// <summary>
    ///     Expert Pac-Man brain. Builds a <em>time-aware</em> danger field (a ghost is only dangerous on a
    ///     cell if it can arrive there around the same step Pac-Man would, measured through the maze, not as
    ///     the crow flies) and plans with Dijkstra over per-cell danger cost. It hunts frightened ghosts,
    ///     baits with power pellets when pressured, clears normal pellets first (saving power pellets), and
    ///     makes a smart escape when truly cornered.
    /// </summary>
    private Direction GetAIDirection()
    {
        if (_state == null) return Direction.None;

        var maze = _state.Maze;
        var ghosts = _state.Ghosts;
        var w = maze.GridWidth;
        var h = maze.GridHeight;
        var pac = ((int)Math.Round(_state.PacMan.Position.X), (int)Math.Round(_state.PacMan.Position.Y));

        var pacDist = maze.DistanceFrom(new[] { pac });

        // Threat field from dangerous ghosts. Seed at the cell AHEAD of each ghost (they can't reverse, so
        // the cell behind them is safe) plus their current cell to cover turns.
        var threatSeeds = new List<(int x, int y)>();
        foreach (var g in ghosts)
        {
            if (g.State != GhostState.Chase) continue; // frightened/dead aren't threats
            var gc = ((int)Math.Round(g.Position.X), (int)Math.Round(g.Position.Y));
            threatSeeds.Add(gc);
            var v = g.Direction.ToVector();
            var ahead = (gc.Item1 + (int)v.X, gc.Item2 + (int)v.Y);
            if (maze.IsOpenCell(ahead.Item1, ahead.Item2)) threatSeeds.Add(ahead);
        }

        var ghostThreat = maze.DistanceFrom(threatSeeds);

        // Per-cell entry cost: 1 + danger. Danger spikes where a ghost reaches the cell at (or before) the
        // same step Pac-Man would. "slack" = how many steps Pac-Man is ahead of the nearest ghost there.
        // The Caution slider widens the avoidance horizon (how far around ghosts gets penalised).
        var horizon = Math.Max(2, _caution);
        var cost = new float[w, h];
        for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++)
        {
            var c = 1f;
            var gt = ghostThreat[x, y];
            if (gt >= 0)
            {
                var pd = pacDist[x, y] >= 0 ? pacDist[x, y] : 999;
                var slack = gt - pd;
                if (slack <= 0) c += 120f; // ghost arrives first/together: lethal, route around it
                else if (slack < horizon) c += (horizon - slack) * 8f;
            }

            cost[x, y] = c;
        }

        // 1) Frightened ghosts on the board: hunt the nearest one (path still dodges live ghosts via cost).
        var frightened = ghosts.Where(g => g.State == GhostState.Frightened)
            .Select(g => ((int)Math.Round(g.Position.X), (int)Math.Round(g.Position.Y)))
            .ToHashSet();
        if (frightened.Count > 0)
        {
            var dir = maze.LowestCostStepToTarget(pac, (x, y) => frightened.Contains((x, y)), cost,
                _committedDirection);
            if (dir != Direction.None) return Commit(dir);
        }

        // 2) Head for the nearest ACTUAL pellet along the safest path (normal pellets first to save power
        //    pellets, then power pellets). This is the key to non-dumb routing: Pac-Man always targets a
        //    real pellet, with ghost danger folded into the path cost rather than abandoning the goal.
        var goal = maze.LowestCostStepToTarget(pac, maze.HasPellet, cost, _committedDirection);
        if (goal == Direction.None)
            goal = maze.LowestCostStepToTarget(pac, maze.HasPowerPellet, cost, _committedDirection);

        // 3) Take the pellet step unless it would walk straight into a ghost; if so, dodge while still
        //    biasing toward the pellet. Because step danger is baked into the cost above, this override is
        //    rare - it's just the final "don't suicide" guard.
        if (goal != Direction.None)
        {
            if (IsStepSafe(pac, goal, ghostThreat)) return Commit(goal);
            return Commit(ChooseSafeDirection(pac, ghostThreat, maze, goal));
        }

        // 4) Nothing reachable to collect (rare): just stay alive.
        return Commit(ChooseSafeDirection(pac, ghostThreat, maze, Direction.None));
    }

    private Direction Commit(Direction dir)
    {
        if (dir != Direction.None) _committedDirection = dir;
        return _committedDirection;
    }

    private static bool IsStepSafe((int x, int y) pac, Direction dir, int[,] ghostThreat)
    {
        var v = dir.ToVector();
        int nx = pac.x + (int)v.X, ny = pac.y + (int)v.Y;
        if (nx < 0 || ny < 0 || nx >= ghostThreat.GetLength(0) || ny >= ghostThreat.GetLength(1)) return false;
        var gt = ghostThreat[nx, ny];
        return gt < 0 || gt > 1; // -1 = ghost can't reach it; otherwise needs >1 step
    }

    /// <summary>
    ///     Picks the safest immediate move when the desired pellet step is dangerous. A neighbour a ghost can
    ///     reach in 0-1 steps is suicidal and skipped (unless every option is). Among survivable moves it
    ///     favours cells the ghosts reach latest, with more exits, biased toward <paramref name="goal" /> so
    ///     it keeps making progress. Reversing is allowed - escaping beats greed.
    /// </summary>
    private Direction ChooseSafeDirection((int x, int y) pac, int[,] ghostThreat, Maze maze, Direction goal)
    {
        var opposite = Maze.Opposite(_committedDirection);
        var best = Direction.None;
        var bestScore = float.MinValue;

        // Fallback if literally every move is lethal: take the one the ghost reaches latest.
        var leastBad = Direction.None;
        var leastBadSafety = int.MinValue;

        foreach (var dir in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
        {
            var v = dir.ToVector();
            int nx = pac.x + (int)v.X, ny = pac.y + (int)v.Y;
            if (!maze.IsOpenCell(nx, ny)) continue;

            var gt = ghostThreat[nx, ny];
            var safety = gt < 0 ? 999 : gt; // -1 = unreachable by a ghost = safest
            if (safety > leastBadSafety)
            {
                leastBadSafety = safety;
                leastBad = dir;
            }

            if (safety <= 1) continue; // ghost is on / one step from this cell: don't walk into it

            var score = safety * 10f + CountExits(nx, ny, maze) * 3f;
            if (dir == goal) score += 8f; // keep heading for the pellet when it's safe to
            if (dir == _committedDirection) score += 1.5f; // anti-oscillation
            if (dir == opposite) score -= 1f; // mild: reversing to survive is fine

            if (score > bestScore)
            {
                bestScore = score;
                best = dir;
            }
        }

        return best != Direction.None ? best : leastBad;
    }

    private int CountExits(int x, int y, Maze maze)
    {
        var count = 0;
        if (maze.IsOpenCell(x - 1, y)) count++;
        if (maze.IsOpenCell(x + 1, y)) count++;
        if (maze.IsOpenCell(x, y - 1)) count++;
        if (maze.IsOpenCell(x, y + 1)) count++;
        return count;
    }

    public void Dispose()
    {
        Stop();
    }
}