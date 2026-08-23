using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.AudioPlayer;

[ExtensionInfo(
    "Audio Player with VU Meters",
    "Plays audio files and internet radio with real-time VU meter visualizations",
    "Media Players",
    IconResourceName = "audio.svg")]
public sealed class AudioPlayerExtension : ICanvasExtension, IDisposable
{
    private const int WaterfallHistorySize = 100; // Store 100 frames of history
    private readonly object _audioLock = new();

    // BPM detection
    private readonly List<DateTime> _beatTimes = new();
    private readonly object _bitmapLock = new();
    private readonly ICanvas _canvas;

    // Audio analysis buffers  
    private readonly float[] _leftChannelBuffer = new float[1];
    private readonly float[] _rightChannelBuffer = new float[1];

    // Frequency bands for spectrum analyzer (8 or 16 bands)
    private readonly float[] _spectrumBands = new float[16]; // Support up to 16 bands

    // Waterfall spectrum
    private readonly List<float[]> _waterfallHistory = new();
    private readonly float[] _waveformBuffer = new float[1024]; // For waveform visualization

    // Extension parameters
    private string _audioUrl = "";

    // Double buffering to prevent flicker
    private SKBitmap? _backBuffer;

    private BassAudioPlayer? _bassPlayer;

    // Beat detection
    private float _beatLevel;
    private float _beatPulse;
    private float _currentBpm;
    private int _decayRate = 5;
    private bool _disposed;
    private int _frameCount; // For periodic metadata updates
    private DateTime _lastBpmUpdate = DateTime.MinValue;

    // VU meter data
    private float _leftPeak;
    private float _leftPeakHold;
    private DateTime _leftPeakHoldTime = DateTime.MinValue;
    private SKColor _peakColor = SKColors.Red;
    private int _peakHoldTime = 1000;

    // VU meter rendering
    private CancellationTokenSource? _renderCts;
    private Task? _renderTask;
    private float _rightPeak;
    private float _rightPeakHold;
    private DateTime _rightPeakHoldTime = DateTime.MinValue;
    private float _scrollPosition;
    private int _scrollSpeed = 2; // Pixels per frame

    // Scrolling text parameters
    private int _volume = 100;
    private VuMeterStyle _vuMeterStyle = VuMeterStyle.Waveform;

    public AudioPlayerExtension(ICanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    [ExtensionParameter("Audio URL", "Audio file path, HTTP stream, or internet radio URL",
        DefaultValue = "")]
    public string AudioUrl
    {
        get => _audioUrl;
        set
        {
            if (_audioUrl != value)
            {
                _audioUrl = value;
                Console.WriteLine($"[BASS Audio] AudioUrl set to: {value}");

                if (IsRunning && !string.IsNullOrWhiteSpace(value)) LoadAndPlayAudio(value);
            }
        }
    }

    [ExtensionParameter("Auto Play", "Automatically play when audio URL is set",
        DefaultValue = true)]
    public bool AutoPlay { get; set; } = true;

    [ExtensionParameter("Loop", "Restart playback when audio ends",
        DefaultValue = false)]
    public bool Loop { get; set; }

    [ExtensionParameter("Volume", "Playback volume (0-100)",
        MinValue = 0, MaxValue = 100, DefaultValue = 100)]
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _bassPlayer?.SetVolume(_volume);
        }
    }

    [ExtensionParameter("VU Meter Style", "Visualization style",
        DefaultValue = VuMeterStyle.Waveform)]
    public VuMeterStyle VuMeterStyle
    {
        get => _vuMeterStyle;
        set
        {
            _vuMeterStyle = value;
            Console.WriteLine($"[BASS Audio] VU Meter Style set to: {value}");
        }
    }

    [ExtensionParameter("Primary Color", "Primary color for VU meter (hex)",
        DefaultValue = "#00FF00")]
    public SKColor PrimaryColor { get; set; } = SKColors.Green;

    [ExtensionParameter("Secondary Color", "Secondary/warning color (hex)",
        DefaultValue = "#FFFF00")]
    public SKColor SecondaryColor { get; set; } = SKColors.Yellow;

    [ExtensionParameter("Peak Color", "Peak/clipping color (hex)",
        DefaultValue = "#FF0000")]
    public SKColor PeakColor
    {
        get => _peakColor;
        set => _peakColor = value;
    }

    [ExtensionParameter("Background Color", "Background color for the audio player",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Decay Rate", "How fast meters decay (1-20)",
        MinValue = 1, MaxValue = 20, DefaultValue = 5)]
    public int DecayRate
    {
        get => _decayRate;
        set => _decayRate = Math.Clamp(value, 1, 20);
    }

    [ExtensionParameter("Peak Hold Time", "How long to hold peak indicators (ms)",
        MinValue = 0, MaxValue = 5000, DefaultValue = 1000)]
    public int PeakHoldTime
    {
        get => _peakHoldTime;
        set => _peakHoldTime = Math.Clamp(value, 0, 5000);
    }

    [ExtensionParameter("Show Track Info", "Display scrolling track information",
        DefaultValue = true)]
    public bool ShowScrollingText { get; set; } = true;

    [ExtensionParameter("Scroll Text Color", "Color for scrolling text (hex)",
        DefaultValue = "#9900FF")]
    public SKColor ScrollTextColor { get; set; } = SKColors.Blue;

    [ExtensionParameter("Scroll Speed", "Scrolling speed in pixels per frame (1-10)",
        MinValue = 1, MaxValue = 10, DefaultValue = 2)]
    public int ScrollSpeed
    {
        get => _scrollSpeed;
        set => _scrollSpeed = Math.Clamp(value, 1, 10);
    }

    [ExtensionParameter("Playback State", "Current playback state (read-only)",
        ReadOnly = true)]
    public string PlaybackState => _bassPlayer?.IsPlaying == true ? "Playing" : "Stopped";

    [ExtensionParameter("Track Info", "Current track information (read-only)",
        ReadOnly = true)]
    public string TrackInfo => _bassPlayer?.TrackInfo ?? "No track loaded";

    public string Name => "Audio Player with VU Meters";
    public bool IsRunning { get; private set; }

    #region ExtensionMethod Exposed Methods

    // ==================== Playback Control ====================

    /// <summary>
    /// Pause audio playback
    /// </summary>
    [ExtensionMethod("Pause", "Pause the current audio playback", Category = "Playback", IconName = "pause", KeyboardShortcut = "Space")]
    public void Pause()
    {
        _bassPlayer?.Pause();
        Console.WriteLine("[BASS Audio] Playback paused");
    }

    /// <summary>
    /// Resume audio playback
    /// </summary>
    [ExtensionMethod("Resume", "Resume paused audio playback", Category = "Playback", IconName = "play", KeyboardShortcut = "Space")]
    public void Resume()
    {
        _bassPlayer?.Resume();
        Console.WriteLine("[BASS Audio] Playback resumed");
    }

    /// <summary>
    /// Toggle between play and pause states
    /// </summary>
    [ExtensionMethod("Play/Pause", "Toggle between play and pause", Category = "Playback", IconName = "play_pause", KeyboardShortcut = "Space")]
    public void TogglePlayPause()
    {
        if (_bassPlayer == null) return;

        if (_bassPlayer.IsPlaying)
        {
            _bassPlayer.Pause();
            Console.WriteLine("[BASS Audio] Toggled to paused");
        }
        else
        {
            _bassPlayer.Resume();
            Console.WriteLine("[BASS Audio] Toggled to playing");
        }
    }

    /// <summary>
    /// Stop the current audio playback
    /// </summary>
    [ExtensionMethod("Stop Playback", "Stop the current audio playback", Category = "Playback", IconName = "stop", KeyboardShortcut = "S")]
    public void StopPlayback()
    {
        _bassPlayer?.Stop();
        Console.WriteLine("[BASS Audio] Playback stopped");
    }

    /// <summary>
    /// Restart the current audio from the beginning
    /// </summary>
    [ExtensionMethod("Restart", "Restart the current audio from the beginning", Category = "Playback", IconName = "restart", KeyboardShortcut = "R")]
    public void Restart()
    {
        if (_bassPlayer != null && !string.IsNullOrWhiteSpace(_audioUrl))
        {
            LoadAndPlayAudio(_audioUrl);
            Console.WriteLine("[BASS Audio] Playback restarted");
        }
    }

    // ==================== Volume Control ====================

    /// <summary>
    /// Toggle mute on/off
    /// </summary>
    [ExtensionMethod("Toggle Mute", "Toggle audio mute on/off", Category = "Volume", IconName = "mute", KeyboardShortcut = "M")]
    public void ToggleMute()
    {
        if (_volume > 0)
        {
            _previousVolume = _volume;
            Volume = 0;
            Console.WriteLine("[BASS Audio] Muted");
        }
        else
        {
            Volume = _previousVolume > 0 ? _previousVolume : 100;
            Console.WriteLine($"[BASS Audio] Unmuted (volume: {_volume}%)");
        }
    }

    /// <summary>
    /// Increase volume by specified amount
    /// </summary>
    [ExtensionMethod("Volume Up", "Increase volume by 5%", Category = "Volume", IconName = "volume_up", KeyboardShortcut = "Up")]
    public void VolumeUp(int amount = 5)
    {
        Volume = Math.Min(100, _volume + amount);
        Console.WriteLine($"[BASS Audio] Volume: {_volume}%");
    }

    /// <summary>
    /// Decrease volume by specified amount
    /// </summary>
    [ExtensionMethod("Volume Down", "Decrease volume by 5%", Category = "Volume", IconName = "volume_down", KeyboardShortcut = "Down")]
    public void VolumeDown(int amount = 5)
    {
        Volume = Math.Max(0, _volume - amount);
        Console.WriteLine($"[BASS Audio] Volume: {_volume}%");
    }

    /// <summary>
    /// Set volume to a specific level
    /// </summary>
    [ExtensionMethod("Set Volume", "Set volume to a specific level (0-100)", Category = "Volume", IconName = "volume")]
    public void SetVolume(int level)
    {
        Volume = Math.Clamp(level, 0, 100);
        Console.WriteLine($"[BASS Audio] Volume set to: {_volume}%");
    }

    // ==================== URL/Source Control ====================

    /// <summary>
    /// Play audio from a URL or file path
    /// </summary>
    [ExtensionMethod("Play URL", "Play audio from a URL or file path", Category = "Source", IconName = "link")]
    public void PlayUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("[BASS Audio] PlayUrl: URL cannot be empty");
            return;
        }

        _audioUrl = url;
        if (IsRunning)
        {
            LoadAndPlayAudio(url);
        }
        Console.WriteLine($"[BASS Audio] Playing: {url}");
    }

    // ==================== Visualization Control ====================

    /// <summary>
    /// Cycle to the next visualization style
    /// </summary>
    [ExtensionMethod("Next Visualization", "Switch to the next VU meter visualization style", Category = "Visualization", IconName = "spectrum", KeyboardShortcut = "V")]
    public void NextVisualization()
    {
        var styles = Enum.GetValues<VuMeterStyle>();
        var currentIndex = Array.IndexOf(styles, _vuMeterStyle);
        var nextIndex = (currentIndex + 1) % styles.Length;
        VuMeterStyle = styles[nextIndex];
        Console.WriteLine($"[BASS Audio] Visualization: {_vuMeterStyle}");
    }

    /// <summary>
    /// Set visualization style by name
    /// </summary>
    [ExtensionMethod("Set Visualization", "Set the VU meter visualization style", Category = "Visualization", IconName = "spectrum")]
    public void SetVisualization(string styleName)
    {
        if (Enum.TryParse<VuMeterStyle>(styleName, true, out var style))
        {
            VuMeterStyle = style;
            Console.WriteLine($"[BASS Audio] Visualization set to: {_vuMeterStyle}");
        }
        else
        {
            Console.WriteLine($"[BASS Audio] Unknown visualization style: {styleName}");
            Console.WriteLine($"[BASS Audio] Available styles: {string.Join(", ", Enum.GetNames<VuMeterStyle>())}");
        }
    }

    // ==================== Info Getters ====================

    /// <summary>
    /// Get current playback state
    /// </summary>
    [ExtensionMethod("Get Is Playing", "Returns true if audio is currently playing", Category = "Info", IconName = "info")]
    public bool GetIsPlaying()
    {
        return _bassPlayer?.IsPlaying ?? false;
    }

    /// <summary>
    /// Get current volume level
    /// </summary>
    [ExtensionMethod("Get Volume", "Get the current volume level (0-100)", Category = "Info", IconName = "volume")]
    public int GetVolume()
    {
        return _volume;
    }

    /// <summary>
    /// Get current track information
    /// </summary>
    [ExtensionMethod("Get Track Info", "Get current track information (artist, title, album)", Category = "Info", IconName = "music")]
    public string GetTrackInfo()
    {
        return _bassPlayer?.TrackInfo ?? "No track loaded";
    }

    /// <summary>
    /// Get current track title
    /// </summary>
    [ExtensionMethod("Get Track Title", "Get the current track title", Category = "Info", IconName = "music")]
    public string GetTrackTitle()
    {
        return _bassPlayer?.TrackTitle ?? "";
    }

    /// <summary>
    /// Get current track artist
    /// </summary>
    [ExtensionMethod("Get Track Artist", "Get the current track artist", Category = "Info", IconName = "artist")]
    public string GetTrackArtist()
    {
        return _bassPlayer?.TrackArtist ?? "";
    }

    /// <summary>
    /// Get current visualization style name
    /// </summary>
    [ExtensionMethod("Get Visualization", "Get the current visualization style name", Category = "Info", IconName = "spectrum")]
    public string GetVisualization()
    {
        return _vuMeterStyle.ToString();
    }

    /// <summary>
    /// Get available visualization styles
    /// </summary>
    [ExtensionMethod("Get Available Visualizations", "Get list of available visualization styles", Category = "Info", IconName = "list")]
    public string[] GetAvailableVisualizations()
    {
        return Enum.GetNames<VuMeterStyle>();
    }

    #endregion

    private int _previousVolume = 100; // For mute toggle

    public void Start()
    {
        if (IsRunning)
            return;

        try
        {
            Console.WriteLine("[BASS Audio] Starting BASS Audio Player extension...");

            // Create back buffer for double-buffering
            _backBuffer?.Dispose();
            _backBuffer =
                new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

            // Initialize BASS audio engine
            _bassPlayer = new BassAudioPlayer();

            if (!_bassPlayer.Initialize())
            {
                // The native BASS library (libbass.so) isn't installed on this device. Don't abort the
                // assignment - run in a degraded state showing a clear message instead of crashing.
                IsRunning = true;
                Console.WriteLine(
                    "[BASS Audio] BASS native library not available - install libbass.so on the device. " +
                    "Showing a placeholder instead.");
                RenderUnavailableMessage();
                return;
            }

            IsRunning = true;
            Console.WriteLine("[BASS Audio] Extension started successfully");

            // Start VU meter rendering loop
            StartVUMeterRendering();

            // Auto-play if configured
            if (AutoPlay && !string.IsNullOrWhiteSpace(_audioUrl))
            {
                Console.WriteLine($"[BASS Audio] Auto-playing: {_audioUrl}");
                LoadAndPlayAudio(_audioUrl);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS Audio] ERROR during Start(): {ex.Message}");
            Cleanup();
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _bassPlayer?.Stop();
        Cleanup();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private void Cleanup()
    {
        _renderCts?.Cancel();
        _renderTask?.Wait(1000);

        _backBuffer?.Dispose();
        _backBuffer = null;

        _bassPlayer?.Dispose();
        _bassPlayer = null;
    }

    private void LoadAndPlayAudio(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || _bassPlayer == null)
            return;

        try
        {
            Console.WriteLine($"[BASS Audio] Loading: {url}");

            if (!_bassPlayer.LoadAndPlay(url))
            {
                Console.WriteLine("[BASS Audio] Failed to load/play audio");
                return;
            }

            _bassPlayer.SetVolume(_volume);
            Console.WriteLine("[BASS Audio] Playback started successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS Audio] Error loading audio: {ex.Message}");
        }
    }

    private void StartVUMeterRendering()
    {
        _renderCts = new CancellationTokenSource();
        var token = _renderCts.Token;

        _renderTask = Task.Run(async () =>
        {
            Console.WriteLine("[BASS Audio] VU meter rendering started");
            Console.WriteLine("[BASS Audio] Using BASS built-in FFT analysis!");

            while (IsRunning && !token.IsCancellationRequested)
                try
                {
                    // Get FFT data from BASS (super easy!)
                    UpdateAudioLevels();

                    // Render VU meter
                    RenderVUMeter();

                    // Update metadata periodically (every 5 seconds)
                    if (_frameCount++ % 100 == 0) // Every 100 frames at 20 FPS = 5 seconds
                        _bassPlayer?.UpdateMetadata();

                    // 20 FPS - smooth enough
                    await Task.Delay(50, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BASS Audio] Error in render loop: {ex.Message}");
                }

            Console.WriteLine("[BASS Audio] VU meter rendering stopped");
        }, token);
    }

    private void UpdateAudioLevels()
    {
        if (_bassPlayer == null || !_bassPlayer.IsPlaying)
        {
            // Decay to zero when not playing
            lock (_audioLock)
            {
                _leftPeak = Math.Max(0, _leftPeak - _decayRate / 20f);
                _rightPeak = Math.Max(0, _rightPeak - _decayRate / 20f);
                _leftPeakHold = Math.Max(0, _leftPeakHold - _decayRate / 20f);
                _rightPeakHold = Math.Max(0, _rightPeakHold - _decayRate / 20f);

                for (var i = 0; i < _spectrumBands.Length; i++)
                    _spectrumBands[i] = Math.Max(0, _spectrumBands[i] - _decayRate / 20f);

                _beatLevel = Math.Max(0, _beatLevel - _decayRate / 10f);
                _beatPulse = Math.Max(0, _beatPulse - 0.05f);
            }

            return;
        }

        // Get data from BASS
        lock (_audioLock)
        {
            // Get FFT data (always - needed for multiple modes)
            if (_bassPlayer.GetFFTData(_leftChannelBuffer, _rightChannelBuffer, _spectrumBands))
            {
                // Update peak levels from BASS data
                if (_leftChannelBuffer.Length > 0)
                    _leftPeak = _leftChannelBuffer[0];

                if (_rightChannelBuffer.Length > 0)
                    _rightPeak = _rightChannelBuffer[0];

                // Update peak hold
                var now = DateTime.Now;

                if (_leftPeak > _leftPeakHold)
                {
                    _leftPeakHold = _leftPeak;
                    _leftPeakHoldTime = now;
                }
                else if ((now - _leftPeakHoldTime).TotalMilliseconds > _peakHoldTime)
                {
                    _leftPeakHold = Math.Max(0, _leftPeakHold - _decayRate / 100f);
                }

                if (_rightPeak > _rightPeakHold)
                {
                    _rightPeakHold = _rightPeak;
                    _rightPeakHoldTime = now;
                }
                else if ((now - _rightPeakHoldTime).TotalMilliseconds > _peakHoldTime)
                {
                    _rightPeakHold = Math.Max(0, _rightPeakHold - _decayRate / 100f);
                }

                // Apply smoothing to spectrum
                for (var i = 0; i < _spectrumBands.Length; i++)
                    _spectrumBands[i] = Math.Max(0, _spectrumBands[i] - _decayRate / 200f);
            }

            // Get waveform data (always - instant mode switching)
            _bassPlayer.GetWaveformData(_waveformBuffer);

            // Get beat detection (always - instant mode switching)
            var newBeatLevel = _bassPlayer.DetectBeat();

            // Trigger pulse on beat with better threshold
            if (newBeatLevel > 0.6f && _beatLevel < 0.4f)
            {
                _beatPulse = 1.0f; // Trigger full pulse

                // Track beat for BPM calculation
                var now = DateTime.Now;

                // Only add beat if enough time has passed (prevent double-triggers)
                if (_beatTimes.Count == 0 || (now - _beatTimes[^1]).TotalMilliseconds > 200) _beatTimes.Add(now);

                // Keep only recent beats (last 8 seconds for more stable BPM)
                _beatTimes.RemoveAll(t => (now - t).TotalSeconds > 8);

                // Calculate BPM if we have enough beats
                if (_beatTimes.Count >= 8 && (now - _lastBpmUpdate).TotalMilliseconds > 1000)
                {
                    var timeSpan = (_beatTimes[^1] - _beatTimes[0]).TotalMinutes;
                    if (timeSpan > 0)
                    {
                        var rawBpm = (_beatTimes.Count - 1) / (float)timeSpan;

                        // Smooth BPM changes
                        if (_currentBpm == 0)
                            _currentBpm = rawBpm;
                        else
                            // Exponential smoothing
                            _currentBpm = _currentBpm * 0.7f + rawBpm * 0.3f;

                        _lastBpmUpdate = now;
                    }
                }
            }

            _beatLevel = newBeatLevel;
            _beatPulse = Math.Max(0, _beatPulse - 0.05f); // Pulse decay

            // Update waterfall history (always - instant mode switching)
            // Copy current spectrum to history
            var snapshot = new float[16];
            Array.Copy(_spectrumBands, snapshot, 16);
            _waterfallHistory.Insert(0, snapshot);

            // Limit history size
            while (_waterfallHistory.Count > WaterfallHistorySize)
                _waterfallHistory.RemoveAt(_waterfallHistory.Count - 1);
        }
    }

    private void RenderUnavailableMessage()
    {
        var bb = _backBuffer;
        if (bb == null) return;

        using var canvas = new SKCanvas(bb);
        canvas.Clear(SKColors.Black);

        var size = Math.Max(8f, _canvas.Height * 0.12f);
        using (var font = new SKFont { Size = size })
        using (var paint = new SKPaint { Color = new SKColor(255, 120, 120), IsAntialias = true })
            canvas.DrawText("Audio unavailable", _canvas.Width / 2f, _canvas.Height / 2f - size * 0.2f,
                SKTextAlign.Center, font, paint);

        using (var font = new SKFont { Size = Math.Max(6f, size * 0.55f) })
        using (var paint = new SKPaint { Color = new SKColor(170, 175, 190), IsAntialias = true })
            canvas.DrawText("install libbass.so", _canvas.Width / 2f, _canvas.Height / 2f + size * 0.8f,
                SKTextAlign.Center, font, paint);

        canvas.Flush();
        _canvas.SubmitCompletedFrame(bb);
    }

    private void RenderVUMeter()
    {
        if (_backBuffer == null) return;

        float leftLevel, rightLevel, leftHold, rightHold;
        var spectrumData = new float[_spectrumBands.Length];
        var waveformData = new float[_waveformBuffer.Length];
        float beatLevel, beatPulse, currentBpm;
        List<float[]>? waterfallData = null;

        lock (_audioLock)
        {
            leftLevel = _leftPeak;
            rightLevel = _rightPeak;
            leftHold = _leftPeakHold;
            rightHold = _rightPeakHold;
            Array.Copy(_spectrumBands, spectrumData, _spectrumBands.Length);
            Array.Copy(_waveformBuffer, waveformData, _waveformBuffer.Length);
            beatLevel = _beatLevel;
            beatPulse = _beatPulse;
            currentBpm = _currentBpm;

            // Copy waterfall history for rendering
            if (_vuMeterStyle == VuMeterStyle.WaterfallSpectrum && _waterfallHistory.Count > 0)
                waterfallData = new List<float[]>(_waterfallHistory);
        }

        lock (_bitmapLock)
        {
            using var canvas = new SKCanvas(_backBuffer);

            // Clear with background color
            canvas.Clear(BackgroundColor);

            switch (_vuMeterStyle)
            {
                case VuMeterStyle.StereoBars:
                    RenderStereoBars(canvas, leftLevel, rightLevel, leftHold, rightHold);
                    break;
                case VuMeterStyle.SpectrumAnalyzer:
                    RenderSpectrumAnalyzer(canvas, spectrumData, 8);
                    break;
                case VuMeterStyle.Spectrum16Band:
                    RenderSpectrumAnalyzer(canvas, spectrumData, 16);
                    break;
                case VuMeterStyle.WaterfallSpectrum:
                    RenderWaterfallSpectrum(canvas, waterfallData);
                    break;
                case VuMeterStyle.CircularMeter:
                    RenderCircularMeter(canvas, leftLevel, rightLevel);
                    break;
                case VuMeterStyle.Waveform:
                    RenderWaveform(canvas, waveformData);
                    break;
                case VuMeterStyle.Oscilloscope:
                    RenderOscilloscope(canvas, waveformData);
                    break;
                case VuMeterStyle.BeatDetection:
                    RenderBeatDetection(canvas, beatLevel, beatPulse, currentBpm);
                    break;
                case VuMeterStyle.PeakMeter:
                    RenderPeakMeter(canvas, leftLevel, rightLevel, leftHold, rightHold);
                    break;
                default:
                    RenderStereoBars(canvas, leftLevel, rightLevel, leftHold, rightHold);
                    break;
            }

            // Render scrolling text if enabled
            if (ShowScrollingText && _bassPlayer != null)
            {
                var trackInfo = _bassPlayer.TrackInfo;
                if (!string.IsNullOrEmpty(trackInfo) && trackInfo != "Unknown") RenderScrollingText(canvas, trackInfo);
            }

            canvas.Flush();// Blit to canvas in ONE atomic operation
            _canvas.SubmitCompletedFrame(_backBuffer);
        }
    }

    private void RenderStereoBars(SKCanvas canvas, float leftLevel, float rightLevel, float leftHold, float rightHold)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;

        var barHeight = (height - 30) / 2;
        var leftY = 10;
        var rightY = height / 2 + 5;

        DrawLevelBar(canvas, 10, leftY, width - 20, barHeight, leftLevel, leftHold, "L");
        DrawLevelBar(canvas, 10, rightY, width - 20, barHeight, rightLevel, rightHold, "R");
    }

    private void DrawLevelBar(SKCanvas canvas, int x, int y, int width, int height, float level, float peakHold,
        string label)
    {
        using var bgPaint = new SKPaint { Color = SKColors.DarkGray, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, bgPaint);

        var levelWidth = (int)(width * level);
        var color = GetLevelColor(level);
        using var levelPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, levelWidth, height, levelPaint);

        if (peakHold > 0)
        {
            var peakX = x + (int)(width * peakHold);
            using var peakPaint = new SKPaint { Color = _peakColor, StrokeWidth = 2 };
            canvas.DrawLine(peakX, y, peakX, y + height, peakPaint);
        }

        // Dynamic text size based on bar height
        var fontSize = Math.Max(8, Math.Min(16, height / 2));
        using var font = new SKFont { Size = fontSize };
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        canvas.DrawText(label, x + 5, y + height / 2 + fontSize / 3, SKTextAlign.Left, font, textPaint);
    }

    private void RenderSpectrumAnalyzer(SKCanvas canvas, float[] spectrumData, int bands)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var bandWidth = (width - 20) / bands;
        var spacing = 2;

        for (var i = 0; i < bands; i++)
        {
            var x = 10 + i * bandWidth;
            var barHeight = (int)((height - 20) * spectrumData[i]);
            var y = height - 10 - barHeight;

            var color = GetLevelColor(spectrumData[i]);
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, bandWidth - spacing, barHeight, paint);
        }

        // Draw frequency labels for 8-band - dynamic font size
        if (bands == 8)
        {
            var labels = new[] { "Bass", "Low", "Mid-L", "Mid", "Mid-H", "High", "VHigh", "Ultra" };
            var fontSize = Math.Max(6, Math.Min(10, Math.Min(width / 50, height / 20)));

            using var font = new SKFont { Size = fontSize };
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            for (var i = 0; i < labels.Length; i++)
            {
                var x = 10 + i * bandWidth + bandWidth / 2;
                canvas.DrawText(labels[i], x, height - 2, SKTextAlign.Center, font, textPaint);
            }
        }
    }

    private void RenderCircularMeter(SKCanvas canvas, float leftLevel, float rightLevel)
    {
        var centerX = _canvas.Width / 2;
        var centerY = _canvas.Height / 2;
        var radius = Math.Min(_canvas.Width, _canvas.Height) / 2 - 20;

        DrawCircularLevel(canvas, centerX, centerY, radius, leftLevel, -180, 90);
        DrawCircularLevel(canvas, centerX, centerY, radius, rightLevel, 0, 90);
    }

    private void DrawCircularLevel(SKCanvas canvas, int centerX, int centerY, int radius, float level, float startAngle,
        float sweepAngle)
    {
        var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);

        using var bgPaint = new SKPaint
        {
            Color = SKColors.DarkGray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10,
            IsAntialias = true
        };

        using var path = new SKPath();
        path.AddArc(rect, startAngle, sweepAngle);
        canvas.DrawPath(path, bgPaint);

        var levelSweep = sweepAngle * level;
        var color = GetLevelColor(level);

        using var levelPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10,
            IsAntialias = true
        };

        using var levelPath = new SKPath();
        levelPath.AddArc(rect, startAngle, levelSweep);
        canvas.DrawPath(levelPath, levelPaint);
    }

    private void RenderWaveform(SKCanvas canvas, float[] waveformData)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerY = height / 2;

        // Ensure we have valid data
        if (waveformData == null || waveformData.Length == 0)
        {
            var fontSize = Math.Max(10, Math.Min(14, Math.Min(width, height) / 15));
            using var font = new SKFont { Size = fontSize };
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            canvas.DrawText("No waveform data", width / 2, centerY, SKTextAlign.Center, font, textPaint);
            return;
        }

        using var path = new SKPath();

        // Use all available samples, distributed across the width
        for (var x = 0; x < width; x++)
        {
            var samplePos = x / (float)width * waveformData.Length;
            var sampleIdx = (int)samplePos;

            if (sampleIdx >= waveformData.Length)
                sampleIdx = waveformData.Length - 1;

            var amplitude = Math.Clamp(waveformData[sampleIdx], -1.0f, 1.0f);
            var y = centerY - amplitude * (height / 2.2f);

            if (x == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        using var waveformPaint = new SKPaint
        {
            Color = PrimaryColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawPath(path, waveformPaint);

        // Draw center line and bounds
        using var gridPaint = new SKPaint { Color = SKColors.DarkGray, StrokeWidth = 1 };
        canvas.DrawLine(0, centerY, width, centerY, gridPaint);
        canvas.DrawLine(0, 10, width, 10, gridPaint);
        canvas.DrawLine(0, height - 10, width, height - 10, gridPaint);

        // Draw markers - dynamic font size
        var markerSize = Math.Max(6, Math.Min(10, Math.Min(width, height) / 25));
        using var markerFont = new SKFont { Size = markerSize };
        using var markerPaint = new SKPaint
        {
            Color = SKColors.Gray,
            IsAntialias = true
        };
        canvas.DrawText("+1.0", 5, 15 + markerSize / 2, SKTextAlign.Left, markerFont, markerPaint);
        canvas.DrawText("0.0", 5, centerY + markerSize / 2, SKTextAlign.Left, markerFont, markerPaint);
        canvas.DrawText("-1.0", 5, height - 10 + markerSize / 2, SKTextAlign.Left, markerFont, markerPaint);
    }

    private void RenderOscilloscope(SKCanvas canvas, float[] waveformData)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2;
        var centerY = height / 2;
        var radius = Math.Min(width, height) / 2 - 20;

        // Draw circle background
        var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);

        using var circlePaint = new SKPaint
        {
            Color = SKColors.DarkGray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawOval(rect, circlePaint);

        // Draw crosshairs
        using var gridPaint = new SKPaint { Color = SKColors.DarkGray, StrokeWidth = 1 };
        canvas.DrawLine(centerX, 0, centerX, height, gridPaint);
        canvas.DrawLine(0, centerY, width, centerY, gridPaint);

        // Draw oscilloscope trace
        using var path = new SKPath();
        var first = true;

        for (var i = 0; i < waveformData.Length - 1; i += 2)
        {
            if (i + 1 >= waveformData.Length)
                break;

            var left = waveformData[i];
            var right = waveformData[i + 1];

            var x = centerX + left * radius;
            var y = centerY + right * radius;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        using var tracePaint = new SKPaint
        {
            Color = PrimaryColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawPath(path, tracePaint);

        // Labels - dynamic font size
        //var fontSize = Math.Max(8, Math.Min(12, Math.Min(width, height) / 20));
        //using var labelPaint = new SKPaint 
        //{ 
        //    Color = SKColors.White, 
        //    TextSize = fontSize, 
        //    IsAntialias = true 
        //};
        //canvas.DrawText("L", 5, centerY + fontSize / 3, labelPaint);
        //canvas.DrawText("R", centerX - fontSize / 2, 15, labelPaint);
    }

    private void RenderBeatDetection(SKCanvas canvas, float beatLevel, float beatPulse, float currentBpm)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var centerX = width / 2;
        var centerY = height / 2;

        var maxRadius = Math.Min(width, height) / 2 - 20;
        var beatRadius = (int)(maxRadius * beatLevel);

        // Outer ring (beat level)
        if (beatRadius > 0)
        {
            var color = GetLevelColor(beatLevel);
            var rect = new SKRect(centerX - beatRadius, centerY - beatRadius, centerX + beatRadius,
                centerY + beatRadius);

            using var beatPaint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 10,
                IsAntialias = true
            };
            canvas.DrawOval(rect, beatPaint);
        }

        // Inner pulse (beat trigger)
        if (beatPulse > 0)
        {
            var pulseRadius = (int)(maxRadius * (1.0f - beatPulse));
            var pulseAlpha = (byte)(255 * beatPulse);

            var pulseColor = new SKColor(
                _peakColor.Red,
                _peakColor.Green,
                _peakColor.Blue,
                pulseAlpha
            );

            var pulseRect = new SKRect(centerX - pulseRadius, centerY - pulseRadius, centerX + pulseRadius,
                centerY + pulseRadius);

            using var pulsePaint = new SKPaint
            {
                Color = pulseColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 20,
                IsAntialias = true
            };
            canvas.DrawOval(pulseRect, pulsePaint);
        }

        // Center indicator - scaled to canvas size
        var indicatorSize = Math.Max(5, Math.Min(10, Math.Min(width, height) / 20));
        using var centerPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(centerX - indicatorSize / 2, centerY - indicatorSize / 2, indicatorSize, indicatorSize,
            centerPaint);

        // Dynamic font sizes based on canvas size
        var bpmFontSize = Math.Max(12, Math.Min(24, Math.Min(width, height) / 8));

        // BPM display - centered
        if (currentBpm > 0)
        {
            var bpmText = $"BPM: {(int)currentBpm}";
            using var bpmFont = new SKFont { Size = bpmFontSize };
            using var bpmPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            canvas.DrawText(bpmText, centerX, centerY, SKTextAlign.Center, bpmFont, bpmPaint);
        }
    }

    private void RenderWaterfallSpectrum(SKCanvas canvas, List<float[]>? waterfallData)
    {
        if (waterfallData == null || waterfallData.Count == 0)
        {
            using var font = new SKFont { Size = 16 };
            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("Building history...", _canvas.Width / 2 - 60, _canvas.Height / 2, SKTextAlign.Left, font,
                textPaint);
            return;
        }

        var width = _canvas.Width;
        var height = _canvas.Height;
        var bands = 16;

        var bandWidth = Math.Max(1, width / bands);
        var labelHeight = Math.Max(12, height / 20);
        var availableHeight = height - labelHeight;
        var rowHeight = Math.Max(1, availableHeight / Math.Min(waterfallData.Count, WaterfallHistorySize));

        // Draw waterfall
        for (var row = 0; row < waterfallData.Count && row < WaterfallHistorySize; row++)
        {
            var spectrumFrame = waterfallData[row];
            var y = row * rowHeight;

            if (y + rowHeight > availableHeight)
                break;

            for (var band = 0; band < bands && band < spectrumFrame.Length; band++)
            {
                var x = band * bandWidth;
                var intensity = spectrumFrame[band];

                SKColor color;
                if (intensity < 0.2f)
                {
                    color = new SKColor(0, 0, (byte)(intensity * 1275));
                }
                else if (intensity < 0.5f)
                {
                    var t = (intensity - 0.2f) / 0.3f;
                    color = new SKColor(0, (byte)(t * 255), (byte)(255 * (1 - t)));
                }
                else if (intensity < 0.8f)
                {
                    var t = (intensity - 0.5f) / 0.3f;
                    color = new SKColor((byte)(t * 255), 255, 0);
                }
                else
                {
                    var t = (intensity - 0.8f) / 0.2f;
                    color = new SKColor(255, (byte)(255 * (1 - t)), 0);
                }

                using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x, y, bandWidth + 1, rowHeight + 1, paint);
            }
        }

        // Draw frequency labels
        var labels = new[]
        {
            "Sub", "Bass", "Low", "Mid-L", "Mid", "Mid-H", "High", "V.High",
            "Brill", "Air", "12k", "14k", "16k", "18k", "20k", "22k"
        };
        var fontSize = Math.Max(6, Math.Min(10, width / 100));

        using var labelFont = new SKFont { Size = fontSize };
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        for (var i = 0; i < Math.Min(bands, labels.Length); i++)
        {
            var x = i * bandWidth + bandWidth / 2;
            var labelWidth = labels[i].Length * fontSize / 2;
            canvas.DrawText(labels[i], Math.Max(0, x - labelWidth / 2), height - 2, SKTextAlign.Left, labelFont,
                labelPaint);
        }

        // Draw title
        var titleSize = Math.Max(10, Math.Min(14, width / 50));
        using var titleFont = new SKFont { Size = titleSize };
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("WATERFALL SPECTRUM", 10, 15, SKTextAlign.Left, titleFont, titlePaint);

        var timeSize = Math.Max(8, titleSize - 2);
        using var timeFont = new SKFont { Size = timeSize };
        using var timePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("? Time", width - 50, 15, SKTextAlign.Left, timeFont, timePaint);
    }

    private void RenderPeakMeter(SKCanvas canvas, float leftLevel, float rightLevel, float leftHold, float rightHold)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;

        var meterWidth = Math.Clamp(width / 7, 30, 60);
        var spacing = Math.Max(10, width / 50);

        var leftX = width / 2 - meterWidth - spacing;
        var rightX = width / 2 + spacing;
        var meterHeight = height - 40;

        DrawPeakMeter(canvas, leftX, 20, meterWidth, meterHeight, leftLevel, leftHold, "L");
        DrawPeakMeter(canvas, rightX, 20, meterWidth, meterHeight, rightLevel, rightHold, "R");
    }

    private void DrawPeakMeter(SKCanvas canvas, int x, int y, int width, int height, float level, float peakHold,
        string label)
    {
        using var bgPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, bgPaint);

        var segments = Math.Max(10, Math.Min(40, height / 10));
        var segmentHeight = (height - 10) / segments;
        var segmentSpacing = Math.Max(1, segmentHeight / 10);

        for (var i = 0; i < segments; i++)
        {
            var segY = y + height - i * segmentHeight - segmentHeight;
            var segLevel = (float)i / segments;

            if (segLevel <= level)
            {
                var color = GetLevelColor(segLevel);
                using var segPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + 2, segY, width - 4, segmentHeight - segmentSpacing, segPaint);
            }
        }

        if (peakHold > 0)
        {
            var peakY = y + height - (int)(height * peakHold);
            using var peakPaint = new SKPaint { Color = _peakColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x + 2, peakY, width - 4, Math.Max(2, segmentHeight / 3), peakPaint);
        }

        var fontSize = Math.Max(10, Math.Min(16, width / 4));
        using var font = new SKFont { Size = fontSize };
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        canvas.DrawText(label, x + width / 2, y - 5, SKTextAlign.Center, font, textPaint);
    }

    private SKColor GetLevelColor(float level)
    {
        // More lenient thresholds for better color distribution
        if (level > 0.85f)
            return _peakColor; // Red: only very loud
        if (level > 0.60f)
            return SecondaryColor; // Yellow: moderately loud
        return PrimaryColor; // Green: normal levels
    }

    private SKColor ParseColor(string hexColor)
    {
        try
        {
            return SKColor.Parse(hexColor);
        }
        catch
        {
            return SKColors.White;
        }
    }

    /// <summary>
    ///     Render scrolling text at the bottom of the canvas
    /// </summary>
    private void RenderScrollingText(SKCanvas canvas, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var width = _canvas.Width;
        var height = _canvas.Height;

        var fontSize = Math.Max(8, Math.Min(14, height / 10));
        var textColor = ScrollTextColor;

        var charWidth = fontSize * 0.6f;
        var textWidth = text.Length * charWidth;

        _scrollPosition -= _scrollSpeed;

        if (_scrollPosition < -textWidth) _scrollPosition = width;

        var textY = height - Math.Max(4, fontSize / 2);

        // Draw semi-transparent background bar
        var barHeight = fontSize + 4;
        var barY = height - barHeight;
        var bgColor = new SKColor(0, 0, 0, 180);

        using var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, barY, width, barHeight, bgPaint);

        // Draw scrolling text
        using var font = new SKFont { Size = fontSize };
        using var textPaint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true
        };
        canvas.DrawText(text, (int)_scrollPosition, textY, SKTextAlign.Left, font, textPaint);

        // Draw wrapped text for seamless loop
        if (_scrollPosition < 0 && _scrollPosition > -textWidth)
            canvas.DrawText(text, (int)(_scrollPosition + textWidth + 20), textY, SKTextAlign.Left, font, textPaint);
    }
}