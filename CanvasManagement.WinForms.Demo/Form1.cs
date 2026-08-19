using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace CanvasManagement.WinForms.Demo;

public partial class Form1 : Form
{
    private const long MinInvalidateIntervalTicks = 166666; // ~60 FPS (16.67ms in ticks)
    private static CanvasManager _canvasManager = null!;
    private readonly object _bitmapLock = new(); // Protect bitmap access
    private readonly List<Canvas> _canvases = new();
    private SKBitmap _displayBitmap; // Our own bitmap copy for display
    private ExtensionControlPanel? _extensionControlPanel;

    // Control panels
    private FilterControlPanel? _filterControlPanel;
    private volatile bool _isClosing; // Flag to prevent access during close
    private bool _isUpdatingCanvasControls;
    private long _lastInvalidateTime;
    private float _scale = 1.0f;

    // Canvas management
    private Canvas? _selectedCanvas;

    // Stats timer
    private Timer? _statsTimer;

    public Form1()
    {
        InitializeComponent();

        // Initialize our own display bitmap (copy, not reference)
        _displayBitmap = new SKBitmap(384, 192, SKColorType.Rgba8888, SKAlphaType.Premul);
        _displayBitmap.Erase(SKColors.Black);

        // Initialize CanvasManager
        _canvasManager = new CanvasManager(384, 192);
        _canvasManager.RenderCompleted += _canvasManager_RenderCompleted;

        // Load extension and filter assemblies
        LoadPluginAssemblies();

        // Initialize scale
        UpdateScale();

        // Setup stats timer
        _statsTimer = new Timer { Interval = 1000 };
        _statsTimer.Tick += (s, e) => UpdateStats();
        _statsTimer.Start();

        // Initial stats update
        UpdateStats();
    }

    private void LoadPluginAssemblies()
    {
        try
        {
            // Load extensions from default locations
            Canvas.ExtensionDiscovery.LoadAssembliesFromCommonLocations();

            // Load filters from default locations
            CanvasManager.FilterDiscovery.LoadAssembliesFromCommonLocations();

            var extensionCount = Canvas.ExtensionDiscovery.GetAvailableTypes().Count();
            var filterCount = CanvasManager.FilterDiscovery.GetAvailableTypes().Count();

            Console.WriteLine($"Loaded {extensionCount} extensions and {filterCount} filters");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading plugin assemblies: {ex.Message}");
        }
    }

    private void _canvasManager_RenderCompleted(object? sender, SKBitmap e)
    {
        if (_isClosing || e == null) return;

        // Copy the bitmap data to our own bitmap to prevent race conditions
        // The source bitmap is owned by CanvasManager and modified in its render loop
        lock (_bitmapLock)
        {
            if (_displayBitmap == null || _displayBitmap.Width != e.Width || _displayBitmap.Height != e.Height)
            {
                _displayBitmap?.Dispose();
                _displayBitmap = new SKBitmap(e.Width, e.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            }

            // Fast memory copy of pixel data
            unsafe
            {
                var srcPixels = e.GetPixels();
                var dstPixels = _displayBitmap.GetPixels();
                if (srcPixels != IntPtr.Zero && dstPixels != IntPtr.Zero)
                {
                    var byteCount = e.Width * e.Height * 4;
                    Buffer.MemoryCopy(srcPixels.ToPointer(), dstPixels.ToPointer(), byteCount, byteCount);
                }
            }
        }

        // Throttle invalidation to prevent excessive UI updates
        var currentTime = DateTime.UtcNow.Ticks;
        if (currentTime - _lastInvalidateTime >= MinInvalidateIntervalTicks)
        {
            _lastInvalidateTime = currentTime;

            if (!_isClosing)
                try
                {
                    skglControl1?.Invalidate();
                }
                catch (ObjectDisposedException)
                {
                    // Control was disposed, ignore
                }
        }
    }

    private void skglControl1_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
    {
        if (_isClosing) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        lock (_bitmapLock)
        {
            if (_displayBitmap == null) return;

            // Calculate centered position
            var controlWidth = skglControl1.Width;
            var controlHeight = skglControl1.Height;
            var scaledWidth = _displayBitmap.Width * _scale;
            var scaledHeight = _displayBitmap.Height * _scale;
            var offsetX = (controlWidth - scaledWidth) / 2;
            var offsetY = (controlHeight - scaledHeight) / 2;

            canvas.Save();
            canvas.Translate(offsetX, offsetY);
            canvas.Scale(_scale, _scale);
            canvas.DrawBitmap(_displayBitmap, new SKPoint(0, 0));
            canvas.Restore();
        }
    }

    #region Stats

    private void UpdateStats()
    {
        var extensionCount = Canvas.ExtensionDiscovery.GetAvailableTypes().Count();
        var filterCount = CanvasManager.FilterDiscovery.GetAvailableTypes().Count();
        var activeFilters = _canvasManager.GetFilterCount();
        var canvasCount = _canvasManager.GetCanvases.Count;

        labelStatsContent.Text = $"Resolution: 384x192\n\n" +
                                 $"Canvases: {canvasCount}\n" +
                                 $"Active Filters: {activeFilters}\n\n" +
                                 $"Available:\n" +
                                 $"  Extensions: {extensionCount}\n" +
                                 $"  Filters: {filterCount}\n\n" +
                                 $"Global Brightness:\n" +
                                 $"  {_canvasManager.Brightness * 100:F0}%";
    }

    #endregion

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Set closing flag first to prevent any new render operations
        _isClosing = true;

        // Stop stats timer
        _statsTimer?.Stop();
        _statsTimer?.Dispose();
        _statsTimer = null;

        try
        {
            // Clean up all extensions first (they're running on canvases)
            ExtensionControlPanel.CleanupAllExtensions();

            // Close child windows
            _filterControlPanel?.Close();
            _extensionControlPanel?.Close();
            _filterControlPanel = null;
            _extensionControlPanel = null;

            // Unsubscribe from events BEFORE stopping
            _canvasManager.RenderCompleted -= _canvasManager_RenderCompleted;

            // Stop render loop and wait for it to complete
            _canvasManager.Stop();

            // Give a small delay for any pending paint operations to complete
            Thread.Sleep(50);

            // Dispose our display bitmap under lock
            lock (_bitmapLock)
            {
                _displayBitmap?.Dispose();
                _displayBitmap = null!;
            }

            // Finally dispose the canvas manager
            _canvasManager.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during form closing: {ex.Message}");
        }

        base.OnFormClosing(e);
    }

    #region Canvas Management

    private void RefreshCanvasList()
    {
        _isUpdatingCanvasControls = true;
        try
        {
            var selectedIndex = canvasListBox.SelectedIndex;
            canvasListBox.Items.Clear();

            var canvasesWithInfo = _canvasManager.GetCanvasesWithInfo().OrderBy(c => c.ZOrder).ToList();
            _canvases.Clear();

            foreach (var (zOrder, name, id, canvas) in canvasesWithInfo)
            {
                _canvases.Add(canvas);
                var status = canvas.IsHidden ? "Hidden" : "Visible";
                canvasListBox.Items.Add($"[Z:{zOrder}] {name} ({status})");
            }

            if (selectedIndex >= 0 && selectedIndex < canvasListBox.Items.Count)
                canvasListBox.SelectedIndex = selectedIndex;
            else if (canvasListBox.Items.Count > 0)
                canvasListBox.SelectedIndex = canvasListBox.Items.Count - 1;
        }
        finally
        {
            _isUpdatingCanvasControls = false;
        }
    }

    private void canvasListBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (_isUpdatingCanvasControls || canvasListBox.SelectedIndex < 0) return;

        if (canvasListBox.SelectedIndex < _canvases.Count)
        {
            _selectedCanvas = _canvases[canvasListBox.SelectedIndex];
            UpdateCanvasPropertyControls();

            // Update extension panel if visible
            if (_extensionControlPanel != null && !_extensionControlPanel.IsDisposed && _extensionControlPanel.Visible)
                _extensionControlPanel.SetCanvas(_selectedCanvas);
        }
    }

    private void UpdateCanvasPropertyControls()
    {
        if (_selectedCanvas == null) return;

        _isUpdatingCanvasControls = true;
        try
        {
            opacityTrackBar.Value = (int)(_selectedCanvas.Opacity * 100);
            labelOpacity.Text = $"Opacity: {opacityTrackBar.Value}%";

            brightnessTrackBar.Value = (int)(_selectedCanvas.Brightness * 100);
            labelBrightness.Text = $"Brightness: {brightnessTrackBar.Value}%";

            checkBoxHidden.Checked = _selectedCanvas.IsHidden;
            labelZOrder.Text = $"Z-Order: {_selectedCanvas.ZOrder}";
        }
        finally
        {
            _isUpdatingCanvasControls = false;
        }
    }

    private void buttonAddCanvas_Click(object sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Add New Canvas",
            Size = new Size(300, 250),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var labelName = new Label { Text = "Name:", Location = new Point(20, 20), AutoSize = true };
        var textName = new TextBox
            { Location = new Point(20, 40), Width = 240, Text = $"Canvas_{_canvases.Count + 1}" };

        var labelZ = new Label { Text = "Z-Order:", Location = new Point(20, 70), AutoSize = true };
        var numericZ = new NumericUpDown
            { Location = new Point(20, 90), Width = 100, Minimum = -100, Maximum = 100, Value = _canvases.Count + 1 };

        var labelSize = new Label
            { Text = "Size (use 0 for full screen):", Location = new Point(20, 120), AutoSize = true };
        var labelW = new Label { Text = "W:", Location = new Point(20, 145), AutoSize = true };
        var numericW = new NumericUpDown { Location = new Point(45, 143), Width = 80, Maximum = 1920, Value = 0 };
        var labelH = new Label { Text = "H:", Location = new Point(140, 145), AutoSize = true };
        var numericH = new NumericUpDown { Location = new Point(165, 143), Width = 80, Maximum = 1080, Value = 0 };

        var buttonOk = new Button
            { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(80, 180), Width = 60 };
        var buttonCancel = new Button
            { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(150, 180), Width = 60 };

        dialog.Controls.AddRange(new Control[]
        {
            labelName, textName, labelZ, numericZ, labelSize, labelW, numericW, labelH, numericH, buttonOk, buttonCancel
        });
        dialog.AcceptButton = buttonOk;
        dialog.CancelButton = buttonCancel;

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var width = (int)numericW.Value == 0 ? 384 : (int)numericW.Value;
            var height = (int)numericH.Value == 0 ? 192 : (int)numericH.Value;

            var canvas = _canvasManager.GetCanvas(0, 0, width, height, (int)numericZ.Value, textName.Text);
            canvas.Clear(SKColors.Transparent);
            RefreshCanvasList();
        }
    }

    private void buttonRemoveCanvas_Click(object sender, EventArgs e)
    {
        if (_selectedCanvas == null) return;

        var result = MessageBox.Show($"Remove canvas '{_selectedCanvas.Name}'?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            // Clean up any extension running on this canvas BEFORE removing it
            ExtensionControlPanel.CleanupExtensionForCanvas(_selectedCanvas);

            // If extension panel is showing this canvas, clear it
            if (_extensionControlPanel != null && !_extensionControlPanel.IsDisposed)
            {
                // Find another canvas to show, or null if none left
                var otherCanvas = _canvases.FirstOrDefault(c => c != _selectedCanvas);
                if (otherCanvas != null) _extensionControlPanel.SetCanvas(otherCanvas);
            }

            _canvasManager.RemoveCanvas(_selectedCanvas);
            _selectedCanvas = null;
            RefreshCanvasList();
        }
    }

    private void opacityTrackBar_Scroll(object sender, EventArgs e)
    {
        if (_isUpdatingCanvasControls || _selectedCanvas == null) return;

        _selectedCanvas.Opacity = opacityTrackBar.Value / 100f;
        labelOpacity.Text = $"Opacity: {opacityTrackBar.Value}%";
    }

    private void brightnessTrackBar_Scroll(object sender, EventArgs e)
    {
        if (_isUpdatingCanvasControls || _selectedCanvas == null) return;

        _selectedCanvas.Brightness = brightnessTrackBar.Value / 100f;
        labelBrightness.Text = $"Brightness: {brightnessTrackBar.Value}%";
    }

    private void buttonBringToFront_Click(object sender, EventArgs e)
    {
        if (_selectedCanvas == null) return;

        _canvasManager.BringToFront(_selectedCanvas);
        RefreshCanvasList();
        UpdateCanvasPropertyControls();
    }

    private void buttonSendToBack_Click(object sender, EventArgs e)
    {
        if (_selectedCanvas == null) return;

        _canvasManager.SendToBack(_selectedCanvas);
        RefreshCanvasList();
        UpdateCanvasPropertyControls();
    }

    private void checkBoxHidden_CheckedChanged(object sender, EventArgs e)
    {
        if (_isUpdatingCanvasControls || _selectedCanvas == null) return;

        if (checkBoxHidden.Checked)
            _selectedCanvas.Hide();
        else
            _selectedCanvas.Show();

        RefreshCanvasList();
    }

    #endregion

    #region Global Controls

    private void globalBrightnessTrackBar_Scroll(object sender, EventArgs e)
    {
        _canvasManager.Brightness = globalBrightnessTrackBar.Value / 100f;
        labelGlobalBrightness.Text = $"Global Brightness: {globalBrightnessTrackBar.Value}%";
    }

    private void scaleTrackBar_Scroll(object sender, EventArgs e)
    {
        UpdateScale();
        skglControl1?.Invalidate();
    }

    private void UpdateScale()
    {
        _scale = scaleTrackBar.Value / 10.0f;
        scaleLabel.Text = $"Scale: {(int)(_scale * 100)}%";
    }

    #endregion

    #region Control Panels

    private void buttonFilterPanel_Click(object sender, EventArgs e)
    {
        try
        {
            if (_filterControlPanel == null || _filterControlPanel.IsDisposed)
            {
                _filterControlPanel = new FilterControlPanel(_canvasManager);
                _filterControlPanel.Location = new Point(Location.X + Width + 10, Location.Y);
            }

            if (_filterControlPanel.Visible)
            {
                _filterControlPanel.BringToFront();
                _filterControlPanel.Focus();
            }
            else
            {
                _filterControlPanel.Show();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening filter panel: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void buttonExtensionPanel_Click(object sender, EventArgs e)
    {
        try
        {
            // Need at least one canvas for extensions
            if (_canvases.Count == 0)
            {
                // Create a default canvas
                var canvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1, "Main");
                RefreshCanvasList();
            }

            var targetCanvas = _selectedCanvas ?? _canvases.FirstOrDefault();
            if (targetCanvas == null)
            {
                MessageBox.Show("Please create a canvas first.", "No Canvas",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_extensionControlPanel == null || _extensionControlPanel.IsDisposed)
            {
                _extensionControlPanel = new ExtensionControlPanel(targetCanvas);
                _extensionControlPanel.Location = new Point(Location.X + Width + 10, Location.Y + 300);
            }
            else
            {
                // Update the panel's target canvas if it's different
                _extensionControlPanel.SetCanvas(targetCanvas);
            }

            if (_extensionControlPanel.Visible)
            {
                _extensionControlPanel.BringToFront();
                _extensionControlPanel.Focus();
            }
            else
            {
                _extensionControlPanel.Show();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening extension panel: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion

    #region Start/Stop

    private async void button1_Click(object sender, EventArgs e)
    {
        try
        {
            buttonStart.Enabled = false;
            buttonStop.Enabled = true;

            // Start the render loop
            _canvasManager.Run();

            // Create a default canvas if none exists
            if (_canvases.Count == 0)
            {
                var mainCanvas = _canvasManager.GetCanvas(0, 0, 384, 192, 1, "Main");
                mainCanvas.Clear(SKColors.Black);
                RefreshCanvasList();
            }

            // Show a welcome message
            await ShowWelcomeDemo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error starting: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            buttonStart.Enabled = true;
            buttonStop.Enabled = false;
        }
    }

    private async Task ShowWelcomeDemo()
    {
        if (_canvases.Count == 0) return;

        var canvas = _canvases[0];

        // Draw welcome text
        canvas.Clear(new SKColor(20, 20, 40));

        // Draw gradient background
        canvas.FillGradient(0, 0, 384, 192, new SKColor(30, 30, 60), new SKColor(10, 10, 30), false);

        // Draw title
        canvas.DrawText("Canvas Management Demo", 10, 30, SKColors.Cyan, 16);
        canvas.DrawText("Full Feature Showcase", 10, 55, SKColors.White);

        // Draw info
        canvas.DrawText("Features:", 10, 85, SKColors.Yellow, 10);
        canvas.DrawText("• Dynamic Extension Loading", 20, 100, SKColors.LightGray, 9);
        canvas.DrawText("• Dynamic Filter System", 20, 115, SKColors.LightGray, 9);
        canvas.DrawText("• Multi-Canvas Layers", 20, 130, SKColors.LightGray, 9);
        canvas.DrawText("• Z-Order Management", 20, 145, SKColors.LightGray, 9);
        canvas.DrawText("• Opacity & Brightness", 20, 160, SKColors.LightGray, 9);

        canvas.DrawText("Use the panels on the right to explore!", 10, 180, SKColors.Lime, 9);

        await Task.Delay(100);
    }

    private void buttonStop_Click(object sender, EventArgs e)
    {
        try
        {
            _canvasManager.Stop();
            buttonStart.Enabled = true;
            buttonStop.Enabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error stopping: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion
}