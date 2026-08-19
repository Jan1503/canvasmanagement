using CanvasManagement.Interfaces;
using SkiaSharp;

namespace CanvasManagement.Extension.SlideShowPlayer;

[ExtensionInfo("Slideshow Player",
    "Displays a slideshow with animated transitions between images",
    "Media Players",
    IconResourceName = "slideshow.svg")]
public class SlideShowPlayerExtension : ICanvasExtension, IDisposable
{
    public enum Direction
    {
        // Simple slides
        LeftToRight,
        RightToLeft,
        TopToBottom,
        BottomToTop,

        // Diagonal slides
        TopLeftToBottomRight,
        TopRightToBottomLeft,
        BottomLeftToTopRight,
        BottomRightToTopLeft,

        // Fade effects
        Fade,
        FadeBlack,
        FadeWhite,

        // Zoom effects
        ZoomIn,
        ZoomOut,
        ZoomInRotate,
        ZoomOutRotate,

        // Wipe effects
        WipeLeft,
        WipeRight,
        WipeUp,
        WipeDown,
        WipeCenter,
        WipeEdges,

        // Push effects
        PushLeft,
        PushRight,
        PushUp,
        PushDown,

        // Reveal effects
        RevealLeft,
        RevealRight,
        RevealUp,
        RevealDown,

        // Pattern effects
        VenetianBlinds,
        CheckerBoard,
        Dissolve,
        Pixelate,
        Spiral,
        CircleExpand,
        CircleContract,
        DiamondExpand,

        // Rotation effects
        RotateIn,
        RotateOut,
        Flip3D,

        // Split effects
        SplitVertical,
        SplitHorizontal,

        Random
    }

    private readonly object _imageListLock = new();

    private readonly ICanvas _parentCanvas;
    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private SKBitmap? _backBuffer;
    private SKColor _backgroundColor = SKColors.Black;
    private IList<string> _bitmapPaths = new List<string>();
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private DateTime _lastDirectoryCheck = DateTime.MinValue;
    private int _lastImageCount;
    private Task? _monitorTask;
    private Task? _playbackTask;

    internal SlideShowPlayerExtension(ICanvas canvas)
    {
        _parentCanvas = canvas;

        // Set default image directory
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Slideshow");
        if (Directory.Exists(defaultPath)) ImageDirectory = defaultPath;
    }

    [ExtensionParameter("Background Color", "Background color for the slideshow",
        DefaultValue = "#000000")]
    public SKColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }
    [ExtensionParameter("Image Directory", "Directory containing images for slideshow",
        DefaultValue = "Images/Slideshow")]
    public string ImageDirectory { get; set; } = "Images/Slideshow";

    [ExtensionParameter("Delay", "Time to display each image in milliseconds",
        MinValue = 100, MaxValue = 60000, DefaultValue = 3000, Unit = "ms")]
    public int Delay { get; set; } = 3000;

    [ExtensionParameter("Transition Speed", "Speed of transition animation in milliseconds",
        MinValue = 1, MaxValue = 100, DefaultValue = 10, Unit = "ms")]
    public int TransitionSpeed { get; set; } = 10;

    [ExtensionParameter("Transition Direction", "Direction of slide transition",
        DefaultValue = Direction.Random)]
    public Direction TransitionDirection { get; set; } = Direction.Random;

    [ExtensionParameter("Loop Count", "Number of times to loop slideshow (-1 = infinite)",
        MinValue = -1, MaxValue = 1000, DefaultValue = -1)]
    public int LoopCount { get; set; } = -1;

    [ExtensionParameter("Shuffle Images", "Randomize image order on each loop",
        DefaultValue = true)]
    public bool ShuffleImages { get; set; } = true;

    [ExtensionParameter("Image Extensions", "Comma-separated list of file extensions to include",
        DefaultValue = "jpg,jpeg,png,bmp,gif")]
    public string ImageExtensions { get; set; } = "jpg,jpeg,png,bmp,gif";

    [ExtensionParameter("Auto Reload Images", "Automatically check for new/removed images in directory",
        DefaultValue = true)]
    public bool AutoReloadImages { get; set; } = true;

    [ExtensionParameter("Reload Check Interval", "How often to check for image changes in seconds",
        MinValue = 5, MaxValue = 300, DefaultValue = 30, Unit = "seconds")]
    public int ReloadCheckInterval { get; set; } = 30;

    [ExtensionParameter("Include Subdirectories", "Include images from subdirectories",
        DefaultValue = true)]
    public bool IncludeSubdirectories { get; set; } = true;

    public bool IsPaused { get; private set; }

    [ExtensionParameter("Current Image Count", "Number of images currently in rotation",
        ReadOnly = true)]
    public int CurrentImageCount => _bitmapPaths.Count;

    public string Name => "Slideshow Player";

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;

        // Load images from directory
        LoadImagesFromDirectory();

        if (_bitmapPaths.Count == 0)
        {
            Console.WriteLine($"No images found in directory: {ImageDirectory}");
            return;
        }

        Stop();

        IsPaused = false;

        // Create back buffer
        _backBuffer?.Dispose();
        _backBuffer = new SKBitmap(new SKImageInfo(_parentCanvas.Width, _parentCanvas.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        _cts = new CancellationTokenSource();
        _playbackTask = StartAsync(_cts.Token);

        // Start directory monitoring if enabled
        if (AutoReloadImages) _monitorTask = MonitorDirectoryAsync(_cts.Token);

        IsRunning = true;

        Console.WriteLine(
            $"Slideshow started - {_bitmapPaths.Count} images, Delay: {Delay}ms, Transition: {TransitionDirection}");
        if (AutoReloadImages)
            Console.WriteLine($"Directory monitoring enabled - checking every {ReloadCheckInterval} seconds");
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

        _backBuffer?.Dispose();
        _backBuffer = null;

        _parentCanvas.Clear(SKColors.Transparent);
        Console.WriteLine("Slideshow stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _pauseSemaphore?.Dispose();
        _backBuffer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Suspend()
    {
        if (!IsRunning || IsPaused) return;

        IsPaused = true;
        _pauseSemaphore.Wait(); // Acquire the semaphore to block playback
        Console.WriteLine("Slideshow paused");
    }

    public void Resume()
    {
        if (!IsRunning || !IsPaused) return;

        IsPaused = false;
        _pauseSemaphore.Release(); // Release the semaphore to resume playback
        Console.WriteLine("Slideshow resumed");
    }

    public void ReloadImages()
    {
        lock (_imageListLock)
        {
            var previousCount = _bitmapPaths.Count;
            LoadImagesFromDirectory();

            if (_bitmapPaths.Count != previousCount)
                Console.WriteLine($"Image list updated: {previousCount} -> {_bitmapPaths.Count} images");
        }
    }

    // Helper method to draw with background and atomic submission
    private void DrawWithBackground(Action<ICanvas> drawAction)
    {
        if (_backBuffer == null) return;

        using var canvas = new SKCanvas(_backBuffer);

        // Clear with background color (supports transparency)
        if (_backgroundColor.Alpha == 0)
        {
            canvas.Clear(SKColors.Transparent);
        }
        else if (_backgroundColor.Alpha == 255)
        {
            canvas.Clear(_backgroundColor);
        }
        else
        {
            canvas.Clear(SKColors.Transparent);
            using var bgPaint = new SKPaint { Color = _backgroundColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, _parentCanvas.Width, _parentCanvas.Height, bgPaint);
        }

        canvas.Flush();

        // Transitions draw directly to canvas - just ensure background is clear first
        if (_backgroundColor.Alpha > 0)
            _parentCanvas.Clear(_backgroundColor);
        else
            _parentCanvas.Clear(SKColors.Transparent);

        // Let the draw action proceed
        drawAction(_parentCanvas);
    }

    private void LoadImagesFromDirectory()
    {
        _bitmapPaths.Clear();

        if (!Directory.Exists(ImageDirectory))
        {
            Console.WriteLine($"Image directory not found: {ImageDirectory}");
            return;
        }

        var extensions = ImageExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(ext => ext.Trim().TrimStart('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var searchOption = IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var imageFiles = Directory.GetFiles(ImageDirectory, "*.*", searchOption)
            .Where(file => extensions.Contains(Path.GetExtension(file).TrimStart('.')))
            .OrderBy(f => f)
            .ToList();

        _bitmapPaths = imageFiles;
        _lastImageCount = imageFiles.Count;
        _lastDirectoryCheck = DateTime.UtcNow;

        Console.WriteLine($"Loaded {_bitmapPaths.Count} images from {ImageDirectory}" +
                          (IncludeSubdirectories ? " (including subdirectories)" : ""));
    }

    private async Task MonitorDirectoryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ReloadCheckInterval), ct);

                if (ct.IsCancellationRequested) break;

                // Check if directory still exists
                if (!Directory.Exists(ImageDirectory))
                {
                    Console.WriteLine($"Warning: Image directory no longer exists: {ImageDirectory}");
                    continue;
                }

                // Quick check: compare file count first
                var searchOption = IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var extensions = ImageExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim().TrimStart('.'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var currentFiles = Directory.GetFiles(ImageDirectory, "*.*", searchOption)
                    .Where(file => extensions.Contains(Path.GetExtension(file).TrimStart('.')))
                    .OrderBy(f => f)
                    .ToList();

                // Compare with current list
                var hasChanged = false;

                lock (_imageListLock)
                {
                    if (currentFiles.Count != _bitmapPaths.Count)
                    {
                        hasChanged = true;
                    }
                    else
                    {
                        // Check if files are different (deep comparison)
                        var currentSet = new HashSet<string>(currentFiles);
                        var existingSet = new HashSet<string>(_bitmapPaths);

                        hasChanged = !currentSet.SetEquals(existingSet);
                    }

                    if (hasChanged)
                    {
                        var oldCount = _bitmapPaths.Count;
                        _bitmapPaths = currentFiles;
                        _lastImageCount = currentFiles.Count;

                        var added = currentFiles.Count - oldCount;
                        if (added > 0)
                            Console.WriteLine(
                                $"? Image list updated: +{added} new images (total: {currentFiles.Count})");
                        else if (added < 0)
                            Console.WriteLine(
                                $"? Image list updated: {Math.Abs(added)} images removed (total: {currentFiles.Count})");
                        else
                            Console.WriteLine($"? Image list updated: files changed (total: {currentFiles.Count})");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Error monitoring directory: {ex.Message}");
            }
    }

    private async Task StartAsync(CancellationToken ct)
    {
        var infinite = LoopCount == -1;
        var loopsRemaining = LoopCount;
        var firstRun = true;
        var rng = new Random();

        while ((infinite || loopsRemaining > 0) && !ct.IsCancellationRequested)
        {
            if (!infinite) loopsRemaining--;

            // Get a snapshot of current images (thread-safe)
            IList<string> imagesToDisplay;
            lock (_imageListLock)
            {
                imagesToDisplay = _bitmapPaths.ToList();
            }

            // Check if we have any images
            if (imagesToDisplay.Count == 0)
            {
                Console.WriteLine("No images available, waiting for images...");
                await Task.Delay(5000, ct);
                continue;
            }

            // Shuffle the bitmaps if enabled
            if (ShuffleImages) imagesToDisplay = imagesToDisplay.OrderBy(a => rng.Next()).ToList();

            foreach (var bitmapPath in imagesToDisplay)
            {
                if (ct.IsCancellationRequested)
                {
                    _parentCanvas.Clear(SKColors.Transparent);
                    break;
                }

                // Check for pause state before processing
                await WaitIfPausedAsync(ct);

                // Verify file still exists before trying to load
                if (!File.Exists(bitmapPath))
                {
                    Console.WriteLine($"Skipping missing file: {Path.GetFileName(bitmapPath)}");
                    continue;
                }

                // Load and dispose bitmap properly
                using var currentBitmap = SKBitmap.Decode(bitmapPath);
                if (currentBitmap == null)
                {
                    Console.WriteLine($"Failed to decode image: {bitmapPath}");
                    continue;
                }

                var transitionDirection = TransitionDirection;
                if (TransitionDirection == Direction.Random)
                {
                    // Randomly select from all available transitions (excluding Random itself)
                    var allDirections = Enum.GetValues<Direction>()
                        .Where(d => d != Direction.Random)
                        .ToArray();
                    transitionDirection = allDirections[rng.Next(allDirections.Length)];
                }

                if (firstRun)
                {
                    await WaitIfPausedAsync(ct);
                    _parentCanvas.DrawBitmap(currentBitmap, 0, 0);
                    firstRun = false;
                }
                else
                {
                    await AnimateTransitionAsync(currentBitmap, transitionDirection, ct);
                }

                await Task.Delay(Delay, ct);
            }
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken ct)
    {
        // If paused, wait on the semaphore asynchronously
        if (IsPaused)
        {
            await _pauseSemaphore.WaitAsync(ct);
            _pauseSemaphore.Release(); // Immediately release to allow next check
        }
    }

    private async Task AnimateTransitionAsync(SKBitmap newBitmap, Direction direction, CancellationToken ct)
    {
        try
        {
            var steps = Math.Max(1, 1000 / TransitionSpeed);

            switch (direction)
            {
                // ===== SIMPLE SLIDES =====
                case Direction.RightToLeft:
                    for (var x = _parentCanvas.Width; x >= 0; x -= 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, x, 0, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                case Direction.LeftToRight:
                    for (var x = -_parentCanvas.Width; x <= 0; x += 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, x, 0, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                case Direction.TopToBottom:
                    for (var y = -_parentCanvas.Height; y <= 0; y += 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, 0, y, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                case Direction.BottomToTop:
                    for (var y = _parentCanvas.Height; y >= 0; y -= 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, 0, y, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                // ===== DIAGONAL SLIDES =====
                case Direction.TopLeftToBottomRight:
                    for (var x = -_parentCanvas.Width; x <= 0; x += 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, x, x / 2, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                case Direction.TopRightToBottomLeft:
                    for (var y = -_parentCanvas.Height; y <= 0; y += 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, 2 * y * -1, y, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                case Direction.BottomLeftToTopRight:
                    for (var y = _parentCanvas.Height; y >= 0; y -= 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, 2 * y * -1, y, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                case Direction.BottomRightToTopLeft:
                    for (var x = _parentCanvas.Width; x >= 0; x -= 2)
                    {
                        await WaitIfPausedAsync(ct);
                        _parentCanvas.DrawBitmap(newBitmap, x, x / 2, fitToCanvas: true);
                        await Task.Delay(TransitionSpeed, ct);
                    }

                    break;

                // ===== FADE EFFECTS =====
                case Direction.Fade:
                    await FadeTransition(newBitmap, SKColors.Transparent, ct);
                    break;

                case Direction.FadeBlack:
                    await FadeTransition(newBitmap, SKColors.Black, ct);
                    break;

                case Direction.FadeWhite:
                    await FadeTransition(newBitmap, SKColors.White, ct);
                    break;

                // ===== ZOOM EFFECTS =====
                case Direction.ZoomIn:
                    await ZoomTransition(newBitmap, 0.1f, 1.0f, 0, ct);
                    break;

                case Direction.ZoomOut:
                    await ZoomTransition(newBitmap, 1.5f, 1.0f, 0, ct);
                    break;

                case Direction.ZoomInRotate:
                    await ZoomTransition(newBitmap, 0.1f, 1.0f, 360, ct);
                    break;

                case Direction.ZoomOutRotate:
                    await ZoomTransition(newBitmap, 1.5f, 1.0f, -360, ct);
                    break;

                // ===== WIPE EFFECTS =====
                case Direction.WipeLeft:
                    await WipeTransition(newBitmap, WipeDirection.Left, ct);
                    break;

                case Direction.WipeRight:
                    await WipeTransition(newBitmap, WipeDirection.Right, ct);
                    break;

                case Direction.WipeUp:
                    await WipeTransition(newBitmap, WipeDirection.Up, ct);
                    break;

                case Direction.WipeDown:
                    await WipeTransition(newBitmap, WipeDirection.Down, ct);
                    break;

                case Direction.WipeCenter:
                    await WipeTransition(newBitmap, WipeDirection.Center, ct);
                    break;

                case Direction.WipeEdges:
                    await WipeTransition(newBitmap, WipeDirection.Edges, ct);
                    break;

                // ===== PUSH EFFECTS =====
                case Direction.PushLeft:
                    await PushTransition(newBitmap, PushDirection.Left, ct);
                    break;

                case Direction.PushRight:
                    await PushTransition(newBitmap, PushDirection.Right, ct);
                    break;

                case Direction.PushUp:
                    await PushTransition(newBitmap, PushDirection.Up, ct);
                    break;

                case Direction.PushDown:
                    await PushTransition(newBitmap, PushDirection.Down, ct);
                    break;

                // ===== REVEAL EFFECTS =====
                case Direction.RevealLeft:
                    await RevealTransition(newBitmap, RevealDirection.Left, ct);
                    break;

                case Direction.RevealRight:
                    await RevealTransition(newBitmap, RevealDirection.Right, ct);
                    break;

                case Direction.RevealUp:
                    await RevealTransition(newBitmap, RevealDirection.Up, ct);
                    break;

                case Direction.RevealDown:
                    await RevealTransition(newBitmap, RevealDirection.Down, ct);
                    break;

                // ===== PATTERN EFFECTS =====
                case Direction.VenetianBlinds:
                    await VenetianBlindsTransition(newBitmap, ct);
                    break;

                case Direction.CheckerBoard:
                    await CheckerBoardTransition(newBitmap, ct);
                    break;

                case Direction.Dissolve:
                    await DissolveTransition(newBitmap, ct);
                    break;

                case Direction.Pixelate:
                    await PixelateTransition(newBitmap, ct);
                    break;

                case Direction.Spiral:
                    await SpiralTransition(newBitmap, ct);
                    break;

                case Direction.CircleExpand:
                    await CircleTransition(newBitmap, true, ct);
                    break;

                case Direction.CircleContract:
                    await CircleTransition(newBitmap, false, ct);
                    break;

                case Direction.DiamondExpand:
                    await DiamondTransition(newBitmap, ct);
                    break;

                // ===== ROTATION EFFECTS =====
                case Direction.RotateIn:
                    await RotateTransition(newBitmap, 0, 360, 0.1f, 1.0f, ct);
                    break;

                case Direction.RotateOut:
                    await RotateTransition(newBitmap, 0, -360, 1.0f, 0.1f, ct);
                    break;

                case Direction.Flip3D:
                    await Flip3DTransition(newBitmap, ct);
                    break;

                // ===== SPLIT EFFECTS =====
                case Direction.SplitVertical:
                    await SplitTransition(newBitmap, true, ct);
                    break;

                case Direction.SplitHorizontal:
                    await SplitTransition(newBitmap, false, ct);
                    break;
            }

            // Ensure final position is exactly at (0,0)
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Try to restore display even after crash
            try
            {
                _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
            }
            catch
            {
                // Silent fail on restore
            }

            throw;
        }
    }

    // ===== TRANSITION IMPLEMENTATIONS =====

    private async Task FadeTransition(SKBitmap newBitmap, SKColor fadeColor, CancellationToken ct)
    {
        var steps = 20;

        // Fade out to color (if not transparent)
        if (fadeColor != SKColors.Transparent)
            for (var i = 0; i <= steps; i++)
            {
                await WaitIfPausedAsync(ct);
                var alpha = (byte)(255 * i / steps);
                var color = new SKColor(fadeColor.Red, fadeColor.Green, fadeColor.Blue, alpha);
                _parentCanvas.DrawRect(0, 0, _parentCanvas.Width, _parentCanvas.Height, color, SKPaintStyle.Fill);
                await Task.Delay(TransitionSpeed, ct);
            }

        // Draw new image
        _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

        // Fade in from color (if not transparent)
        if (fadeColor != SKColors.Transparent)
            for (var i = steps; i >= 0; i--)
            {
                await WaitIfPausedAsync(ct);
                _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
                var alpha = (byte)(255 * i / steps);
                var color = new SKColor(fadeColor.Red, fadeColor.Green, fadeColor.Blue, alpha);
                _parentCanvas.DrawRect(0, 0, _parentCanvas.Width, _parentCanvas.Height, color, SKPaintStyle.Fill);
                await Task.Delay(TransitionSpeed, ct);
            }
    }

    private async Task ZoomTransition(SKBitmap newBitmap, float startScale, float endScale, float rotation,
        CancellationToken ct)
    {
        try
        {
            var steps = 30;
            for (var i = 0; i <= steps; i++)
            {
                await WaitIfPausedAsync(ct);
                var progress = (float)i / steps;
                var scale = startScale + (endScale - startScale) * progress;
                var angle = rotation * progress;

                var centerX = _parentCanvas.Width / 2f;
                var centerY = _parentCanvas.Height / 2f;
                var offsetX = (int)(centerX - centerX * scale);
                var offsetY = (int)(centerY - centerY * scale);

                _parentCanvas.Clear(SKColors.Black);

                // Use correct overload: DrawBitmap(bitmap, x, y, rotateDegrees, scale, fitToCanvas)
                _parentCanvas.DrawBitmap(newBitmap, offsetX, offsetY, angle, scale);
                await Task.Delay(TransitionSpeed, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // If cancelled, still draw final image
        }
        finally
        {
            // Always ensure image is displayed at end
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
        }
    }

    private async Task WipeTransition(SKBitmap newBitmap, WipeDirection wipeDir, CancellationToken ct)
    {
        var steps = 40;

        for (var i = 0; i <= steps; i++)
        {
            await WaitIfPausedAsync(ct);
            var progress = (float)i / steps;

            // Draw new image
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            // Draw black wipe overlay
            switch (wipeDir)
            {
                case WipeDirection.Left:
                    var widthLeft = (int)(_parentCanvas.Width * (1 - progress));
                    _parentCanvas.DrawRect(0, 0, widthLeft, _parentCanvas.Height, SKColors.Black, SKPaintStyle.Fill);
                    break;

                case WipeDirection.Right:
                    var xRight = (int)(_parentCanvas.Width * progress);
                    _parentCanvas.DrawRect(xRight, 0, _parentCanvas.Width - xRight, _parentCanvas.Height,
                        SKColors.Black, SKPaintStyle.Fill);
                    break;

                case WipeDirection.Up:
                    var heightUp = (int)(_parentCanvas.Height * (1 - progress));
                    _parentCanvas.DrawRect(0, 0, _parentCanvas.Width, heightUp, SKColors.Black, SKPaintStyle.Fill);
                    break;

                case WipeDirection.Down:
                    var yDown = (int)(_parentCanvas.Height * progress);
                    _parentCanvas.DrawRect(0, yDown, _parentCanvas.Width, _parentCanvas.Height - yDown, SKColors.Black,
                        SKPaintStyle.Fill);
                    break;

                case WipeDirection.Center:
                    var widthCenter = (int)(_parentCanvas.Width * (1 - progress) / 2);
                    var heightCenter = (int)(_parentCanvas.Height * (1 - progress) / 2);
                    _parentCanvas.DrawRect(0, 0, widthCenter, _parentCanvas.Height, SKColors.Black, SKPaintStyle.Fill);
                    _parentCanvas.DrawRect(_parentCanvas.Width - widthCenter, 0, widthCenter, _parentCanvas.Height,
                        SKColors.Black, SKPaintStyle.Fill);
                    _parentCanvas.DrawRect(widthCenter, 0, _parentCanvas.Width - 2 * widthCenter, heightCenter,
                        SKColors.Black, SKPaintStyle.Fill);
                    _parentCanvas.DrawRect(widthCenter, _parentCanvas.Height - heightCenter,
                        _parentCanvas.Width - 2 * widthCenter, heightCenter, SKColors.Black, SKPaintStyle.Fill);
                    break;

                case WipeDirection.Edges:
                    var widthEdges = (int)(_parentCanvas.Width * progress / 2);
                    var heightEdges = (int)(_parentCanvas.Height * progress / 2);
                    _parentCanvas.DrawRect(0, 0, widthEdges, _parentCanvas.Height, SKColors.Black, SKPaintStyle.Fill);
                    _parentCanvas.DrawRect(_parentCanvas.Width - widthEdges, 0, widthEdges, _parentCanvas.Height,
                        SKColors.Black, SKPaintStyle.Fill);
                    _parentCanvas.DrawRect(widthEdges, 0, _parentCanvas.Width - 2 * widthEdges, heightEdges,
                        SKColors.Black, SKPaintStyle.Fill);
                    _parentCanvas.DrawRect(widthEdges, _parentCanvas.Height - heightEdges,
                        _parentCanvas.Width - 2 * widthEdges, heightEdges, SKColors.Black, SKPaintStyle.Fill);
                    break;
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task PushTransition(SKBitmap newBitmap, PushDirection pushDir, CancellationToken ct)
    {
        // Store current image (would need to capture before transition in real implementation)
        // For simplicity, we'll just slide the new image in
        var steps = _parentCanvas.Width / 2;

        switch (pushDir)
        {
            case PushDirection.Left:
                for (var x = _parentCanvas.Width; x >= 0; x -= 2)
                {
                    await WaitIfPausedAsync(ct);
                    _parentCanvas.DrawBitmap(newBitmap, x, 0, fitToCanvas: true);
                    await Task.Delay(TransitionSpeed, ct);
                }

                break;

            case PushDirection.Right:
                for (var x = -_parentCanvas.Width; x <= 0; x += 2)
                {
                    await WaitIfPausedAsync(ct);
                    _parentCanvas.DrawBitmap(newBitmap, x, 0, fitToCanvas: true);
                    await Task.Delay(TransitionSpeed, ct);
                }

                break;

            case PushDirection.Up:
                for (var y = _parentCanvas.Height; y >= 0; y -= 2)
                {
                    await WaitIfPausedAsync(ct);
                    _parentCanvas.DrawBitmap(newBitmap, 0, y, fitToCanvas: true);
                    await Task.Delay(TransitionSpeed, ct);
                }

                break;

            case PushDirection.Down:
                for (var y = -_parentCanvas.Height; y <= 0; y += 2)
                {
                    await WaitIfPausedAsync(ct);
                    _parentCanvas.DrawBitmap(newBitmap, 0, y, fitToCanvas: true);
                    await Task.Delay(TransitionSpeed, ct);
                }

                break;
        }
    }

    private async Task RevealTransition(SKBitmap newBitmap, RevealDirection revealDir, CancellationToken ct)
    {
        // Draw new image first
        _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

        var steps = 40;
        for (var i = steps; i >= 0; i--)
        {
            await WaitIfPausedAsync(ct);
            var progress = 1.0f - (float)i / steps;

            // Draw shrinking black overlay
            switch (revealDir)
            {
                case RevealDirection.Left:
                    var widthLeft = (int)(_parentCanvas.Width * (1 - progress));
                    _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
                    _parentCanvas.DrawRect(0, 0, widthLeft, _parentCanvas.Height, SKColors.Black, SKPaintStyle.Fill);
                    break;

                case RevealDirection.Right:
                    var xRight = (int)(_parentCanvas.Width * progress);
                    _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
                    _parentCanvas.DrawRect(xRight, 0, _parentCanvas.Width - xRight, _parentCanvas.Height,
                        SKColors.Black, SKPaintStyle.Fill);
                    break;

                case RevealDirection.Up:
                    var heightUp = (int)(_parentCanvas.Height * (1 - progress));
                    _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
                    _parentCanvas.DrawRect(0, 0, _parentCanvas.Width, heightUp, SKColors.Black, SKPaintStyle.Fill);
                    break;

                case RevealDirection.Down:
                    var yDown = (int)(_parentCanvas.Height * progress);
                    _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
                    _parentCanvas.DrawRect(0, yDown, _parentCanvas.Width, _parentCanvas.Height - yDown, SKColors.Black,
                        SKPaintStyle.Fill);
                    break;
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task VenetianBlindsTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        var blindCount = 8;
        var blindHeight = _parentCanvas.Height / blindCount;
        var steps = 20;

        for (var i = 0; i <= steps; i++)
        {
            await WaitIfPausedAsync(ct);
            var revealHeight = (int)(blindHeight * i / (float)steps);

            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            // Draw black bars
            for (var blind = 0; blind < blindCount; blind++)
            {
                var y = blind * blindHeight;
                var heightToHide = blindHeight - revealHeight;
                _parentCanvas.DrawRect(0, y, _parentCanvas.Width, heightToHide, SKColors.Black, SKPaintStyle.Fill);
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task CheckerBoardTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        var checkSize = 16;
        var checksX = (_parentCanvas.Width + checkSize - 1) / checkSize;
        var checksY = (_parentCanvas.Height + checkSize - 1) / checkSize;
        var totalChecks = checksX * checksY;
        var rng = new Random();

        // Create list of all check positions
        var checks = new List<(int x, int y)>();
        for (var y = 0; y < checksY; y++)
        for (var x = 0; x < checksX; x++)
            checks.Add((x, y));

        // Shuffle the checks
        checks = checks.OrderBy(c => rng.Next()).ToList();

        // Draw new image
        _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

        // Reveal in random order
        var checksPerFrame = Math.Max(1, totalChecks / 30);
        for (var i = 0; i < checks.Count; i += checksPerFrame)
        {
            await WaitIfPausedAsync(ct);

            // Black out already-revealed checks
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            for (var j = i + checksPerFrame; j < checks.Count; j++)
            {
                var (x, y) = checks[j];
                _parentCanvas.DrawRect(x * checkSize, y * checkSize, checkSize, checkSize, SKColors.Black,
                    SKPaintStyle.Fill);
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task DissolveTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        var steps = 30;
        var rng = new Random();
        var pixelsPerStep = _parentCanvas.Width * _parentCanvas.Height / steps;

        // Draw new image
        _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

        // Create random pixel positions
        var pixels = new List<(int x, int y)>();
        for (var y = 0; y < _parentCanvas.Height; y += 2)
        for (var x = 0; x < _parentCanvas.Width; x += 2)
            pixels.Add((x, y));

        pixels = pixels.OrderBy(p => rng.Next()).ToList();

        for (var step = steps; step >= 0; step--)
        {
            await WaitIfPausedAsync(ct);
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            // Draw black pixels
            var pixelsToBlack = step * pixels.Count / steps;
            for (var i = 0; i < pixelsToBlack; i++)
            {
                var (x, y) = pixels[i];
                _parentCanvas.SetPixel(x, y, SKColors.Black);
                _parentCanvas.SetPixel(x + 1, y, SKColors.Black);
                _parentCanvas.SetPixel(x, y + 1, SKColors.Black);
                _parentCanvas.SetPixel(x + 1, y + 1, SKColors.Black);
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task PixelateTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        try
        {
            var maxBlockSize = 32;
            var steps = 15;

            for (var step = steps; step >= 0; step--)
            {
                await WaitIfPausedAsync(ct);
                var blockSize = Math.Max(1, maxBlockSize * step / steps);

                if (blockSize == 1)
                {
                    _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
                }
                else
                {
                    _parentCanvas.Clear(SKColors.Black);

                    for (var y = 0; y < _parentCanvas.Height; y += blockSize)
                    for (var x = 0; x < _parentCanvas.Width; x += blockSize)
                    {
                        // Sample color from new bitmap
                        var sampleX = Math.Min(x, newBitmap.Width - 1);
                        var sampleY = Math.Min(y, newBitmap.Height - 1);
                        var color = newBitmap.GetPixel(sampleX, sampleY);

                        var width = Math.Min(blockSize, _parentCanvas.Width - x);
                        var height = Math.Min(blockSize, _parentCanvas.Height - y);
                        _parentCanvas.DrawRect(x, y, width, height, color, SKPaintStyle.Fill);
                    }
                }

                await Task.Delay(TransitionSpeed * 2, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // If cancelled, still draw final image
        }
        finally
        {
            // Always ensure image is displayed at end
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
        }
    }

    private async Task SpiralTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        var centerX = _parentCanvas.Width / 2f;
        var centerY = _parentCanvas.Height / 2f;
        var maxRadius = Math.Sqrt(centerX * centerX + centerY * centerY);
        var steps = 50;

        _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

        for (var step = steps; step >= 0; step--)
        {
            await WaitIfPausedAsync(ct);
            var radius = (float)(maxRadius * step / steps);

            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            // Draw spiral mask (simplified - draw expanding circle)
            if (step > 0) _parentCanvas.DrawCircle(centerX, centerY, radius, SKColors.Black);

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task CircleTransition(SKBitmap newBitmap, bool expand, CancellationToken ct)
    {
        try
        {
            var centerX = _parentCanvas.Width / 2f;
            var centerY = _parentCanvas.Height / 2f;
            var maxRadius = (float)Math.Sqrt(centerX * centerX + centerY * centerY);
            var steps = 30;

            for (var step = 0; step <= steps; step++)
            {
                await WaitIfPausedAsync(ct);
                var progress = (float)step / steps;
                var radius = expand ? maxRadius * progress : maxRadius * (1 - progress);

                _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

                if (expand)
                {
                    // Circle expands, black outside
                    _parentCanvas.DrawCircle(centerX, centerY, radius, SKColors.Black);
                }
                else
                {
                    // Circle contracts, black inside - draw full black then draw image in circle
                    _parentCanvas.Clear(SKColors.Black);
                    // Drawing image only in circle would require clipping - skip for simplicity
                    // Just do a simple circular wipe instead
                    for (var r = maxRadius; r >= radius; r -= 2)
                        _parentCanvas.DrawCircle(centerX, centerY, r, SKColors.Black);
                }

                await Task.Delay(TransitionSpeed, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // If cancelled, still draw final image
        }
        finally
        {
            // Always ensure image is displayed at end
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
        }
    }

    private async Task DiamondTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        var centerX = _parentCanvas.Width / 2;
        var centerY = _parentCanvas.Height / 2;
        var maxDist = centerX + centerY;
        var steps = 30;

        for (var step = 0; step <= steps; step++)
        {
            await WaitIfPausedAsync(ct);
            var progress = (float)step / steps;
            var dist = (int)(maxDist * progress);

            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            // Draw diamond mask (black outside diamond)
            for (var y = 0; y < _parentCanvas.Height; y++)
            for (var x = 0; x < _parentCanvas.Width; x += 4) // Sample every 4 pixels for performance
            {
                var manhattanDist = Math.Abs(x - centerX) + Math.Abs(y - centerY);
                if (manhattanDist > dist) _parentCanvas.DrawRect(x, y, 4, 1, SKColors.Black, SKPaintStyle.Fill);
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    private async Task RotateTransition(SKBitmap newBitmap, float startAngle, float endAngle, float startScale,
        float endScale, CancellationToken ct)
    {
        try
        {
            var steps = 30;

            for (var i = 0; i <= steps; i++)
            {
                await WaitIfPausedAsync(ct);
                var progress = (float)i / steps;
                var angle = startAngle + (endAngle - startAngle) * progress;
                var scale = startScale + (endScale - startScale) * progress;

                var centerX = _parentCanvas.Width / 2f;
                var centerY = _parentCanvas.Height / 2f;
                var offsetX = (int)(centerX - centerX * scale);
                var offsetY = (int)(centerY - centerY * scale);

                _parentCanvas.Clear(SKColors.Black);

                // Use correct overload: DrawBitmap(bitmap, x, y, rotateDegrees, scale, fitToCanvas)
                _parentCanvas.DrawBitmap(newBitmap, offsetX, offsetY, angle, scale);
                await Task.Delay(TransitionSpeed, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // If cancelled, still draw final image
        }
        finally
        {
            // Always ensure image is displayed at end
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
        }
    }

    private async Task Flip3DTransition(SKBitmap newBitmap, CancellationToken ct)
    {
        try
        {
            var steps = 20;

            // Simulate 3D flip by scaling horizontally
            for (var i = 0; i <= steps; i++)
            {
                await WaitIfPausedAsync(ct);
                var progress = (float)i / steps;

                float scaleX;
                if (progress < 0.5f)
                    // First half - scale down to 0
                    scaleX = 1.0f - progress * 2;
                else
                    // Second half - scale up from 0
                    scaleX = (progress - 0.5f) * 2;

                var width = (int)(_parentCanvas.Width * scaleX);
                var offsetX = (_parentCanvas.Width - width) / 2;

                _parentCanvas.Clear(SKColors.Black);

                if (width > 0)
                    // Draw scaled image
                    _parentCanvas.DrawBitmap(newBitmap, offsetX, 0, width, _parentCanvas.Height);

                await Task.Delay(TransitionSpeed, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // If cancelled, still draw final image
        }
        finally
        {
            // Always ensure image is displayed at end
            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);
        }
    }

    private async Task SplitTransition(SKBitmap newBitmap, bool vertical, CancellationToken ct)
    {
        var steps = 30;

        for (var i = 0; i <= steps; i++)
        {
            await WaitIfPausedAsync(ct);
            var progress = (float)i / steps;

            _parentCanvas.DrawBitmap(newBitmap, 0, 0, fitToCanvas: true);

            if (vertical)
            {
                var halfWidth = _parentCanvas.Width / 2;
                var blackWidth = (int)(halfWidth * (1 - progress));

                // Black bars moving from center to edges
                _parentCanvas.DrawRect(halfWidth - blackWidth, 0, blackWidth, _parentCanvas.Height, SKColors.Black,
                    SKPaintStyle.Fill);
                _parentCanvas.DrawRect(halfWidth, 0, blackWidth, _parentCanvas.Height, SKColors.Black,
                    SKPaintStyle.Fill);
            }
            else
            {
                var halfHeight = _parentCanvas.Height / 2;
                var blackHeight = (int)(halfHeight * (1 - progress));

                // Black bars moving from center to edges
                _parentCanvas.DrawRect(0, halfHeight - blackHeight, _parentCanvas.Width, blackHeight, SKColors.Black,
                    SKPaintStyle.Fill);
                _parentCanvas.DrawRect(0, halfHeight, _parentCanvas.Width, blackHeight, SKColors.Black,
                    SKPaintStyle.Fill);
            }

            await Task.Delay(TransitionSpeed, ct);
        }
    }

    ~SlideShowPlayerExtension()
    {
        Dispose();
    }

    // ===== TRANSITION HELPER ENUMS =====
    private enum WipeDirection
    {
        Left,
        Right,
        Up,
        Down,
        Center,
        Edges
    }

    private enum PushDirection
    {
        Left,
        Right,
        Up,
        Down
    }

    private enum RevealDirection
    {
        Left,
        Right,
        Up,
        Down
    }
}