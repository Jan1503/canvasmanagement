using System.Diagnostics;
using System.Text;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.Trailer;

/// <summary>
///     Plays video files from a folder through FFmpeg (raw BGRA + optional ALSA), looping / shuffling
///     like the GIF player. YouTube is not used — drop files you already have onto the Pi.
/// </summary>
[ExtensionInfo("Trailers",
    "Play video files from a folder (loop / shuffle). Drop trailers you downloaded onto the Pi.",
    "Media Players",
    IconResourceName = "trailer.svg")]
public sealed class TrailerExtension : ICanvasExtension, IDisposable
{
    private static readonly string[] Extensions =
        [".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".mpg", ".mpeg", ".wmv"];

    private readonly ICanvas _canvas;
    private readonly object _lock = new();
    private readonly int _frameSize;

    private Process? _ffmpegProcess;
    private Thread? _frameReaderThread;
    private CancellationTokenSource? _cts;
    private SKBitmap? _frameBitmap;
    private byte[]? _frameBuffer;

    private bool _disposed;
    private volatile bool _isPlaying;
    private volatile bool _isLoading;
    private volatile bool _advanceOnExit;
    private volatile int _frames;
    private int _index;
    private List<string> _queue = [];
    private string _directory = "Videos/Trailers";

    internal TrailerExtension(ICanvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _frameSize = canvas.Width * canvas.Height * 4;
        var nextToApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Videos", "Trailers");
        if (Directory.Exists(nextToApp)) _directory = nextToApp;
    }

    [ExtensionParameter("Directory", "Folder of video files on this machine (mp4, mkv, webm, …)",
        DefaultValue = "Videos/Trailers", Order = 1)]
    public string DirectoryPath
    {
        get => _directory;
        set
        {
            var v = (value ?? "").Trim();
            if (_directory == v) return;
            _directory = v;
            if (IsRunning) RestartFromFolder();
        }
    }

    [ExtensionParameter("Include Subfolders", "Also pick up videos in subdirectories", DefaultValue = true, Order = 2)]
    public bool IncludeSubfolders { get; set; } = true;

    [ExtensionParameter("Shuffle", "Randomize order each time the folder is scanned", DefaultValue = true, Order = 3)]
    public bool Shuffle { get; set; } = true;

    [ExtensionParameter("Loop", "Start again when the last file ends", DefaultValue = true, Order = 4)]
    public bool Loop { get; set; } = true;

    [ExtensionParameter("Show Title", "Draw the filename over the first seconds of each clip", DefaultValue = true,
        Order = 5)]
    public bool ShowTitle { get; set; } = true;

    [ExtensionParameter("Use BDF Font", "Render titles with the crisp bitmap (BDF) font", DefaultValue = false,
        Order = 6)]
    public bool UseBdfFont { get; set; }

    [ExtensionParameter("Font Size", "Title height in pixels (0 = auto)", DefaultValue = 0, MinValue = 0,
        MaxValue = 64, Unit = "px", Order = 7)]
    public int FontSize { get; set; }

    [ExtensionParameter("Auto Play", "Start when the extension starts", DefaultValue = true, Order = 8)]
    public bool AutoPlay { get; set; } = true;

    [ExtensionParameter("Volume", "Playback volume (0-100)", MinValue = 0, MaxValue = 100, DefaultValue = 100,
        Order = 9)]
    public int Volume { get; set; } = 100;

    [ExtensionParameter("Now Playing", "Current file (read-only)", ReadOnly = true, Order = 10)]
    public string NowPlaying { get; private set; } = "";

    [ExtensionParameter("File Count", "Videos found in the folder (read-only)", ReadOnly = true, Order = 11)]
    public int FileCount => _queue.Count;

    public string Name => "Trailers";
    public bool IsRunning { get; private set; }

    [ExtensionMethod("Next", "Skip to the next video", Category = "Playback", IconName = "skip", Order = 10)]
    public void Next()
    {
        if (!IsRunning) return;
        Advance(1);
        Task.Run(StartPlayback);
    }

    [ExtensionMethod("Previous", "Go back one video", Category = "Playback", IconName = "prev", Order = 20)]
    public void Previous()
    {
        if (!IsRunning) return;
        Advance(-1);
        Task.Run(StartPlayback);
    }

    [ExtensionMethod("Rescan", "Reload the folder and start from the first file", Category = "Playback",
        IconName = "refresh", Order = 30)]
    public void Rescan() => RestartFromFolder();

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _frameBitmap = new SKBitmap(new SKImageInfo(_canvas.Width, _canvas.Height, SKColorType.Bgra8888));
            _frameBuffer = new byte[_frameSize];
            _cts = new CancellationTokenSource();
            RebuildQueue();
            _index = 0;
            IsRunning = true;
            if (_queue.Count == 0)
            {
                ShowStatus("No videos", ResolveDirectory());
                return;
            }

            ShowStatus($"{_queue.Count} videos", Path.GetFileName(_queue[0]));
            if (AutoPlay) Task.Run(StartPlayback);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _advanceOnExit = false;
            StopFFmpeg();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _frameBitmap?.Dispose();
            _frameBitmap = null;
            _frameBuffer = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private void RestartFromFolder()
    {
        if (!IsRunning) return;
        RebuildQueue();
        _index = 0;
        if (_queue.Count == 0)
        {
            ShowStatus("No videos", ResolveDirectory());
            return;
        }

        if (AutoPlay) Task.Run(StartPlayback);
    }

    private string ResolveDirectory()
    {
        var raw = string.IsNullOrWhiteSpace(_directory) ? "Videos/Trailers" : _directory;
        return Path.IsPathRooted(raw)
            ? raw
            : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, raw));
    }

    private void RebuildQueue()
    {
        var dir = ResolveDirectory();
        var list = new List<string>();
        if (System.IO.Directory.Exists(dir))
        {
            var opt = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.*", opt))
            {
                var ext = Path.GetExtension(file);
                if (Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    list.Add(file);
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            if (Shuffle && list.Count > 1)
            {
                var rng = Random.Shared;
                for (var i = list.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
            }
        }

        _queue = list;
        Console.WriteLine($"[Trailer] {list.Count} file(s) in {dir}");
    }

    private void Advance(int delta)
    {
        if (_queue.Count == 0) return;
        _index = (_index + delta) % _queue.Count;
        if (_index < 0) _index += _queue.Count;
    }

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
            if (_queue.Count == 0) RebuildQueue();
            if (_queue.Count == 0)
            {
                _isLoading = false;
                ShowStatus("No videos", ResolveDirectory());
                return;
            }

            if (_index >= _queue.Count) _index = 0;
            var path = _queue[_index];
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Trailer] missing {path} — rescanning");
                RebuildQueue();
                _isLoading = false;
                if (_queue.Count == 0)
                {
                    ShowStatus("No videos", ResolveDirectory());
                    return;
                }

                _index %= _queue.Count;
                StartPlayback();
                return;
            }

            NowPlaying = Path.GetFileNameWithoutExtension(path);
            Console.WriteLine($"[Trailer] {_index + 1}/{_queue.Count}: {NowPlaying}");
            ShowStatus($"{_index + 1}/{_queue.Count}", NowPlaying);

            _advanceOnExit = false;
            StopFFmpeg();
            StartFFmpegProcess(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trailer] {ex.Message}");
            _isLoading = false;
            ShowStatus("error", ex.Message);
            SkipAfterDelay();
        }
    }

    private void SkipAfterDelay()
    {
        _isLoading = false;
        if (!IsRunning) return;
        Task.Run(() =>
        {
            Thread.Sleep(1500);
            if (!IsRunning) return;
            Advance(1);
            if (_index == 0 && !Loop) return;
            StartPlayback();
        });
    }

    private void ShowStatus(string title, string detail)
    {
        var bb = _frameBitmap;
        if (bb == null || !IsRunning) return;
        try
        {
            using var c = new SKCanvas(bb);
            c.Clear(new SKColor(12, 10, 16));
            var titleSize = CanvasText.ResolveSize(FontSize, Math.Max(10, bb.Height * 0.16f));
            var bodySize = FontSize > 0 ? Math.Max(6f, FontSize * 0.7f) : Math.Max(8, bb.Height * 0.10f);
            CanvasText.Draw(c, _canvas, title, new SKColor(255, 200, 80), 6, bb.Height * 0.42f, titleSize,
                SKTextAlign.Left, UseBdfFont);
            CanvasText.Draw(c, _canvas, detail, new SKColor(210, 210, 220), 6, bb.Height * 0.68f, bodySize,
                SKTextAlign.Left, UseBdfFont);
            c.Flush();
            _canvas.SubmitCompletedFrame(bb);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trailer] status: {ex.Message}");
        }
    }

    private void StartFFmpegProcess(string filePath, bool withAudio = true)
    {
        var width = _canvas.Width;
        var height = _canvas.Height;
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-fflags", "+genpts+discardcorrupt",
            "-i", filePath,
            "-map", "0:v:0",
            "-vf", $"scale={width}:{height}:flags=fast_bilinear:sws_dither=none",
            "-pix_fmt", "bgra", "-f", "rawvideo", "-r", "25", "-vsync", "cfr", "pipe:1"
        };

        if (withAudio)
        {
            args.AddRange(["-map", "0:a:0?"]);
            if (Volume < 100)
                args.AddRange(["-af", $"volume={Volume / 100.0:F2}"]);
            args.AddRange(["-f", "alsa", "-ac", "2", "-ar", "44100", "-thread_queue_size", "512", "default"]);
        }
        else
        {
            args.Add("-an");
        }

        try
        {
            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            foreach (var a in args) _ffmpegProcess.StartInfo.ArgumentList.Add(a);

            var ffmpegErr = new StringBuilder();
            _ffmpegProcess.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ffmpegErr.AppendLine(e.Data);
                Console.WriteLine($"[Trailer/FFmpeg] {e.Data}");
            };

            _advanceOnExit = true;
            _frames = 0;
            _ffmpegProcess.Exited += (_, _) =>
            {
                Console.WriteLine($"[Trailer] FFmpeg exited after {_frames} frames");
                _isPlaying = false;
                var err = ffmpegErr.ToString();
                var alsaFail = withAudio && _frames == 0 &&
                               err.Contains("alsa", StringComparison.OrdinalIgnoreCase);
                if (alsaFail && IsRunning)
                {
                    Console.WriteLine("[Trailer] ALSA failed — retrying without audio");
                    _advanceOnExit = false;
                    Task.Run(() =>
                    {
                        StopFFmpeg();
                        StartFFmpegProcess(filePath, withAudio: false);
                    });
                    return;
                }

                if (!_advanceOnExit || !IsRunning || _cts is not { IsCancellationRequested: false }) return;
                if (_index == _queue.Count - 1)
                {
                    RebuildQueue();
                    _index = 0;
                    if (!Loop) return;
                }
                else
                {
                    Advance(1);
                }

                Task.Run(() =>
                {
                    Thread.Sleep(400);
                    StartPlayback();
                });
            };

            Console.WriteLine($"[Trailer] FFmpeg {width}x{height} file={Path.GetFileName(filePath)} audio={(withAudio ? "on" : "off")}");
            _isPlaying = true;
            _isLoading = false;
            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();
            _frameReaderThread = new Thread(ReadFramesLoop)
            {
                Name = "Trailer-FrameReader",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _frameReaderThread.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trailer] FFmpeg: {ex.Message}");
            _isLoading = false;
            _isPlaying = false;
            if (withAudio)
            {
                Console.WriteLine("[Trailer] retrying without audio");
                StartFFmpegProcess(filePath, withAudio: false);
            }
            else
            {
                SkipAfterDelay();
            }
        }
    }

    private void ReadFramesLoop()
    {
        Console.WriteLine("[Trailer] Frame reader started");
        try
        {
            var stream = _ffmpegProcess?.StandardOutput.BaseStream;
            if (stream == null)
            {
                Console.WriteLine("[Trailer] no stdout stream");
                return;
            }

            var buffer = _frameBuffer;
            if (buffer == null)
            {
                Console.WriteLine("[Trailer] no frame buffer");
                return;
            }

            var frameInterval = TimeSpan.FromMilliseconds(40);
            var lastFrameTime = DateTime.UtcNow;
            var titleUntil = DateTime.UtcNow.AddSeconds(3);
            var title = NowPlaying;

            while (IsRunning && _isPlaying && !_cts!.IsCancellationRequested)
            {
                var bytesRead = 0;
                while (bytesRead < _frameSize)
                {
                    var read = stream.Read(buffer, bytesRead, _frameSize - bytesRead);
                    if (read == 0)
                    {
                        Console.WriteLine($"[Trailer] end of stream after {_frames} frames");
                        return;
                    }

                    bytesRead += read;
                }

                if (_frameBitmap != null)
                {
                    var pixels = _frameBitmap.GetPixels();
                    System.Runtime.InteropServices.Marshal.Copy(buffer, 0, pixels, _frameSize);
                    if (ShowTitle && DateTime.UtcNow < titleUntil && !string.IsNullOrEmpty(title))
                        DrawTitle(_frameBitmap, title);
                    _canvas.SubmitCompletedFrame(_frameBitmap);
                }

                _frames++;
                if (_frames == 1) Console.WriteLine("[Trailer] first frame");

                var elapsed = DateTime.UtcNow - lastFrameTime;
                if (elapsed < frameInterval)
                {
                    var sleep = frameInterval - elapsed;
                    if (sleep.TotalMilliseconds > 1)
                        Thread.Sleep((int)sleep.TotalMilliseconds);
                }

                lastFrameTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            if (_cts is not { IsCancellationRequested: true })
                Console.WriteLine($"[Trailer] frames: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("[Trailer] Frame reader ended");
            _isPlaying = false;
        }
    }

    private void DrawTitle(SKBitmap bb, string title)
    {
        using var c = new SKCanvas(bb);
        var h = Math.Max(10f, bb.Height * 0.12f);
        using var bg = new SKPaint { Color = new SKColor(0, 0, 0, 140) };
        c.DrawRect(0, bb.Height - h - 8, bb.Width, h + 8, bg);
        CanvasText.Draw(c, _canvas, title, SKColors.White, 6, bb.Height - 10,
            CanvasText.ResolveSize(FontSize, h * 0.75f),
            SKTextAlign.Left, UseBdfFont);
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
}
