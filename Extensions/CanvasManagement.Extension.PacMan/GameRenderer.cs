namespace CanvasManagement.Extension.PacMan;

/// <summary>
///     Draws a <see cref="Game"/> 1:1 into a <see cref="Frame"/>: neon-outlined walls (cached per maze),
///     dots, sprites built for the cell size, a score bar on top and centred messages.
/// </summary>
internal sealed class GameRenderer
{
    private Maze? _wallsFor;
    private uint _wallsColor;
    private uint[] _walls = [];

    public int Cell { get; private set; } = 8;
    public int Hud { get; private set; } = 8;
    public int OffX { get; private set; }
    public int OffY { get; private set; }
    public bool Demo { get; set; }
    public bool Debug { get; set; }
    public uint Background { get; set; } = Art.Black;
    public Autopilot? Pilot { get; set; }
    private float _time;

    /// <summary>Grid size that fits a canvas: about 31×15 cells on a 256×128 wall.</summary>
    public static (int Cols, int Rows, int Cell, int Hud) Fit(int w, int h)
    {
        var cell = Math.Clamp(Math.Min(w / 29, h / 16), 4, 32);
        var hud = Math.Max(8, cell);
        var cols = Math.Min(w / cell, 61);
        var rows = Math.Min((h - hud) / cell, 41);
        if (cols % 2 == 0) cols--;
        if (rows % 2 == 0) rows--;
        return (Math.Max(15, cols), Math.Max(11, rows), cell, hud);
    }

    public void Render(Frame f, Game g, float dt)
    {
        _time += dt;
        var m = g.Maze;
        (_, _, Cell, Hud) = Fit(f.W, f.H);
        OffX = (f.W - m.W * Cell) / 2;
        OffY = Hud + Math.Max(0, (f.H - Hud - m.H * Cell) / 2);

        f.Clear(Background);
        var wall = Art.Walls[(g.Level - 1) % Art.Walls.Length];
        if (g.Phase == Phase.LevelClear && g.PhaseTime > 0.6f && (int)((g.PhaseTime - 0.6f) * 4) % 2 == 0)
            wall = Art.White;
        DrawWalls(f, m, wall);
        DrawDots(f, g);

        if (g.Fruit is { } fruit) DrawFruit(f, OffX + fruit.X * Cell, OffY + fruit.Y * Cell, fruit.Kind);

        var showActors = g.Phase != Phase.LevelClear || g.PhaseTime < 0.6f;
        var dying = g.Phase == Phase.Dying;
        if (showActors && g.Phase != Phase.GameOver)
        {
            if (!dying || g.PhaseTime < 0.8f)
                foreach (var ghost in g.Ghosts)
                {
                    // The ghost just eaten is replaced by its points for a moment.
                    if (g.Phase == Phase.GhostEaten && ghost.State == GhostState.Eyes && g.Popup is { } p &&
                        MathF.Abs(ghost.Fx(m) - p.X) < 0.01f && MathF.Abs(ghost.Fy - p.Y) < 0.01f)
                        continue;
                    DrawGhost(f, g, ghost);
                }
            if (g.Phase == Phase.GhostEaten && g.Popup is { } pop) DrawPopup(f, pop.X, pop.Y, pop.Points, Art.Rgb(0, 230, 255));
            else DrawPac(f, g);
        }
        if (g.Phase != Phase.GhostEaten && g.Popup is { } fp) DrawPopup(f, fp.X, fp.Y, fp.Points, Art.Rgb(255, 160, 215));

        DrawHud(f, g);
        DrawMessages(f, g);
        if (Debug) DrawDebug(f, g);
    }

    private int Px(float tx) => OffX + (int)MathF.Round(tx * Cell);
    private int Py(float ty) => OffY + (int)MathF.Round(ty * Cell);

    // ------------------------------------------------------------------ walls

    private void DrawWalls(Frame f, Maze m, uint color)
    {
        var mw = m.W * Cell;
        var mh = m.H * Cell;
        if (_wallsFor != m || _wallsColor != color || _walls.Length != mw * mh)
        {
            _wallsFor = m;
            _wallsColor = color;
            _walls = BuildWalls(m, color, mw, mh);
        }

        for (var y = 0; y < mh; y++)
        {
            var fy = OffY + y;
            if ((uint)fy >= f.H) continue;
            for (var x = 0; x < mw; x++)
            {
                var c = _walls[y * mw + x];
                if (c != 0) f.Set(OffX + x, fy, c);
            }
        }
    }

    /// <summary>
    ///     Corridors are the non-wall cells grown by a pixel or two (diamond-shaped, so outer corners come out
    ///     bevelled). Wall pixels touching a corridor are the bright outline; the rest is a faint fill.
    /// </summary>
    private uint[] BuildWalls(Maze m, uint color, int mw, int mh)
    {
        var grow = Math.Max(1, Cell / 6);
        var open = new bool[mw * mh];
        for (var y = 0; y < mh; y++)
        for (var x = 0; x < mw; x++)
            open[y * mw + x] = m[x / Cell, y / Cell] != global::CanvasManagement.Extension.PacMan.Cell.Wall;

        var corridor = new bool[mw * mh];
        for (var y = 0; y < mh; y++)
        for (var x = 0; x < mw; x++)
        {
            if (open[y * mw + x]) { corridor[y * mw + x] = true; continue; }
            for (var dy = -grow; dy <= grow && !corridor[y * mw + x]; dy++)
            for (var dx = -grow; dx <= grow; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > grow) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
                if (open[ny * mw + nx]) { corridor[y * mw + x] = true; break; }
            }
        }

        var px = new uint[mw * mh];
        var fill = Art.Scale(color, 0.10f);
        for (var y = 0; y < mh; y++)
        for (var x = 0; x < mw; x++)
        {
            if (corridor[y * mw + x]) continue;
            var edge = false;
            for (var dy = -1; dy <= 1 && !edge; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= mw || ny >= mh) continue;
                if (corridor[ny * mw + nx]) { edge = true; break; }
            }
            px[y * mw + x] = edge ? color : fill;
        }

        // Door: a pink bar across the middle of the door cell.
        var (dxc, dyc) = m.Door;
        var barY = dyc * Cell + Cell / 2 - Math.Max(1, Cell / 8);
        for (var y = barY; y < barY + Math.Max(1, Cell / 4); y++)
        for (var x = dxc * Cell - grow; x < (dxc + 1) * Cell + grow; x++)
            if (x >= 0 && x < mw && y >= 0 && y < mh)
                px[y * mw + x] = Art.DoorPink;
        return px;
    }

    // ------------------------------------------------------------------ items

    private void DrawDots(Frame f, Game g)
    {
        var m = g.Maze;
        var size = Cell >= 6 ? Math.Max(2, Cell / 4) : 1;
        var o = (Cell - size) / 2;
        var blink = g.Phase is Phase.Playing or Phase.GhostEaten && (int)(_time * 4) % 2 == 1;
        var r = Cell / 2f - 0.6f;
        var cc = (Cell - 1) / 2f;
        for (var x = 0; x < m.W; x++)
        for (var y = 0; y < m.H; y++)
        {
            int bx = OffX + x * Cell, by = OffY + y * Cell;
            if (m.HasDot(x, y)) f.Rect(bx + o, by + o, size, size, Art.Dot);
            else if (m.HasPower(x, y) && !blink)
                for (var yy = 0; yy < Cell; yy++)
                for (var xx = 0; xx < Cell; xx++)
                    if ((xx - cc) * (xx - cc) + (yy - cc) * (yy - cc) <= r * r)
                        f.Set(bx + xx, by + yy, Art.Dot);
        }
    }

    private void DrawFruit(Frame f, int x, int y, int kind)
    {
        var art = Art.Fruits[kind % Art.Fruits.Length];
        for (var yy = 0; yy < Cell; yy++)
        for (var xx = 0; xx < Cell; xx++)
        {
            var c = Art.FruitColor(art[yy * 8 / Cell][xx * 8 / Cell]);
            if (c != 0) f.Set(x + xx, y + yy, c);
        }
    }

    private void DrawPopup(Frame f, float tx, float ty, int points, uint color)
    {
        var text = points.ToString();
        var w = Art.MeasureSmall(text);
        var x = Px(tx) + Cell / 2 - w / 2;
        var y = Py(ty) + Cell / 2 - 2;
        Art.DrawSmall(f, text, x, y, color);
    }

    // ------------------------------------------------------------------ actors

    private void DrawPac(Frame f, Game g)
    {
        var pac = g.Pac;
        var m = g.Maze;
        float mouth;
        var facing = pac.Dir;
        if (g.Phase == Phase.Dying)
        {
            var t = g.PhaseTime;
            if (t < 0.8f) mouth = 20;
            else if (t < 2.3f) { facing = Direction.Up; mouth = 20 + (t - 0.8f) / 1.5f * 165; }
            else
            {
                DrawPop(f, pac.Fx(m), pac.Fy, (t - 2.3f) / 0.5f);
                return;
            }
        }
        else if (g.Phase == Phase.Ready) mouth = 0;
        else mouth = 4 + 44 * MathF.Abs(MathF.Sin(pac.Chomp * MathF.PI * 1.6f));

        DrawWrapped(f, m, pac.Fx(m), pac.Fy, (x, y) => PacSprite(f, x, y, facing, mouth, Art.Yellow));
    }

    private void PacSprite(Frame f, int x, int y, Direction facing, float mouthDeg, uint color)
    {
        var cc = (Cell - 1) / 2f;
        var r = Cell / 2f + 0.25f;
        var face = facing switch
        {
            Direction.Right => 0f,
            Direction.Down => MathF.PI / 2,
            Direction.Left => MathF.PI,
            Direction.Up => -MathF.PI / 2,
            _ => 0f
        };
        var half = mouthDeg * MathF.PI / 180f;
        for (var yy = 0; yy < Cell; yy++)
        for (var xx = 0; xx < Cell; xx++)
        {
            float dx = xx - cc, dy = yy - cc;
            if (dx * dx + dy * dy > r * r) continue;
            if (half > 0.01f && dx * dx + dy * dy > 0.3f)
            {
                var a = MathF.Atan2(dy, dx) - face;
                while (a > MathF.PI) a -= MathF.Tau;
                while (a < -MathF.PI) a += MathF.Tau;
                if (MathF.Abs(a) < half) continue;
            }
            f.Set(x + xx, y + yy, color);
        }
    }

    private void DrawPop(Frame f, float tx, float ty, float t)
    {
        if (t >= 1) return;
        var cx = Px(tx) + Cell / 2;
        var cy = Py(ty) + Cell / 2;
        var len = 1 + t * Cell * 0.9f;
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4;
            for (var k = (int)(len * 0.5f); k <= len; k++)
                f.Set(cx + (int)MathF.Round(MathF.Cos(a) * k), cy + (int)MathF.Round(MathF.Sin(a) * k),
                    Art.Scale(Art.Yellow, 1 - t * 0.7f));
        }
    }

    private void DrawGhost(Frame f, Game g, Ghost ghost)
    {
        var m = g.Maze;
        var tx = ghost.DrawX(m);
        var ty = ghost.DrawY;
        if (ghost.State == GhostState.Home) ty += MathF.Sin(ghost.Bob) * 0.25f;
        var eyesOnly = ghost.State is GhostState.Eyes or GhostState.Entering;
        var look = ghost.State switch
        {
            GhostState.Home => MathF.Sin(ghost.Bob) > 0 ? Direction.Down : Direction.Up,
            GhostState.Leaving => Direction.Up,
            GhostState.Entering => Direction.Down,
            _ => ghost.Dir
        };
        var frightened = ghost.Frightened && !eyesOnly;
        var blink = frightened && g.Fright < 2f && (int)(g.Fright * 5) % 2 == 0;
        var body = frightened ? blink ? Art.BlinkWhite : Art.FrightBlue : Art.GhostColors[ghost.Index % Art.GhostColors.Length];
        var face = blink ? Art.BlinkFace : Art.FrightFace;
        var skirt = (int)(_time * 8) % 2;
        DrawWrapped(f, m, tx, ty, (x, y) => GhostSprite(f, x, y, body, face, look, frightened, eyesOnly, skirt));
    }

    private void GhostSprite(Frame f, int x, int y, uint body, uint face, Direction look, bool frightened,
        bool eyesOnly, int skirt)
    {
        var n = Cell;
        // Author on 8×8, sample to the cell size.
        var mask = new char[8, 8];
        for (var yy = 0; yy < 8; yy++)
        for (var xx = 0; xx < 8; xx++)
        {
            var row = yy < 7 ? Art.GhostBody[yy] : Art.Skirts[skirt];
            mask[xx, yy] = eyesOnly ? '.' : row[xx] == '#' ? 'b' : '.';
        }

        if (frightened)
        {
            mask[2, 3] = mask[5, 3] = 'f';
            foreach (var (xx, yy) in new[] { (1, 5), (2, 4), (3, 5), (4, 4), (5, 5), (6, 4) }) mask[xx, yy] = 'f';
        }
        else
        {
            int ex = look switch { Direction.Left => -1, Direction.Right => 1, _ => 0 };
            int ey = look switch { Direction.Up => -1, Direction.Down => 1, _ => 0 };
            foreach (var bx in new[] { 1, 5 })
            {
                var wx = bx + ex;
                var wy = 2 + ey;
                for (var yy = 0; yy < 3; yy++)
                for (var xx = 0; xx < 2; xx++)
                    if (wx + xx is >= 0 and < 8 && wy + yy is >= 0 and < 8)
                        mask[wx + xx, wy + yy] = 'w';
                // Pupil on the side it looks to.
                var (px, py) = look switch
                {
                    Direction.Left => (wx, wy + 1),
                    Direction.Right => (wx + 1, wy + 1),
                    Direction.Up => (wx, wy),
                    Direction.Down => (wx, wy + 2),
                    _ => (wx, wy + 1)
                };
                var (qx, qy) = look is Direction.Up or Direction.Down ? (px + 1, py) : (px, py + 1);
                foreach (var (ux, uy) in new[] { (px, py), (qx, qy) })
                    if (ux is >= 0 and < 8 && uy is >= 0 and < 8)
                        mask[ux, uy] = 'p';
            }
        }

        for (var yy = 0; yy < n; yy++)
        for (var xx = 0; xx < n; xx++)
        {
            var c = mask[xx * 8 / n, yy * 8 / n] switch
            {
                'b' => body,
                'f' => face,
                'w' => Art.White,
                'p' => Art.Pupil,
                _ => 0u
            };
            if (c != 0) f.Set(x + xx, y + yy, c);
        }
    }

    /// <summary>Draws at a tile position, and again on the far side while it is in the tunnel.</summary>
    private void DrawWrapped(Frame f, Maze m, float tx, float ty, Action<int, int> draw)
    {
        var x = Px(tx);
        var y = Py(ty);
        var mw = m.W * Cell;
        var left = OffX;
        var right = OffX + mw;
        // Clip to the maze so nothing pokes out of the tunnel mouth.
        void Clip(int sx)
        {
            if (sx + Cell <= left || sx >= right) return;
            if (sx >= left && sx + Cell <= right) { draw(sx, y); return; }
            var saved = new uint[f.H * Cell];
            for (var yy = 0; yy < Cell; yy++)
            for (var xx = 0; xx < Cell; xx++)
            {
                int fx = sx + xx, fy = y + yy;
                if ((uint)fx < f.W && (uint)fy < f.H) saved[yy * Cell + xx] = f.Px[fy * f.W + fx];
            }
            draw(sx, y);
            for (var yy = 0; yy < Cell; yy++)
            for (var xx = 0; xx < Cell; xx++)
            {
                int fx = sx + xx, fy = y + yy;
                if ((fx < left || fx >= right) && (uint)fx < f.W && (uint)fy < f.H)
                    f.Px[fy * f.W + fx] = saved[yy * Cell + xx];
            }
        }
        Clip(x);
        if (x < left) Clip(x + mw);
        if (x + Cell > right) Clip(x - mw);
    }

    // ------------------------------------------------------------------ text

    private void DrawHud(Frame f, Game g)
    {
        var y = (Hud - Art.BigH) / 2;
        Art.DrawBig(f, g.Score.ToString(), 2, y, Art.White);

        string mid;
        uint midColor;
        if (g.Phase == Phase.Ready)
        {
            mid = "LEVEL " + g.Level;
            midColor = Art.Rgb(0, 225, 255);
        }
        else if (Demo && (int)(_time / 2.5f) % 2 == 0)
        {
            mid = "DEMO";
            midColor = Art.Rgb(0, 225, 255);
        }
        else
        {
            mid = "HI " + Game.HiScore;
            midColor = Art.Rgb(255, 200, 60);
        }
        Art.DrawBig(f, mid, f.W / 2 - Art.MeasureBig(mid) / 2, y, midColor);

        // Lives (the one in play is not shown) and the level's fruit on the right.
        var x = f.W - 2 - 8;
        DrawFruitIcon(f, x, (Hud - 8) / 2, g.FruitKind);
        x -= 10;
        var lives = Math.Min(5, g.Lives - (g.Phase is Phase.Playing or Phase.GhostEaten or Phase.Paused ? 1 : 0));
        if (g.Phase == Phase.Ready) lives = Math.Min(5, g.Lives - 1);
        for (var i = 0; i < lives; i++, x -= 9)
            IconPac(f, x, (Hud - 7) / 2);
    }

    private void DrawFruitIcon(Frame f, int x, int y, int kind)
    {
        var art = Art.Fruits[kind % Art.Fruits.Length];
        for (var yy = 0; yy < 8; yy++)
        for (var xx = 0; xx < 8; xx++)
        {
            var c = Art.FruitColor(art[yy][xx]);
            if (c != 0) f.Set(x + xx, y + yy, c);
        }
    }

    private static void IconPac(Frame f, int x, int y)
    {
        string[] art = { "..###..", ".#####.", "####...", "###....", "####...", ".#####.", "..###.." };
        for (var yy = 0; yy < 7; yy++)
        for (var xx = 0; xx < 7; xx++)
            if (art[yy][6 - xx] == '#')
                f.Set(x + xx, y + yy, Art.Yellow);
    }

    private void DrawMessages(Frame f, Game g)
    {
        var m = g.Maze;
        var below = OffY + (m.HouseY + 2) * Cell + Cell / 2 - Art.BigH / 2;
        var above = OffY + (m.HouseY - 2) * Cell + Cell / 2 - Art.BigH / 2;
        switch (g.Phase)
        {
            case Phase.Ready:
                Banner(f, "READY!", below, Art.Yellow);
                break;
            case Phase.Paused:
                Banner(f, "PAUSED", below, Art.White);
                break;
            case Phase.GameOver:
                Banner(f, "GAME  OVER", below, Art.Rgb(255, 40, 40));
                if (!Demo && g.PhaseTime > 1.5f && (int)(_time * 2) % 2 == 0)
                    Banner(f, "PRESS START", above, Art.White);
                break;
            case Phase.LevelClear when g.PhaseTime > 0.6f:
                Banner(f, "LEVEL CLEAR", below, Art.White);
                break;
        }
    }

    private static void Banner(Frame f, string text, int y, uint color)
    {
        var w = Art.MeasureBig(text);
        var x = f.W / 2 - w / 2;
        f.Rect(x - 2, y - 1, w + 4, Art.BigH + 2, Art.Black);
        Art.DrawBig(f, text, x, y, color);
    }

    private void DrawDebug(Frame f, Game g)
    {
        if (Pilot?.Goal is { } goal)
        {
            var x = OffX + goal.X * Cell;
            var y = OffY + goal.Y * Cell;
            for (var i = 0; i < Cell; i++)
            {
                f.Set(x + i, y, Art.Rgb(0, 255, 0));
                f.Set(x + i, y + Cell - 1, Art.Rgb(0, 255, 0));
                f.Set(x, y + i, Art.Rgb(0, 255, 0));
                f.Set(x + Cell - 1, y + i, Art.Rgb(0, 255, 0));
            }
        }
        if (Pilot != null) Art.DrawBig(f, Pilot.Reason, 2, f.H - Art.BigH - 1, Art.Rgb(0, 255, 0));
    }
}
