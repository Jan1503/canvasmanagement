using System.Diagnostics;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.YouTubePlayer;

/// <summary>
///     YouTube Player Extension using FFmpeg + yt-dlp pipeline.
///     Designed for reliable YouTube playback on Raspberry Pi.
///     
///     Pipeline: yt-dlp → FFmpeg → raw frames → Canvas
/// </summary>
[ExtensionInfo(
    "YouTube Player",
    "Plays YouTube videos using FFmpeg + yt-dlp. Optimized for Raspberry Pi.",
    "Media Players",
    IconResourceName = "youtube.svg")]
public sealed class YouTubePlayerExtension : ICanvasExtension, IDisposable
{
    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    
    // FFmpeg process
    private Process? _ffmpegProcess;
    private Thread? _frameReaderThread;
    private CancellationTokenSource? _cts;
    
    // Frame buffer
    private SKBitmap? _frameBitmap;
    private byte[]? _frameBuffer;
    private readonly int _frameSize;
    
    // State
    private bool _disposed;
    private string _youtubeUrl = "";
    private string _lastPlayedUrl = "";
    private volatile bool _isPlaying;
    private volatile bool _isLoading;
    
    // Settings
    private bool _autoPlay = true;
    private bool _loop;
    private int _volume = 100;

    public YouTubePlayerExtension(ICanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _frameSize = canvas.Width * canvas.Height * 4; // BGRA
        Console.WriteLine($"[YT] YouTubePlayerExtension created for {canvas.Width}x{canvas.Height} canvas");
    }

    #region Extension Parameters

    [ExtensionParameter("YouTube URL",
        "YouTube or YouTube Music URL (e.g., https://youtube.com/watch?v=...)",
        DefaultValue = "")]
    public string YouTubeUrl
    {
        get => _youtubeUrl;
        set
        {
            if (_youtubeUrl != value)
            {
                var oldValue = _youtubeUrl;
                _youtubeUrl = value;
                Console.WriteLine($"[YT] YouTubeUrl set to: {value}");

                if (IsRunning && !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(oldValue))
                {
                    Console.WriteLine("[YT] URL changed while running, starting playback...");
                    Task.Run(() => StartPlayback());
                }
            }
        }
    }

    [ExtensionParameter("Auto Play", "Automatically play when URL is set",
        DefaultValue = true)]
    public bool AutoPlay
    {
        get => _autoPlay;
        set
        {
            _autoPlay = value;
            Console.WriteLine($"[YT] AutoPlay set to: {value}");
            
            // Trigger playback if this is the last parameter being set
            if (IsRunning && _autoPlay && !string.IsNullOrWhiteSpace(_youtubeUrl) && !_isPlaying && !_isLoading)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(100); // Wait for other parameters
                    if (!_isPlaying && !_isLoading && _lastPlayedUrl != _youtubeUrl)
                    {
                        Console.WriteLine("[YT] Delayed playback trigger after AutoPlay set");
                        StartPlayback();
                    }
                });
            }
        }
    }

    [ExtensionParameter("Loop", "Restart playback when video ends",
        DefaultValue = false)]
    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    [ExtensionParameter("Volume", "Playback volume (0-100)",
        MinValue = 0, MaxValue = 100, DefaultValue = 100)]
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            // Note: Volume is applied when starting FFmpeg
        }
    }

    [ExtensionParameter("Playback State", "Current playback state (read-only)",
        ReadOnly = true)]
    public string PlaybackState
    {
        get
        {
            if (_isLoading) return "Loading";
            if (_isPlaying) return "Playing";
            return "Stopped";
        }
    }

    public string Name => "YouTube Player";
    public bool IsRunning { get; private set; }

    #endregion

    #region Extension Methods

    [ExtensionMethod("Play", "Start or resume playback",
        Category = "Playback", IconName = "play", Order = 10)]
    public void Play()
    {
        if (!IsRunning) return;
        
        if (!_isPlaying && !string.IsNullOrWhiteSpace(_youtubeUrl))
        {
            Task.Run(() => StartPlayback());
        }
    }

    [ExtensionMethod("Stop", "Stop playback",
        Category = "Playback", IconName = "stop", Order = 20)]
    public void StopPlayback()
    {
        StopFFmpeg();
        Console.WriteLine("[YT] Playback stopped");
    }

    [ExtensionMethod("Play URL", "Load and play a new YouTube URL",
        Category = "Media", IconName = "link", Order = 30)]
    public void PlayUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Console.WriteLine("[YT] PlayUrl: URL is empty");
            return;
        }

        _youtubeUrl = url;
        
        if (IsRunning)
        {
            Console.WriteLine($"[YT] Playing new URL: {url}");
            Task.Run(() => StartPlayback());
        }
    }

    [ExtensionMethod("Is Playing", "Returns true if video is currently playing",
        Category = "Info", IconName = "info", Order = 40, ReturnsValue = true)]
    public bool GetIsPlaying() => _isPlaying;

    #endregion

    #region ICanvasExtension Implementation

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            Console.WriteLine("[YT] Starting YouTube Player extension...");

            // Initialize frame buffer
            _frameBitmap = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888));
            _frameBuffer = new byte[_frameSize];
            _cts = new CancellationTokenSource();

            IsRunning = true;
            Console.WriteLine("[YT] Extension started successfully");

            // Auto-play if configured
            if (_autoPlay && !string.IsNullOrWhiteSpace(_youtubeUrl))
            {
                Console.WriteLine($"[YT] Auto-playing: {_youtubeUrl}");
                Task.Run(() => StartPlayback());
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;

            Console.WriteLine("[YT] Stopping YouTube Player extension...");
            IsRunning = false;

            StopFFmpeg();

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _frameBitmap?.Dispose();
            _frameBitmap = null;
            _frameBuffer = null;

            Console.WriteLine("[YT] Extension stopped");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Playback Implementation

    private void StartPlayback()
    {
        if (_isLoading || !IsRunning) return;

        lock (_lock)
        {
            if (_isLoading) return;
            _isLoading = true;
        }

        try
        {
            // Stop any existing playback
            StopFFmpeg();

            Console.WriteLine($"[YT] Starting playback for: {_youtubeUrl}");
            _lastPlayedUrl = _youtubeUrl;

            // Resolve YouTube URL to stream URLs
            var (videoUrl, audioUrl) = ResolveYouTubeUrl(_youtubeUrl);
            
            if (string.IsNullOrEmpty(videoUrl))
            {
                Console.WriteLine("[YT] Failed to resolve YouTube URL");
                _isLoading = false;
                return;
            }

            // Start FFmpeg
            StartFFmpegProcess(videoUrl, audioUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[YT] Error starting playback: {ex.Message}");
            _isLoading = false;
        }
    }

    private (string videoUrl, string? audioUrl) ResolveYouTubeUrl(string url)
    {
        Console.WriteLine($"[YT] Resolving YouTube URL: {url}");

        // Convert YouTube Music URLs
        if (url.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            url = url.Replace("music.youtube.com", "www.youtube.com", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"[YT] Converted to: {url}");
        }

        // Use yt-dlp to get stream URLs
        // Prefer 360p H.264 for Pi compatibility
        var formats = new[] { "230+234", "229+234", "bv[vcodec^=avc][height<=480]+ba", null };
        
        foreach (var format in formats)
        {
            var result = TryYtDlp(url, format);
            if (!string.IsNullOrEmpty(result.videoUrl))
            {
                Console.WriteLine($"[YT] ✓ Resolved with format: {format ?? "default"}");
                return result;
            }
        }

        Console.WriteLine("[YT] ✗ Failed to resolve URL");
        return (string.Empty, null);
    }

    private (string videoUrl, string? audioUrl) TryYtDlp(string url, string? format)
    {
        try
        {
            var formatArg = string.IsNullOrEmpty(format) ? "" : $"-f \"{format}\" ";
            var arguments = $"--no-cache-dir {formatArg}-g \"{url}\"";

            Console.WriteLine($"[YT] Running yt-dlp {formatArg}-g ...");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
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
            process.WaitForExit(30000);

            if (process.ExitCode != 0)
                return (string.Empty, null);

            var urls = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            if (urls.Length >= 2)
                return (urls[0].Trim(), urls[1].Trim());
            if (urls.Length == 1)
                return (urls[0].Trim(), null);

            return (string.Empty, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[YT] yt-dlp error: {ex.Message}");
            return (string.Empty, null);
        }
    }

    private void StartFFmpegProcess(string videoUrl, string? audioUrl)
    {
        Console.WriteLine("[YT] Starting FFmpeg process...");

        var width = _canvas.Width;
        var height = _canvas.Height;

        // Build FFmpeg arguments optimized for Raspberry Pi
        // Key optimizations:
        // 1. Large input buffer for HLS streams
        // 2. Hardware decoding if available
        // 3. Efficient scaling
        // 4. Proper output buffering
        
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            
            // === INPUT BUFFERING (critical for smooth playback) ===
            "-fflags", "+genpts+discardcorrupt",
            "-analyzeduration", "2000000",   // 2 seconds analysis
            "-probesize", "2000000",         // 2MB probe
            
            // Reconnect options for HLS streams
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
            
            // First input: video
            "-i", $"\"{videoUrl}\""
        };

        // Add audio input if separate
        if (!string.IsNullOrEmpty(audioUrl))
        {
            args.AddRange(new[]
            {
                "-reconnect", "1",
                "-reconnect_streamed", "1",
                "-reconnect_delay_max", "5",
                "-i", $"\"{audioUrl}\""
            });
        }

        // === OUTPUT 1: Video to pipe (raw BGRA frames) ===
        args.AddRange(new[]
        {
            "-map", "0:v:0",
            
            // Use fast scaling with point sampling for speed
            "-vf", $"scale={width}:{height}:flags=fast_bilinear:sws_dither=none",
            
            // Output format
            "-pix_fmt", "bgra",
            "-f", "rawvideo",
            
            // Lower frame rate for Pi (25fps is smoother than struggling at 30)
            "-r", "25",
            
            // Disable frame dropping - we want consistent output
            "-vsync", "cfr",
            
            "pipe:1"
        });

        // === OUTPUT 2: Audio to ALSA device ===
        if (!string.IsNullOrEmpty(audioUrl))
        {
            args.AddRange(new[] { "-map", "1:a:0" });
        }
        else
        {
            args.AddRange(new[] { "-map", "0:a:0?" });
        }

        // Volume filter
        if (_volume < 100)
        {
            args.AddRange(new[] { "-af", $"volume={_volume / 100.0:F2}" });
        }

        // Audio output - add buffer for smooth playback
        args.AddRange(new[]
        {
            "-f", "alsa",
            "-ac", "2",
            "-ar", "44100",
            "-thread_queue_size", "512",
            "default"
        });

        var arguments = string.Join(" ", args);
        Console.WriteLine($"[YT] FFmpeg command: ffmpeg {arguments}");

        try
        {
            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _ffmpegProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[YT/FFmpeg] {e.Data}");
            };

            _ffmpegProcess.Exited += (s, e) =>
            {
                Console.WriteLine("[YT] FFmpeg process exited");
                _isPlaying = false;
                
                // Loop if enabled
                if (_loop && IsRunning && !_cts!.IsCancellationRequested)
                {
                    Console.WriteLine("[YT] Looping...");
                    Task.Run(() =>
                    {
                        Thread.Sleep(500);
                        StartPlayback();
                    });
                }
            };

            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();

            // Start frame reader thread with higher priority
            _frameReaderThread = new Thread(ReadFramesLoop)
            {
                Name = "YT-FrameReader",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _frameReaderThread.Start();

            _isPlaying = true;
            _isLoading = false;
            Console.WriteLine("[YT] ✓ FFmpeg started successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[YT] Failed to start FFmpeg: {ex.Message}");
            _isLoading = false;
            _isPlaying = false;
        }
    }

    private void ReadFramesLoop()
    {
        Console.WriteLine("[YT] Frame reader thread started");

        try
        {
            var stream = _ffmpegProcess?.StandardOutput.BaseStream;
            if (stream == null) return;

            var buffer = _frameBuffer;
            if (buffer == null) return;

            // Wrap in buffered stream for better performance
            using var bufferedStream = new BufferedStream(stream, _frameSize * 2);

            // Track frame timing for smooth playback
            var frameInterval = TimeSpan.FromMilliseconds(40); // 25 FPS = 40ms per frame
            var lastFrameTime = DateTime.UtcNow;

            while (IsRunning && _isPlaying && !_cts!.IsCancellationRequested)
            {
                // Read exactly one frame
                var bytesRead = 0;
                while (bytesRead < _frameSize)
                {
                    var read = bufferedStream.Read(buffer, bytesRead, _frameSize - bytesRead);
                    if (read == 0)
                    {
                        // End of stream
                        Console.WriteLine("[YT] End of stream");
                        return;
                    }
                    bytesRead += read;
                }

                // Copy to bitmap and submit
                if (_frameBitmap != null)
                {
                    var pixels = _frameBitmap.GetPixels();
                    System.Runtime.InteropServices.Marshal.Copy(buffer, 0, pixels, _frameSize);
                    _canvas.SubmitCompletedFrame(_frameBitmap);
                }

                // Frame pacing - ensure consistent frame rate
                var now = DateTime.UtcNow;
                var elapsed = now - lastFrameTime;
                if (elapsed < frameInterval)
                {
                    var sleepTime = frameInterval - elapsed;
                    if (sleepTime.TotalMilliseconds > 1)
                    {
                        Thread.Sleep((int)sleepTime.TotalMilliseconds);
                    }
                }
                lastFrameTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            if (!_cts!.IsCancellationRequested)
                Console.WriteLine($"[YT] Frame reader error: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("[YT] Frame reader thread ended");
            _isPlaying = false;
        }
    }

    private void StopFFmpeg()
    {
        _isPlaying = false;

        if (_ffmpegProcess != null)
        {
            try
            {
                if (!_ffmpegProcess.HasExited)
                {
                    _ffmpegProcess.Kill();
                    _ffmpegProcess.WaitForExit(1000);
                }
            }
            catch { }

            _ffmpegProcess.Dispose();
            _ffmpegProcess = null;
        }

        _frameReaderThread?.Join(1000);
        _frameReaderThread = null;
    }

    #endregion
}
