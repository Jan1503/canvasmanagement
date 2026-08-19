using System.Runtime.InteropServices;
using CanvasManagement.Interfaces;
using LibVLCSharp.Shared;
using SkiaSharp;
using VideoLibrary;

namespace CanvasManagement.Extension.VLCPlayer;

[ExtensionInfo(
    "VLC Media Player",
    "Plays video files and streams directly on the canvas using LibVLC.",
    "Media Players",
    IconResourceName = "vlc.svg")]
public sealed class VLCMediaPlayerExtension : ICanvasExtension, IDisposable
{
    private static bool _coreInitialized;
    private static readonly object _coreInitLock = new();
    private readonly ICanvas _canvas;
    private readonly object _frameLock = new();
    private readonly object _lock = new();
    private bool _autoPlay = true;
    private SKBitmap? _backBuffer; // Back buffer for atomic frame submission
    private SKBitmap? _currentBitmap;
    private bool _disposed;
    private string _lastPlayedUrl = ""; // Track last played URL to avoid restarts
    private volatile bool _isLoadingMedia = false; // Guard against concurrent LoadAndPlayMedia calls

    private LibVLC? _libVLC;
    private bool _loop;
    private MediaPlayer? _mediaPlayer;

    // Extension parameters
    private string _mediaUrl = "";
    private int _pitch;
    private string _smbDomain = "";
    private string _smbPassword = "";
    private int _volume = 100;
    private bool _mute = false;
    
    public VLCMediaPlayerExtension(ICanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

        // Initialize LibVLC core only once, in a thread-safe manner
        lock (_coreInitLock)
        {
            if (!_coreInitialized)
                try
                {
                    Console.WriteLine("[VLC] Initializing LibVLC core...");
                    Core.Initialize();
                    _coreInitialized = true;
                    Console.WriteLine("[VLC] LibVLC core initialized successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VLC] CRITICAL: Failed to initialize LibVLC core: {ex.Message}");
                    Console.WriteLine($"[VLC] Stack trace: {ex.StackTrace}");
                    throw new InvalidOperationException(
                        "Failed to initialize LibVLC. Ensure VLC is installed: " +
                        "Linux: sudo apt-get install vlc libvlc-dev | " +
                        "Windows: Install VLC from videolan.org", ex);
                }
        }
    }

    [ExtensionParameter("Media URL",
        "File path, HTTP, RTSP, or SMB URL (smb://server/share/path or \\\\server\\share\\path)",
        DefaultValue = "")]
    public string MediaUrl
    {
        get => _mediaUrl;
        set
        {
            if (_mediaUrl != value)
            {
                var oldValue = _mediaUrl;
                _mediaUrl = value;
                Console.WriteLine($"[VLC] MediaUrl property set to: {value}");

                // Don't auto-play immediately when URL changes during configuration
                // Wait for all parameters to be set (credentials might come after URL)
                if (IsRunning && !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(oldValue))
                {
                    // URL changed after initial configuration - play immediately
                    Console.WriteLine("[VLC] Extension is running, loading media...");
                    Task.Run(() => LoadAndPlayMedia(value));
                }
                else if (IsRunning && string.IsNullOrWhiteSpace(value))
                {
                    Console.WriteLine("[VLC] MediaUrl is empty - no media to play");
                }
                else
                {
                    Console.WriteLine(
                        "[VLC] Extension not yet running or initial configuration - will play via AutoPlay if enabled");
                }
            }
        }
    }

    [ExtensionParameter("Auto Play", "Automatically play when media URL is set",
        DefaultValue = true)] // Changed default to true
    public bool AutoPlay
    {
        get => _autoPlay;
        set
        {
            Console.WriteLine($"[VLC] AutoPlay set to: {value}");
            _autoPlay = value;
        }
    }

    [ExtensionParameter("Loop", "Restart playback when media ends",
        DefaultValue = false)]
    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_mediaPlayer != null && IsRunning)
            {
                // VLC doesn't have direct loop property, we'll handle it in EndReached event
            }
        }
    }

    [ExtensionParameter("Volume", "Playback volume (0-100)",
        MinValue = 0, MaxValue = 100, DefaultValue = 100)]
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_mediaPlayer != null && IsRunning)
                try
                {
                    _mediaPlayer.Volume = _volume;
                    Console.WriteLine($"[VLC] Volume set to: {_volume}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VLC] Error setting volume: {ex.Message}");
                }

            // Trigger delayed playback check - this handles cases where Volume is the last parameter
            // (common for HTTP URLs where no SMB credentials are provided)
            if (IsRunning && _autoPlay && !string.IsNullOrWhiteSpace(_mediaUrl))
                Task.Run(async () =>
                {
                    // Wait 100ms for all parameters to be set
                    await Task.Delay(100);

                    // Skip if already loading or if URL was already handled
                    if (_isLoadingMedia || _lastPlayedUrl == _mediaUrl)
                    {
                        Console.WriteLine("[VLC] Media already loading or played, skipping delayed trigger");
                        return;
                    }

                    // Check if media is already playing or starting
                    var isActiveOrStarting = _mediaPlayer != null &&
                                             (_mediaPlayer.State == VLCState.Playing ||
                                              _mediaPlayer.State == VLCState.Opening ||
                                              _mediaPlayer.State == VLCState.Buffering);
                    
                    if (isActiveOrStarting)
                    {
                        Console.WriteLine("[VLC] Media already playing, skipping delayed trigger");
                        return;
                    }

                    var notPlaying = _mediaPlayer != null &&
                                     (_mediaPlayer.State == VLCState.Stopped ||
                                      _mediaPlayer.State == VLCState.Ended ||
                                      _mediaPlayer.State == VLCState.Error ||
                                      _mediaPlayer.State == VLCState.NothingSpecial);

                    if (notPlaying && !string.IsNullOrWhiteSpace(_mediaUrl))
                    {
                        var isNetworkStream = _mediaUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                              _mediaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                              _mediaUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);

                        var isSmbPath = _mediaUrl.StartsWith(@"\\") || _mediaUrl.StartsWith("//") ||
                                        _mediaUrl.StartsWith("smb://", StringComparison.OrdinalIgnoreCase);
                        var smbCredentialsComplete = !string.IsNullOrWhiteSpace(SmbUsername) &&
                                                     !string.IsNullOrWhiteSpace(_smbPassword);

                        // Trigger playback if ready
                        if (isNetworkStream || (isSmbPath && smbCredentialsComplete))
                        {
                            Console.WriteLine("[VLC] Delayed playback trigger after Volume set");
                            LoadAndPlayMedia(_mediaUrl);
                        }
                    }
                });
        }
    }

    [ExtensionParameter("Enable Audio", "Enable audio playback (if supported)",
        DefaultValue = true)]
    public bool EnableAudio { get; set; } = true;

    [ExtensionParameter("Mute", "Mute audio playback",
        DefaultValue = false)]
    public bool Mute 
    {
        get => _mute;
        set
        {
            _mute = value;
            if (_mediaPlayer != null && IsRunning)
                try
                {
                    _mediaPlayer.Mute = _mute;
                    Console.WriteLine($"[VLC] Mute set to: {_mute}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VLC] Error setting mute: {ex.Message}");
                }
        }
    }

    [ExtensionParameter("SMB Username", "Username for SMB/CIFS shares (leave empty for guest access)",
        DefaultValue = "")]
    public string SmbUsername { get; set; } = "";

    [ExtensionParameter("SMB Password", "Password for SMB/CIFS shares",
        DefaultValue = "")]
    public string SmbPassword
    {
        get => _smbPassword;
        set
        {
            _smbPassword = value;

            // If we have a media URL, username, and password, and AutoPlay is enabled,
            // trigger playback now (this is likely the last parameter being set during configuration)
            if (IsRunning && _autoPlay &&
                !string.IsNullOrWhiteSpace(_mediaUrl) &&
                !string.IsNullOrWhiteSpace(SmbUsername) &&
                !string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine("[VLC] SMB credentials complete - triggering playback");
                Task.Run(() => LoadAndPlayMedia(_mediaUrl));
            }
        }
    }

    [ExtensionParameter("SMB Domain", "Domain for SMB/CIFS shares (optional)",
        DefaultValue = "")]
    public string SmbDomain
    {
        get => _smbDomain;
        set
        {
            _smbDomain = value;

            // This is often the last parameter set via API/remote
            // Trigger playback if we have a media URL and AutoPlay is enabled
            // (works for both SMB shares with domain, and HTTP URLs that don't need credentials)
            if (IsRunning && _autoPlay && !string.IsNullOrWhiteSpace(_mediaUrl))
            {
                // Check if this is an HTTP/RTSP URL (doesn't need SMB credentials)
                var isNetworkStream = _mediaUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                      _mediaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                      _mediaUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);

                // Check if SMB credentials are complete (for UNC paths)
                var isSmbPath = _mediaUrl.StartsWith(@"\\") || _mediaUrl.StartsWith("//") ||
                                _mediaUrl.StartsWith("smb://", StringComparison.OrdinalIgnoreCase);
                var smbCredentialsComplete = !string.IsNullOrWhiteSpace(SmbUsername) &&
                                             !string.IsNullOrWhiteSpace(_smbPassword);

                // Trigger playback if:
                // 1. It's a network stream (HTTP/RTSP) - doesn't need credentials, OR
                // 2. It's an SMB path with credentials complete
                if (isNetworkStream || (isSmbPath && smbCredentialsComplete) || (!isSmbPath && !isNetworkStream))
                {
                    Console.WriteLine("[VLC] Configuration complete - triggering playback");
                    Task.Run(() => LoadAndPlayMedia(_mediaUrl));
                }
                else
                {
                    Console.WriteLine("[VLC] Waiting for SMB credentials to complete...");
                }
            }
        }
    }

    [ExtensionParameter("Playback State", "Current playback state (read-only)",
        ReadOnly = true)]
    public string PlaybackState
    {
        get
        {
            if (_mediaPlayer == null || !IsRunning)
                return "Stopped";

            return _mediaPlayer.State switch
            {
                VLCState.Playing => "Playing",
                VLCState.Paused => "Paused",
                VLCState.Stopped => "Stopped",
                VLCState.Ended => "Ended",
                VLCState.Error => "Error",
                VLCState.Opening => "Opening",
                VLCState.Buffering => "Buffering",
                _ => "Unknown"
            };
        }
    }

    [ExtensionParameter("Position", "Current playback position (seconds, read-only)",
        ReadOnly = true)]
    public long Position
    {
        get
        {
            if (_mediaPlayer?.Media == null || !IsRunning)
                return 0;

            return _mediaPlayer.Time / 1000; // Convert ms to seconds
        }
    }

    [ExtensionParameter("Duration", "Total media duration (seconds, read-only)",
        ReadOnly = true)]
    public long Duration
    {
        get
        {
            if (_mediaPlayer?.Media == null || !IsRunning)
                return 0;

            return _mediaPlayer.Length / 1000; // Convert ms to seconds
        }
    }

    [ExtensionParameter("Background Color", "Background color for the player",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    public string Name => "VLC Media Player";

    public bool IsRunning { get; private set; }

    #region ExtensionMethods - Callable actions exposed to UI/API

    /// <summary>
    /// Pauses media playback
    /// </summary>
    [ExtensionMethod("Pause", "Pauses the current media playback",
        Category = "Playback", IconName = "pause", KeyboardShortcut = "Space", Order = 10)]
    public void Pause()
    {
        if (_mediaPlayer != null && IsRunning && _mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            Console.WriteLine("[VLC] Playback paused");
        }
    }

    /// <summary>
    /// Resumes media playback
    /// </summary>
    [ExtensionMethod("Resume", "Resumes paused media playback",
        Category = "Playback", IconName = "play", KeyboardShortcut = "Space", Order = 11)]
    public void Resume()
    {
        if (_mediaPlayer != null && IsRunning && !_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Play();
            Console.WriteLine("[VLC] Playback resumed");
        }
    }

    /// <summary>
    /// Toggles between play and pause
    /// </summary>
    [ExtensionMethod("Toggle Play/Pause", "Toggles between play and pause states",
        Category = "Playback", IconName = "play-pause", KeyboardShortcut = "Space", Order = 12)]
    public void TogglePlayPause()
    {
        if (_mediaPlayer != null && IsRunning)
        {
            if (_mediaPlayer.IsPlaying)
                _mediaPlayer.Pause();
            else
                _mediaPlayer.Play();
            
            Console.WriteLine($"[VLC] Playback toggled: {(_mediaPlayer.IsPlaying ? "Playing" : "Paused")}");
        }
    }

    /// <summary>
    /// Stops media playback (keeps extension running)
    /// </summary>
    [ExtensionMethod("Stop Playback", "Stops the current media playback",
        Category = "Playback", IconName = "stop", Order = 20)]
    public void StopPlayback()
    {
        if (_mediaPlayer != null && IsRunning)
        {
            _mediaPlayer.Stop();
            Console.WriteLine("[VLC] Playback stopped");
        }
    }

    /// <summary>
    /// Restarts playback from the beginning
    /// </summary>
    [ExtensionMethod("Restart", "Restarts playback from the beginning",
        Category = "Playback", IconName = "restart", Order = 25)]
    public void Restart()
    {
        if (_mediaPlayer != null && IsRunning && !string.IsNullOrWhiteSpace(_mediaUrl))
        {
            _mediaPlayer.Stop();
            Thread.Sleep(100);
            LoadAndPlayMedia(_mediaUrl);
            Console.WriteLine("[VLC] Playback restarted");
        }
    }

    /// <summary>
    /// Seeks forward by specified seconds
    /// </summary>
    [ExtensionMethod("Skip Forward", "Skips forward by the specified number of seconds",
        Category = "Navigation", IconName = "skip-forward", KeyboardShortcut = "?", Order = 30)]
    public void SkipForward(int seconds = 10)
    {
        if (_mediaPlayer != null && IsRunning && _mediaPlayer.IsSeekable)
        {
            var newTime = _mediaPlayer.Time + (seconds * 1000);
            if (newTime < _mediaPlayer.Length)
            {
                _mediaPlayer.Time = newTime;
                Console.WriteLine($"[VLC] Skipped forward {seconds}s to {newTime / 1000}s");
            }
        }
    }

    /// <summary>
    /// Seeks backward by specified seconds
    /// </summary>
    [ExtensionMethod("Skip Backward", "Skips backward by the specified number of seconds",
        Category = "Navigation", IconName = "skip-back", KeyboardShortcut = "?", Order = 31)]
    public void SkipBackward(int seconds = 10)
    {
        if (_mediaPlayer != null && IsRunning && _mediaPlayer.IsSeekable)
        {
            var newTime = Math.Max(0, _mediaPlayer.Time - (seconds * 1000));
            _mediaPlayer.Time = newTime;
            Console.WriteLine($"[VLC] Skipped backward {seconds}s to {newTime / 1000}s");
        }
    }

    /// <summary>
    /// Seeks to a specific position in seconds
    /// </summary>
    [ExtensionMethod("Seek To", "Seeks to a specific position in seconds",
        Category = "Navigation", IconName = "clock", Order = 35)]
    public void SeekTo(long positionSeconds)
    {
        if (_mediaPlayer != null && IsRunning && _mediaPlayer.IsSeekable)
        {
            var newTime = positionSeconds * 1000;
            if (newTime >= 0 && newTime <= _mediaPlayer.Length)
            {
                _mediaPlayer.Time = newTime;
                Console.WriteLine($"[VLC] Seeked to {positionSeconds}s");
            }
        }
    }

    /// <summary>
    /// Seeks to a percentage of the total duration
    /// </summary>
    [ExtensionMethod("Seek To Percent", "Seeks to a percentage of the total duration (0-100)",
        Category = "Navigation", IconName = "percent", Order = 36)]
    public void SeekToPercent(float percent)
    {
        if (_mediaPlayer != null && IsRunning && _mediaPlayer.IsSeekable)
        {
            var clampedPercent = Math.Clamp(percent, 0f, 100f) / 100f;
            _mediaPlayer.Position = clampedPercent;
            Console.WriteLine($"[VLC] Seeked to {percent}%");
        }
    }

    /// <summary>
    /// Toggles mute state
    /// </summary>
    [ExtensionMethod("Toggle Mute", "Toggles audio mute on/off",
        Category = "Audio", IconName = "volume-x", KeyboardShortcut = "M", Order = 40)]
    public void ToggleMute()
    {
        if (_mediaPlayer != null && IsRunning)
        {
            _mute = !_mute;
            _mediaPlayer.Mute = _mute;
            Console.WriteLine($"[VLC] Mute toggled: {(_mute ? "Muted" : "Unmuted")}");
        }
    }

    /// <summary>
    /// Increases volume by specified amount
    /// </summary>
    [ExtensionMethod("Volume Up", "Increases volume by the specified amount",
        Category = "Audio", IconName = "volume-2", KeyboardShortcut = "?", Order = 41)]
    public void VolumeUp(int amount = 10)
    {
        if (_mediaPlayer != null && IsRunning)
        {
            _volume = Math.Clamp(_volume + amount, 0, 100);
            _mediaPlayer.Volume = _volume;
            Console.WriteLine($"[VLC] Volume increased to {_volume}");
        }
    }

    /// <summary>
    /// Decreases volume by specified amount
    /// </summary>
    [ExtensionMethod("Volume Down", "Decreases volume by the specified amount",
        Category = "Audio", IconName = "volume-1", KeyboardShortcut = "?", Order = 42)]
    public void VolumeDown(int amount = 10)
    {
        if (_mediaPlayer != null && IsRunning)
        {
            _volume = Math.Clamp(_volume - amount, 0, 100);
            _mediaPlayer.Volume = _volume;
            Console.WriteLine($"[VLC] Volume decreased to {_volume}");
        }
    }

    /// <summary>
    /// Sets volume to a specific level
    /// </summary>
    [ExtensionMethod("Set Volume", "Sets volume to a specific level (0-100)",
        Category = "Audio", IconName = "volume", Order = 43)]
    public void SetVolume(int level)
    {
        if (_mediaPlayer != null && IsRunning)
        {
            _volume = Math.Clamp(level, 0, 100);
            _mediaPlayer.Volume = _volume;
            Console.WriteLine($"[VLC] Volume set to {_volume}");
        }
    }

    /// <summary>
    /// Loads and plays a new media URL
    /// </summary>
    [ExtensionMethod("Play URL", "Loads and plays a new media URL",
        Category = "Media", IconName = "link", Order = 50)]
    public void PlayUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("[VLC] PlayUrl: URL is empty");
            return;
        }
        
        _mediaUrl = url;
        
        if (IsRunning)
        {
            Console.WriteLine($"[VLC] Playing new URL: {url}");
            Task.Run(() => LoadAndPlayMedia(url));
        }
        else
        {
            Console.WriteLine("[VLC] PlayUrl: Extension not running, URL saved for when Start() is called");
        }
    }

    /// <summary>
    /// Gets the current playback position in seconds
    /// </summary>
    [ExtensionMethod("Get Position", "Gets the current playback position in seconds",
        Category = "Info", IconName = "info", Order = 60, ReturnsValue = true)]
    public long GetPosition()
    {
        if (_mediaPlayer != null && IsRunning)
            return _mediaPlayer.Time / 1000;
        return 0;
    }

    /// <summary>
    /// Gets the total media duration in seconds
    /// </summary>
    [ExtensionMethod("Get Duration", "Gets the total media duration in seconds",
        Category = "Info", IconName = "info", Order = 61, ReturnsValue = true)]
    public long GetDuration()
    {
        if (_mediaPlayer != null && IsRunning)
            return _mediaPlayer.Length / 1000;
        return 0;
    }

    /// <summary>
    /// Gets whether media is currently playing
    /// </summary>
    [ExtensionMethod("Is Playing", "Returns true if media is currently playing",
        Category = "Info", IconName = "info", Order = 62, ReturnsValue = true)]
    public bool GetIsPlaying()
    {
        return _mediaPlayer?.IsPlaying ?? false;
    }

    /// <summary>
    /// Gets playback progress as percentage (0-100)
    /// </summary>
    [ExtensionMethod("Get Progress", "Gets playback progress as percentage (0-100)",
        Category = "Info", IconName = "info", Order = 63, ReturnsValue = true)]
    public float GetProgress()
    {
        if (_mediaPlayer != null && IsRunning && _mediaPlayer.Length > 0)
            return _mediaPlayer.Position * 100f;
        return 0f;
    }

    #endregion

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning)
                return;

            try
            {
                Console.WriteLine("[VLC] Starting VLC Media Player extension...");

                // Verify LibVLC core is initialized
                if (!_coreInitialized)
                    throw new InvalidOperationException("LibVLC core not initialized. This should not happen.");

                // Try hardware decoder configurations for Raspberry Pi 4
                // Priority order: Let VLC auto-detect first, then specific codecs
                var hwDecoderConfigs = new[]
                {
                    // Config 1: Auto-detect (best - works for both H.264 and HEVC)
                    new[]
                    {
                        "--avcodec-hw=any", "--avcodec-dr", "--avcodec-fast", "--file-caching=300",
                        "--network-caching=1000", "--no-spu"
                    },
                    //new[] { "--avcodec-hw=none", "--avcodec-dr", "--avcodec-fast", "--file-caching=300", "--network-caching=1000", "--no-spu" },

                    // Config 2: HEVC hardware decoder (for x265 videos)
                    new[]
                    {
                        "--codec=hevc_v4l2m2m", "--v4l2-chroma=RV32", "--file-caching=300", "--network-caching=1000",
                        "--no-spu"
                    },

                    // Config 3: H.264 V4L2 M2M (for x264 videos)
                    new[]
                    {
                        "--codec=h264_v4l2m2m", "--v4l2-chroma=RV32", "--file-caching=300", "--network-caching=1000",
                        "--no-spu"
                    },

                    // Config 4: MMAL via avcodec (older Pi 4 method)
                    new[]
                    {
                        "--codec=avcodec", "--avcodec-hw=mmal", "--file-caching=300", "--network-caching=1000",
                        "--no-spu"
                    },

                    // Config 5: Software fallback (slow but reliable)
                    new[]
                    {
                        "--codec=avcodec", "--file-caching=300", "--clock-jitter=0", "--network-caching=1000",
                        "--no-spu"
                    }
                };

                LibVLC? testLibVLC = null;
                string? successfulConfig = null;

                // Try each config until one initializes successfully
                foreach (var config in hwDecoderConfigs)
                {
                    var args = new List<string>(config);

                    // Note: VLC 3.0.x doesn't support --smb-* options via command line
                    // SMB credentials must be embedded in the URL (smb://user:pass@server/share)
                    // Don't add SMB options here as they cause initialization to fail

                    // Audio configuration
                    if (!EnableAudio)
                    {
                        args.Add("--no-audio");
                    }
                    else
                    {
                        // Configure audio for Raspberry Pi (avoid PulseAudio digital pass-through issues)
                        args.Add("--audio-desync=0");
                        args.Add("--alsa-audio-device=default");
                    }

                    args.Add("--live-caching=0");
                    args.Add("--clock-jitter=0");
                    args.Add("--clock-synchro=0");
                    args.Add("--avcodec-hw=none");

                    try
                    {
                        Console.WriteLine($"[VLC] Trying decoder: {string.Join(" ", config)}");
                        testLibVLC = new LibVLC(args.ToArray());
                        successfulConfig = string.Join(" ", config);
                        Console.WriteLine($"[VLC] ? Decoder initialized: {successfulConfig}");
                        break; // Success!
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VLC] ? Decoder failed: {ex.Message}");
                        testLibVLC?.Dispose();
                        testLibVLC = null;
                    }
                }

                if (testLibVLC == null)
                    throw new InvalidOperationException("Failed to initialize LibVLC with any decoder configuration");

                _libVLC = testLibVLC;
                Console.WriteLine($"[VLC] Using configuration: {successfulConfig}");

                // DO NOT attach Log event handler on ARM64 - causes segfaults!

                Console.WriteLine("[VLC] Creating MediaPlayer...");
                _mediaPlayer = new MediaPlayer(_libVLC);

                // Setup video format and callbacks - must be done BEFORE loading media
                _pitch = _canvas.Width * 4; // RGBA8888 = 4 bytes per pixel

                Console.WriteLine($"[VLC] Creating bitmap {_canvas.Width}x{_canvas.Height}...");
                _currentBitmap = new SKBitmap(
                    new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888));

                // Create back buffer for atomic frame submission
                _backBuffer = new SKBitmap(
                    new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888));

                Console.WriteLine($"[VLC] Setting video format: RV32 {_canvas.Width}x{_canvas.Height} pitch={_pitch}");
                _mediaPlayer.SetVideoFormat("RV32",
                    (uint)_canvas.Width,
                    (uint)_canvas.Height,
                    (uint)_pitch);

                Console.WriteLine("[VLC] Setting video callbacks...");
                _mediaPlayer.SetVideoCallbacks(LockVideo, null, DisplayVideo);

                // Set volume
                _mediaPlayer.Volume = _volume;
                
                // Handle end reached for loop functionality
                _mediaPlayer.EndReached += OnEndReached;

                // Add state change event for debugging
                _mediaPlayer.Playing += (s, e) => Console.WriteLine("[VLC] Event: Playing");
                _mediaPlayer.Paused += (s, e) => Console.WriteLine("[VLC] Event: Paused");
                _mediaPlayer.Stopped += (s, e) => Console.WriteLine("[VLC] Event: Stopped");
                _mediaPlayer.EncounteredError += (s, e) => Console.WriteLine("[VLC] Event: Error encountered!");

                IsRunning = true;
                Console.WriteLine("[VLC] Extension started successfully");
                Console.WriteLine($"[VLC] Current MediaUrl: '{_mediaUrl}'");
                Console.WriteLine($"[VLC] AutoPlay: {_autoPlay}");

                // Auto-play if configured
                if (_autoPlay && !string.IsNullOrWhiteSpace(_mediaUrl))
                {
                    Console.WriteLine($"[VLC] Auto-playing media: {_mediaUrl}");
                    Task.Run(() => LoadAndPlayMedia(_mediaUrl));
                }
                else if (string.IsNullOrWhiteSpace(_mediaUrl))
                {
                    Console.WriteLine("[VLC] ?? No MediaUrl provided - extension is ready but has nothing to play");
                    Console.WriteLine("[VLC] Set the 'Media URL' parameter to start playback");
                }
                else if (!_autoPlay)
                {
                    Console.WriteLine("[VLC] AutoPlay is disabled - set MediaUrl to trigger playback");
                }

                // Note: If MediaUrl is set during initial configuration (via API/HTML remote),
                // playback will be triggered by the SmbDomain setter (last parameter typically set)
                // or by the SmbPassword setter (if SMB credentials are provided)
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VLC] CRITICAL ERROR during Start(): {ex.Message}");
                Console.WriteLine($"[VLC] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[VLC] Stack trace: {ex.StackTrace}");

                if (ex.InnerException != null) Console.WriteLine($"[VLC] Inner exception: {ex.InnerException.Message}");

                Cleanup();
                throw new InvalidOperationException($"Failed to start VLC Media Player: {ex.Message}", ex);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning)
                return;

            IsRunning = false;

            try
            {
                _mediaPlayer?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VLC] Error stopping playback: {ex.Message}");
            }

            Cleanup();
        }
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
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.EndReached -= OnEndReached;
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }

            _libVLC?.Dispose();
            _libVLC = null;

            _currentBitmap?.Dispose();
            _currentBitmap = null;

            _backBuffer?.Dispose();
            _backBuffer = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VLC] Error during cleanup: {ex.Message}");
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (_loop && IsRunning && !string.IsNullOrWhiteSpace(_mediaUrl))
            // Restart playback
            Task.Run(() =>
            {
                try
                {
                    _mediaPlayer?.Stop();
                    Thread.Sleep(100); // Small delay before restart
                    LoadAndPlayMedia(_mediaUrl);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VLC] Error restarting playback: {ex.Message}");
                }
            });
    }

    private async void LoadAndPlayMedia(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Guard against concurrent calls - critical for preventing race conditions
        if (_isLoadingMedia)
        {
            Console.WriteLine("[VLC] Already loading media, skipping duplicate call");
            return;
        }

        // Set the guard and track URL immediately BEFORE any slow operations
        _isLoadingMedia = true;
        _lastPlayedUrl = url;

        lock (_lock)
        {
            if (!IsRunning || _mediaPlayer == null || _libVLC == null)
            {
                _isLoadingMedia = false;
                return;
            }

            try
            {
                Console.WriteLine($"[VLC] LoadAndPlayMedia called with: {url}");

                // Check if this is a YouTube URL and resolve it
                var mediaUrl = url;
                string? audioSlaveUrl = null;
                
                if (IsYouTubeUrl(url))
                {
                    Console.WriteLine("[VLC] YouTube URL detected, resolving to direct stream...");
                    var (videoUrl, audioUrl) = ResolveYouTubeUrl(url);

                    if (string.IsNullOrWhiteSpace(videoUrl))
                    {
                        Console.WriteLine("[VLC] Failed to resolve YouTube URL");
                        _isLoadingMedia = false;
                        return;
                    }

                    mediaUrl = videoUrl;
                    audioSlaveUrl = audioUrl;
                    Console.WriteLine("[VLC] YouTube URL resolved successfully");
                }
                else
                {
                    // Convert Windows UNC paths to SMB URLs
                    mediaUrl = ConvertToSmbUrl(url);
                }

                // Determine media type
                FromType fromType;
                if (mediaUrl.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
                    mediaUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    mediaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    mediaUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                    mediaUrl.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                {
                    fromType = FromType.FromLocation;
                    Console.WriteLine($"[VLC] Detected network location: {fromType}");
                }
                else
                {
                    fromType = FromType.FromPath;
                    Console.WriteLine($"[VLC] Detected local path: {fromType}");
                }

                Console.WriteLine($"[VLC] Creating Media object (type: {fromType})...");
                Console.WriteLine($"[VLC] Final URL: {HidePassword(mediaUrl)}");

                var media = new Media(_libVLC, mediaUrl, fromType);

                // For YouTube URLs, add required HTTP headers to avoid 403 Forbidden
                // YouTube validates that the request comes from the same client type that fetched the URL
                var isYouTubeStream = mediaUrl.Contains("googlevideo.com") || 
                                      mediaUrl.Contains("youtube.com");
                if (isYouTubeStream)
                {
                    Console.WriteLine("[VLC] Adding YouTube-compatible HTTP headers...");
                    // Match the Android client that VideoLibrary uses to fetch URLs
                    media.AddOption(":http-user-agent=com.google.android.youtube/20.10.38 (Linux; U; Android 11)");
                    media.AddOption(":http-referrer=https://www.youtube.com/");
                    // Additional headers that may help
                    media.AddOption(":no-video-title-show");
                }

                // If we have a separate audio stream (YouTube adaptive), use --input-slave
                if (!string.IsNullOrWhiteSpace(audioSlaveUrl))
                {
                    Console.WriteLine("[VLC] Adding audio slave for adaptive stream...");
                    media.AddOption($":input-slave={audioSlaveUrl}");
                }

                // Don't wait for parse - it can hang for streams
                // Just set and play immediately
                Console.WriteLine("[VLC] Setting media on player...");
                _mediaPlayer.Media = media;

                Console.WriteLine("[VLC] Calling Play()...");
                var result = _mediaPlayer.Play();
                Console.WriteLine($"[VLC] Play() returned: {result}");

                // Give VLC a moment to start
                Thread.Sleep(100);
                Console.WriteLine($"[VLC] Current state: {_mediaPlayer.State}");
                Console.WriteLine($"[VLC] Is playing: {_mediaPlayer.IsPlaying}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VLC] Error loading media: {ex.Message}");
                Console.WriteLine($"[VLC] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _isLoadingMedia = false;
            }
        }
    }

    /// <summary>
    ///     Checks if the URL is a YouTube URL (including YouTube Music)
    /// </summary>
    private bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("youtube.com/v/", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("youtube.com/embed/", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("music.youtube.com/watch", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Resolves a YouTube URL to direct stream URL(s).
    ///     Tries yt-dlp first (returns HLS URLs that work reliably), 
    ///     falls back to VideoLibrary if yt-dlp is not available.
    /// </summary>
    private (string videoUrl, string? audioUrl) ResolveYouTubeUrl(string youtubeUrl)
    {
        Console.WriteLine($"[VLC] Resolving YouTube URL: {youtubeUrl}");
        Console.WriteLine($"[VLC] Canvas size: {_canvas.Width}x{_canvas.Height}");

        // Convert YouTube Music URLs to regular YouTube URLs
        if (youtubeUrl.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            youtubeUrl = youtubeUrl.Replace("music.youtube.com", "www.youtube.com", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"[VLC] Converted YouTube Music URL to: {youtubeUrl}");
        }

        // Try yt-dlp first - it returns HLS manifest URLs that work more reliably
        var ytdlpResult = ResolveYouTubeUrlWithYtDlp(youtubeUrl);
        if (!string.IsNullOrEmpty(ytdlpResult.videoUrl))
        {
            Console.WriteLine("[VLC] ✓ Resolved via yt-dlp");
            return ytdlpResult;
        }

        // Fall back to VideoLibrary
        Console.WriteLine("[VLC] yt-dlp not available, trying VideoLibrary...");
        return ResolveYouTubeUrlWithVideoLibrary(youtubeUrl);
    }

    /// <summary>
    ///     Resolves YouTube URL using yt-dlp command-line tool.
    ///     Returns HLS manifest URLs which are more reliable than direct videoplayback URLs.
    /// </summary>
    private (string videoUrl, string? audioUrl) ResolveYouTubeUrlWithYtDlp(string youtubeUrl)
    {
        try
        {
            Console.WriteLine("[VLC] Trying yt-dlp...");
            
            // Try to get a single muxed stream first (better for VLC callbacks on Pi)
            // "b" = best single stream (muxed) without the warning that "best" gives
            var result = TryYtDlpWithFormat(youtubeUrl, "b");
            if (!string.IsNullOrEmpty(result.videoUrl) && result.audioUrl == null)
            {
                Console.WriteLine("[VLC] ✓ Got single muxed stream from yt-dlp");
                return result;
            }

            // For Pi: Get H.264 video at a low resolution
            // VP9 is too CPU-intensive, and high-res is overkill for a small LED display
            // Available H.264: 144p(269), 240p(229), 360p(230), 480p(231), 720p(232), 1080p(270)
            // Try 360p first (good balance of quality and performance for 192px canvas)
            Console.WriteLine("[VLC] Trying 360p H.264 video stream...");
            result = TryYtDlpWithFormat(youtubeUrl, "230+234");  // 360p H.264 + high quality audio
            if (!string.IsNullOrEmpty(result.videoUrl))
            {
                Console.WriteLine("[VLC] ✓ Got 360p H.264 video stream");
                return result;
            }

            // Try 240p H.264 if 360p not available
            Console.WriteLine("[VLC] Trying 240p H.264...");
            result = TryYtDlpWithFormat(youtubeUrl, "229+234");  // 240p H.264
            if (!string.IsNullOrEmpty(result.videoUrl))
            {
                Console.WriteLine("[VLC] ✓ Got 240p H.264 video stream");
                return result;
            }

            // Fall back to any H.264 with height filter
            Console.WriteLine("[VLC] Trying any H.264 <= 480p...");
            result = TryYtDlpWithFormat(youtubeUrl, "bv[vcodec^=avc][height<=480]+ba");
            if (!string.IsNullOrEmpty(result.videoUrl))
            {
                Console.WriteLine("[VLC] ✓ Got H.264 video stream");
                return result;
            }

            // Fall back to default (may get VP9 which is slow on Pi)
            Console.WriteLine("[VLC] No suitable H.264 available, using default...");
            return TryYtDlpWithFormat(youtubeUrl, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VLC] yt-dlp not available: {ex.Message}");
            return (string.Empty, null);
        }
    }

    private (string videoUrl, string? audioUrl) TryYtDlpWithFormat(string youtubeUrl, string? format)
    {
        try
        {
            var formatArg = string.IsNullOrEmpty(format) ? "" : $"-f \"{format}\" ";
            // Add --no-cache-dir to avoid permission errors when running as root
            var arguments = $"--no-cache-dir {formatArg}-g \"{youtubeUrl}\"";
            
            Console.WriteLine($"[VLC] Running: yt-dlp {formatArg}-g ...");
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            
            if (process.ExitCode != 0)
            {
                // Only log error if it's not just a "format not available" warning
                if (!error.Contains("Requested format is not available"))
                    Console.WriteLine($"[VLC] yt-dlp format '{format}' failed");
                return (string.Empty, null);
            }

            var urls = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            if (urls.Length >= 2)
            {
                Console.WriteLine($"[VLC] yt-dlp returned {urls.Length} URLs (video + audio)");
                return (urls[0].Trim(), urls[1].Trim());
            }
            if (urls.Length == 1)
            {
                Console.WriteLine("[VLC] yt-dlp returned 1 URL (single stream)");
                return (urls[0].Trim(), null);
            }

            Console.WriteLine("[VLC] yt-dlp returned no URLs");
            return (string.Empty, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VLC] yt-dlp format '{format}' failed: {ex.Message}");
            return (string.Empty, null);
        }
    }

    /// <summary>
    ///     Resolves YouTube URL using VideoLibrary.
    ///     Note: Returns direct videoplayback URLs which may require specific headers.
    /// </summary>
    private (string videoUrl, string? audioUrl) ResolveYouTubeUrlWithVideoLibrary(string youtubeUrl)
    {
        try
        {
            var youtube = YouTube.Default;
            var allVideos = youtube.GetAllVideos(youtubeUrl).ToList();

            if (allVideos == null || !allVideos.Any())
            {
                Console.WriteLine("[VLC] No videos found for URL");
                return (string.Empty, null);
            }

            Console.WriteLine($"[VLC] Found {allVideos.Count} streams total");

            // STEP 1: Try muxed streams first (video + audio combined)
            var muxedStreams = allVideos
                .Where(v => v.AdaptiveKind == AdaptiveKind.None)
                .ToList();

            if (muxedStreams.Any())
            {
                Console.WriteLine($"[VLC] Found {muxedStreams.Count} muxed streams");
                
                // Select resolution appropriate for canvas size
                var bestMuxed = SelectBestResolutionForCanvas(muxedStreams, _canvas.Height);
                
                if (bestMuxed != null)
                {
                    Console.WriteLine($"[VLC] ✓ Selected muxed stream: {bestMuxed.Resolution}p, {bestMuxed.Format}");
                    Console.WriteLine($"[VLC] Title: {bestMuxed.Title}");
                    return (bestMuxed.Uri, null);
                }
            }

            Console.WriteLine("[VLC] No muxed streams available, trying adaptive streams...");

            // STEP 2: Use adaptive streams (separate video + audio)
            var videoStreams = allVideos
                .Where(v => v.AdaptiveKind == AdaptiveKind.Video)
                .ToList();
            
            var audioStreams = allVideos
                .Where(v => v.AdaptiveKind == AdaptiveKind.Audio)
                .ToList();

            Console.WriteLine($"[VLC] Found {videoStreams.Count} video-only streams, {audioStreams.Count} audio-only streams");

            if (!videoStreams.Any())
            {
                Console.WriteLine("[VLC] No video streams available");
                return (string.Empty, null);
            }

            // Select best video for canvas size
            var bestVideo = SelectBestResolutionForCanvas(videoStreams, _canvas.Height);
            
            if (bestVideo == null)
            {
                Console.WriteLine("[VLC] Could not select appropriate video stream");
                return (string.Empty, null);
            }

            Console.WriteLine($"[VLC] ✓ Selected video stream: {bestVideo.Resolution}p, {bestVideo.Format}");

            // Select best audio (highest bitrate)
            string? audioUrl = null;
            if (audioStreams.Any())
            {
                var bestAudio = audioStreams
                    .OrderByDescending(a => a.AudioBitrate)
                    .FirstOrDefault();
                
                if (bestAudio != null)
                {
                    audioUrl = bestAudio.Uri;
                    Console.WriteLine($"[VLC] ✓ Selected audio stream: {bestAudio.AudioBitrate}kbps, {bestAudio.AudioFormat}");
                }
            }

            if (audioUrl == null)
            {
                Console.WriteLine("[VLC] ⚠ No audio stream available - video will play without sound");
            }
            else
            {
                Console.WriteLine("[VLC] Will use --input-slave to combine video and audio");
            }

            return (bestVideo.Uri, audioUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VLC] Error resolving YouTube URL: {ex.Message}");
            Console.WriteLine("[VLC] ⚠ YouTube has likely updated their page structure.");
            Console.WriteLine($"[VLC] Stack trace: {ex.StackTrace}");
            return (string.Empty, null);
        }
    }

    /// <summary>
    ///     Selects the best video resolution for the canvas size.
    ///     Picks the smallest resolution that is >= canvas height, or the highest available if all are smaller.
    /// </summary>
    private YouTubeVideo? SelectBestResolutionForCanvas(List<YouTubeVideo> videos, int canvasHeight)
    {
        if (!videos.Any()) return null;

        // Sort by resolution ascending
        var sorted = videos
            .Where(v => v.Resolution > 0)
            .OrderBy(v => v.Resolution)
            .ToList();

        if (!sorted.Any())
        {
            // No resolution info, just take first available
            return videos.FirstOrDefault();
        }

        // Find smallest resolution >= canvas height (no need for 4K on 64px display)
        var appropriate = sorted.FirstOrDefault(v => v.Resolution >= canvasHeight);
        
        if (appropriate != null)
        {
            Console.WriteLine($"[VLC] Selected {appropriate.Resolution}p (>= canvas height {canvasHeight}px)");
            return appropriate;
        }

        // All resolutions are smaller than canvas, use highest available
        var highest = sorted.LastOrDefault();
        Console.WriteLine($"[VLC] All resolutions < canvas, using highest: {highest?.Resolution}p");
        return highest;
    }

    /// <summary>
    ///     Converts Windows UNC path to SMB URL for VLC
    ///     Example: \\server\share\path -> smb://server/share/path
    /// </summary>
    private string ConvertToSmbUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        Console.WriteLine($"[VLC] ConvertToSmbUrl input: '{path}'");

        // Remove surrounding quotes if present (from JSON/API input)
        path = path.Trim('"', '\'');
        Console.WriteLine($"[VLC] After quote trimming: '{path}'");

        // If already a proper URL scheme, return as-is
        if (path.Contains("://"))
        {
            Console.WriteLine("[VLC] Already contains '://' - returning as-is");
            return path;
        }

        // Convert Windows UNC path to SMB URL
        if (path.StartsWith(@"\\") || path.StartsWith("//"))
        {
            var cleanPath = path.TrimStart('\\', '/');
            Console.WriteLine($"[VLC] Cleaned path: '{cleanPath}'");

            var smbUrl = $"smb://{cleanPath.Replace('\\', '/')}";
            Console.WriteLine($"[VLC] Base SMB URL: '{smbUrl}'");

            // Add credentials if configured
            if (!string.IsNullOrWhiteSpace(SmbUsername) && !string.IsNullOrWhiteSpace(_smbPassword))
            {
                Console.WriteLine(
                    $"[VLC] Adding credentials - Username: '{SmbUsername}', Has Password: {!string.IsNullOrWhiteSpace(_smbPassword)}");

                var credentials = string.IsNullOrWhiteSpace(_smbDomain)
                    ? $"{Uri.EscapeDataString(SmbUsername)}:{Uri.EscapeDataString(_smbPassword)}"
                    : $"{Uri.EscapeDataString(_smbDomain)};{Uri.EscapeDataString(SmbUsername)}:{Uri.EscapeDataString(_smbPassword)}";

                // Insert credentials: smb://user:pass@server/share/path
                smbUrl = smbUrl.Replace("smb://", $"smb://{credentials}@");
                Console.WriteLine($"[VLC] SMB URL with credentials: '{smbUrl.Replace(_smbPassword, "***")}'");
            }
            else
            {
                Console.WriteLine("[VLC] No credentials configured - using guest access");
            }

            Console.WriteLine("[VLC] Converted UNC path to SMB URL");
            return smbUrl;
        }

        Console.WriteLine("[VLC] Not a UNC path - returning as-is");
        // Return as-is if not a UNC path
        return path;
    }

    /// <summary>
    ///     Hides password in URL for logging purposes
    /// </summary>
    private string HidePassword(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("@"))
            return url;

        try
        {
            var uri = new Uri(url);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var userInfo = uri.UserInfo;
                var parts = userInfo.Split(':');
                if (parts.Length >= 2)
                {
                    var maskedUserInfo = $"{parts[0]}:***";
                    return url.Replace(userInfo, maskedUserInfo);
                }
            }
        }
        catch
        {
            // If URL parsing fails, just mask everything after :// and before @
            var atIndex = url.IndexOf('@');
            var schemeIndex = url.IndexOf("://");
            if (atIndex > schemeIndex && schemeIndex >= 0)
                return url.Substring(0, schemeIndex + 3) + "***@" + url.Substring(atIndex + 1);
        }

        return url;
    }

    private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
    {
        // VLC writes to _currentBitmap
        if (_currentBitmap != null) Marshal.WriteIntPtr(planes, _currentBitmap.GetPixels());
        return IntPtr.Zero;
    }

    private void DisplayVideo(IntPtr opaque, IntPtr picture)
    {
        if (_currentBitmap == null || _backBuffer == null || !IsRunning)
            return;

        try
        {
            // Copy the current frame to back buffer
            lock (_frameLock)
            {
                var srcPixels = _currentBitmap.GetPixels();
                var dstPixels = _backBuffer.GetPixels();
                var totalBytes = _canvas.Width * _canvas.Height * 4;

                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)srcPixels,
                        (void*)dstPixels,
                        totalBytes,
                        totalBytes);
                }
            }// ? ATOMIC SUBMIT: Send complete frame to canvas
            _canvas.SubmitCompletedFrame(_backBuffer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VLC] Error in DisplayVideo: {ex.Message}");
        }
    }
}