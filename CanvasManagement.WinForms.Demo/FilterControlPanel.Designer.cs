namespace CanvasManagement.WinForms.Demo
{
    partial class FilterControlPanel
    {
        private System.ComponentModel.IContainer components = null;
        
        private Panel panelLeft;
        private Panel panelRight;
        private ListBox filterListBox;
        private Panel parameterPanel;
        private Button buttonAddFilter;
        private Button buttonRemoveFilter;
        private Button buttonClearFilters;
        private Button buttonMoveUp;
        private Button buttonMoveDown;
        private Label labelActiveFilters;
        private Label labelParameters;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            panelLeft = new Panel();
            panelRight = new Panel();
            filterListBox = new ListBox();
            parameterPanel = new Panel();
            buttonAddFilter = new Button();
            buttonRemoveFilter = new Button();
            buttonClearFilters = new Button();
            buttonMoveUp = new Button();
            buttonMoveDown = new Button();
            labelActiveFilters = new Label();
            labelParameters = new Label();
            
            panelLeft.SuspendLayout();
            panelRight.SuspendLayout();
            SuspendLayout();
            
            // 
            // panelLeft - Fixed width left panel
            // 
            panelLeft.Dock = DockStyle.Left;
            panelLeft.Width = 220;
            panelLeft.Padding = new Padding(10);
            panelLeft.Controls.Add(buttonClearFilters);
            panelLeft.Controls.Add(buttonMoveDown);
            panelLeft.Controls.Add(buttonMoveUp);
            panelLeft.Controls.Add(buttonRemoveFilter);
            panelLeft.Controls.Add(buttonAddFilter);
            panelLeft.Controls.Add(filterListBox);
            panelLeft.Controls.Add(labelActiveFilters);
            
            // 
            // panelRight - Fill remaining space
            // 
            panelRight.Dock = DockStyle.Fill;
            panelRight.Padding = new Padding(10);
            panelRight.Controls.Add(parameterPanel);
            panelRight.Controls.Add(labelParameters);
            
            // 
            // labelActiveFilters
            // 
            labelActiveFilters.Dock = DockStyle.Top;
            labelActiveFilters.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelActiveFilters.Height = 25;
            labelActiveFilters.Text = "Active Filters";
            labelActiveFilters.TextAlign = ContentAlignment.MiddleLeft;
            
            // 
            // filterListBox
            // 
            filterListBox.Dock = DockStyle.Top;
            filterListBox.Height = 250;
            filterListBox.IntegralHeight = false;
            filterListBox.TabIndex = 0;
            filterListBox.SelectedIndexChanged += filterListBox_SelectedIndexChanged;
            
            // 
            // buttonAddFilter
            // 
            buttonAddFilter.Dock = DockStyle.Top;
            buttonAddFilter.Height = 30;
            buttonAddFilter.Text = "+ Add Filter";
            buttonAddFilter.Margin = new Padding(0, 5, 0, 0);
            buttonAddFilter.Click += buttonAddFilter_Click;
            
            // 
            // buttonRemoveFilter
            // 
            buttonRemoveFilter.Dock = DockStyle.Top;
            buttonRemoveFilter.Height = 30;
            buttonRemoveFilter.Text = "- Remove Selected";
            buttonRemoveFilter.Click += buttonRemoveFilter_Click;
            
            // 
            // buttonMoveUp
            // 
            buttonMoveUp.Dock = DockStyle.Top;
            buttonMoveUp.Height = 30;
            buttonMoveUp.Text = "↑ Move Up";
            buttonMoveUp.Click += buttonMoveUp_Click;
            
            // 
            // buttonMoveDown
            // 
            buttonMoveDown.Dock = DockStyle.Top;
            buttonMoveDown.Height = 30;
            buttonMoveDown.Text = "↓ Move Down";
            buttonMoveDown.Click += buttonMoveDown_Click;
            
            // 
            // buttonClearFilters
            // 
            buttonClearFilters.Dock = DockStyle.Top;
            buttonClearFilters.Height = 30;
            buttonClearFilters.Text = "Clear All";
            buttonClearFilters.Click += buttonClearFilters_Click;
            
            // 
            // labelParameters
            // 
            labelParameters.Dock = DockStyle.Top;
            labelParameters.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelParameters.Height = 25;
            labelParameters.Text = "Filter Parameters";
            labelParameters.TextAlign = ContentAlignment.MiddleLeft;
            
            // 
            // parameterPanel
            // 
            parameterPanel.Dock = DockStyle.Fill;
            parameterPanel.AutoScroll = true;
            parameterPanel.BackColor = SystemColors.Window;
            parameterPanel.BorderStyle = BorderStyle.FixedSingle;
            
            // 
            // FilterControlPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(600, 450);
            Controls.Add(panelRight);
            Controls.Add(panelLeft);
            MinimumSize = new Size(500, 400);
            Name = "FilterControlPanel";
            StartPosition = FormStartPosition.Manual;
            Text = "Filter Control Panel";
            
            panelLeft.ResumeLayout(false);
            panelRight.ResumeLayout(false);
            ResumeLayout(false);
        }
    }
}
