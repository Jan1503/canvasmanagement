using System.Diagnostics;
using System.Reflection;
using CanvasManagement.Interfaces;

namespace CanvasManagement.WinForms.Demo;

/// <summary>
///     Control panel for dynamically loading, managing, and controlling canvas extensions.
///     Each canvas can only have ONE extension attached at a time.
///     Extensions persist on their canvas even when switching views.
/// </summary>
public partial class ExtensionControlPanel : Form
{
    // Track extensions per canvas - extensions persist when switching canvases
    private static readonly Dictionary<Canvas, DynamicExtension> _extensionsByCanvas = new();
    private Canvas _canvas;
    private bool _isInitialized;
    private bool _isUpdating;

    public ExtensionControlPanel(Canvas canvas)
    {
        _canvas = canvas;
        InitializeComponent();
        Load += ExtensionControlPanel_Load;
    }

    /// <summary>
    ///     Gets the extension loaded on the current canvas (if any)
    /// </summary>
    private DynamicExtension? CurrentExtension =>
        _extensionsByCanvas.TryGetValue(_canvas, out var ext) ? ext : null;

    /// <summary>
    ///     Change the target canvas for this control panel.
    ///     Extensions on other canvases continue running.
    /// </summary>
    public void SetCanvas(Canvas canvas)
    {
        if (_canvas == canvas) return;

        _canvas = canvas;
        UpdateTitle();

        // Show the extension for this canvas (if any), or clear the panels
        _ = PopulateActiveExtensionAsync();

        if (CurrentExtension != null)
            LoadExtensionControls();
        else
            ClearControlPanels();
    }

    private void UpdateTitle()
    {
        Text = $"Extension Control Panel - {_canvas.Name}";
    }

    private void ClearControlPanels()
    {
        parameterPanel.Controls.Clear();
        methodPanel.Controls.Clear();
        UpdateControlButtonStates();
    }

    private async void ExtensionControlPanel_Load(object? sender, EventArgs e)
    {
        if (_isInitialized) return;

        await Task.Yield();

        try
        {
            UpdateTitle();
            Canvas.ExtensionDiscovery.LoadAssembliesFromCommonLocations();
            await PopulateAvailableExtensionsAsync();
            await PopulateActiveExtensionAsync();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading extensions: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PopulateAvailableExtensionsAsync()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            var extensionsByCategory = await Task.Run(() => Canvas.ExtensionDiscovery.GetByCategory());

            availableTreeView.Nodes.Clear();

            foreach (var category in extensionsByCategory.OrderBy(c => c.Key))
            {
                var categoryNode = new TreeNode(category.Key)
                {
                    Tag = "category",
                    NodeFont = new Font(availableTreeView.Font, FontStyle.Bold)
                };

                foreach (var ext in category.Value.OrderBy(e => e.DisplayName))
                {
                    var extNode = new TreeNode(ext.DisplayName)
                    {
                        Tag = ext,
                        ToolTipText = ext.Description
                    };
                    categoryNode.Nodes.Add(extNode);
                }

                availableTreeView.Nodes.Add(categoryNode);
                categoryNode.Expand();
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private async Task PopulateActiveExtensionAsync()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            activeListBox.Items.Clear();

            var ext = CurrentExtension;
            if (ext != null)
            {
                var status = ext.IsRunning ? "▶ Running" : "⏹ Stopped";
                activeListBox.Items.Add($"{ext.Name} ({status})");
                activeListBox.SelectedIndex = 0;
            }

            // Update the "Load" button state
            buttonLoadExtension.Enabled = ext == null && availableTreeView.SelectedNode?.Tag is ExtensionTypeInfo;

            UpdateControlButtonStates();
        }
        finally
        {
            _isUpdating = false;
        }

        await Task.CompletedTask;
    }

    private void availableTreeView_AfterSelect(object sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is ExtensionTypeInfo info)
        {
            ShowExtensionInfo(info);
            // Only enable load if no extension is currently loaded on this canvas
            buttonLoadExtension.Enabled = CurrentExtension == null;
        }
        else
        {
            extensionInfoLabel.Text = "Select an extension to view details";
            extensionInfoLabel.ForeColor = SystemColors.GrayText;
            buttonLoadExtension.Enabled = false;
        }
    }

    private void ShowExtensionInfo(ExtensionTypeInfo info)
    {
        extensionInfoLabel.ForeColor = SystemColors.ControlText;
        extensionInfoLabel.Text = $"{info.DisplayName}\n{info.Description}";
    }

    private async void buttonLoadExtension_Click(object sender, EventArgs e)
    {
        if (availableTreeView.SelectedNode?.Tag is not ExtensionTypeInfo info) return;

        var currentExt = CurrentExtension;

        // Check if an extension is already loaded on this canvas
        if (currentExt != null)
        {
            var result = MessageBox.Show(
                $"Canvas '{_canvas.Name}' already has an extension loaded: {currentExt.Name}\n\n" +
                "Each canvas can only have ONE extension. Do you want to replace the current extension?",
                "Extension Already Loaded",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            // Stop and unload current extension
            try
            {
                if (currentExt.IsRunning) await Task.Run(() => currentExt.Stop());
                currentExt.Dispose();
                _extensionsByCanvas.Remove(_canvas);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error unloading current extension: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        try
        {
            buttonLoadExtension.Enabled = false;
            buttonLoadExtension.Text = "Loading...";

            var extension = await Task.Run(() => _canvas.CreateDynamicExtension(info.Name));

            if (extension == null)
            {
                MessageBox.Show($"Failed to create extension: {info.Name}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _extensionsByCanvas[_canvas] = extension;
            await PopulateActiveExtensionAsync();

            // Show the controls for the new extension
            LoadExtensionControls();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading extension: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            buttonLoadExtension.Enabled = CurrentExtension == null;
            buttonLoadExtension.Text = "Load Selected";
        }
    }

    private void activeListBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (_isUpdating || activeListBox.SelectedIndex < 0) return;

        // We only have one extension per canvas, so just load its controls
        if (CurrentExtension != null) LoadExtensionControls();
    }

    private void LoadExtensionControls()
    {
        if (CurrentExtension == null) return;

        LoadParameterControls();
        LoadMethodControls();
        UpdateControlButtonStates();
    }

    private void LoadParameterControls()
    {
        parameterPanel.SuspendLayout();
        parameterPanel.Controls.Clear();

        var ext = CurrentExtension;
        if (ext == null)
        {
            parameterPanel.ResumeLayout(true);
            return;
        }

        var yPos = 8;
        var controlWidth = parameterPanel.ClientSize.Width - 24;

        var type = ext.Type;
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var paramAttr = prop.GetCustomAttributes(typeof(ExtensionParameterAttribute), false)
                .FirstOrDefault() as ExtensionParameterAttribute;

            if (paramAttr == null) continue;

            var displayName = paramAttr.DisplayName ?? prop.Name;

            try
            {
                var currentValue = prop.GetValue(ext.Instance);

                if (prop.PropertyType == typeof(float))
                    AddFloatSlider(displayName, paramAttr, prop, (float)(currentValue ?? 0f), controlWidth, ref yPos);
                else if (prop.PropertyType == typeof(int))
                    AddIntSlider(displayName, paramAttr, prop, (int)(currentValue ?? 0), controlWidth, ref yPos);
                else if (prop.PropertyType == typeof(bool))
                    AddBoolCheckbox(displayName, prop, (bool)(currentValue ?? false), controlWidth, ref yPos);
                else if (prop.PropertyType == typeof(string))
                    AddStringTextbox(displayName, prop, currentValue?.ToString() ?? "", controlWidth, ref yPos);
                else if (prop.PropertyType.IsEnum)
                    AddEnumCombobox(displayName, prop, currentValue, controlWidth, ref yPos);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading parameter {prop.Name}: {ex.Message}");
            }
        }

        parameterPanel.ResumeLayout(true);
    }

    private void AddFloatSlider(string name, ExtensionParameterAttribute attr, PropertyInfo prop, float value,
        int controlWidth, ref int yPos)
    {
        var min = attr.MinValue != null ? Convert.ToSingle(attr.MinValue) : 0f;
        var max = attr.MaxValue != null ? Convert.ToSingle(attr.MaxValue) : 1f;

        var label = new Label
        {
            Text = $"{name}: {value:F2}",
            Location = new Point(8, yPos),
            Width = controlWidth
        };
        parameterPanel.Controls.Add(label);
        yPos += 18;

        var trackBar = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp((int)((value - min) / (max - min) * 100), 0, 100),
            Location = new Point(4, yPos),
            Width = controlWidth,
            Height = 32,
            TickFrequency = 10,
            AutoSize = false
        };

        trackBar.Scroll += (s, e) =>
        {
            var newValue = min + trackBar.Value / 100f * (max - min);
            prop.SetValue(CurrentExtension?.Instance, newValue);
            label.Text = $"{name}: {newValue:F2}";
        };

        parameterPanel.Controls.Add(trackBar);
        yPos += 40;
    }

    private void AddIntSlider(string name, ExtensionParameterAttribute attr, PropertyInfo prop, int value,
        int controlWidth, ref int yPos)
    {
        var min = attr.MinValue != null ? Convert.ToInt32(attr.MinValue) : 0;
        var max = attr.MaxValue != null ? Convert.ToInt32(attr.MaxValue) : 100;

        var label = new Label
        {
            Text = $"{name}: {value}",
            Location = new Point(8, yPos),
            Width = controlWidth
        };
        parameterPanel.Controls.Add(label);
        yPos += 18;

        var trackBar = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Location = new Point(4, yPos),
            Width = controlWidth,
            Height = 32,
            TickFrequency = Math.Max(1, (max - min) / 10),
            AutoSize = false
        };

        trackBar.Scroll += (s, e) =>
        {
            prop.SetValue(CurrentExtension?.Instance, trackBar.Value);
            label.Text = $"{name}: {trackBar.Value}";
        };

        parameterPanel.Controls.Add(trackBar);
        yPos += 40;
    }

    private void AddBoolCheckbox(string name, PropertyInfo prop, bool value, int controlWidth, ref int yPos)
    {
        var checkbox = new CheckBox
        {
            Text = name,
            Checked = value,
            Location = new Point(8, yPos),
            Width = controlWidth
        };

        checkbox.CheckedChanged += (s, e) => prop.SetValue(CurrentExtension?.Instance, checkbox.Checked);
        parameterPanel.Controls.Add(checkbox);
        yPos += 26;
    }

    private void AddStringTextbox(string name, PropertyInfo prop, string value, int controlWidth, ref int yPos)
    {
        var label = new Label
        {
            Text = name + ":",
            Location = new Point(8, yPos),
            Width = controlWidth
        };
        parameterPanel.Controls.Add(label);
        yPos += 18;

        var textBox = new TextBox
        {
            Text = value,
            Location = new Point(8, yPos),
            Width = controlWidth
        };

        textBox.TextChanged += (s, e) => prop.SetValue(CurrentExtension?.Instance, textBox.Text);
        parameterPanel.Controls.Add(textBox);
        yPos += 28;
    }

    private void AddEnumCombobox(string name, PropertyInfo prop, object? value, int controlWidth, ref int yPos)
    {
        var label = new Label
        {
            Text = name + ":",
            Location = new Point(8, yPos),
            Width = controlWidth
        };
        parameterPanel.Controls.Add(label);
        yPos += 18;

        var comboBox = new ComboBox
        {
            Location = new Point(8, yPos),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        var enumValues = Enum.GetValues(prop.PropertyType);
        foreach (var enumValue in enumValues) comboBox.Items.Add(enumValue);

        if (value != null) comboBox.SelectedItem = value;

        comboBox.SelectedIndexChanged += (s, e) =>
        {
            if (comboBox.SelectedItem != null)
                prop.SetValue(CurrentExtension?.Instance, comboBox.SelectedItem);
        };

        parameterPanel.Controls.Add(comboBox);
        yPos += 28;
    }

    private void LoadMethodControls()
    {
        methodPanel.SuspendLayout();
        methodPanel.Controls.Clear();

        var ext = CurrentExtension;
        if (ext == null)
        {
            methodPanel.ResumeLayout(true);
            return;
        }

        var yPos = 8;
        var controlWidth = methodPanel.ClientSize.Width - 24;

        var methods = ext.GetAvailableMethods().ToList();

        if (methods.Count == 0)
        {
            var noMethodsLabel = new Label
            {
                Text = "No callable methods available",
                Location = new Point(8, yPos),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };
            methodPanel.Controls.Add(noMethodsLabel);
        }
        else
        {
            // Group by category
            var grouped = methods.GroupBy(m => m.Category ?? "General").OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                // Category header
                var categoryLabel = new Label
                {
                    Text = group.Key,
                    Location = new Point(8, yPos),
                    Font = new Font(Font, FontStyle.Bold),
                    AutoSize = true
                };
                methodPanel.Controls.Add(categoryLabel);
                yPos += 20;

                // Method buttons in a flow layout
                var xPos = 8;
                foreach (var method in group)
                {
                    var buttonWidth = Math.Min(TextRenderer.MeasureText(method.DisplayName, Font).Width + 20,
                        controlWidth / 2 - 4);

                    if (xPos + buttonWidth > controlWidth + 8)
                    {
                        xPos = 8;
                        yPos += 30;
                    }

                    var button = new Button
                    {
                        Text = method.DisplayName,
                        Location = new Point(xPos, yPos),
                        Width = buttonWidth,
                        Height = 26,
                        FlatStyle = FlatStyle.System
                    };

                    if (!string.IsNullOrEmpty(method.Description))
                    {
                        var tooltip = new ToolTip();
                        tooltip.SetToolTip(button, method.Description);
                    }

                    if (method.IsDangerous) button.ForeColor = Color.Red;

                    var methodCopy = method; // Capture for lambda
                    button.Click += async (s, e) => await InvokeMethodAsync(methodCopy, button);

                    methodPanel.Controls.Add(button);
                    xPos += buttonWidth + 4;
                }

                yPos += 34;
            }
        }

        methodPanel.ResumeLayout(true);
    }

    private async Task InvokeMethodAsync(ExtensionMethodInfo method, Button button)
    {
        try
        {
            button.Enabled = false;

            var ext = CurrentExtension;
            if (method.Parameters.Count == 0)
            {
                var result = ext?.InvokeMethod(method.Name);

                if (method.ReturnsValue && result != null)
                    MessageBox.Show($"Result: {result}", method.DisplayName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var parameters = ShowParameterInputDialog(method);
                if (parameters != null)
                {
                    var result = ext?.InvokeMethod(method.Name, parameters);

                    if (method.ReturnsValue && result != null)
                        MessageBox.Show($"Result: {result}", method.DisplayName,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            await PopulateActiveExtensionAsync();
            UpdateControlButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error invoking {method.DisplayName}: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private object?[]? ShowParameterInputDialog(ExtensionMethodInfo method)
    {
        using var dialog = new Form
        {
            Text = method.DisplayName,
            Size = new Size(350, 120 + method.Parameters.Count * 50),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var controls = new List<Control>();
        var yPos = 20;

        foreach (var param in method.Parameters)
        {
            var label = new Label
            {
                Text = $"{param.Name} ({param.ParameterType.Name}):",
                Location = new Point(20, yPos),
                AutoSize = true
            };
            dialog.Controls.Add(label);
            yPos += 20;

            var textBox = new TextBox
            {
                Location = new Point(20, yPos),
                Width = 290,
                Text = param.DefaultValue?.ToString() ?? ""
            };
            dialog.Controls.Add(textBox);
            controls.Add(textBox);
            yPos += 30;
        }

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(120, yPos + 10),
            Width = 80
        };
        dialog.Controls.Add(okButton);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(210, yPos + 10),
            Width = 80
        };
        dialog.Controls.Add(cancelButton);

        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog() == DialogResult.OK)
            try
            {
                var result = new object?[method.Parameters.Count];
                for (var i = 0; i < method.Parameters.Count; i++)
                {
                    var param = method.Parameters[i];
                    var textBox = (TextBox)controls[i];
                    result[i] = Convert.ChangeType(textBox.Text, param.ParameterType);
                }

                return result;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid parameter value: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        return null;
    }

    private void UpdateControlButtonStates()
    {
        var ext = CurrentExtension;
        buttonStart.Enabled = ext != null && !ext.IsRunning;
        buttonStop.Enabled = ext != null && ext.IsRunning;
        buttonUnload.Enabled = ext != null;

        // Update load button: can only load if no extension on this canvas and something is selected
        buttonLoadExtension.Enabled = ext == null && availableTreeView.SelectedNode?.Tag is ExtensionTypeInfo;
    }

    private async void buttonStart_Click(object sender, EventArgs e)
    {
        var ext = CurrentExtension;
        if (ext == null) return;

        try
        {
            await Task.Run(() => ext.Start());
            await PopulateActiveExtensionAsync();
            UpdateControlButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error starting extension: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void buttonStop_Click(object sender, EventArgs e)
    {
        var ext = CurrentExtension;
        if (ext == null) return;

        try
        {
            await Task.Run(() => ext.Stop());
            await PopulateActiveExtensionAsync();
            UpdateControlButtonStates();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error stopping extension: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void buttonUnload_Click(object sender, EventArgs e)
    {
        var ext = CurrentExtension;
        if (ext == null) return;

        try
        {
            if (ext.IsRunning) await Task.Run(() => ext.Stop());

            ext.Dispose();
            _extensionsByCanvas.Remove(_canvas);

            ClearControlPanels();
            await PopulateActiveExtensionAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error unloading extension: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void buttonRefresh_Click(object sender, EventArgs e)
    {
        Canvas.ExtensionDiscovery.LoadAssembliesFromCommonLocations();
        _ = PopulateAvailableExtensionsAsync();
        UpdateControlButtonStates();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    ///     Clean up the extension for a specific canvas. Call this before removing a canvas.
    /// </summary>
    public static void CleanupExtensionForCanvas(Canvas canvas)
    {
        if (_extensionsByCanvas.TryGetValue(canvas, out var ext))
        {
            try
            {
                if (ext.IsRunning) ext.Stop();
                ext.Dispose();
            }
            catch
            {
            }

            _extensionsByCanvas.Remove(canvas);
        }
    }

    /// <summary>
    ///     Clean up all extensions on all canvases. Call this when the application is closing.
    /// </summary>
    public static void CleanupAllExtensions()
    {
        foreach (var kvp in _extensionsByCanvas.ToList())
            try
            {
                if (kvp.Value.IsRunning) kvp.Value.Stop();
                kvp.Value.Dispose();
            }
            catch
            {
            }

        _extensionsByCanvas.Clear();
    }
}