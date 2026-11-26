namespace CanvasManagement.Interfaces;

/// <summary>
///     A <see cref="FrameExtension"/> that is a game: it has sound effects and background music on the audio
///     board, with the same four settings in every game (Sound, Music, Volume, Music Volume). Play effects with
///     <see cref="Audio"/> and call <c>Audio.Update()</c> once per frame; the sound stops with the game.
/// </summary>
public abstract class GameExtension : FrameExtension
{
    protected GameExtension(ICanvas canvas, GameSong song) : base(canvas) => Audio = new GameSound(canvas, song);

    /// <summary>Effects and the background song of this game.</summary>
    protected GameSound Audio { get; }

    [ExtensionParameter("Sound", "Sound effects on the audio board", DefaultValue = true, Order = 20)]
    public bool Sound
    {
        get => Audio.Effects;
        set => Audio.Effects = value;
    }

    [ExtensionParameter("Music", "Background music", DefaultValue = true, Order = 21)]
    public bool Music
    {
        get => Audio.Music;
        set => Audio.Music = value;
    }

    [ExtensionParameter("Volume", "How loud this game is on the audio board", DefaultValue = 70, MinValue = 0,
        MaxValue = 100, Order = 22)]
    public int Volume
    {
        get => (int)MathF.Round(Audio.Volume * 100);
        set => Audio.Volume = Math.Clamp(value, 0, 100) / 100f;
    }

    [ExtensionParameter("Music Volume", "Music level relative to the effects", DefaultValue = 50, MinValue = 0,
        MaxValue = 100, Order = 23)]
    public int MusicVolume
    {
        get => (int)MathF.Round(Audio.MusicVolume * 100);
        set => Audio.MusicVolume = Math.Clamp(value, 0, 100) / 100f;
    }

    /// <summary>Stops effects and music. Call <c>base.OnStop()</c> when overriding.</summary>
    protected override void OnStop() => Audio.Stop();
}
