namespace CanvasManagement.Extension.AudioPlayer;

/// <summary>
///     Available visualization styles for the audio VU meters
/// </summary>
public enum VuMeterStyle
{
    /// <summary>
    ///     Classic left/right stereo level bars
    /// </summary>
    StereoBars,

    /// <summary>
    ///     8-band frequency spectrum analyzer
    /// </summary>
    SpectrumAnalyzer,

    /// <summary>
    ///     16-band high-resolution frequency spectrum
    /// </summary>
    Spectrum16Band,

    /// <summary>
    ///     Time-frequency waterfall display
    /// </summary>
    WaterfallSpectrum,

    /// <summary>
    ///     Radial circular level meter
    /// </summary>
    CircularMeter,

    /// <summary>
    ///     Real-time audio waveform display
    /// </summary>
    Waveform,

    /// <summary>
    ///     X/Y stereo phase oscilloscope
    /// </summary>
    Oscilloscope,

    /// <summary>
    ///     Bass beat detection with BPM counter
    /// </summary>
    BeatDetection,

    /// <summary>
    ///     Professional dB peak meter
    /// </summary>
    PeakMeter
}