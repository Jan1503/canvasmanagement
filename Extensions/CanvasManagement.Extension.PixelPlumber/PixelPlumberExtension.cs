using System.Timers;
using CanvasManagement.Interfaces;
using SkiaSharp;
using Timer = System.Timers.Timer;

namespace CanvasManagement.Extension.PixelPlumber;

/// <summary>
///     Original jump'n'run: teal-and-orange plumber, brick platforms, gold coins and bouncing slimes.
///     Autopilot until a key is pressed in Studio.
/// </summary>
[ExtensionInfo("Pixel Plumber",
    "Colorful jump'n'run — arrows / WASD + Space, or autopilot",
    "Games",
    IconResourceName = "pixel-plumber.svg")]
public class PixelPlumberExtension : ICanvasExtension, IDisposable
{
    // d outline, c teal cap, s skin, w/k eye, m mustache, o shirt, t overalls, n button, b shoe
    private static readonly string[] WalkA =
    {
        ".....dddddd.....",
        "....dccccccd....",
        "...dccssssccd...",
        "...dcswwksscd...",
        "....dcsssscd....",
        ".....dsmmssd....",
        "....doooooood...",
        "...dootttttood..",
        "...dotttntttod..",
        "....dtttttttd...",
        "....dt.dd.td....",
        "....dbd..dbd....",
        ".....d....d....."
    };

    private static readonly string[] WalkB =
    {
        ".....dddddd.....",
        "....dccccccd....",
        "...dccssssccd...",
        "...dcswwksscd...",
        "....dcsssscd....",
        ".....dsmmssd....",
        "....doooooood...",
        "...dootttttood..",
        "...dotttntttod..",
        "....dtttttttd...",
        ".....dtddtd.....",
        "....dbd..dbd....",
        "....d......d...."
    };

    private static readonly string[] JumpSpr =
    {
        ".....dddddd.....",
        "....dccccccd....",
        "...dccssssccd...",
        "...dcswwksscd...",
        "....dcsssscd....",
        ".....dsmmssd....",
        "....doooooood...",
        "...dootttttood..",
        "...dotttntttod..",
        "....dtttttttd...",
        "...dt......td...",
        "..dbd......dbd..",
        "..d..........d.."
    };

    private static readonly string[] CoinA =
    {
        "..ddyydd..",
        ".dyyyyyyd.",
        "dyyywyyyyd",
        "dyyykyyyyd",
        "dyyywyyyyd",
        ".dyyyyyyd.",
        "..ddyydd.."
    };

    private static readonly string[] CoinB =
    {
        "...dyyd...",
        "..dyyyyyd.",
        ".dyywyyyd.",
        ".dyykyyyd.",
        ".dyywyyyd.",
        "..dyyyyyd.",
        "...dyyd..."
    };

    private static readonly string[] Heart =
    {
        ".drrd.drrd.",
        "drrrrrrrrrd",
        "drrwrrrrrdd",
        ".drrrrrrrd.",
        "..drrrrrd..",
        "...drrd...",
        "....dd....."
    };

    private static readonly string[] StarA =
    {
        ".....y.....",
        "....yyy....",
        "yyyyywyyyyy",
        ".yyyyyyyyy.",
        "..yy.y.yy..",
        ".yy.....yy.",
        "yy.......yy"
    };

    private static readonly string[] StarB =
    {
        ".....w.....",
        "....yyy....",
        "yyyyyyyyyyy",
        ".yyyywyyyy.",
        "..yy.y.yy..",
        ".yy.....yy.",
        "y.........y"
    };

    private static readonly string[] Bolt =
    {
        "...dccyd...",
        "..dccyd....",
        ".dccyyyyyd.",
        "....dccyd..",
        "...dccyd...",
        "..dccyd....",
        ".dccyd....."
    };

    private static readonly string[] Spring =
    {
        "..dggggd..",
        ".dggwwggd.",
        "..dggggd..",
        "...dooood.",
        "..dddddd..",
        ".doooooood",
        "..dddddd.."
    };

    private static readonly string[] Gem =
    {
        "...dccd...",
        "..dccccd..",
        ".dcwccccd.",
        ".dccccccd.",
        "..dccccd..",
        "...dccd..."
    };

    private static readonly string[] SlimeA =
    {
        "...dmmmmd...",
        "..dmmmmmmd..",
        ".dmwwkkmmmd.",
        "dmmmmmmmmmmd",
        "dmmmmmmmmmmd",
        ".dmmmmmmmmd.",
        "..dddddddd.."
    };

    private static readonly string[] SlimeB =
    {
        "....dmmmd...",
        "..dmmmmmmd..",
        ".dmwwkkmmmd.",
        "dmmmmmmmmmmd",
        ".dmmmmmmmmd.",
        "..dmmmmmmd..",
        "...dddddd..."
    };

    // s spike, r rust shell, not stomable
    private static readonly string[] BugA =
    {
        "....s.s.....",
        "...dssssd...",
        "..drrrrrrd..",
        ".drwwkkrrrd.",
        "drrrrrrrrrrd",
        ".dbd....dbd."
    };

    private static readonly string[] BugB =
    {
        ".....s......",
        "...dssssd...",
        "..drrrrrrd..",
        ".drwwkkrrrd.",
        "drrrrrrrrrrd",
        "dbd......dbd"
    };

    // m wing membrane, flying, stomable
    private static readonly string[] BatA =
    {
        "d...d....d...d",
        "dd.dmmmmmmd.dd",
        ".dmwwkkmmmmmmd",
        "..dmmmmmmmmmmd",
        "...db....bd..."
    };

    private static readonly string[] BatB =
    {
        "..d........d..",
        "d.d.dmmmd.d.d.",
        "ddmwwkkmmmmmmd",
        ".dmmmmmmmmmmmd",
        "..db......bd.."
    };

    // hopper: squat / stretch
    private static readonly string[] HopA =
    {
        "...dyyyd....",
        "..dywwkyyd..",
        "...dyyyyd...",
        "..doooooood.",
        ".dtdddddtd..",
        "dbd......dbd"
    };

    private static readonly string[] HopB =
    {
        "...dyyyd....",
        "..dywwkyyd..",
        "...dyyyyd...",
        "...doooood..",
        "....dtdd....",
        "...db..bd...",
        "..d......d.."
    };

    // stationary spike plant — jump over
    private static readonly string[] Plant =
    {
        "....s.s.....",
        "...sgsgs....",
        "..sgggggs...",
        ".dggggggd...",
        "..dgggggd...",
        "...dgggd....",
        "....ddd.....",
        "...dDDDd...."
    };

    private static readonly string[] GrassHang =
    {
        "gGgG",
        "g..G",
        "G...",
        ".g.."
    };

    private static readonly string[] Grass =
    {
        "gGgGGggG",
        "GggggGgg",
        "dddddddd",
        "dDddDddd",
        "dddddDdd",
        "dDddddDd",
        "dddddddd",
        "DdddDddd"
    };

    private static readonly string[] Brick =
    {
        "oooooooo",
        "oBBBBBbo",
        "oBBBBBbo",
        "oooooooo",
        "BBoBBBBo",
        "BBoBBBBo",
        "oooooooo",
        "bbbbbboo"
    };

    private static readonly string[] Cloud =
    {
        ".....11111......",
        "...111111111....",
        ".1111111111111..",
        "111111111111111.",
        ".1111111111111.."
    };

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly Random _random = new();
    private readonly List<Platform> _plats = new();
    private readonly List<Pit> _pits = new();
    private readonly List<Pickup> _pickups = new();
    private readonly List<Foe> _foes = new();
    private readonly List<Spark> _sparks = new();

    private SKBitmap? _backBuffer;
    private Timer? _timer;
    private int _px = 2;
    private int _frame;
    private float _cam;
    private float _genX;

    private float _pxX, _pxY, _vx, _vy;
    private float _gravity, _jumpV, _walkSpd, _baseJump, _baseWalk;
    private int _pw, _ph;
    private bool _onGround;
    private bool _faceRight = true;
    private int _lives;
    private int _score, _best;
    private int _crashTimer;
    private bool _human;
    private int _holdX;
    private int _starTimer, _speedTimer, _springTimer;
    private string _toast = "";
    private int _toastLife;
    private float _lastPowerX;

    internal PixelPlumberExtension(ICanvas canvas) => _canvas = canvas;

    [ExtensionParameter("Game Speed", "Frame interval in milliseconds (lower = faster)", DefaultValue = 28,
        MinValue = 16, MaxValue = 80, Unit = "ms", Order = 1)]
    public int GameSpeed { get; set; } = 28;

    [ExtensionParameter("Difficulty", "Gap size, slime count and speed", DefaultValue = 3, MinValue = 1, MaxValue = 10,
        Order = 2)]
    public int Difficulty { get; set; } = 3;

    [ExtensionParameter("Show Score", "Show score and lives", DefaultValue = true, Order = 3)]
    public bool ShowScore { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render HUD with the crisp bitmap (BDF) font", DefaultValue = true, Order = 4)]
    public bool UseBdfFont { get; set; } = true;

    [ExtensionParameter("Font Size", "HUD height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0, MaxValue = 48,
        Unit = "px", Order = 5)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Pilot", "AI runs until you press a key in Studio", DefaultValue = true, Order = 6)]
    public bool AutoPilot { get; set; } = true;

    private GameSound? _snd;
    private GameSound _sound => _snd ??= new GameSound(_canvas, GameSong.Adventure);

    [ExtensionParameter("Sound", "Sound effects on the audio board", DefaultValue = true, Order = 20)]
    public bool Sound { get => _sound.Effects; set => _sound.Effects = value; }

    [ExtensionParameter("Music", "Background music", DefaultValue = true, Order = 21)]
    public bool Music { get => _sound.Music; set => _sound.Music = value; }

    [ExtensionParameter("Volume", "How loud this game is on the audio board", DefaultValue = 70, MinValue = 0,
        MaxValue = 100, Order = 22)]
    public int Volume { get => (int)MathF.Round(_sound.Volume * 100); set => _sound.Volume = Math.Clamp(value, 0, 100) / 100f; }

    [ExtensionParameter("Music Volume", "Music level relative to the effects", DefaultValue = 50, MinValue = 0,
        MaxValue = 100, Order = 23)]
    public int MusicVolume
    {
        get => (int)MathF.Round(_sound.MusicVolume * 100);
        set => _sound.MusicVolume = Math.Clamp(value, 0, 100) / 100f;
    }

    public string Name => "Pixel Plumber";
    public bool IsRunning { get; private set; }

    private float GroundY => _canvas.Height - Math.Max(16, 16 * _px);

    public void Dispose()
    {
        Stop();
        _backBuffer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _px = PixelArt.Scale(_canvas.Height);
            _human = false;
            _holdX = 0;
            Reset(full: true);
            _backBuffer?.Dispose();
            _backBuffer = new SKBitmap(_canvas.Width, _canvas.Height);
            _timer = new Timer(GameSpeed) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
            _sound.Stop();
            _backBuffer?.Dispose();
            _backBuffer = null;
            try { _canvas.Clear(SKColors.Black); } catch { }
        }
    }

    private void Reset(bool full)
    {
        _pw = WalkA[0].Length * _px;
        _ph = WalkA.Length * _px;
        _gravity = Math.Max(0.28f, _canvas.Height * 0.0055f);
        _baseJump = -(float)Math.Sqrt(Math.Max(2, 2 * _gravity * _canvas.Height * 0.38f));
        _baseWalk = Math.Max(1.4f, (1.6f + Difficulty * 0.12f) * _px);
        _jumpV = _baseJump;
        _walkSpd = _baseWalk;
        _plats.Clear();
        _pits.Clear();
        _pickups.Clear();
        _foes.Clear();
        _sparks.Clear();
        _starTimer = _speedTimer = _springTimer = 0;
        _toast = "";
        _toastLife = 0;
        _lastPowerX = -1_000_000;
        _cam = 0;
        _genX = 0;
        _pxX = _canvas.Width * 0.18f;
        _pxY = GroundY - _ph;
        _vx = _vy = 0;
        _onGround = true;
        _faceRight = true;
        _crashTimer = 0;
        if (full)
        {
            _score = 0;
            _lives = 3;
        }

        StampGround(0, _canvas.Width * 1.4f);
        // The first stretch is safe ground; the level proper starts after it.
        _genX = _canvas.Width * 1.4f;
        _stuckX = _pxX;
        _stuckTicks = 0;
        _lastPlan = new PlanSpec(0, 1, 1, -1);
        while (_genX < _cam + _canvas.Width * 2.2f) GenerateAhead();
    }

    private void StampGround(float x, float w) =>
        _plats.Add(new Platform { X = x, Y = GroundY, W = w, H = _canvas.Height - GroundY + 4, Brick = false });

    private void GenerateAhead()
    {
        var tile = Math.Max(8, 6 * _px);
        var run = tile * (7 + _random.Next(8));
        StampGround(_genX, run);
        var mid = _genX + run * 0.45f;
        var runEnd = _genX + run;

        float brickL = 0, brickR = 0;
        if (_random.Next(100) < 70)
        {
            // High enough that the hero can jump a foe underneath without bumping his head,
            // low enough to reach from the ground.
            var minGap = _ph + 13 * _px;
            var py = GroundY - Math.Max(minGap, tile * (3 + _random.Next(3)));
            var pw = tile * (3 + _random.Next(4));
            brickL = mid - pw * 0.3f;
            brickR = brickL + pw;
            _plats.Add(new Platform { X = brickL, Y = py, W = pw, H = 8 * _px, Brick = true });
            ScatterLoot(mid - pw * 0.2f, py - 5 * _px);
        }
        else
            ScatterLoot(mid, GroundY - 8 * _px);

        if (_random.Next(100) < 42 + Difficulty * 4)
        {
            var roll = _random.Next(100);
            var kind = roll < 28 ? FoeKind.Slime
                : roll < 48 ? FoeKind.SpikeBug
                : roll < 68 ? FoeKind.Bat
                : roll < 84 ? FoeKind.Hopper
                : FoeKind.Plant;
            var sw = FoeSize(kind).w;
            var ground = GroundY;
            var y = kind == FoeKind.Bat ? ground - 18 * _px : ground;
            var fx = mid + run * 0.2f;
            // Foes you must jump over stay out from under the bricks (and off the run's ends).
            if (kind is FoeKind.Plant or FoeKind.SpikeBug && brickR > brickL && fx + sw > brickL - _pw && fx < brickR + _pw)
                fx = Math.Min(runEnd - sw - tile, brickR + _pw * 2);
            // A foe that must be jumped needs a landing spot before the next gap: keep it well clear of the
            // run's end (a pit may follow), or make it a slime that can be stomped instead.
            if (kind is FoeKind.Plant or FoeKind.SpikeBug)
            {
                var air = 2f * Math.Abs(_baseJump) / Math.Max(0.2f, _gravity);
                var clear = _baseWalk * air + _pw;
                fx = Math.Min(fx, runEnd - sw - clear);
            }
            if (fx < mid || fx > runEnd - sw) kind = FoeKind.Slime;
            _foes.Add(new Foe
            {
                Kind = kind,
                X = fx,
                Y = y,
                BaseY = y,
                Hue = _random.Next(360),
                Dir = kind == FoeKind.Plant ? 0 : (_random.Next(2) == 0 ? -1 : 1),
                Left = mid,
                Right = runEnd - sw,
                Phase = _random.Next(100)
            });
        }

        _genX = runEnd;
        if (_random.Next(100) < 18 + Difficulty * 2)
        {
            var air = 2f * Math.Abs(_jumpV) / Math.Max(0.2f, _gravity);
            var jumpDist = _walkSpd * air * 0.55f;
            var pitW = Math.Clamp(tile * (2.2f + Difficulty * 0.15f), tile * 2f, Math.Max(tile * 2.2f, jumpDist));
            _pits.Add(new Pit { X = _genX, W = pitW, Lava = _random.Next(100) < 35 });
            _genX += pitW;
        }
    }

    private void ScatterLoot(float x, float y)
    {
        // Most stretches have nothing. Occasional 1–2 coins, not a row of goodies.
        if (_random.Next(100) < 36)
        {
            var n = 1 + _random.Next(2);
            for (var i = 0; i < n; i++)
                _pickups.Add(new Pickup
                {
                    X = x + i * 6 * _px,
                    Y = y - _random.Next(3) * _px,
                    Kind = PickupKind.Coin
                });
        }

        // One power-up every ~2 screens, not on every platform.
        var gap = Math.Max(220f, _canvas.Width * 2.1f);
        if (_genX - _lastPowerX < gap || _random.Next(100) >= 70)
            return;

        _pickups.Add(new Pickup
        {
            X = x + 8 * _px,
            Y = y - 5 * _px,
            Kind = RollPower()
        });
        _lastPowerX = _genX;
    }

    private PickupKind RollPower()
    {
        var r = _random.Next(100);
        if (r < 28) return PickupKind.Heart;
        if (r < 50) return PickupKind.Star;
        if (r < 70) return PickupKind.Speed;
        if (r < 88) return PickupKind.Spring;
        return PickupKind.Gem;
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRunning || _backBuffer == null) return;
            try
            {
                if (_timer != null && Math.Abs(_timer.Interval - GameSpeed) > 0.5) _timer.Interval = GameSpeed;
                if (_crashTimer > 0)
                {
                    _crashTimer--;
                    if (_crashTimer == 0)
                    {
                        if (_lives <= 0) Reset(full: true);
                        else Reset(full: false);
                    }
                }
                else
                    Update();

                Render();
                _sound.Duck(_lives <= 0 && _crashTimer > 0 ? 0.3f : 1f);
                _sound.Update();
                _frame++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PixelPlumber] {ex.Message}");
            }
        }
    }

    private void Update()
    {
        while (_genX < _cam + _canvas.Width * 2.4f) GenerateAhead();

        if (_starTimer > 0) _starTimer--;
        if (_speedTimer > 0) _speedTimer--;
        if (_springTimer > 0) _springTimer--;
        if (_toastLife > 0) _toastLife--;
        _walkSpd = _baseWalk * (_speedTimer > 0 ? 1.55f : 1f);
        _jumpV = _baseJump * (_springTimer > 0 ? 1.38f : 1f);

        var walk = 0;
        if (AutoPilot && !_human) RunAi(out walk);
        else walk = _holdX;
        if (walk != 0) _faceRight = walk > 0;
        _vx = walk * _walkSpd;

        _vy += _gravity;
        Move(_vx, 0);
        _onGround = false;
        Move(0, _vy);
        if (_onGround) _vy = Math.Min(_vy, 0);

        _cam = Math.Max(0, _pxX - _canvas.Width * 0.28f);
        PruneBehindCamera();

        for (var i = _foes.Count - 1; i >= 0; i--)
        {
            var s = _foes[i];
            StepFoe(ref s);
            _foes[i] = s;
            var spr = FoeSprite(s.Kind, _frame);
            var sw = spr[0].Length * _px;
            var sh = spr.Length * _px;
            var sy = s.Y - sh;
            if (!Overlap(_pxX + 2, _pxY + 4, _pw - 4, _ph - 4, s.X, sy, sw, sh)) continue;

            var feet = _pxY + _ph;
            if (CanStomp(s.Kind) && _vy > 0 && feet < sy + sh * 0.55f)
            {
                _foes.RemoveAt(i);
                _vy = _jumpV * 0.62f;
                _onGround = false;
                _score += s.Kind == FoeKind.Bat ? 80 : 50;
                _sound.Play(GameSfx.Stomp);
                _gfx.Burst(s.X + sw * 0.5f, sy + sh * 0.5f, PixelArt.Hsv(s.Hue, 0.8f, 1f), 14, 2f, 0.6f, 0.06f);
                _gfx.Pop(s.Kind == FoeKind.Bat ? "+80" : "+50", s.X + sw * 0.5f - _cam, sy - 2, SKColors.White);
                Burst(s.X + sw * 0.5f, sy, PixelArt.Hsv(s.Hue, 0.8f, 1f));
                continue;
            }

            if (_starTimer > 0)
            {
                _foes.RemoveAt(i);
                _score += 40;
                _sound.Play(GameSfx.Hit, 0.7f);
                Burst(s.X + sw * 0.5f, sy, new SKColor(255, 230, 80));
                continue;
            }

            Die();
            return;
        }

        for (var i = _pickups.Count - 1; i >= 0; i--)
        {
            var p = _pickups[i];
            var spr = PickupSprite(p.Kind, _frame);
            var bob = MathF.Sin((_frame + i * 7) * 0.12f) * 2 * _px;
            if (!Overlap(_pxX, _pxY, _pw, _ph, p.X, p.Y + bob, spr[0].Length * _px, spr.Length * _px))
                continue;
            Collect(p);
            _pickups.RemoveAt(i);
        }

        if (_pxY > _canvas.Height + 8)
        {
            Die();
            return;
        }

        _score = Math.Max(_score, (int)(_pxX / 8));
        _best = Math.Max(_best, _score);

        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i];
            p.X += p.Vx;
            p.Y += p.Vy;
            p.Vy += 0.12f;
            p.Life--;
            if (p.Life <= 0) _sparks.RemoveAt(i);
            else _sparks[i] = p;
        }
    }

    // ------------------------------------------------------------------ autopilot

    private struct Body
    {
        public float X, Y, Vy;
        public bool OnGround;
    }

    private readonly struct PlanSpec(int k, int w1, int w2, int jump, bool reflex = false)
    {
        public readonly int K = k, W1 = w1, W2 = w2, Jump = jump;

        /// <summary>No script: the runner's reflexes from the first tick.</summary>
        public readonly bool Reflex = reflex;

        /// <summary>The same plan one tick later.</summary>
        public PlanSpec Advance() => Reflex
            ? this
            : new PlanSpec(Math.Max(0, K - 1), K > 1 ? W1 : W2, W2, Jump > 0 ? Jump - 1 : Jump == 0 ? -1 : Jump);
        public int Walk(int t) => t < K ? W1 : W2;
    }

    private static readonly PlanSpec[] Plans = BuildPlans();
    private PlanSpec _lastPlan = new(0, 1, 1, -1);
    private static readonly bool Debug = Environment.GetEnvironmentVariable("PLUMBER_DEBUG") == "1";
    private bool DebugTrace;
    private Foe[] _simFoes = [];
    private float _stuckX, _lastScore;
    private bool _wasGround = true;
    private int _stuckTicks;
    private bool[] _simTaken = [];

    private static PlanSpec[] BuildPlans()
    {
        var list = new List<PlanSpec>();
        foreach (var k in new[] { 0, 6, 14 })
        foreach (var w1 in new[] { 1, 0, -1 })
        foreach (var w2 in new[] { 1, 0, -1 })
        foreach (var j in new[] { -1, 0, 4, 9, 15 })
        {
            if (k == 0 && w1 != w2) continue; // same as k=0 with w2
            list.Add(new PlanSpec(k, w1, w2, j));
        }
        list.Add(new PlanSpec(0, 1, 1, -1, true));
        return list.ToArray();
    }

    /// <summary>
    ///     Demo player. Every tick it plays each candidate plan forward with the game's own physics, foe
    ///     movement and stomp rules for about two and a half seconds, then takes the first step of the best one:
    ///     alive first, then distance made, coins and power-ups picked up, foes stomped. Plans are short
    ///     combinations of "walk / stand / back off, then something else" with a jump at a chosen moment, so it
    ///     waits for a hopper to land, runs under a bat, jumps late over a pit and grabs the coin on a brick.
    /// </summary>
    private void RunAi(out int walk)
    {
        const int horizon = 80;
        // The plan chosen last tick, one tick further along. Sticking to it (unless something is clearly
        // better) stops the "wait a moment, then jump" plan from winning anew every tick forever.
        var carry = _lastPlan.Advance();
        var best = carry;
        var bestScore = Simulate(carry, horizon) + 25f;
        foreach (var plan in Plans)
        {
            var score = Simulate(plan, horizon);
            if (score > bestScore)
            {
                bestScore = score;
                best = plan;
            }
        }

        if (Debug && _stuckTicks == 50)
        {
            var dead = Plans.Count(pl => Simulate(pl, horizon) < -50000);
            DebugTrace = true;
            Simulate(new PlanSpec(0, 1, 1, -1), horizon);
            Simulate(new PlanSpec(0, 1, 1, -1, true), horizon);
            Simulate(new PlanSpec(6, 0, 1, -1), horizon);
            DebugTrace = false;
            Console.WriteLine("   " + string.Join(" | ", Plans.Select(pl => (pl, sc: Simulate(pl, horizon))).OrderByDescending(q => q.sc).Take(8).Select(q => $"k{q.pl.K} {q.pl.W1}/{q.pl.W2} j{q.pl.Jump}={q.sc:0}")));
            Console.WriteLine($"[AI] x {_pxX:0} y {_pxY:0} ground {_onGround} best k{best.K} {best.W1}/{best.W2} j{best.Jump} score {bestScore:0} dead {dead}/{Plans.Length} foes {string.Join(",", _foes.Where(f => f.X < _pxX + 120).Select(f => $"{f.Kind}@{f.X - _pxX:0}"))} pits {string.Join(",", _pits.Where(q => q.X > _pxX - 20 && q.X < _pxX + 150).Select(q => $"{q.X - _pxX:0}w{q.W:0}"))}");
        }
        // Never stall for good: if nothing has moved him forward for a few seconds, just go.
        if (_pxX > _stuckX + 8 * _px) { _stuckX = _pxX; _stuckTicks = 0; }
        else if (++_stuckTicks > 90)
        {
            // Stuck for a while: take the best plan that at least sets off to the right.
            var go = Plans.Where(p => p.Walk(0) == 1).Select(p => (p, sc: Simulate(p, horizon))).MaxBy(q => q.sc);
            best = go.p;
            if (_stuckTicks > 200) _stuckTicks = 0;
        }

        _lastPlan = best;
        _lastScore = bestScore;
        walk = best.Walk(0);
        if (best.Jump == 0) DoJump();
        else if (best.Reflex && _onGround)
        {
            if (_simFoes.Length < _foes.Count) _simFoes = new Foe[_foes.Count * 2];
            _foes.CopyTo(_simFoes);
            if (Reflex(new Body { X = _pxX, Y = _pxY, Vy = _vy, OnGround = true }, _foes.Count)) DoJump();
        }
    }

    private float Simulate(PlanSpec plan, int horizon)
    {
        var b = new Body { X = _pxX, Y = _pxY, Vy = _vy, OnGround = _onGround };
        if (_simFoes.Length < _foes.Count) _simFoes = new Foe[_foes.Count * 2];
        _foes.CopyTo(_simFoes);
        var foes = _foes.Count;
        if (_simTaken.Length < _pickups.Count) _simTaken = new bool[_pickups.Count * 2];
        Array.Clear(_simTaken);
        var startX = _pxX;
        var midX = _pxX;
        var idle = 0;
        var gain = 0f;
        var star = _starTimer;
        var dead = -1;

        for (var t = 0; t < horizon; t++)
        {
            // The plan covers the first moments; after that a simple runner plays on (run right, hop gaps
            // and foes), so a plan is judged by where it leaves him, not by the second obstacle it never saw.
            var scripted = !plan.Reflex && t < Math.Max(plan.K, plan.Jump) + 8;
            var w = scripted ? plan.Walk(t) : 1;
            if (w <= 0 && t < 30) idle++;
            // Power-ups run out during the look-ahead, exactly as they will in the game.
            var walkSpd = _baseWalk * (_speedTimer - t > 0 ? 1.55f : 1f);
            var jumpV = _baseJump * (_springTimer - t > 0 ? 1.38f : 1f);
            var jump = scripted ? t == plan.Jump : b.OnGround && Reflex(b, foes);
            if (jump && b.OnGround)
            {
                b.Vy = jumpV;
                b.OnGround = false;
            }

            b.Vy += _gravity;
            MoveBody(ref b, w * walkSpd, 0);
            b.OnGround = false;
            MoveBody(ref b, 0, b.Vy);
            if (b.OnGround) b.Vy = Math.Min(b.Vy, 0);
            if (star > 0) star--;

            for (var i = 0; i < foes; i++)
            {
                ref var s = ref _simFoes[i];
                if (s.Kind == (FoeKind)(-1)) continue;
                StepFoe(ref s);
                var fs = FoeSprite(s.Kind, _frame + t);
                var sw = fs[0].Length * _px;
                var sh = fs.Length * _px;
                var sy = s.Y - sh;
                if (!Overlap(b.X + 2, b.Y + 4, _pw - 4, _ph - 4, s.X, sy, sw, sh)) continue;
                if (CanStomp(s.Kind) && b.Vy > 0 && b.Y + _ph < sy + sh * 0.55f)
                {
                    b.Vy = jumpV * 0.62f;
                    b.OnGround = false;
                    gain += 40;
                    s.Kind = (FoeKind)(-1);
                }
                else if (star > 0)
                {
                    gain += 30;
                    s.Kind = (FoeKind)(-1);
                }
                else
                {
                    dead = t;
                    break;
                }
            }
            if (dead >= 0) break;

            for (var i = 0; i < _pickups.Count; i++)
            {
                if (_simTaken[i]) continue;
                var p = _pickups[i];
                var spr = PickupSprite(p.Kind, 0);
                if (!Overlap(b.X, b.Y, _pw, _ph, p.X, p.Y - 2 * _px, spr[0].Length * _px, spr.Length * _px + 4 * _px))
                    continue;
                _simTaken[i] = true;
                gain += p.Kind == PickupKind.Coin ? 25 : 60;
            }

            if (b.Y > _canvas.Height + 8)
            {
                dead = t;
                break;
            }
            if (t == horizon / 2) midX = b.X;
        }

        // Still in the air when time runs out: finish the jump (right, no new jumps) to see if it lands.
        var endX = b.X;
        if (dead < 0 && !b.OnGround)
        {
            for (var t = 0; t < 40 && !b.OnGround; t++)
            {
                b.Vy += _gravity;
                MoveBody(ref b, _baseWalk, 0);
                b.OnGround = false;
                MoveBody(ref b, 0, b.Vy);
                if (b.Y > _canvas.Height + 8)
                {
                    dead = horizon;
                    break;
                }
            }
        }
        b.X = endX;

        if (DebugTrace) Console.WriteLine($"      sim k{plan.K} {plan.W1}/{plan.W2} j{plan.Jump} r{plan.Reflex}: dead {dead} end x {b.X - startX:0} y {b.Y:0} ground {b.OnGround} mid {midX - startX:0} gain {gain}");
        // A death far ahead comes from the simple runner, not from this plan: weigh it, do not veto it.
        if (dead >= 0 && dead < horizon * 0.7f) return -100000 + dead * 100;
        // The closer to the horizon, the less it says about this plan.
        var late = dead >= 0 ? (horizon - dead) * 6f : 0f;

        // Ending in mid-air over nothing is not "alive" yet: falling below the floor line is a loss.
        // Ground made early counts extra, so "wait a little, then go" never beats going now.
        var progress = b.X - startX + 0.6f * (midX - startX) - late - idle * 1.5f;
        return progress + gain * _px;
    }

    private bool Reflex(Body b, int foes)
    {
        var feetY = b.Y + _ph + 1;
        if (GroundUnder(b.X + _pw + _walkSpd * 2, feetY) == null) return true;
        for (var i = 0; i < foes; i++)
        {
            var s = _simFoes[i];
            if (s.Kind == (FoeKind)(-1) || s.Kind == FoeKind.Bat) continue;
            var dx = s.X - (b.X + _pw);
            if (dx > 0 && dx < _walkSpd * 7) return true;
        }
        return false;
    }

    private void MoveBody(ref Body b, float dx, float dy)
    {
        b.X += dx;
        b.Y += dy;
        foreach (var p in _plats)
        {
            if (!Overlap(b.X, b.Y, _pw, _ph, p.X, p.Y, p.W, p.H)) continue;
            if (dx > 0) b.X = p.X - _pw;
            else if (dx < 0) b.X = p.X + p.W;
            if (dy > 0)
            {
                b.Y = p.Y - _ph;
                b.OnGround = true;
                b.Vy = 0;
            }
            else if (dy < 0)
            {
                b.Y = p.Y + p.H;
                b.Vy = 0;
            }
        }
    }

    private void Move(float dx, float dy)
    {
        _pxX += dx;
        _pxY += dy;
        foreach (var p in _plats)
        {
            if (!Overlap(_pxX, _pxY, _pw, _ph, p.X, p.Y, p.W, p.H)) continue;
            if (dx > 0) _pxX = p.X - _pw;
            else if (dx < 0) _pxX = p.X + p.W;
            if (dy > 0)
            {
                _pxY = p.Y - _ph;
                _onGround = true;
                _vy = 0;
            }
            else if (dy < 0)
            {
                _pxY = p.Y + p.H;
                _vy = 0;
            }
        }
    }

    private Platform? GroundUnder(float x, float y)
    {
        foreach (var p in _plats)
            if (x >= p.X && x <= p.X + p.W && y >= p.Y - 3 && y <= p.Y + p.H + 6)
                return p;
        return null;
    }

    private static bool Overlap(float x, float y, float w, float h, float ox, float oy, float ow, float oh) =>
        x < ox + ow && x + w > ox && y < oy + oh && y + h > oy;

    private void DoJump()
    {
        if (!_onGround) return;
        _sound.Play(GameSfx.Jump, 0.6f, 0, _springTimer > 0 ? 0.8f : 1f);
        _vy = _jumpV;
        _onGround = false;
        Burst(_pxX + _pw * 0.5f, _pxY + _ph, new SKColor(200, 170, 90));
    }

    private void Die()
    {
        _lives--;
        _crashTimer = 36;
        _sound.Play(_lives <= 0 ? GameSfx.GameOver : GameSfx.Die, 0.8f);
        _gfx.Burst(_pxX + _pw / 2f, _pxY + _ph / 2f, new SKColor(255, 90, 60), 24, 2.6f, 0.8f, 0.08f);
        _gfx.Flash(new SKColor(255, 60, 40), 0.6f);
        _gfx.Shake(5);
        if (Debug)
            Console.WriteLine($"[DIE] x {_pxX:0} y {_pxY:0} vy {_vy:0.0} ground {_onGround} stuck {_stuckTicks} plan k{_lastPlan.K} {_lastPlan.W1}/{_lastPlan.W2} j{_lastPlan.Jump} last {_lastScore:0} foes {string.Join(",", _foes.Where(f => Math.Abs(f.X - _pxX) < 60).Select(f => $"{f.Kind}@{f.X - _pxX:0},{f.Y - _pxY:0}"))}");
        Burst(_pxX + _pw * 0.5f, _pxY + _ph * 0.5f, new SKColor(255, 80, 60));
    }

    private void Burst(float x, float y, SKColor col)
    {
        for (var i = 0; i < 8; i++)
            _sparks.Add(new Spark
            {
                X = x, Y = y,
                Vx = (float)(_random.NextDouble() * 2 - 1) * _px,
                Vy = (float)(_random.NextDouble() * -2) * _px,
                Life = 10 + _random.Next(8),
                Color = col
            });
    }

    [ExtensionMethod("Move Left", "Hold left — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Left|A", Order = 1)]
    public void MoveLeft()
    {
        lock (_lock) { _human = true; _holdX = -1; }
    }

    [ExtensionMethod("Move Right", "Hold right — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Right|D", Order = 2)]
    public void MoveRight()
    {
        lock (_lock) { _human = true; _holdX = 1; }
    }

    [ExtensionMethod("Release Left", "Release left",
        Category = "Controls", KeyboardShortcut = "Left:up|A:up", Order = 3)]
    public void ReleaseLeft()
    {
        lock (_lock) { if (_holdX < 0) _holdX = 0; }
    }

    [ExtensionMethod("Release Right", "Release right",
        Category = "Controls", KeyboardShortcut = "Right:up|D:up", Order = 4)]
    public void ReleaseRight()
    {
        lock (_lock) { if (_holdX > 0) _holdX = 0; }
    }

    [ExtensionMethod("Jump", "Jump — takes over from autopilot",
        Category = "Controls", KeyboardShortcut = "Space|Up|W", Order = 5)]
    public void Jump()
    {
        lock (_lock)
        {
            _human = true;
            if (_crashTimer > 0)
            {
                _crashTimer = 0;
                Reset(full: _lives <= 0);
            }

            DoJump();
        }
    }

    private readonly GameFx _gfx = new();
    private readonly System.Diagnostics.Stopwatch _gclock = System.Diagnostics.Stopwatch.StartNew();
    private double _glast;

    /// <summary>
    ///     A sun and drifting clouds over the hills, glowing power-ups, dust when he runs and lands, a rainbow
    ///     trail with the star, bursts and score pop-ups for stomps and coins, and a shake when he gets hurt.
    /// </summary>
    private void Render()
    {
        var bb = _backBuffer;
        if (bb == null) return;
        var gnow = _gclock.Elapsed.TotalSeconds;
        var gdt = (float)Math.Clamp(gnow - _glast, 0.001, 0.1);
        _glast = gnow;
        _gfx.Update(gdt);
        using var canvas = new SKCanvas(bb);
        var w = _canvas.Width;
        var h = _canvas.Height;

        canvas.Clear(new SKColor(18, 10, 28));
        using (var sky = new SKPaint())
        {
            sky.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, GroundY),
                new[] { new SKColor(70, 170, 255), new SKColor(255, 196, 120) },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, w, GroundY, sky);
        }

        GameFx.GlowDisc(canvas, w * 0.78f, GroundY * 0.32f, h * 0.2f, new SKColor(255, 230, 150), 0.4f);
        using (var sun = new SKPaint { Color = new SKColor(255, 240, 170), IsAntialias = false })
            PixelArt.Disc(canvas, sun, (int)(w * 0.78f), (int)(GroundY * 0.32f), Math.Max(4, h / 18));
        using (var cloud = new SKPaint { Color = new SKColor(255, 255, 255, 220), IsAntialias = false })
            for (var i = 0; i < 5; i++)
            {
                var cx = ((i * 97 - _cam * 0.08f - _gfx.Time * 3) % (w + 60) + w + 60) % (w + 60) - 30;
                var cy = 8 + i * 13 % 30;
                canvas.DrawRect(cx, cy, 22, 4, cloud);
                canvas.DrawRect(cx + 5, cy - 3, 12, 3, cloud);
            }
        DrawHills(canvas, 0.18f, new SKColor(186, 72, 196), 28);
        DrawHills(canvas, 0.42f, new SKColor(98, 42, 154), 16);

        _gfx.BeginShake(canvas);
        using var pitPaint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        foreach (var pit in _pits)
        {
            var sx = pit.X - _cam;
            if (sx + pit.W < -4 || sx > w + 4) continue;
            DrawPit(canvas, pitPaint, pit, sx, w, h);
        }

        foreach (var p in _plats)
        {
            var sx = p.X - _cam;
            if (sx + p.W < -8 || sx > w + 8) continue;
            Tile(canvas, p.Brick ? Brick : Grass, sx, p.Y, p.W, p.H, p.Brick ? ChBrick : ChGrass);
        }

        var coinSpr = _frame / 6 % 2 == 0 ? CoinA : CoinB;
        foreach (var p in _pickups)
        {
            var sx = p.X - _cam;
            if (sx < -14 || sx > w + 14) continue;
            var bob = MathF.Sin((_frame + p.X) * 0.12f) * 2 * _px;
            var spr = PickupSprite(p.Kind, _frame);
            if (p.Kind != PickupKind.Coin)
                GameFx.GlowDisc(canvas, sx + spr[0].Length * _px / 2f, p.Y + bob + spr.Length * _px / 2f, spr[0].Length * _px,
                    p.Kind switch { PickupKind.Heart => new SKColor(255, 70, 100), PickupKind.Star => new SKColor(255, 230, 70), PickupKind.Speed => new SKColor(80, 200, 255), PickupKind.Spring => new SKColor(80, 220, 70), _ => new SKColor(180, 90, 255) }, 0.4f);
            else if ((_frame + (int)p.X) % 40 == 0)
                _gfx.Spark(p.X, p.Y + bob, 0, -0.2f, SKColors.White, 0.3f, 1, true);
            PixelArt.Blit(canvas, spr, sx, p.Y + bob, ch => ChPickup(ch, p.Kind), _px);
        }

        foreach (var s in _foes)
        {
            var sx = s.X - _cam;
            if (sx < -20 || sx > w + 20) continue;
            var spr = FoeSprite(s.Kind, _frame);
            var sh = spr.Length * _px;
            PixelArt.Blit(canvas, spr, sx, s.Y - sh, ch => ChFoe(ch, s.Kind, s.Hue), _px, flipX: s.Dir < 0);
        }

        var walking = _onGround && Math.Abs(_vx) > 0.01f;
        var hero = !_onGround ? JumpSpr : walking ? (_frame / 4 % 2 == 0 ? WalkA : WalkB) : WalkA;
        var flashStar = _starTimer > 0 && _frame / 2 % 2 == 0;
        if (_starTimer > 0)
        {
            GameFx.GlowDisc(canvas, _pxX - _cam + _pw / 2f, _pxY + _ph / 2f, _ph, PixelArt.Hsv(_frame * 20, 0.8f, 1f), 0.4f);
            _gfx.Spark(_pxX, _pxY + _gfx.Rand(0, _ph), -_vx * 0.2f, 0, PixelArt.Hsv(_frame * 20, 0.8f, 1f), 0.4f, 1, true);
        }
        if (walking && _frame % 4 == 0)
            _gfx.Spark(_pxX + _pw / 2f, _pxY + _ph, -_vx * 0.3f, -_gfx.Rand(0.1f, 0.5f), new SKColor(220, 200, 150), 0.35f, 1, false, 0.03f);
        if (_onGround && !_wasGround)
            _gfx.Spray(_pxX + _pw / 2f, _pxY + _ph, -MathF.PI / 2, 1.3f, new SKColor(220, 200, 150), 8, 1.4f, 0.4f, 0.06f);
        _wasGround = _onGround;
        PixelArt.Blit(canvas, hero, _pxX - _cam, _pxY, flashStar ? ChHeroStar : ChHero, _px, flipX: !_faceRight);

        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        foreach (var p in _sparks)
        {
            paint.Color = p.Color.WithAlpha((byte)Math.Clamp(p.Life * 12, 0, 255));
            canvas.DrawRect(p.X - _cam, p.Y, _px, _px, paint);
        }
        // World-space effects: draw shifted by the camera.
        canvas.Save();
        canvas.Translate(-_cam, 0);
        _gfx.DrawParticles(canvas);
        canvas.Restore();
        _gfx.EndShake(canvas);

        if (ShowScore)
        {
            var size = CanvasText.ResolveSize(FontSize, Math.Max(8f, h * 0.09f));
            var hud = $"{_score}  x{_lives}";
            if (_starTimer > 0) hud += "  STAR";
            if (_speedTimer > 0) hud += "  SPD";
            if (_springTimer > 0) hud += "  JMP";
            CanvasText.Draw(canvas, _canvas, hud, SKColors.White,
                4, size + 1, size, SKTextAlign.Left, UseBdfFont);
            if (_toastLife > 0 && _toast.Length > 0)
                CanvasText.Draw(canvas, _canvas, _toast, new SKColor(255, 230, 90),
                    w / 2f, size * 2.2f, size, SKTextAlign.Center, UseBdfFont);
        }

        _gfx.DrawOverlay(canvas, _canvas, w, h, Math.Max(7f, h * 0.07f));
        if (_crashTimer > 0)
        {
            var msg = _lives <= 0 ? "TRY AGAIN" : "OUCH";
            var size = CanvasText.ResolveSize(FontSize, Math.Max(12f, h * 0.14f));
            CanvasText.Draw(canvas, _canvas, msg, SKColors.White,
                w / 2f, h * 0.42f, size, SKTextAlign.Center, UseBdfFont);
        }

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void DrawHills(SKCanvas canvas, float parallax, SKColor col, int amp)
    {
        using var p = new SKPaint { Color = col, IsAntialias = false, Style = SKPaintStyle.Fill };
        var baseY = GroundY - amp * _px;
        var scroll = _cam * parallax;
        for (var x = 0; x < _canvas.Width; x++)
        {
            var wx = x + scroll;
            var y = baseY
                    + MathF.Sin(wx * 0.018f) * amp * _px * 0.55f
                    + MathF.Sin(wx * 0.041f) * amp * _px * 0.22f;
            var iy = (int)MathF.Round(y);
            if (iy < GroundY)
                canvas.DrawRect(x, iy, 1, GroundY - iy, p);
        }
    }

    private void DrawPit(SKCanvas canvas, SKPaint paint, Pit pit, float sx, int w, int h)
    {
        var gy = (int)MathF.Round(GroundY);
        var x0 = Math.Max(0, (int)MathF.Floor(sx));
        var x1 = Math.Min(w, (int)MathF.Ceiling(sx + pit.W));
        var pw = Math.Max(1f, pit.W);

        for (var x = x0; x < x1; x++)
        {
            var u = (x - sx) / pw;
            var edge = Math.Min(u, 1f - u); // 0 at lips, 0.5 in the middle
            var lip = edge < 0.12f;
            var wall = (int)MathF.Round(Math.Min(edge * pw * 0.7f, 14f * _px));
            var waterTop = h - Math.Max(7, 8 * _px);

            // Only carve the floor — hills behind the gap stay intact.
            for (var y = gy; y < h; y++)
            {
                var down = y - gy;
                SKColor col;
                if (lip && down < 3 * _px)
                    col = down == 0 ? new SKColor(62, 214, 78) : new SKColor(196, 118, 48);
                else if (down < wall)
                {
                    var band = (down + x) / Math.Max(1, 3 * _px);
                    col = band % 2 == 0 ? new SKColor(168, 96, 42) : new SKColor(130, 70, 32);
                    if (down < 2 * _px) col = new SKColor(36, 168, 52);
                }
                else if (y >= waterTop)
                {
                    var wave = (int)(MathF.Sin((x + _frame * (pit.Lava ? 0.35f : 0.18f)) * 0.45f) * 2);
                    if (y < waterTop + 1 + wave)
                        col = pit.Lava ? new SKColor(255, 210, 70) : new SKColor(140, 220, 255);
                    else if (y < waterTop + 4 * _px)
                        col = pit.Lava ? new SKColor(255, 90, 24) : new SKColor(36, 110, 210);
                    else
                        col = pit.Lava ? new SKColor(160, 24, 12) : new SKColor(12, 36, 90);
                }
                else
                {
                    var shade = Math.Clamp(40 + down * 2, 40, 90);
                    col = new SKColor((byte)(shade * 0.45f), (byte)(shade * 0.28f), (byte)(shade * 0.38f));
                    if ((x + y) % 7 == 0) col = new SKColor(70, 42, 36);
                }

                paint.Color = col;
                canvas.DrawRect(x, y, 1, 1, paint);
            }
        }

        // Grass tufts hanging over both lips.
        PixelArt.Blit(canvas, GrassHang, sx - 1, gy - GrassHang.Length * _px + 1, ChGrass, _px);
        PixelArt.Blit(canvas, GrassHang, sx + pit.W - GrassHang[0].Length * _px + 1, gy - GrassHang.Length * _px + 1,
            ChGrass, _px, flipX: true);
    }

    private void Tile(SKCanvas canvas, string[] spr, float x, float y, float w, float h, Func<char, SKColor> pal)
    {
        var tw = spr[0].Length * _px;
        var th = spr.Length * _px;
        if (tw <= 0 || th <= 0) return;
        canvas.Save();
        canvas.ClipRect(new SKRect(x, y, x + w, y + h));
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = (int)MathF.Ceiling(x + w);
        var y1 = (int)MathF.Ceiling(y + h);
        for (var py = y0; py < y1; py += th)
        for (var px = x0; px < x1; px += tw)
            PixelArt.Blit(canvas, spr, px, py, pal, _px);
        canvas.Restore();
    }

    private static SKColor ChHero(char ch) => ch switch
    {
        'd' => new SKColor(28, 22, 18),
        'c' => new SKColor(24, 214, 186),
        's' => new SKColor(244, 198, 148),
        'w' => SKColors.White,
        'k' => new SKColor(20, 16, 14),
        'm' => new SKColor(52, 32, 18),
        'o' => new SKColor(255, 122, 24),
        't' => new SKColor(16, 176, 160),
        'n' => new SKColor(255, 214, 64),
        'b' => new SKColor(110, 52, 22),
        _ => SKColors.Transparent
    };

    private static SKColor ChCoin(char ch) => ch switch
    {
        'd' => new SKColor(140, 90, 16),
        'y' => new SKColor(255, 214, 48),
        'w' => new SKColor(255, 252, 200),
        'k' => new SKColor(196, 128, 16),
        _ => SKColors.Transparent
    };

    private static SKColor ChHeroStar(char ch) => ch switch
    {
        'd' => new SKColor(80, 50, 10),
        'c' => new SKColor(255, 230, 70),
        's' => new SKColor(255, 236, 180),
        'w' => SKColors.White,
        'k' => new SKColor(40, 24, 8),
        'm' => new SKColor(180, 90, 20),
        'o' => new SKColor(255, 180, 40),
        't' => new SKColor(255, 220, 60),
        'n' => SKColors.White,
        'b' => new SKColor(160, 90, 20),
        _ => SKColors.Transparent
    };

    private static SKColor ChPickup(char ch, PickupKind kind) => kind switch
    {
        PickupKind.Heart => ch switch
        {
            'd' => new SKColor(90, 16, 24),
            'r' => new SKColor(255, 50, 80),
            'w' => new SKColor(255, 180, 200),
            _ => SKColors.Transparent
        },
        PickupKind.Star => ch switch
        {
            'y' => new SKColor(255, 220, 40),
            'w' => new SKColor(255, 255, 220),
            _ => SKColors.Transparent
        },
        PickupKind.Speed => ch switch
        {
            'd' => new SKColor(20, 40, 80),
            'c' => new SKColor(80, 200, 255),
            'y' => new SKColor(255, 240, 80),
            _ => SKColors.Transparent
        },
        PickupKind.Spring => ch switch
        {
            'd' => new SKColor(20, 50, 20),
            'g' => new SKColor(80, 220, 70),
            'w' => SKColors.White,
            'o' => new SKColor(255, 140, 40),
            _ => SKColors.Transparent
        },
        PickupKind.Gem => ch switch
        {
            'd' => new SKColor(40, 16, 70),
            'c' => new SKColor(170, 80, 255),
            'w' => new SKColor(240, 210, 255),
            _ => SKColors.Transparent
        },
        _ => ChCoin(ch)
    };

    private static string[] PickupSprite(PickupKind kind, int frame)
    {
        var a = frame / 6 % 2 == 0;
        return kind switch
        {
            PickupKind.Heart => Heart,
            PickupKind.Star => a ? StarA : StarB,
            PickupKind.Speed => Bolt,
            PickupKind.Spring => Spring,
            PickupKind.Gem => Gem,
            _ => a ? CoinA : CoinB
        };
    }

    private void Collect(Pickup p)
    {
        switch (p.Kind)
        {
            case PickupKind.Heart:
                _sound.Play(GameSfx.LevelUp, 0.7f);
                _lives = Math.Min(9, _lives + 1);
                Toast("1 UP");
                Burst(p.X, p.Y, new SKColor(255, 70, 100));
                break;
            case PickupKind.Star:
                _sound.Play(GameSfx.PowerUp, 0.7f);
                _starTimer = 200;
                Toast("STAR");
                Burst(p.X, p.Y, new SKColor(255, 230, 70));
                break;
            case PickupKind.Speed:
                _sound.Play(GameSfx.PowerUp, 0.7f, 0, 1.25f);
                _speedTimer = 180;
                Toast("SPEED");
                Burst(p.X, p.Y, new SKColor(80, 200, 255));
                break;
            case PickupKind.Spring:
                _sound.Play(GameSfx.PowerUp, 0.7f, 0, 0.85f);
                _springTimer = 180;
                Toast("SUPER JUMP");
                Burst(p.X, p.Y, new SKColor(80, 220, 70));
                break;
            case PickupKind.Gem:
                _sound.Play(GameSfx.Score, 0.7f);
                _score += 100;
                Toast("+100");
                Burst(p.X, p.Y, new SKColor(180, 90, 255));
                break;
            default:
                _sound.Play(GameSfx.Coin, 0.6f);
                _gfx.Burst(p.X + 3 * _px, p.Y + 3 * _px, new SKColor(255, 230, 80), 8, 1.4f, 0.4f, 0, true);
                _score += 10;
                Burst(p.X, p.Y, new SKColor(255, 220, 60));
                break;
        }
    }

    private void Toast(string msg)
    {
        _toast = msg;
        _toastLife = 40;
    }

    private static SKColor ChSlime(char ch, int hue)
    {
        var body = PixelArt.Hsv(hue, 0.78f, 1f);
        var dark = PixelArt.Hsv(hue, 0.85f, 0.45f);
        return ch switch
        {
            'd' => dark,
            'm' => body,
            'w' => SKColors.White,
            'k' => new SKColor(20, 16, 14),
            _ => SKColors.Transparent
        };
    }

    private static SKColor ChFoe(char ch, FoeKind kind, int hue) => kind switch
    {
        FoeKind.Slime => ChSlime(ch, hue),
        FoeKind.Bat => ch switch
        {
            'd' => new SKColor(28, 16, 40),
            'm' => PixelArt.Hsv((hue + 280) % 360, 0.55f, 0.85f),
            'w' => SKColors.White,
            'k' => new SKColor(16, 12, 20),
            'b' => new SKColor(40, 24, 16),
            _ => SKColors.Transparent
        },
        FoeKind.SpikeBug => ch switch
        {
            'd' => new SKColor(40, 20, 12),
            'r' => new SKColor(196, 72, 36),
            's' => new SKColor(230, 230, 236),
            'w' => SKColors.White,
            'k' => new SKColor(16, 12, 12),
            'b' => new SKColor(60, 28, 16),
            _ => SKColors.Transparent
        },
        FoeKind.Hopper => ch switch
        {
            'd' => new SKColor(24, 40, 12),
            'y' => new SKColor(180, 230, 50),
            'w' => SKColors.White,
            'k' => new SKColor(16, 16, 16),
            'o' => new SKColor(80, 160, 40),
            't' => new SKColor(50, 110, 28),
            'b' => new SKColor(40, 28, 12),
            _ => SKColors.Transparent
        },
        _ => ch switch
        {
            's' => new SKColor(255, 80, 70),
            'g' => new SKColor(36, 168, 52),
            'd' => new SKColor(20, 70, 28),
            'D' => new SKColor(150, 82, 32),
            _ => SKColors.Transparent
        }
    };

    private void StepFoe(ref Foe s)
    {
        s.Phase += 0.12f;
        switch (s.Kind)
        {
            case FoeKind.Plant:
                return;
            case FoeKind.Bat:
                s.X += s.Dir * _baseWalk * 0.55f;
                if (s.X < s.Left || s.X > s.Right) s.Dir = -s.Dir;
                s.Y = s.BaseY + MathF.Sin(s.Phase) * 7 * _px;
                break;
            case FoeKind.Hopper:
            {
                var hop = (int)s.Phase % 36;
                if (hop < 10)
                    s.Y = s.BaseY - hop * 1.1f * _px;
                else if (hop < 20)
                    s.Y = s.BaseY - (20 - hop) * 1.1f * _px;
                else
                    s.Y = s.BaseY;
                if (hop >= 10 && hop < 20)
                    s.X += s.Dir * _baseWalk * 0.7f;
                if (s.X < s.Left || s.X > s.Right) s.Dir = -s.Dir;
                break;
            }
            default:
                s.X += s.Dir * _baseWalk * (s.Kind == FoeKind.SpikeBug ? 0.28f : 0.45f);
                if (s.X < s.Left || s.X > s.Right) s.Dir = -s.Dir;
                break;
        }
    }

    private static bool CanStomp(FoeKind kind) => kind is FoeKind.Slime or FoeKind.Bat or FoeKind.Hopper;

    private string[] FoeSprite(FoeKind kind, int frame)
    {
        var a = frame / 5 % 2 == 0;
        return kind switch
        {
            FoeKind.SpikeBug => a ? BugA : BugB,
            FoeKind.Bat => a ? BatA : BatB,
            FoeKind.Hopper => a ? HopA : HopB,
            FoeKind.Plant => Plant,
            _ => a ? SlimeA : SlimeB
        };
    }

    private (int w, int h) FoeSize(FoeKind kind)
    {
        var spr = FoeSprite(kind, 0);
        return (spr[0].Length * _px, spr.Length * _px);
    }

    private static SKColor ChGrass(char ch) => ch switch
    {
        'g' => new SKColor(62, 214, 78),
        'G' => new SKColor(36, 168, 52),
        'd' => new SKColor(196, 118, 48),
        'D' => new SKColor(150, 82, 32),
        _ => SKColors.Transparent
    };

    private static SKColor ChBrick(char ch) => ch switch
    {
        'o' => new SKColor(92, 36, 28),
        'B' => new SKColor(228, 96, 64),
        'b' => new SKColor(168, 56, 40),
        _ => SKColors.Transparent
    };

    private void PruneBehindCamera()
    {
        var cutoff = _cam - Math.Max(32, _canvas.Width);
        _plats.RemoveAll(p => p.X + p.W < cutoff);
        _pits.RemoveAll(p => p.X + p.W < cutoff);
        _pickups.RemoveAll(p => p.X < cutoff);
        _foes.RemoveAll(f => f.X < cutoff);
        _sparks.RemoveAll(s => s.X < cutoff && s.Life <= 0 || s.X < cutoff);
    }

    private struct Platform
    {
        public float X, Y, W, H;
        public bool Brick;
    }

    private struct Pit
    {
        public float X, W;
        public bool Lava;
    }

    private struct Pickup
    {
        public float X, Y;
        public PickupKind Kind;
    }

    private enum PickupKind { Coin, Heart, Star, Speed, Spring, Gem }

    private enum FoeKind { Slime, SpikeBug, Bat, Hopper, Plant }

    private struct Foe
    {
        public FoeKind Kind;
        public float X, Y, BaseY, Left, Right, Phase;
        public int Dir, Hue;
    }

    private struct Spark
    {
        public float X, Y, Vx, Vy;
        public int Life;
        public SKColor Color;
    }
}
