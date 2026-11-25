using SkiaSharp;

namespace CanvasManagement.Extension.TurboKart;

internal delegate void TextFn(string text, SKColor color, float x, float y, float size, SKTextAlign align,
    SKColor? shadow = null, bool outline = true);

/// <summary>Per-screen drawing: pixels first (<see cref="RenderFrame"/>), then BDF text (<see cref="DrawText"/>).</summary>
internal sealed partial class Game
{
    private int[]? _menuPx;

    private static readonly SKColor Yellow = new(255, 226, 60);
    private static readonly SKColor Orange = new(255, 140, 20);
    private static readonly SKColor White = SKColors.White;
    private static readonly SKColor Sky = new(170, 210, 255);

    private static SKColor Sk(int c) => new((byte)Gfx.R(c), (byte)Gfx.G(c), (byte)Gfx.B(c));

    private int S => Math.Max(1, (int)MathF.Round(H / 128f));

    private bool ShowsRace => Race != null && Renderer != null && Renderer.Track == Race.T &&
                              Screen is not (Screen.Boot or Screen.Loading or Screen.Podium);

    public int[] RenderFrame()
    {
        int[] px;
        if (ShowsRace)
        {
            px = Renderer!.Px;
            Renderer.Render(Race!);
        }
        else
        {
            if (_menuPx == null || _menuPx.Length != W * H) _menuPx = new int[W * H];
            px = _menuPx;
        }

        var ui = new Ui(px, W, H);
        var s = ui.S;
        switch (Screen)
        {
            case Screen.Boot:
                ui.Gradient(Gfx.Rgb(10, 14, 50), Gfx.Rgb(40, 20, 70));
                break;
            case Screen.Loading:
                LoadingPixels(ui);
                break;
            case Screen.Demo:
            case Screen.Race:
                Renderer!.DrawHud(Race!, HudLayout(s));
                if (Race!.Phase == RacePhase.Results) ResultsPixels(ui, Race);
                break;
            case Screen.Paused:
                Renderer!.DrawHud(Race!, HudLayout(s));
                ui.Dim(0.55f);
                ui.Panel(W / 2 - 56 * s, H / 2 - 36 * s, 112 * s, 72 * s, Gfx.Rgb(14, 18, 50), 0.9f, Gfx.Rgb(255, 255, 255));
                for (var i = 0; i < 3; i++)
                    if (i == PauseSel)
                        ui.FillRect(W / 2 - 50 * s, H / 2 - 12 * s + i * 14 * s, 100 * s, 12 * s, Gfx.Rgb(40, 90, 220), 0.9f);
                break;
            case Screen.Title:
                ui.Dim(0.42f);
                for (var i = 0; i < 2; i++)
                {
                    var sel = i == TitleSel;
                    var y = 64 * s + i * 18 * s;
                    ui.Panel(W / 2 - 58 * s, y, 116 * s, 15 * s, sel ? Gfx.Rgb(40, 90, 220) : Gfx.Rgb(10, 12, 36),
                        sel ? 0.9f : 0.65f, sel ? Pulse(Gfx.Rgb(255, 226, 60)) : Gfx.Rgb(90, 96, 130));
                }

                break;
            case Screen.CupSelect:
                ui.Dim(0.5f);
                CupCards(ui);
                break;
            case Screen.TrackSelect:
                ui.Dim(0.5f);
                TrackGrid(ui);
                break;
            case Screen.Standings:
                ui.Dim(0.62f);
                StandingsPixels(ui);
                break;
            case Screen.Podium:
                PodiumPixels(ui);
                break;
        }

        return px;
    }

    private int Pulse(int c) => ((int)(Clock * 4) & 1) == 0 ? c : Gfx.Rgb(255, 255, 255);

    private HudLayout HudLayout(int s)
    {
        var bottom = H - 4 * s;
        return new HudLayout(3 * s, 3 * s, 3 * s, bottom - 18 * s, 3 * s, bottom - 7 * s);
    }

    // ------------------------------------------------------------------ pixel screens

    private (int x, int y, int w, int h) CardRect(int i)
    {
        var s = S;
        var m = 6 * s;
        var cw = (W - 3 * m) / 2;
        var y0 = 17 * s;
        var ch = H - y0 - 13 * s;
        return (m + i * (cw + m), y0, cw, ch);
    }

    private void CupCards(Ui ui)
    {
        var s = ui.S;
        for (var i = 0; i < Circuits.Cups.Length && i < 2; i++)
        {
            var cup = Circuits.Cups[i];
            var (x, y, w, h) = CardRect(i);
            var sel = i == CupSel;
            var fill = cup.Emblem == 0 ? Gfx.Rgb(120, 52, 22) : Gfx.Rgb(22, 30, 92);
            ui.Panel(x, y, w, h, fill, 0.88f, sel ? Pulse(Gfx.Rgb(255, 226, 60)) : Gfx.Rgb(90, 90, 120));
            ui.Blit(cup.Emblem == 0 ? Ui.Sun : Ui.Moon, x + 3 * s, y + 3 * s, s, Ui.IconColor);
            var tw = (w - 10 * s) / 2;
            var th = (h - 22 * s) / 2;
            for (var t = 0; t < 4; t++)
            {
                var tx = x + 4 * s + (t % 2) * (tw + 2 * s);
                var ty = y + 17 * s + (t / 2) * (th + 2 * s);
                var def = cup.Tracks[t];
                ui.FillRect(tx, ty, tw, th, Gfx.Rgb(8, 10, 28), 0.75f);
                ui.Frame(tx, ty, tw, th, Gfx.Rgb(70, 74, 110));
                ui.Thumb(Thumbs[def], tx, ty, tw, th, def.Theme.MiniColor, false);
            }

            if (!sel) ui.FillRect(x, y, w, h, Gfx.Rgb(4, 6, 20), 0.45f);
        }
    }

    private (int x, int y, int w, int h) GridRect(int t)
    {
        var s = S;
        var top = 16 * s;
        var bottom = H - 26 * s;
        var tw = (W - 20 * s) / 2;
        var th = (bottom - top - 4 * s) / 2;
        return (8 * s + (t % 2) * (tw + 4 * s), top + (t / 2) * (th + 4 * s), tw, th);
    }

    private void TrackGrid(Ui ui)
    {
        var s = ui.S;
        for (var t = 0; t < 4; t++)
        {
            var def = CurrentCup.Tracks[t];
            var (x, y, w, h) = GridRect(t);
            var sel = t == TrackSel;
            ui.Panel(x, y, w, h, Gfx.Rgb(10, 12, 34), 0.85f, sel ? Pulse(Gfx.Rgb(255, 226, 60)) : Gfx.Rgb(80, 84, 120));
            ui.Thumb(Thumbs[def], x, y, w, h, def.Theme.MiniColor, sel);
            if (!sel) ui.FillRect(x, y, w, h, Gfx.Rgb(4, 6, 20), 0.35f);
        }
    }

    private void LoadingPixels(Ui ui)
    {
        var s = ui.S;
        var def = LoadingDef ?? Circuits.All.First();
        ui.Gradient(Gfx.Rgb(8, 10, 32), Gfx.Shade(def.Theme.MiniColor, 0.38f));
        var size = Math.Min(H - 30 * s, 84 * s);
        var x = 10 * s;
        var y = (H - size) / 2;
        ui.Panel(x, y, size, size, Gfx.Rgb(6, 8, 24), 0.85f, Gfx.Rgb(255, 255, 255));
        ui.Thumb(Thumbs[def], x, y, size, size, def.Theme.MiniColor, true);
        var bar = (int)((W - x - size - 20 * s) * Math.Clamp(ScreenTime / 1.6f, 0, 1));
        ui.FillRect(x + size + 10 * s, H - 22 * s, bar, 3 * s, def.Theme.MiniColor);
    }

    private void StandingsPixels(Ui ui)
    {
        var s = ui.S;
        var order = StandingsOrder();
        var rowH = Math.Min(12 * s, (H - 20 * s) / Math.Max(1, order.Count));
        for (var i = 0; i < order.Count; i++)
        {
            var look = order[i];
            var y = 17 * s + i * rowH;
            if (look == 0) ui.FillRect(4 * s, y, W - 8 * s, rowH - s, Gfx.Rgb(50, 80, 200), 0.75f);
            ui.FillRect(26 * s, y + rowH / 2 - 3 * s, 6 * s, 6 * s, Gfx.Rgb(14, 14, 20));
            ui.FillRect(27 * s, y + rowH / 2 - 2 * s, 4 * s, 4 * s, Art.Racers[look].Body);
        }
    }

    private void ResultsPixels(Ui ui, Race race)
    {
        var s = ui.S;
        var rows = race.Karts.OrderBy(k => k.Place).ToList();
        var rowH = Math.Min(11f * s, (H - 28f * s) / Math.Max(1, rows.Count));
        var x0 = W * 0.18f;
        for (var i = 0; i < rows.Count; i++)
        {
            var y = (int)(18f * s + rowH * (i + 1));
            ui.FillRect((int)x0 + 12 * s, y - 6 * s, 6 * s, 6 * s, Gfx.Rgb(14, 14, 20));
            ui.FillRect((int)x0 + 13 * s, y - 5 * s, 4 * s, 4 * s, Art.Racers[rows[i].Look].Body);
        }
    }

    private (int x, int baseY, int w, int h)[] Podium()
    {
        var s = S;
        var baseY = H - 14 * s;
        var w = 44 * s;
        var cx = W / 2;
        return new[]
        {
            (cx - w / 2, baseY, w, 30 * s),
            (cx - w / 2 - w - 2 * s, baseY, w, 21 * s),
            (cx + w / 2 + 2 * s, baseY, w, 14 * s)
        };
    }

    private void PodiumPixels(Ui ui)
    {
        var s = ui.S;
        ui.Gradient(Gfx.Rgb(10, 12, 48), Gfx.Rgb(70, 30, 100));
        ui.Glow(W * 0.5f, H * 0.35f, H * 0.6f, Gfx.Rgb(255, 230, 160), 0.35f);
        ui.Glow(W * 0.2f, H * 0.2f, H * 0.4f, Gfx.Rgb(120, 160, 255), 0.25f);
        ui.Glow(W * 0.8f, H * 0.2f, H * 0.4f, Gfx.Rgb(255, 120, 200), 0.25f);
        ui.FillRect(0, H - 14 * s, W, 14 * s, Gfx.Rgb(30, 20, 50));

        var order = StandingsOrder();
        var blocks = Podium();
        var colors = new[] { Gfx.Rgb(250, 200, 40), Gfx.Rgb(200, 208, 224), Gfx.Rgb(210, 124, 64) };
        for (var i = 0; i < 3 && i < order.Count; i++)
        {
            var (x, baseY, w, h) = blocks[i];
            var top = baseY - h;
            ui.FillRect(x, top, w, h, colors[i]);
            ui.FillRect(x, top, w, 2 * s, Gfx.Add(colors[i], 50));
            ui.FillRect(x + w - 3 * s, top, 3 * s, h, Gfx.Shade(colors[i], 0.75f));
            ui.Frame(x, top, w, h, Gfx.Rgb(20, 14, 30));
            var bob = MathF.Abs(MathF.Sin(Clock * 3 + i)) * 2 * s * (i == 0 ? 1 : 0.5f);
            if (Renderer != null) ui.Sprite(Renderer.KartSprite(order[i], 16 + (int)(Clock * 2) % 3 - 1), x + w / 2f, top - bob);
        }

        var wx = W / 2 - 13 * s;
        ui.Blit(Ui.Trophy, wx, H - 14 * s - 30 * s - 30 * s - 26 * s, 2 * s, Ui.IconColor);

        foreach (var c in ConfettiList)
            ui.FillRect((int)c.Pos.X, (int)c.Pos.Y, 2 * s, s, c.Color);
    }

    // ------------------------------------------------------------------ text

    private static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    private static string Time(float t) => $"{(int)(t / 60)}:{t % 60:00.00}";

    public void DrawText(TextFn text)
    {
        var s = S;
        var hint = new SKColor(190, 210, 255);
        switch (Screen)
        {
            case Screen.Boot:
                text("TURBO KART GP", Yellow, W / 2f, H * 0.46f, 13 * s, SKTextAlign.Center);
                text("building track...", Sky, W / 2f, H * 0.62f, 7 * s, SKTextAlign.Center);
                break;

            case Screen.Demo:
                RaceText(text);
                if (((int)(Clock * 1.5f) & 1) == 0)
                    text("PRESS ANY KEY OR BUTTON", hint, W / 2f, 9 * s, 7 * s, SKTextAlign.Center);
                break;

            case Screen.Race:
                RaceText(text);
                if (Race!.Phase == RacePhase.Results && Race.PhaseTime > 1.5f)
                    text(GrandPrix ? "SPACE / A: STANDINGS" : "SPACE / A: CONTINUE", hint, W / 2f, H - 3 * s, 7 * s,
                        SKTextAlign.Center);
                break;

            case Screen.Paused:
                RaceText(text);
                text("PAUSED", Yellow, W / 2f, H / 2f - 18 * s, 10 * s, SKTextAlign.Center);
                var opts = new[] { "CONTINUE", "RESTART", "QUIT" };
                for (var i = 0; i < 3; i++)
                    text(opts[i], i == PauseSel ? Yellow : White, W / 2f, H / 2f - 2 * s + i * 14 * s, 10 * s,
                        SKTextAlign.Center);
                break;

            case Screen.Title:
            {
                Logo(text, 34 * s);
                text("8 TRACKS  -  2 CUPS", Sky, W / 2f, 47 * s, 7 * s, SKTextAlign.Center);
                var items = new[] { "GRAND PRIX", "SINGLE RACE" };
                for (var i = 0; i < 2; i++)
                    text(items[i], i == TitleSel ? Yellow : White, W / 2f, 64 * s + i * 18 * s + 12 * s, 10 * s,
                        SKTextAlign.Center);
                text("ARROWS/D-PAD  SPACE/A: OK", hint, W / 2f, H - 3 * s, 7 * s, SKTextAlign.Center);
                break;
            }

            case Screen.CupSelect:
            {
                text(GrandPrix ? "GRAND PRIX" : "SINGLE RACE", Yellow, W / 2f, 12 * s, 10 * s, SKTextAlign.Center);
                for (var i = 0; i < Circuits.Cups.Length && i < 2; i++)
                {
                    var (x, y, w, _) = CardRect(i);
                    text(Circuits.Cups[i].Name, i == CupSel ? White : new SKColor(150, 150, 170), x + w / 2f + 7 * s,
                        y + 12 * s, 7 * s, SKTextAlign.Center);
                }

                text("SPACE/A: OK   ESC/B: BACK", hint, W / 2f, H - 3 * s, 7 * s, SKTextAlign.Center);
                break;
            }

            case Screen.TrackSelect:
            {
                text(CurrentCup.Name, Yellow, W / 2f, 12 * s, 10 * s, SKTextAlign.Center);
                for (var t = 0; t < 4; t++)
                {
                    var (x, y, _, _) = GridRect(t);
                    text((t + 1).ToString(), t == TrackSel ? Yellow : new SKColor(150, 150, 170), x + 4 * s,
                        y + 10 * s, 7 * s, SKTextAlign.Left);
                }

                text(CurrentCup.Tracks[TrackSel].Name, White, W / 2f, H - 13 * s, 10 * s, SKTextAlign.Center);
                text("SPACE/A: RACE   ESC/B: BACK", hint, W / 2f, H - 2 * s, 7 * s, SKTextAlign.Center);
                break;
            }

            case Screen.Loading:
            {
                var def = LoadingDef ?? Circuits.All.First();
                var size = Math.Min(H - 30 * s, 84 * s);
                var x = 10 * s + size + 10 * s;
                if (GrandPrix)
                {
                    text(CurrentCup.Name, Sky, x, 30 * s, 7 * s, SKTextAlign.Left);
                    text($"RACE {GpIndex + 1}/{CurrentCup.Tracks.Length}", Yellow, x, 46 * s, 10 * s, SKTextAlign.Left);
                }
                else text("SINGLE RACE", Sky, x, 30 * s, 7 * s, SKTextAlign.Left);

                text(def.Name, White, x, 66 * s, 10 * s, SKTextAlign.Left);
                text($"{Laps} LAPS", Sky, x, 80 * s, 7 * s, SKTextAlign.Left);
                text("LOADING" + new string('.', (int)(Clock * 3) % 4), hint, x, H - 26 * s, 7 * s, SKTextAlign.Left);
                break;
            }

            case Screen.Standings:
            {
                text($"{CurrentCup.Name}  RACE {GpIndex + 1}/{CurrentCup.Tracks.Length}", Yellow, W / 2f, 12 * s,
                    10 * s, SKTextAlign.Center);
                var order = StandingsOrder();
                var rowH = Math.Min(12 * s, (H - 20 * s) / Math.Max(1, order.Count));
                for (var i = 0; i < order.Count; i++)
                {
                    var look = order[i];
                    var y = 17 * s + i * rowH + rowH - 2 * s;
                    var col = look == 0 ? Yellow : White;
                    text((i + 1).ToString(), col, 20 * s, y, 10 * s, SKTextAlign.Right);
                    text(Art.Racers[look].Name, col, 36 * s, y, 10 * s, SKTextAlign.Left);
                    if (Gain[look] > 0)
                        text($"+{Gain[look]}", new SKColor(110, 255, 130), W * 0.68f, y, 10 * s, SKTextAlign.Right);
                    text(Points[look].ToString(), Yellow, W - 8 * s, y, 10 * s, SKTextAlign.Right);
                }

                break;
            }

            case Screen.Podium:
            {
                text(CurrentCup.Name, Yellow, W / 2f, 11 * s, 10 * s, SKTextAlign.Center);
                var order = StandingsOrder();
                var rank = order.IndexOf(0) + 1;
                var msg = rank == 1 ? "YOU WIN!" : $"YOU FINISHED {Ordinal(rank).ToUpperInvariant()}";
                text(msg, rank <= 3 ? White : Sky, W / 2f, 23 * s, 7 * s, SKTextAlign.Center);
                var blocks = Podium();
                for (var i = 0; i < 3 && i < order.Count; i++)
                {
                    var (x, baseY, w, h) = blocks[i];
                    text((i + 1).ToString(), new SKColor(40, 24, 10), x + w / 2f, baseY - 2 * s, 10 * s,
                        SKTextAlign.Center, outline: false);
                    text(Art.Racers[order[i]].Name, order[i] == 0 ? Yellow : White, x + w / 2f, H - 3 * s, 7 * s,
                        SKTextAlign.Center);
                }

                break;
            }

            default:
                if (Race != null) RaceText(text);
                break;
        }
    }

    private void Logo(TextFn text, float y)
    {
        var s = S;
        text("TURBO KART GP", Orange, W / 2f, y, 20 * s, SKTextAlign.Center, new SKColor(70, 14, 0));
    }

    private void RaceText(TextFn text)
    {
        var race = Race;
        if (race == null) return;
        var s = S;
        var l = HudLayout(s);
        var p = race.Player;
        var small = 10f * s;

        // Replay: cinema bars only, a blinking tag, no race HUD.
        if (race.Replaying)
        {
            if (((int)(race.Clock * 2.5f) & 1) == 0)
                text("REPLAY", new SKColor(255, 60, 60), 6 * s, H / 9f - 2 * s, small, SKTextAlign.Left);
            return;
        }

        text(p.Gems.ToString("00"), White, l.GemX + 10 * s, l.GemY + 7 * s, small, SKTextAlign.Left);
        var lap = Math.Clamp(p.Lap, 1, race.Laps);
        text($"{lap}/{race.Laps}", White, l.LapX + 10 * s, l.LapY + 7 * s, small, SKTextAlign.Left);

        if (race.Phase is RacePhase.Racing or RacePhase.Finished)
            text(Time(p.Finished ? p.FinishTime : race.RaceTime), White, W - 3 * s, 10 * s, small, SKTextAlign.Right);

        if (race.Phase != RacePhase.Results)
        {
            var big = 20f * s;
            text(Ordinal(p.Place), Orange, W - 4 * s, H - 3 * s, big, SKTextAlign.Right, new SKColor(60, 10, 0));
        }

        if (race.Phase == RacePhase.Countdown)
        {
            var n = 3 - (int)race.PhaseTime;
            if (n > 0)
                text(n.ToString(), Yellow, W / 2f, H * 0.55f, 40f * s, SKTextAlign.Center, new SKColor(120, 20, 10));
        }

        if (race.BannerTime > 0 && race.Banner.Length > 0 && race.Phase != RacePhase.Results)
        {
            var go = race.Banner == "GO!";
            var color = go ? new SKColor(80, 255, 110) : race.Banner == "FINISH!" ? Yellow : White;
            text(race.Banner, color, W / 2f, H * 0.5f, (go ? 40f : 20f) * s, SKTextAlign.Center, new SKColor(20, 20, 60));
        }

        if (race.PlayerWrongWay && ((int)(race.Clock * 3) & 1) == 0)
            text("WRONG WAY!", new SKColor(255, 70, 60), W / 2f, H * 0.42f, 13f * s, SKTextAlign.Center);

        if (race.Phase == RacePhase.Intro)
            text(race.T.Def.Name, Yellow, W / 2f, H * 0.3f, 13f * s, SKTextAlign.Center, new SKColor(80, 20, 10));

        if (race.Phase == RacePhase.Results) ResultsText(text, race);
    }

    private void ResultsText(TextFn text, Race race)
    {
        var s = S;
        text("RESULTS", Yellow, W / 2f, 13f * s, 13f * s, SKTextAlign.Center);
        var rows = race.Karts.OrderBy(k => k.Place).ToList();
        var rowH = Math.Min(11f * s, (H - 28f * s) / Math.Max(1, rows.Count));
        var size = Math.Min(10f * s, rowH);
        var x0 = W * 0.18f;
        for (var i = 0; i < rows.Count; i++)
        {
            var k = rows[i];
            var y = 18f * s + rowH * (i + 1);
            var col = k.IsPlayer ? Yellow : White;
            text($"{k.Place}", col, x0 + 8 * s, y, size, SKTextAlign.Right);
            text(Art.Racers[k.Look].Name, col, x0 + 22 * s, y, size, SKTextAlign.Left);
            text(k.Finished ? Time(k.FinishTime) : "--:--.--", col, W * 0.84f, y, size, SKTextAlign.Right);
        }
    }
}
