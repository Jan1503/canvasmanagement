using CanvasManagement.Interfaces;
using Xunit;

namespace CanvasManagement.Tests;

public class ExtensionVoiceMixerTests
{
    private static float[] Render(ExtensionVoiceMixer mix, int blocks)
    {
        var all = new List<float>();
        var buf = new float[360 * 2];
        for (var i = 0; i < blocks; i++)
        {
            Array.Clear(buf);
            mix.Render(buf);
            all.AddRange(buf);
        }
        return all.ToArray();
    }

    private static float MaxStep(float[] s)
    {
        var m = 0f;
        for (var i = 2; i < s.Length; i++) m = Math.Max(m, Math.Abs(s[i] - s[i - 2]));
        return m;
    }

    [Fact]
    public void Many_loud_voices_stay_below_full_scale()
    {
        var mix = new ExtensionVoiceMixer();
        var clip = SoundSynth.Loop(Wave.Square, 220, 0.5f, 2, 0.1f, 1f);
        for (var slot = 0; slot < ExtensionVoiceMixer.Slots; slot++) mix.LoopSample(slot, clip, SoundSynth.Rate, 1f, 1f);
        for (var i = 0; i < 10; i++) mix.Play(clip, SoundSynth.Rate, 1f, 0, 1f);

        var s = Render(mix, 40);
        Assert.All(s, v => Assert.InRange(v, -1f, 1f));
    }

    [Fact]
    public void Loop_starts_and_stops_without_a_click()
    {
        var mix = new ExtensionVoiceMixer();
        var clip = SoundSynth.Loop(Wave.Sine, 200, 0.5f, 2, 0f, 0.8f);
        mix.LoopSample(0, clip, SoundSynth.Rate, 0.8f, 1f);
        var start = Render(mix, 1);
        // The first samples fade in instead of jumping to full level.
        Assert.True(Math.Abs(start[0]) < 0.05f);

        Render(mix, 20);
        mix.StopLoop(0);
        var stop = Render(mix, 30);
        Assert.True(MaxStep(stop) < 0.1f);
        Assert.False(mix.HasWork);
    }

    [Fact]
    public void Gain_and_pitch_changes_glide()
    {
        var mix = new ExtensionVoiceMixer();
        var clip = SoundSynth.Loop(Wave.Sine, 150, 0.5f, 2, 0f, 0.8f);
        mix.LoopSample(0, clip, SoundSynth.Rate, 0.1f, 1f);
        Render(mix, 20);
        // A game frame jumps gain and pitch at once.
        mix.LoopSample(0, clip, SoundSynth.Rate, 0.9f, 2f);
        var after = Render(mix, 4);
        Assert.True(MaxStep(after) < 0.1f);
    }

    [Fact]
    public void Synth_loop_wraps_seamlessly()
    {
        var clip = SoundSynth.Loop(Wave.Triangle, 433, 0.5f, 2.6f, 0.16f, 0.6f);
        Assert.True(Math.Abs(clip[^1] - clip[0]) < 0.05f);
    }
}

public class ChipMusicTests
{
    [Fact]
    public void Every_song_renders_a_clean_seamless_loop()
    {
        foreach (var song in Enum.GetValues<GameSong>())
        {
            var buf = ChipMusic.Get(song);
            Assert.True(buf.Length > SoundSynth.Rate * 15, $"{song} is too short");
            Assert.All(buf, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
            // The wrap from the last sample to the first is no bigger than an ordinary step in the song.
            Assert.True(Math.Abs(buf[^1] - buf[0]) < 0.2f, $"{song} clicks at the loop point");
        }
    }

    [Fact]
    public void Stock_effects_exist_and_stay_in_range()
    {
        foreach (var clip in new[] { GameSfx.Jump, GameSfx.Coin, GameSfx.Explode, GameSfx.Clear, GameSfx.GameOver, GameSfx.Pop })
        {
            Assert.True(clip.Length > 100);
            Assert.All(clip, v => Assert.InRange(v, -1f, 1f));
        }
    }
}
