using System.Collections.Concurrent;
using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.AnimatedGifPlayer;

[ExtensionInfo("Animated GIF Player",
    "Plays animated GIF files with loop control and smooth playback",
    "Media Players",
    IconResourceName = "gif.svg")]
public class AnimatedGifPlayerExtension : ICanvasExtension, IDisposable
{
    private readonly object _framesLock = new(); // Lock for frame operations
    private readonly object _gifListLock = new();
    private readonly ICanvas _parentCanvas;
    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private SKBitmap? _backBuffer; // Back buffer for atomic rendering
    private CancellationTokenSource? _cts;
    private string? _currentGifPath;
    private bool _disposed;
    private IList<GifAnimationFrame>? _frames;
    private IList<string> _gifPaths = new List<string>();
    private DateTime _lastDirectoryCheck = DateTime.MinValue;
    private int _lastFileCount;
    private Task? _monitorTask;
    private IList<GifAnimationFrame>? _nextFrames; // Pre-loaded next GIF
    private Task? _playbackTask;
    private bool _useDirectory;

    internal AnimatedGifPlayerExtension(ICanvas canvas)
    {
        _parentCanvas = canvas;

        // Set default directory
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "GIFs");
        if (Directory.Exists(defaultPath)) GifDirectory = defaultPath;
    }

    [ExtensionParameter("Background Color", "Background color for the player",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor { get; set; } = SKColors.Black;
    [ExtensionParameter("Mode", "Single file or directory mode",
        DefaultValue = PlaybackMode.Directory)]
    public PlaybackMode Mode { get; set; } = PlaybackMode.Directory;

    [ExtensionParameter("GIF File Path", "Path to single animated GIF file (Single File mode)",
        DefaultValue = "")]
    public string GifFilePath { get; set; } = string.Empty;

    [ExtensionParameter("GIF Directory", "Directory containing GIF files (Directory mode)",
        DefaultValue = "Images/GIFs")]
    public string GifDirectory { get; set; } = "Images/GIFs";

    [ExtensionParameter("Delay Per GIF", "Time to display each GIF in milliseconds (Directory mode)",
        MinValue = 1000, MaxValue = 300000, DefaultValue = 10000, Unit = "ms")]
    public int DelayPerGif { get; set; } = 10000;

    [ExtensionParameter("Loop Count Per GIF", "Loops per GIF in directory mode (-1 = play until delay)",
        MinValue = -1, MaxValue = 1000, DefaultValue = -1)]
    public int LoopCountPerGif { get; set; } = -1;

    [ExtensionParameter("Loop Count", "Number of times to loop animation in single file mode (-1 = infinite)",
        MinValue = -1, MaxValue = 10000, DefaultValue = -1)]
    public int LoopCount { get; set; } = -1;

    [ExtensionParameter("Auto Start", "Automatically start playing when file is loaded",
        DefaultValue = true)]
    public bool AutoStart { get; set; } = true;

    [ExtensionParameter("Scale Mode", "How to scale the GIF to fit the canvas",
        DefaultValue = ScaleMode.Stretch)]
    public ScaleMode ScaleMode { get; set; } = ScaleMode.Stretch;

    [ExtensionParameter("Frame Rate Multiplier", "Speed multiplier for playback (1.0 = normal speed)",
        MinValue = 0.1, MaxValue = 10.0, DefaultValue = 1.0)]
    public double FrameRateMultiplier { get; set; } = 1.0;

    [ExtensionParameter("Shuffle GIFs", "Randomize GIF order in directory mode",
        DefaultValue = true)]
    public bool ShuffleGifs { get; set; } = true;

    [ExtensionParameter("Auto Reload Directory", "Automatically check for new/removed GIFs",
        DefaultValue = true)]
    public bool AutoReloadDirectory { get; set; } = true;

    [ExtensionParameter("Reload Check Interval", "How often to check for changes in seconds",
        MinValue = 5, MaxValue = 300, DefaultValue = 30, Unit = "seconds")]
    public int ReloadCheckInterval { get; set; } = 30;

    [ExtensionParameter("Include Subdirectories", "Include GIFs from subdirectories",
        DefaultValue = true)]
    public bool IncludeSubdirectories { get; set; } = true;

    [ExtensionParameter("Pre-load Next GIF", "Pre-load next GIF while playing current one (smoother playback)",
        DefaultValue = true)]
    public bool PreloadNextGif { get; set; } = true;

    public bool IsPaused { get; private set; }

    [ExtensionParameter("Frame Count", "Total number of frames in current GIF",
        ReadOnly = true)]
    public int FrameCount => _frames?.Count ?? 0;

    [ExtensionParameter("Current Frame", "Currently displayed frame number",
        ReadOnly = true)]
    public int CurrentFrame { get; private set; }

    [ExtensionParameter("Current File", "Currently loaded GIF file",
        ReadOnly = true)]
    public string CurrentFile => _currentGifPath != null ? Path.GetFileName(_currentGifPath) : "None";

    [ExtensionParameter("GIF Count", "Number of GIFs in directory",
        ReadOnly = true)]
    public int GifCount => _gifPaths.Count;

    public string Name => "Animated GIF Player";

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;

        _useDirectory = Mode == PlaybackMode.Directory;

        if (_useDirectory)
        {
            // Directory mode
            LoadGifsFromDirectory();

            if (_gifPaths.Count == 0)
            {
                Console.WriteLine($"No GIF files found in directory: {GifDirectory}");
                return;
            }
        }
        else
        {
            // Single file mode
            if (string.IsNullOrWhiteSpace(GifFilePath))
            {
                Console.WriteLine("No GIF file path specified");
                return;
            }

            if (!File.Exists(GifFilePath))
            {
                Console.WriteLine($"GIF file not found: {GifFilePath}");
                return;
            }
        }

        Stop();

        // Create back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(new SKImageInfo(_parentCanvas.Width, _parentCanvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        IsPaused = false;
        _cts = new CancellationTokenSource();
        _playbackTask = PlayAsync(_cts.Token);

        // Start directory monitoring if enabled and in directory mode
        if (_useDirectory && AutoReloadDirectory) _monitorTask = MonitorDirectoryAsync(_cts.Token);

        IsRunning = true;

        if (_useDirectory)
            Console.WriteLine($"GIF Player started: {_gifPaths.Count} GIFs, Delay: {DelayPerGif}ms per GIF" +
                              (PreloadNextGif ? " (Pre-loading enabled)" : ""));
        else
            Console.WriteLine(
                $"GIF Player started: {Path.GetFileName(GifFilePath)} ({FrameCount} frames, Loop: {(LoopCount == -1 ? "Infinite" : LoopCount.ToString())})");
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();

        // Resume if paused to allow cancellation to complete
        if (IsPaused) Resume();

        try
        {
            _playbackTask?.Wait(TimeSpan.FromSeconds(2));
            _monitorTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
        {
            // Expected when cancelling
        }

        _cts?.Dispose();
        _cts = null;
        _playbackTask = null;
        _monitorTask = null;
        IsRunning = false;
        IsPaused = false;
        CurrentFrame = 0;

        // Dispose frames safely
        lock (_framesLock)
        {
            DisposeFrames(_frames);
            _frames = null;

            DisposeFrames(_nextFrames);
            _nextFrames = null;
        }

        _parentCanvas.Clear(SKColors.Transparent);
        Console.WriteLine("GIF Player stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _pauseSemaphore?.Dispose();

        // Dispose all frames
        lock (_framesLock)
        {
            DisposeFrames(_frames);
            _frames = null;

            DisposeFrames(_nextFrames);
            _nextFrames = null;
        }

        _backBuffer?.Dispose();
        _backBuffer = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Suspend()
    {
        if (!IsRunning || IsPaused) return;

        IsPaused = true;
        _pauseSemaphore.Wait();
        Console.WriteLine("GIF Player paused");
    }

    public void Resume()
    {
        if (!IsRunning || !IsPaused) return;

        IsPaused = false;
        _pauseSemaphore.Release();
        Console.WriteLine("GIF Player resumed");
    }

    /// <summary>
    ///     Reloads GIFs from directory
    /// </summary>
    public void ReloadDirectory()
    {
        lock (_gifListLock)
        {
            var previousCount = _gifPaths.Count;
            LoadGifsFromDirectory();

            if (_gifPaths.Count != previousCount)
                Console.WriteLine($"GIF list updated: {previousCount} -> {_gifPaths.Count} files");
        }
    }

    /// <summary>
    ///     Loads a GIF file and optionally starts playback (backward compatibility method)
    /// </summary>
    public void Play(string filePath, int loops = -1)
    {
        Mode = PlaybackMode.SingleFile;
        GifFilePath = filePath;
        LoopCount = loops;
        Start();
    }

    /// <summary>
    ///     Loads a new GIF file and optionally starts playback
    /// </summary>
    public async Task LoadGif(string filePath, bool autoStart = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("Invalid file path");
            return;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        // Stop current playback
        if (IsRunning) Stop();

        try
        {
            Console.WriteLine($"Loading GIF: {Path.GetFileName(filePath)}...");
            _frames = await LoadAnimatedGifAsync(filePath, CancellationToken.None);
            _currentGifPath = filePath;
            GifFilePath = filePath;
            CurrentFrame = 0;

            Console.WriteLine($"GIF loaded successfully: {FrameCount} frames");

            if (autoStart && AutoStart) Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load GIF: {ex.Message}");
            _frames = null;
            _currentGifPath = null;
        }
    }

    private void DisposeFrames(IList<GifAnimationFrame>? frames)
    {
        if (frames == null) return;

        foreach (var frame in frames)
            try
            {
                frame?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
    }

    private void LoadGifsFromDirectory()
    {
        _gifPaths.Clear();

        if (!Directory.Exists(GifDirectory))
        {
            Console.WriteLine($"GIF directory not found: {GifDirectory}");
            return;
        }

        var searchOption = IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var gifFiles = Directory.GetFiles(GifDirectory, "*.gif", searchOption)
            .OrderBy(f => f)
            .ToList();

        _gifPaths = gifFiles;
        _lastFileCount = gifFiles.Count;
        _lastDirectoryCheck = DateTime.UtcNow;

        Console.WriteLine($"Loaded {_gifPaths.Count} GIF files from {GifDirectory}" +
                          (IncludeSubdirectories ? " (including subdirectories)" : ""));
    }

    private async Task MonitorDirectoryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ReloadCheckInterval), ct);

                if (ct.IsCancellationRequested) break;

                if (!Directory.Exists(GifDirectory))
                {
                    Console.WriteLine($"Warning: GIF directory no longer exists: {GifDirectory}");
                    continue;
                }

                var searchOption = IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var currentFiles = Directory.GetFiles(GifDirectory, "*.gif", searchOption)
                    .OrderBy(f => f)
                    .ToList();

                var hasChanged = false;

                lock (_gifListLock)
                {
                    if (currentFiles.Count != _gifPaths.Count)
                    {
                        hasChanged = true;
                    }
                    else
                    {
                        var currentSet = new HashSet<string>(currentFiles);
                        var existingSet = new HashSet<string>(_gifPaths);
                        hasChanged = !currentSet.SetEquals(existingSet);
                    }

                    if (hasChanged)
                    {
                        var oldCount = _gifPaths.Count;
                        _gifPaths = currentFiles;
                        _lastFileCount = currentFiles.Count;

                        var added = currentFiles.Count - oldCount;
                        if (added > 0)
                            Console.WriteLine($"? GIF list updated: +{added} new files (total: {currentFiles.Count})");
                        else if (added < 0)
                            Console.WriteLine(
                                $"? GIF list updated: {Math.Abs(added)} files removed (total: {currentFiles.Count})");
                        else
                            Console.WriteLine($"? GIF list updated: files changed (total: {currentFiles.Count})");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Error monitoring directory: {ex.Message}");
            }
    }

    private async Task PlayAsync(CancellationToken ct)
    {
        if (_useDirectory)
            await PlayDirectoryModeAsync(ct);
        else
            await PlaySingleFileModeAsync(ct);
    }

    private async Task PlaySingleFileModeAsync(CancellationToken ct)
    {
        // Load GIF if not already loaded
        if (_frames == null || _frames.Count == 0)
            try
            {
                _frames = await LoadAnimatedGifAsync(GifFilePath, ct);
                _currentGifPath = GifFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load GIF: {ex.Message}");
                IsRunning = false;
                return;
            }

        if (_frames == null || _frames.Count == 0)
        {
            Console.WriteLine("No frames to play");
            IsRunning = false;
            return;
        }

        var infinite = LoopCount == -1;
        var loopsRemaining = LoopCount;

        while ((infinite || loopsRemaining > 0) && !ct.IsCancellationRequested)
        {
            if (!infinite) loopsRemaining--;

            await PlayGifFramesAsync(_frames, ct);
        }

        IsRunning = false;
        Console.WriteLine("GIF playback completed");
    }

    private async Task PlayDirectoryModeAsync(CancellationToken ct)
    {
        var rng = new Random();

        while (!ct.IsCancellationRequested)
        {
            // Get snapshot of current GIFs
            IList<string> gifsToPlay;
            lock (_gifListLock)
            {
                gifsToPlay = _gifPaths.ToList();
            }

            if (gifsToPlay.Count == 0)
            {
                Console.WriteLine("No GIFs available, waiting...");
                await Task.Delay(5000, ct);
                continue;
            }

            // Shuffle if enabled
            if (ShuffleGifs) gifsToPlay = gifsToPlay.OrderBy(g => rng.Next()).ToList();

            for (var gifIndex = 0; gifIndex < gifsToPlay.Count; gifIndex++)
            {
                var gifPath = gifsToPlay[gifIndex];

                if (ct.IsCancellationRequested)
                {
                    _parentCanvas.Clear(SKColors.Transparent);
                    break;
                }

                await WaitIfPausedAsync(ct);

                if (!File.Exists(gifPath))
                {
                    Console.WriteLine($"Skipping missing file: {Path.GetFileName(gifPath)}");
                    continue;
                }

                try
                {
                    IList<GifAnimationFrame>? currentFrames = null;
                    IList<GifAnimationFrame>? framesToDispose = null;

                    // CRITICAL: Lock when swapping frames
                    lock (_framesLock)
                    {
                        // Check if we have pre-loaded frames
                        if (_nextFrames != null)
                        {
                            // Use pre-loaded frames
                            currentFrames = _nextFrames;
                            _nextFrames = null;

                            // Mark old frames for disposal
                            framesToDispose = _frames;
                            _frames = currentFrames;
                        }
                        else
                        {
                            // No pre-loaded frames, will load synchronously
                            framesToDispose = _frames;
                            _frames = null;
                        }
                    }

                    // Dispose old frames OUTSIDE the lock
                    DisposeFrames(framesToDispose);

                    // Load if we don't have frames yet
                    if (currentFrames == null)
                    {
                        currentFrames = await LoadAnimatedGifAsync(gifPath, ct);
                        lock (_framesLock)
                        {
                            _frames = currentFrames;
                        }
                    }

                    _currentGifPath = gifPath;

                    if (currentFrames == null || currentFrames.Count == 0)
                    {
                        Console.WriteLine($"Failed to load: {Path.GetFileName(gifPath)}");
                        continue;
                    }

                    // Start pre-loading next GIF in background (if enabled)
                    if (PreloadNextGif && gifIndex + 1 < gifsToPlay.Count)
                    {
                        var nextGifPath = gifsToPlay[gifIndex + 1];

                        // Fire and forget - don't await
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (!File.Exists(nextGifPath))
                                    return;

                                var nextFrames = await LoadAnimatedGifAsync(nextGifPath, ct);

                                // Store pre-loaded frames safely
                                lock (_framesLock)
                                {
                                    // Dispose old pre-loaded frames if any
                                    var oldNextFrames = _nextFrames;
                                    _nextFrames = nextFrames;

                                    // Dispose outside lock
                                    Task.Run(() => DisposeFrames(oldNextFrames));
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                // Pre-loading failed silently
                            }
                        }, ct);
                    }

                    // Play current GIF with time limit
                    var startTime = DateTime.UtcNow;
                    var loopCount = 0;
                    var infinite = LoopCountPerGif == -1;

                    while (!ct.IsCancellationRequested)
                    {
                        // Check time limit
                        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                        if (elapsed >= DelayPerGif) break;

                        // Check loop count
                        if (!infinite && loopCount >= LoopCountPerGif) break;

                        await PlayGifFramesAsync(currentFrames, ct);
                        loopCount++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"Error playing {Path.GetFileName(gifPath)}: {ex.Message}");
                }
            }
        }
    }

    private async Task PlayGifFramesAsync(IList<GifAnimationFrame> frames, CancellationToken ct)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            if (ct.IsCancellationRequested) return;

            await WaitIfPausedAsync(ct);

            var frame = frames[i];
            CurrentFrame = i;

            // Draw frame safely
            try
            {
                DrawFrame(frame.Bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error drawing frame {i}: {ex.Message}");
                return; // Stop playing this GIF
            }

            var delay = (int)(frame.Duration / FrameRateMultiplier);
            delay = Math.Max(1, delay);

            await Task.Delay(delay, ct);
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken ct)
    {
        if (IsPaused)
        {
            await _pauseSemaphore.WaitAsync(ct);
            _pauseSemaphore.Release();
        }
    }

    private void DrawFrame(SKBitmap frame)
    {
        if (_backBuffer == null) return;

        using var canvas = new SKCanvas(_backBuffer);

        // Clear with background color
        canvas.Clear(BackgroundColor);

        // Draw GIF frame based on scale mode
        switch (ScaleMode)
        {
            case ScaleMode.Stretch:
                canvas.DrawBitmap(frame, new SKRect(0, 0, _parentCanvas.Width, _parentCanvas.Height));
                break;

            case ScaleMode.Fit:
                DrawFitToCanvas(canvas, frame);
                break;

            case ScaleMode.Center:
                DrawCentered(canvas, frame);
                break;

            case ScaleMode.Fill:
                DrawFillCanvas(canvas, frame);
                break;

            default:
                canvas.DrawBitmap(frame, 0, 0);
                break;
        }

        canvas.Flush();_parentCanvas.SubmitCompletedFrame(_backBuffer);
    }

    private void DrawFitToCanvas(SKCanvas canvas, SKBitmap bitmap)
    {
        var scaleX = (float)_parentCanvas.Width / bitmap.Width;
        var scaleY = (float)_parentCanvas.Height / bitmap.Height;
        var scale = Math.Min(scaleX, scaleY);

        var scaledWidth = (int)(bitmap.Width * scale);
        var scaledHeight = (int)(bitmap.Height * scale);
        var x = (_parentCanvas.Width - scaledWidth) / 2;
        var y = (_parentCanvas.Height - scaledHeight) / 2;

        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + scaledWidth, y + scaledHeight));
    }

    private void DrawCentered(SKCanvas canvas, SKBitmap bitmap)
    {
        var x = (_parentCanvas.Width - bitmap.Width) / 2;
        var y = (_parentCanvas.Height - bitmap.Height) / 2;

        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + bitmap.Width, y + bitmap.Height));
    }

    private void DrawFillCanvas(SKCanvas canvas, SKBitmap bitmap)
    {
        var scaleX = (float)_parentCanvas.Width / bitmap.Width;
        var scaleY = (float)_parentCanvas.Height / bitmap.Height;
        var scale = Math.Max(scaleX, scaleY);

        var scaledWidth = (int)(bitmap.Width * scale);
        var scaledHeight = (int)(bitmap.Height * scale);
        var x = (_parentCanvas.Width - scaledWidth) / 2;
        var y = (_parentCanvas.Height - scaledHeight) / 2;

        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + scaledWidth, y + scaledHeight));
    }

    private async Task<IList<GifAnimationFrame>> LoadAnimatedGifAsync(string path, CancellationToken ct)
    {
        var frames = new List<GifAnimationFrame>();

        try
        {
            // Load file asynchronously
            var fileBytes = await File.ReadAllBytesAsync(path, ct);

            // Decode on thread pool to avoid blocking
            await Task.Run(() =>
            {
                using var stream = new MemoryStream(fileBytes);
                using var skStream = new SKManagedStream(stream);
                using var codec = SKCodec.Create(skStream);

                if (codec == null) throw new InvalidOperationException("Failed to create codec for GIF file");

                var frameInfo = codec.FrameInfo;
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height);
                var frameCount = frameInfo.Length;

                // Pre-allocate array for frames (thread-safe access by index)
                var frameArray = new GifAnimationFrame[frameCount];

                // Determine optimal parallelism for Raspberry Pi 4 (4 cores)
                // Use 3 cores for decoding, leave 1 for system/playback
                var maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1);

                // For small GIFs, parallel processing overhead isn't worth it
                if (frameCount < 20)
                {
                    // Sequential decoding for small GIFs (faster due to no overhead)
                    using var tempBitmap = new SKBitmap(info);
                    var pixels = tempBitmap.GetPixels();

                    for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                    {
                        if (ct.IsCancellationRequested) break;

                        var duration = frameInfo[frameIndex].Duration;
                        if (duration <= 0)
                            duration = 100;

                        var opts = new SKCodecOptions(frameIndex);
                        var result = codec.GetPixels(info, pixels, opts);

                        if (result == SKCodecResult.Success)
                        {
                            var frameCopy = new SKBitmap(info);
                            tempBitmap.CopyTo(frameCopy);
                            frameArray[frameIndex] = new GifAnimationFrame(frameCopy, duration);
                        }
                    }
                }
                else
                {
                    // Parallel decoding for large GIFs (better performance)
                    // Each thread needs its own codec instance
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxDegreeOfParallelism,
                        CancellationToken = ct
                    };

                    // Use Partitioner for better load balancing
                    var partitioner = Partitioner.Create(0, frameCount,
                        Math.Max(1, frameCount / (maxDegreeOfParallelism * 2)));

                    Parallel.ForEach(partitioner, parallelOptions, (range, loopState) =>
                    {
                        // Each thread gets its own codec instance
                        using var threadStream = new MemoryStream(fileBytes);
                        using var threadSkStream = new SKManagedStream(threadStream);
                        using var threadCodec = SKCodec.Create(threadSkStream);

                        if (threadCodec == null)
                            return;

                        var threadInfo = new SKImageInfo(threadCodec.Info.Width, threadCodec.Info.Height);
                        using var threadBitmap = new SKBitmap(threadInfo);
                        var threadPixels = threadBitmap.GetPixels();

                        for (var frameIndex = range.Item1; frameIndex < range.Item2; frameIndex++)
                        {
                            if (ct.IsCancellationRequested || loopState.IsStopped)
                                break;

                            try
                            {
                                var duration = frameInfo[frameIndex].Duration;
                                if (duration <= 0)
                                    duration = 100;

                                var opts = new SKCodecOptions(frameIndex);
                                var result = threadCodec.GetPixels(threadInfo, threadPixels, opts);

                                if (result == SKCodecResult.Success)
                                {
                                    var frameCopy = new SKBitmap(threadInfo);
                                    threadBitmap.CopyTo(frameCopy);
                                    frameArray[frameIndex] = new GifAnimationFrame(frameCopy, duration);
                                }
                            }
                            catch
                            {
                                // Skip problematic frame, continue with others
                            }
                        }
                    });
                }

                // Convert array to list, filtering out null entries
                frames.Capacity = frameCount;
                for (var i = 0; i < frameCount; i++)
                    if (frameArray[i] != null)
                        frames.Add(frameArray[i]);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Dispose any frames we loaded before cancellation
            foreach (var frame in frames) frame?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading GIF: {ex.Message}");

            // Dispose any frames we loaded before error
            foreach (var frame in frames) frame?.Dispose();
            throw;
        }

        return frames;
    }

    ~AnimatedGifPlayerExtension()
    {
        Dispose();
    }
}

/// <summary>
///     Playback mode for GIF player
/// </summary>
public enum PlaybackMode
{
    /// <summary>
    ///     Play a single GIF file
    /// </summary>
    SingleFile,

    /// <summary>
    ///     Play multiple GIFs from a directory
    /// </summary>
    Directory
}

/// <summary>
///     Defines how the GIF should be scaled to fit the canvas
/// </summary>
public enum ScaleMode
{
    /// <summary>
    ///     Draw at original size, no scaling
    /// </summary>
    None,

    /// <summary>
    ///     Stretch to fill canvas (may distort aspect ratio)
    /// </summary>
    Stretch,

    /// <summary>
    ///     Fit to canvas maintaining aspect ratio (letterbox/pillarbox)
    /// </summary>
    Fit,

    /// <summary>
    ///     Center at original size
    /// </summary>
    Center,

    /// <summary>
    ///     Fill canvas maintaining aspect ratio (may crop)
    /// </summary>
    Fill
}