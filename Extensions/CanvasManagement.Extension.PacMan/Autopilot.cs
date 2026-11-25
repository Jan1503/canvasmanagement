namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Demo player. At every junction each way out is played forward a few seconds on a copy of the game, with
///     the real ghost brains, so pincers, mode flips and ghosts leaving the pen are all seen coming. Among the
///     moves that survive he takes the cheapest path to food; the cost of a cell grows the earlier a ghost could
///     reach it compared to him. He saves power pellets until pressed, then hunts the blue ghosts he can catch.
/// </summary>
internal sealed class Autopilot
{
    private const int Unreached = 1 << 20;
    private Maze? _maze;
    private int[,] _tmp = new int[1, 1];
    private float[,] _ghostAt = new float[1, 1];
    private float[,] _pacAt = new float[1, 1];
    private float[,] _cost = new float[1, 1];
    private float[,] _best = new float[1, 1];
    private int[] _queue = [];
    private readonly PriorityQueue<int, float> _heap = new();

    /// <summary>1..12, from brave to careful.</summary>
    public int Caution { get; set; } = 5;

    /// <summary>Last chosen goal, for the debug overlay.</summary>
    public (int X, int Y)? Goal { get; private set; }

    public string Reason { get; private set; } = "";
    public string Trace { get; private set; } = "";

    private float Margin => 0.12f + Caution * 0.045f;

    /// <summary>How far ahead (s) every candidate move is played out.</summary>
    private float Horizon => 2.0f + Caution * 0.15f;

    private const float SimStep = 1 / 30f;

    public Direction Decide(Game game)
    {
        var m = game.Maze;
        Ensure(m);
        var pac = game.Pac;
        BuildThreat(game, pac.X, pac.Y);

        // Nothing eaten for a while (last dots guarded): weigh danger less; the look-ahead still keeps him alive.
        if (game.DotsEaten != _lastDots || game.Maze != _maze2)
        {
            _lastDots = game.DotsEaten;
            _maze2 = game.Maze;
            _hungrySince = game.Clock;
        }
        _bold = Math.Clamp(1f - (game.Clock - _hungrySince - 4f) / 8f, 0.15f, 1f);

        var pressed = Pressure(game) < 1.4f + Margin;
        var hunting = HuntTargets(game);
        var options = new List<(Direction Dir, float Cost, float Alive, (int X, int Y)? Goal)>();
        foreach (var d in Dirs.All)
        {
            if (!m.Step(pac.X, pac.Y, d, out _, out _)) continue;
            Goal = null;
            var cost = PathCost(game, pac.X, pac.Y, d, pressed, hunting);
            if (d == pac.Dir.Opposite()) cost += 1.5f;
            var goal = Goal;
            // Play the move out with the real ghost brains: once heading for the goal, once running away.
            // The move is as good as the better of the two, since he can still choose later.
            var alive = Rollout(game, d, goal, false);
            if (alive < Horizon) alive = Math.Max(alive, Rollout(game, d, null, false));
            options.Add((d, cost, alive, goal));
        }
        if (options.Count == 0) return Direction.None;

        Trace = $"at {pac.X},{pac.Y} dir {pac.Dir} press {Pressure(game):0.00} " +
                string.Join(" ", options.Select(o => $"{o.Dir}:alive{o.Alive:0.0}/c{o.Cost:0.0}"));
        var safe = options.Where(o => o.Alive >= Horizon - 0.001f).ToList();
        if (safe.Count > 0)
        {
            var pick = safe.OrderBy(o => o.Cost).First();
            Goal = pick.Goal;
            Reason = hunting.Count > 0 ? "hunt" : pressed ? "pressed" : "eat";
            return pick.Dir;
        }

        // Every move gets caught in the look-ahead: buy the most time and hope a ghost turns away.
        Reason = "escape";
        var best = options.OrderByDescending(o => o.Alive).ThenBy(o => o.Cost).First();
        Goal = best.Goal;
        return best.Dir;
    }

    /// <summary>Between tiles: turn back if a ghost is about to take the tile ahead and turning back lives longer.</summary>
    public bool Panic(Game game)
    {
        var m = game.Maze;
        Ensure(m);
        var pac = game.Pac;
        var (ax, ay) = pac.Next(m);
        BuildThreat(game, ax, ay, 1 - pac.Off);
        if (_ghostAt[ax, ay] > (1 - pac.Off) / game.PacSpeed + 0.9f) return false;
        var on = Rollout(game, Direction.None, null, false);
        if (on >= Horizon) return false;
        var back = Rollout(game, Direction.None, null, true);
        return back > on + 0.2f;
    }

    /// <summary>Seconds he survives (up to <see cref="Horizon"/>) if he takes <paramref name="first"/> now.</summary>
    private float Rollout(Game game, Direction first, (int X, int Y)? goal, bool reverseNow)
    {
        var sim = game.CloneForSim();
        var m = sim.Maze;
        int[,]? field = null;
        if (goal is { } gl)
        {
            field = _field;
            m.Distances(gl.X, gl.Y, field);
        }
        var firstCall = first != Direction.None;
        sim.Pilot = s =>
        {
            if (firstCall)
            {
                firstCall = false;
                return first;
            }
            return Policy(s, field);
        };
        if (reverseNow) sim.Pac.Reverse(m);
        for (var t = 0f; t < Horizon; t += SimStep)
        {
            sim.Update(SimStep);
            if (sim.Phase == Phase.Dying) return t;
        }
        return Horizon;
    }

    /// <summary>Look-ahead driver: follow the goal's distance field, or (without one) stay away from ghosts.</summary>
    private static Direction Policy(Game s, int[,]? field)
    {
        var m = s.Maze;
        var p = s.Pac;
        var best = Direction.None;
        var bestScore = float.MinValue;
        var atGoal = field != null && field[p.X, p.Y] == 0;
        foreach (var d in Dirs.All)
        {
            if (!m.Step(p.X, p.Y, d, out var nx, out var ny)) continue;
            float score;
            if (field != null && !atGoal)
                score = field[nx, ny] < 0 ? -999 : -field[nx, ny];
            else
            {
                var near = 99f;
                foreach (var g in s.Ghosts)
                {
                    if (!(g.Dangerous || g.State == GhostState.Leaving)) continue;
                    var dx = Math.Abs(g.X - nx);
                    dx = Math.Min(dx, m.W - dx);
                    near = Math.Min(near, dx + Math.Abs(g.Y - ny));
                }
                score = near + m.Exits(nx, ny) * 0.3f;
            }
            if (d == p.Dir.Opposite()) score -= 1.2f;
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        return best;
    }

    private void Ensure(Maze m)
    {
        if (_maze == m) return;
        _maze = m;
        _tmp = new int[m.W, m.H];
        _ghostAt = new float[m.W, m.H];
        _pacAt = new float[m.W, m.H];
        _cost = new float[m.W, m.H];
        _best = new float[m.W, m.H];
        _queue = new int[m.W * m.H];
        _field = new int[m.W, m.H];
    }

    private int[,] _field = new int[1, 1];
    private int _lastDots = -1;
    private Maze? _maze2;
    private float _hungrySince;
    private float _bold = 1f;

    /// <summary>Earliest time (s) any ghost that can kill him could stand on each cell.</summary>
    private void BuildThreat(Game game, int fromX, int fromY, float startDelay = 0)
    {
        var m = game.Maze;
        for (var x = 0; x < m.W; x++)
        for (var y = 0; y < m.H; y++)
        {
            _ghostAt[x, y] = float.MaxValue;
            _pacAt[x, y] = float.MaxValue;
        }

        foreach (var g in game.Ghosts)
        {
            int sx, sy;
            float delay, speed = game.GhostSpeed * 1.1f;
            var noBack = Direction.None;
            switch (g.State)
            {
                case GhostState.Active:
                    (sx, sy) = g.Next(m);
                    delay = (1 - g.Off) / speed;
                    noBack = g.Dir.Opposite();
                    if (g.Frightened) delay = Math.Max(delay, game.Fright);
                    break;
                case GhostState.Leaving:
                    (sx, sy) = m.Exit;
                    delay = 0.4f + (g.Frightened ? game.Fright : 0);
                    break;
                case GhostState.Home:
                    (sx, sy) = m.Exit;
                    delay = Math.Max(0, g.HomeTimer) + 0.8f + (g.Frightened ? game.Fright : 0);
                    break;
                case GhostState.Entering:
                    (sx, sy) = m.Exit;
                    delay = 0.8f;
                    break;
                default: // eyes: back in the pen soon, then out again
                    (sx, sy) = m.Exit;
                    delay = 1.2f + Math.Abs(g.X - m.Exit.X + g.Y - m.Exit.Y) / game.EyesSpeed;
                    break;
            }

            m.Distances(sx, sy, _tmp, noBack);
            for (var x = 0; x < m.W; x++)
            for (var y = 0; y < m.H; y++)
            {
                if (_tmp[x, y] < 0) continue;
                var t = delay + _tmp[x, y] / speed;
                if (t < _ghostAt[x, y]) _ghostAt[x, y] = t;
            }
        }

        m.Distances(fromX, fromY, _tmp);
        var ps = game.PacSpeed;
        for (var x = 0; x < m.W; x++)
        for (var y = 0; y < m.H; y++)
            if (_tmp[x, y] >= 0)
                _pacAt[x, y] = startDelay / ps + _tmp[x, y] / ps;
    }

    /// <summary>How soon a ghost could reach him where he stands.</summary>
    private float Pressure(Game game) => _ghostAt[game.Pac.X, game.Pac.Y];

    /// <summary>
    ///     Cells he can reach through <paramref name="sx"/>,<paramref name="sy"/> (entered after
    ///     <paramref name="steps"/> tiles) while staying ahead of every ghost. Also whether a power pellet is
    ///     among them.
    /// </summary>
    private (int Room, bool Power, float Slack) Room(Game game, int sx, int sy, float steps)
    {
        var m = game.Maze;
        var ps = game.PacSpeed;
        var margin = Margin;
        var t0 = steps / ps;
        if (t0 + margin >= _ghostAt[sx, sy]) return (0, false, 0);
        var slack = Math.Min(_ghostAt[sx, sy] - t0, 1000f);
        for (var x = 0; x < m.W; x++)
        for (var y = 0; y < m.H; y++)
            _tmp[x, y] = -1;
        int head = 0, tail = 0;
        _tmp[sx, sy] = 0;
        _queue[tail++] = sy * m.W + sx;
        var power = m.HasPower(sx, sy);
        while (head < tail && tail < 200)
        {
            var c = _queue[head++];
            int x = c % m.W, y = c / m.W;
            // Standing next to where he came from does not count as an exit.
            foreach (var d in Dirs.All)
            {
                if (!m.Step(x, y, d, out var nx, out var ny) || _tmp[nx, ny] >= 0) continue;
                if (nx == game.Pac.X && ny == game.Pac.Y && head == 1 && steps >= 1) continue;
                var t = t0 + (_tmp[x, y] + 1) / ps;
                if (t + margin >= _ghostAt[nx, ny]) continue;
                _tmp[nx, ny] = _tmp[x, y] + 1;
                if (m.HasPower(nx, ny)) power = true;
                slack = Math.Max(slack, Math.Min(_ghostAt[nx, ny] - t, 1000f));
                _queue[tail++] = ny * m.W + nx;
            }
        }
        return (tail, power, slack);
    }

    private List<(int X, int Y)> HuntTargets(Game game)
    {
        var list = new List<(int, int)>();
        if (game.Fright <= 0.4f) return list;
        var m = game.Maze;
        foreach (var g in game.Ghosts)
        {
            if (!g.Frightened || g.State != GhostState.Active) continue;
            var (gx, gy) = g.Near(m);
            var dist = _pacAt[gx, gy] * game.PacSpeed;
            // He gains (pac - frightened) tiles per second on a fleeing ghost; only chase what he can catch.
            var catchTime = dist / Math.Max(0.5f, game.PacSpeed - game.FrightSpeed * 0.5f);
            if (catchTime < game.Fright - 0.3f) list.Add((gx, gy));
        }
        return list;
    }

    /// <summary>
    ///     Dijkstra from his tile, first move fixed to <paramref name="first"/>, to the nearest goal: a blue ghost
    ///     he can catch, the fruit, a power pellet when pressed, otherwise any dot. Cells cost more the tighter a
    ///     ghost can cut them off, and a lot when a ghost gets there first.
    /// </summary>
    private float PathCost(Game game, int px, int py, Direction first, bool pressed, List<(int X, int Y)> hunt)
    {
        var m = game.Maze;
        var margin = Margin;
        var fruit = game.Fruit;
        var noDots = m.DotsLeft == CountPower(m);
        for (var x = 0; x < m.W; x++)
        for (var y = 0; y < m.H; y++)
        {
            var slack = _ghostAt[x, y] - _pacAt[x, y];
            var c = slack < margin ? 1f + 79f * _bold : slack < 2.5f ? 1f + (2.5f - slack) * 3f * _bold : 1f;
            if (!m.HasDot(x, y)) c += 0.25f;
            // Save power pellets unless they are the point.
            if (m.HasPower(x, y) && !pressed && !noDots && hunt.Count == 0) c += 30f;
            _cost[x, y] = c;
            _best[x, y] = float.MaxValue;
        }

        bool IsGoal(int x, int y)
        {
            if (hunt.Count > 0) return hunt.Contains((x, y));
            if (fruit is { } f && f.X == x && f.Y == y) return true;
            if (pressed && m.HasPower(x, y)) return true;
            return m.HasDot(x, y) || m.HasPower(x, y);
        }

        if (!m.Step(px, py, first, out var sx, out var sy)) return float.MaxValue;
        _heap.Clear();
        _best[sx, sy] = _cost[sx, sy];
        _heap.Enqueue(sy * m.W + sx, _best[sx, sy]);
        var goalCost = float.MaxValue;
        while (_heap.TryDequeue(out var c, out var pri))
        {
            int x = c % m.W, y = c / m.W;
            if (pri > _best[x, y] + 1e-4f) continue;
            if (IsGoal(x, y))
            {
                goalCost = pri;
                Goal = (x, y);
                break;
            }
            foreach (var d in Dirs.All)
            {
                if (!m.Step(x, y, d, out var nx, out var ny)) continue;
                if (nx == px && ny == py) continue;
                var nc = pri + _cost[nx, ny];
                if (nc >= _best[nx, ny]) continue;
                _best[nx, ny] = nc;
                _heap.Enqueue(ny * m.W + nx, nc);
            }
        }

        // With a fruit or hunt goal out of reach, fall back to plain food.
        if (goalCost == float.MaxValue && (hunt.Count > 0 || pressed))
            return PathCost(game, px, py, first, false, new List<(int, int)>());
        return goalCost;
    }

    private static int CountPower(Maze m)
    {
        var n = 0;
        for (var x = 0; x < m.W; x++)
        for (var y = 0; y < m.H; y++)
            if (m.HasPower(x, y)) n++;
        return n;
    }
}
