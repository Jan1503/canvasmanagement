namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Manages the game state including score, lives, animations, and entity updates.
/// </summary>
public class GameState
{
    public const int DeathAnimationFrames = 90;
    public const int LevelStartAnimationFrames = 60;
    private const int FrightenedDuration = 300;
    private const int GhostRespawnDelay = 60;

    // Classic scatter/chase wave schedule (in ticks); after it runs out the ghosts chase permanently.
    private static readonly (GhostMode mode, int frames)[] ModeSchedule =
    {
        (GhostMode.Scatter, 180), (GhostMode.Chase, 560),
        (GhostMode.Scatter, 180), (GhostMode.Chase, 560),
        (GhostMode.Scatter, 140), (GhostMode.Chase, 600),
        (GhostMode.Scatter, 140)
    };

    // Store original canvas dimensions to prevent shrinkage on reset

    private int _frightenedTimer;
    private int _ghostCount;
    private GhostMode _mode = GhostMode.Scatter;
    private int _modePhase;
    private int _modeTimer;

    public GameState(int pixelWidth, int pixelHeight, int ghostCount = 4, int difficulty = 1)
    {
        // Store original canvas dimensions
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;

        _ghostCount = Math.Clamp(ghostCount, 1, 8);
        Difficulty = Math.Clamp(difficulty, 1, 10);
        Maze = new Maze(pixelWidth, pixelHeight);
        Lives = 3;
        Level = 1;

        var spawn = Maze.GetPacmanSpawn();
        PacMan = new PacManCharacter(spawn.X, spawn.Y);

        Ghosts = new List<Ghost>();
        SpawnGhosts();

        // Start with level intro animation
        IsLevelStartAnimation = true;
        AnimationFrame = 0;
    }

    public Maze Maze { get; private set; }
    public PacManCharacter PacMan { get; }
    public List<Ghost> Ghosts { get; }
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public int Level { get; private set; }
    public bool GameOver { get; private set; }
    public bool LevelComplete { get; private set; }
    public int Difficulty { get; private set; }

    /// <summary>Current global ghost mode (scatter or chase), driving ghost targeting and reversals.</summary>
    public GhostMode Mode => _mode;

    // Animation states
    public bool IsDeathAnimation { get; private set; }
    public bool IsLevelStartAnimation { get; private set; }
    public int AnimationFrame { get; private set; }

    // Return stored canvas dimensions instead of calculated maze dimensions
    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public void SetDifficulty(int difficulty)
    {
        Difficulty = Math.Clamp(difficulty, 1, 10);
    }

    public void SetGhostCount(int count)
    {
        var newCount = Math.Clamp(count, 1, 8);
        if (newCount == _ghostCount) return;

        _ghostCount = newCount;

        if (Ghosts.Count > _ghostCount)
        {
            Ghosts.RemoveRange(_ghostCount, Ghosts.Count - _ghostCount);
        }
        else if (Ghosts.Count < _ghostCount)
        {
            var spawns = Maze.GetGhostSpawns();
            var colors = new[] { GhostColor.Red, GhostColor.Pink, GhostColor.Cyan, GhostColor.Orange };
            for (var i = Ghosts.Count; i < _ghostCount && i < spawns.Length; i++)
                Ghosts.Add(new Ghost(spawns[i].X, spawns[i].Y, colors[i % colors.Length]));
        }
    }

    private void SpawnGhosts()
    {
        Ghosts.Clear();
        var spawns = Maze.GetGhostSpawns();
        var colors = new[]
        {
            GhostColor.Red, GhostColor.Pink, GhostColor.Cyan, GhostColor.Orange,
            GhostColor.Red, GhostColor.Pink, GhostColor.Cyan, GhostColor.Orange
        };

        for (var i = 0; i < Math.Min(_ghostCount, spawns.Length); i++)
            Ghosts.Add(new Ghost(spawns[i].X, spawns[i].Y, colors[i % colors.Length]));

        ResetMode();
    }

    private void ResetMode()
    {
        _mode = GhostMode.Scatter;
        _modePhase = 0;
        _modeTimer = 0;
    }

    /// <summary>Advances the scatter/chase wave timer (paused while ghosts are frightened).</summary>
    private void UpdateMode()
    {
        if (_frightenedTimer > 0) return;
        if (_modePhase >= ModeSchedule.Length)
        {
            _mode = GhostMode.Chase;
            return;
        }

        _modeTimer++;
        if (_modeTimer >= ModeSchedule[_modePhase].frames)
        {
            _modeTimer = 0;
            _modePhase++;
        }

        _mode = _modePhase < ModeSchedule.Length ? ModeSchedule[_modePhase].mode : GhostMode.Chase;
    }

    public void Update()
    {
        if (GameOver) return;

        // Handle animations
        if (IsDeathAnimation)
        {
            AnimationFrame++;
            if (AnimationFrame >= DeathAnimationFrames)
            {
                IsDeathAnimation = false;
                AnimationFrame = 0;

                if (Lives <= 0)
                {
                    GameOver = true;
                }
                else
                {
                    // Reset positions after death animation
                    var spawn = Maze.GetPacmanSpawn();
                    PacMan.Reset(spawn.X, spawn.Y);
                    SpawnGhosts();

                    // Brief level start animation
                    IsLevelStartAnimation = true;
                }
            }

            return;
        }

        if (IsLevelStartAnimation)
        {
            AnimationFrame++;
            if (AnimationFrame >= LevelStartAnimationFrames)
            {
                IsLevelStartAnimation = false;
                AnimationFrame = 0;
            }

            return;
        }

        // Normal gameplay update
        UpdateMode();
        PacMan.Update(Maze);

        var blinky = Ghosts.FirstOrDefault(g => g.GhostColorType == GhostColor.Red);
        foreach (var ghost in Ghosts) ghost.Update(Maze, PacMan.Position, PacMan.Direction, Difficulty, blinky, _mode);

        // Handle dead ghost respawning
        foreach (var ghost in Ghosts)
            if (ghost.State == GhostState.Dead)
            {
                ghost.RespawnTimer++;
                if (ghost.RespawnTimer >= GhostRespawnDelay)
                {
                    var spawns = Maze.GetGhostSpawns();
                    if (spawns.Length > 0)
                    {
                        ghost.Respawn(spawns[0].X, spawns[0].Y);
                        ghost.State = GhostState.Chase;
                        ghost.RespawnTimer = 0;
                    }
                }
            }

        // Check pellet collection
        var cellX = (int)Math.Round(PacMan.Position.X);
        var cellY = (int)Math.Round(PacMan.Position.Y);

        if (Maze.CollectPellet(cellX, cellY)) Score += 10;

        if (Maze.CollectPowerPellet(cellX, cellY))
        {
            Score += 50;
            _frightenedTimer = FrightenedDuration;
            foreach (var ghost in Ghosts)
                if (ghost.State != GhostState.Dead)
                    ghost.State = GhostState.Frightened;
        }

        if (_frightenedTimer > 0)
        {
            _frightenedTimer--;
            if (_frightenedTimer == 0)
                foreach (var ghost in Ghosts)
                    if (ghost.State == GhostState.Frightened)
                        ghost.State = GhostState.Chase;
        }

        // Check ghost collisions
        foreach (var ghost in Ghosts)
        {
            if (ghost.State == GhostState.Dead) continue;

            var dist = Vector2.Distance(PacMan.Position, ghost.Position);
            if (dist < 0.7f)
            {
                if (ghost.State == GhostState.Frightened)
                {
                    Score += 200;
                    ghost.State = GhostState.Dead;
                    ghost.RespawnTimer = 0;
                }
                else
                {
                    // Pac-Man caught! Start death animation
                    Lives--;
                    IsDeathAnimation = true;
                    AnimationFrame = 0;
                    return;
                }
            }
        }

        // Check level complete
        if (Maze.CountPellets() == 0) LevelComplete = true;
    }

    public void NextLevel()
    {
        Level++;
        LevelComplete = false;

        // Use stored canvas dimensions instead of maze dimensions
        Maze = new Maze(PixelWidth, PixelHeight);

        var spawn = Maze.GetPacmanSpawn();
        PacMan.Reset(spawn.X, spawn.Y);
        SpawnGhosts();

        // Level start animation
        IsLevelStartAnimation = true;
        AnimationFrame = 0;
    }

    public void Reset()
    {
        Score = 0;
        Lives = 3;
        Level = 1;
        GameOver = false;
        LevelComplete = false;
        IsDeathAnimation = false;
        IsLevelStartAnimation = true;
        AnimationFrame = 0;

        // Use stored canvas dimensions instead of maze dimensions
        Maze = new Maze(PixelWidth, PixelHeight);
        var spawn = Maze.GetPacmanSpawn();
        PacMan.Reset(spawn.X, spawn.Y);
        SpawnGhosts();
    }
}