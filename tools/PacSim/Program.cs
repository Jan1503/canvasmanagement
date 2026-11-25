using CanvasManagement.Extension.PacMan;

// PacSim [minutes] [difficulty] [ghosts] [caution]
var minutes = args.Length > 0 ? double.Parse(args[0]) : 30;
var diff = args.Length > 1 ? int.Parse(args[1]) : 3;
var ghosts = args.Length > 2 ? int.Parse(args[2]) : 4;
var caution = args.Length > 3 ? int.Parse(args[3]) : 5;
var pilot = new Autopilot { Caution = caution };
var game = new Game(31, 15, ghosts, diff) { Pilot = null };
game.Pilot = pilot.Decide;
game.Panic = pilot.Panic;
const float dt = 1 / 60f;
int deaths = 0, games = 1, levels = 0, ghostsEaten = 0, fruits = 0, stuck = 0, maxLevel = 1;
var reasons = new Dictionary<string, int>();
var lastScore = 0; var still = 0f;
var sw = System.Diagnostics.Stopwatch.StartNew();
for (double t = 0; t < minutes * 60; t += dt)
{
    game.Update(dt);
    var e = game.TakeEvents();
    if (e.HasFlag(GameEvent.Death)) { deaths++; if (deaths <= 8) Console.WriteLine($"DEATH L{game.Level} {pilot.Reason}: {pilot.Trace} | pac now {game.Pac.X},{game.Pac.Y} off {game.Pac.Off:0.00} {game.Pac.Dir} | " + string.Join(" ", game.Ghosts.Select(g => $"g{g.Index}:{g.State}{(g.Frightened?"F":"")}@{g.X},{g.Y}{g.Dir}")));  reasons[pilot.Reason] = reasons.GetValueOrDefault(pilot.Reason) + 1; }
    if (e.HasFlag(GameEvent.LevelClear)) levels++;
    if (e.HasFlag(GameEvent.GhostEaten)) ghostsEaten++;
    if (e.HasFlag(GameEvent.Fruit)) fruits++;
    maxLevel = Math.Max(maxLevel, game.Level);
    if (game.Phase == Phase.Playing)
    {
        if (game.Score == lastScore) still += dt; else { still = 0; lastScore = game.Score; }
        if (still > 20) { stuck++; still = 0; Console.WriteLine($"{pilot.Trace}"); Console.WriteLine($"stuck at level {game.Level}, dots {game.Maze.DotsLeft} pac {game.Pac.X},{game.Pac.Y}"); }
    }
    if (game.Phase == Phase.GameOver && game.PhaseTime > 1)
    {
        games++;
        game = new Game(31, 15, ghosts, diff) { Pilot = pilot.Decide, Panic = pilot.Panic };
        lastScore = 0;
    }
}
Console.WriteLine($"{minutes} min sim in {sw.Elapsed.TotalSeconds:0.0}s: games {games} deaths {deaths} levels cleared {levels} (max level {maxLevel}) ghosts eaten {ghostsEaten} fruits {fruits} stuck {stuck}");
Console.WriteLine("death while: " + string.Join(", ", reasons.Select(kv => $"{kv.Key} {kv.Value}")));
