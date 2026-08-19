using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using CanvasManagement.Interfaces;

namespace CanvasManagement.WinForms.Demo;

public partial class FilterControlPanel : Form
{
    private readonly CanvasManager _canvasManager;
    private ICanvasFilter? _currentFilter;
    private bool _isInitialized;
    private bool _isUpdating;

    public FilterControlPanel(CanvasManager canvasManager)
    {
        _canvasManager = canvasManager;
        InitializeComponent();
        Load += FilterControlPanel_Load;
    }

    private async void FilterControlPanel_Load(object? sender, EventArgs e)
    {
        if (_isInitialized) return;

        await Task.Yield();

        try
        {
            await PopulateFilterListAsync();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading filters: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PopulateFilterListAsync()
    {
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            var currentSelection = filterListBox.SelectedIndex;

            var filters = await Task.Run(() => _canvasManager.GetFilters());

            filterListBox.Items.Clear();

            foreach (var filter in filters)
                filterListBox.Items.Add($"{filter.Name} ({(filter.Enabled ? "ON" : "OFF")})");

            if (currentSelection >= 0 && currentSelection < filters.Count)
                filterListBox.SelectedIndex = currentSelection;
            else if (filters.Count > 0) filterListBox.SelectedIndex = 0;

            UpdateButtonStates();
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void UpdateButtonStates()
    {
        var hasSelection = filterListBox.SelectedIndex >= 0;
        var count = filterListBox.Items.Count;

        buttonRemoveFilter.Enabled = hasSelection;
        buttonMoveUp.Enabled = hasSelection && filterListBox.SelectedIndex > 0;
        buttonMoveDown.Enabled = hasSelection && filterListBox.SelectedIndex < count - 1;
        buttonClearFilters.Enabled = count > 0;
    }

    private void filterListBox_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (_isUpdating) return;
        if (filterListBox.SelectedIndex < 0) return;

        _ = LoadFilterParametersAsync();
        UpdateButtonStates();
    }

    private async Task LoadFilterParametersAsync()
    {
        try
        {
            var filters = await Task.Run(() => _canvasManager.GetFilters());
            if (filterListBox.SelectedIndex < 0 || filterListBox.SelectedIndex >= filters.Count)
                return;

            _currentFilter = filters[filterListBox.SelectedIndex];
            LoadFilterParameters();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading filter parameters: {ex.Message}");
        }
    }

    private void LoadFilterParameters()
    {
        parameterPanel.SuspendLayout();
        try
        {
            parameterPanel.Controls.Clear();
            if (_currentFilter == null) return;

            var yPos = 8;
            var controlWidth = parameterPanel.ClientSize.Width - 24;

            // Enabled checkbox
            var enabledCheckbox = new CheckBox
            {
                Text = "✓ Filter Enabled",
                Checked = _currentFilter.Enabled,
                Location = new Point(8, yPos),
                Width = controlWidth,
                Font = new Font(Font, FontStyle.Bold)
            };
            enabledCheckbox.CheckedChanged += (s, e) =>
            {
                if (_currentFilter != null)
                {
                    _currentFilter.Enabled = enabledCheckbox.Checked;
                    _ = PopulateFilterListAsync();
                }
            };
            parameterPanel.Controls.Add(enabledCheckbox);
            yPos += 30;

            // Separator
            var separator = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Height = 2,
                Width = controlWidth,
                Location = new Point(8, yPos)
            };
            parameterPanel.Controls.Add(separator);
            yPos += 12;

            // Intensity (common to all filters)
            AddSliderControl("Intensity", 0f, 1f, _currentFilter.Intensity,
                value =>
                {
                    if (_currentFilter != null) _currentFilter.Intensity = value;
                },
                controlWidth, ref yPos);

            // Dynamic parameter discovery
            var filterType = _currentFilter.GetType();
            var properties = filterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (prop.Name == "Name" || prop.Name == "Enabled" || prop.Name == "Intensity")
                    continue;

                var paramAttr = prop.GetCustomAttributes(typeof(FilterParameterAttribute), false)
                    .FirstOrDefault() as FilterParameterAttribute;

                var displayName = paramAttr?.DisplayName ?? FormatPropertyName(prop.Name);
                var description = paramAttr?.Description;

                try
                {
                    if (prop.PropertyType == typeof(float))
                    {
                        var currentValue = (float)(prop.GetValue(_currentFilter) ?? 0f);
                        var min = paramAttr?.MinValue != null ? Convert.ToSingle(paramAttr.MinValue) : 0f;
                        var max = paramAttr?.MaxValue != null ? Convert.ToSingle(paramAttr.MaxValue) : 1f;

                        AddSliderControl(displayName, min, max, currentValue,
                            value => prop.SetValue(_currentFilter, value), controlWidth, ref yPos, description);
                    }
                    else if (prop.PropertyType == typeof(int))
                    {
                        var currentValue = (int)(prop.GetValue(_currentFilter) ?? 0);
                        var min = paramAttr?.MinValue != null ? Convert.ToInt32(paramAttr.MinValue) : 0;
                        var max = paramAttr?.MaxValue != null ? Convert.ToInt32(paramAttr.MaxValue) : 100;

                        AddIntSliderControl(displayName, min, max, currentValue,
                            value => prop.SetValue(_currentFilter, value), controlWidth, ref yPos, description);
                    }
                    else if (prop.PropertyType == typeof(byte))
                    {
                        var currentValue = (byte)(prop.GetValue(_currentFilter) ?? 0);
                        var min = paramAttr?.MinValue != null ? Convert.ToByte(paramAttr.MinValue) : (byte)0;
                        var max = paramAttr?.MaxValue != null ? Convert.ToByte(paramAttr.MaxValue) : (byte)255;

                        AddIntSliderControl(displayName, min, max, currentValue,
                            value => prop.SetValue(_currentFilter, (byte)value), controlWidth, ref yPos, description);
                    }
                    else if (prop.PropertyType == typeof(bool))
                    {
                        var currentValue = (bool)(prop.GetValue(_currentFilter) ?? false);
                        AddBoolControl(displayName, currentValue,
                            value => prop.SetValue(_currentFilter, value), controlWidth, ref yPos, description);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading parameter {prop.Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            parameterPanel.ResumeLayout(true);
        }
    }

    private static string FormatPropertyName(string name)
    {
        return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    }

    private void AddSliderControl(string name, float min, float max, float currentValue,
        Action<float> onValueChanged, int controlWidth, ref int yPos, string? description = null)
    {
        var label = new Label
        {
            Text = $"{name}: {currentValue:F2}",
            Location = new Point(8, yPos),
            Width = controlWidth,
            AutoSize = false
        };
        if (!string.IsNullOrEmpty(description))
        {
            var tooltip = new ToolTip();
            tooltip.SetToolTip(label, description);
        }

        parameterPanel.Controls.Add(label);
        yPos += 18;

        var trackBar = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp((int)((currentValue - min) / (max - min) * 100), 0, 100),
            Location = new Point(4, yPos),
            Width = controlWidth,
            TickFrequency = 10,
            AutoSize = false,
            Height = 32
        };

        trackBar.Scroll += (s, e) =>
        {
            try
            {
                var value = min + trackBar.Value / 100f * (max - min);
                onValueChanged(value);
                label.Text = $"{name}: {value:F2}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Filter update error: {ex.Message}");
            }
        };

        parameterPanel.Controls.Add(trackBar);
        yPos += 40;
    }

    private void AddIntSliderControl(string name, int min, int max, int currentValue,
        Action<int> onValueChanged, int controlWidth, ref int yPos, string? description = null)
    {
        var label = new Label
        {
            Text = $"{name}: {currentValue}",
            Location = new Point(8, yPos),
            Width = controlWidth,
            AutoSize = false
        };
        if (!string.IsNullOrEmpty(description))
        {
            var tooltip = new ToolTip();
            tooltip.SetToolTip(label, description);
        }

        parameterPanel.Controls.Add(label);
        yPos += 18;

        var trackBar = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(currentValue, min, max),
            Location = new Point(4, yPos),
            Width = controlWidth,
            TickFrequency = Math.Max(1, (max - min) / 10),
            AutoSize = false,
            Height = 32
        };

        trackBar.Scroll += (s, e) =>
        {
            try
            {
                onValueChanged(trackBar.Value);
                label.Text = $"{name}: {trackBar.Value}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Filter update error: {ex.Message}");
            }
        };

        parameterPanel.Controls.Add(trackBar);
        yPos += 40;
    }

    private void AddBoolControl(string name, bool currentValue, Action<bool> onValueChanged,
        int controlWidth, ref int yPos, string? description = null)
    {
        var checkbox = new CheckBox
        {
            Text = name,
            Checked = currentValue,
            Location = new Point(8, yPos),
            Width = controlWidth
        };

        if (!string.IsNullOrEmpty(description))
        {
            var tooltip = new ToolTip();
            tooltip.SetToolTip(checkbox, description);
        }

        checkbox.CheckedChanged += (s, e) =>
        {
            try
            {
                onValueChanged(checkbox.Checked);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Filter update error: {ex.Message}");
            }
        };

        parameterPanel.Controls.Add(checkbox);
        yPos += 28;
    }

    private void buttonAddFilter_Click(object sender, EventArgs e)
    {
        var menu = new ContextMenuStrip();

        var availableFilters = CanvasManager.FilterDiscovery.GetByCategory();

        if (availableFilters.Count == 0)
            menu.Items.Add("No filters available");
        else
            foreach (var category in availableFilters.OrderBy(c => c.Key))
            {
                var categoryItem = new ToolStripMenuItem(category.Key);

                foreach (var filterInfo in category.Value.OrderBy(f => f.DisplayName))
                {
                    var filterItem = new ToolStripMenuItem(filterInfo.DisplayName);
                    filterItem.ToolTipText = filterInfo.Description;
                    filterItem.Click += async (s, ev) =>
                    {
                        var filter = CanvasManager.FilterDiscovery.Create(filterInfo.Type.Name);
                        if (filter != null)
                            await AddFilterAsync(filter);
                        else
                            MessageBox.Show($"Failed to create filter: {filterInfo.Name}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };
                    categoryItem.DropDownItems.Add(filterItem);
                }

                menu.Items.Add(categoryItem);
            }

        menu.Show(buttonAddFilter, new Point(0, buttonAddFilter.Height));
    }

    private async Task AddFilterAsync(ICanvasFilter filter)
    {
        try
        {
            await Task.Run(() => _canvasManager.AddFilter(filter));
            await PopulateFilterListAsync();

            if (filterListBox.Items.Count > 0) filterListBox.SelectedIndex = filterListBox.Items.Count - 1;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error adding filter: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void buttonRemoveFilter_Click(object sender, EventArgs e)
    {
        if (filterListBox.SelectedIndex < 0) return;

        try
        {
            var selectedIndex = filterListBox.SelectedIndex;
            var filters = await Task.Run(() => _canvasManager.GetFilters());
            if (selectedIndex >= filters.Count) return;

            var filter = filters[selectedIndex];
            await Task.Run(() => _canvasManager.RemoveFilter(filter));

            var newSelection = Math.Max(0, selectedIndex - 1);
            await PopulateFilterListAsync();

            if (filterListBox.Items.Count > 0)
            {
                filterListBox.SelectedIndex = Math.Min(newSelection, filterListBox.Items.Count - 1);
            }
            else
            {
                parameterPanel.Controls.Clear();
                _currentFilter = null;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error removing filter: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void buttonMoveUp_Click(object sender, EventArgs e)
    {
        if (filterListBox.SelectedIndex <= 0) return;

        var index = filterListBox.SelectedIndex;
        var filters = _canvasManager.GetFilters().ToList();

        if (index < filters.Count)
        {
            // Swap filters
            var temp = filters[index];
            filters[index] = filters[index - 1];
            filters[index - 1] = temp;

            // Rebuild filter list
            _canvasManager.ClearFilters();
            foreach (var f in filters) _canvasManager.AddFilter(f);

            _ = PopulateFilterListAsync();
            filterListBox.SelectedIndex = index - 1;
        }
    }

    private void buttonMoveDown_Click(object sender, EventArgs e)
    {
        if (filterListBox.SelectedIndex < 0 || filterListBox.SelectedIndex >= filterListBox.Items.Count - 1) return;

        var index = filterListBox.SelectedIndex;
        var filters = _canvasManager.GetFilters().ToList();

        if (index < filters.Count - 1)
        {
            // Swap filters
            var temp = filters[index];
            filters[index] = filters[index + 1];
            filters[index + 1] = temp;

            // Rebuild filter list
            _canvasManager.ClearFilters();
            foreach (var f in filters) _canvasManager.AddFilter(f);

            _ = PopulateFilterListAsync();
            filterListBox.SelectedIndex = index + 1;
        }
    }

    private async void buttonClearFilters_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show("Remove all filters?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            await Task.Run(() => _canvasManager.ClearFilters());
            await PopulateFilterListAsync();
            parameterPanel.Controls.Clear();
            _currentFilter = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error clearing filters: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
}