namespace CanvasManagement.WinForms.Demo
{
    partial class ExtensionControlPanel
    {
        private System.ComponentModel.IContainer components = null;
        
        private Panel panelLeft;
        private Panel panelRight;
        private Panel panelAvailable;
        private Panel panelActive;
        
        private TreeView availableTreeView;
        private ListBox activeListBox;
        private Panel parameterPanel;
        private Panel methodPanel;
        
        private Button buttonLoadExtension;
        private Button buttonUnload;
        private Button buttonStart;
        private Button buttonStop;
        private Button buttonRefresh;
        
        private Label labelAvailable;
        private Label labelActive;
        private Label labelParameters;
        private Label labelMethods;
        private Label extensionInfoLabel;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Note: Extensions are tracked in a static dictionary and cleaned up
                // by CleanupAllExtensions() when the main form closes.
                // Individual panel disposal doesn't stop extensions - they persist.
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            panelLeft = new Panel();
            panelRight = new Panel();
            panelAvailable = new Panel();
            panelActive = new Panel();
            
            availableTreeView = new TreeView();
            activeListBox = new ListBox();
            parameterPanel = new Panel();
            methodPanel = new Panel();
            
            buttonLoadExtension = new Button();
            buttonUnload = new Button();
            buttonStart = new Button();
            buttonStop = new Button();
            buttonRefresh = new Button();
            
            labelAvailable = new Label();
            labelActive = new Label();
            labelParameters = new Label();
            labelMethods = new Label();
            extensionInfoLabel = new Label();
            
            panelLeft.SuspendLayout();
            panelRight.SuspendLayout();
            panelAvailable.SuspendLayout();
            panelActive.SuspendLayout();
            SuspendLayout();
            
            // 
            // panelLeft - Fixed width left panel
            // 
            panelLeft.Dock = DockStyle.Left;
            panelLeft.Width = 250;
            panelLeft.Padding = new Padding(10);
            panelLeft.Controls.Add(panelActive);
            panelLeft.Controls.Add(panelAvailable);
            
            // 
            // panelAvailable - Top half of left panel
            // 
            panelAvailable.Dock = DockStyle.Top;
            panelAvailable.Height = 220;
            panelAvailable.Controls.Add(availableTreeView);
            panelAvailable.Controls.Add(buttonRefresh);
            panelAvailable.Controls.Add(buttonLoadExtension);
            panelAvailable.Controls.Add(labelAvailable);
            
            // 
            // panelActive - Bottom half of left panel
            // 
            panelActive.Dock = DockStyle.Fill;
            panelActive.Controls.Add(activeListBox);
            panelActive.Controls.Add(buttonUnload);
            panelActive.Controls.Add(buttonStop);
            panelActive.Controls.Add(buttonStart);
            panelActive.Controls.Add(labelActive);
            
            // 
            // panelRight - Fill remaining space
            // 
            panelRight.Dock = DockStyle.Fill;
            panelRight.Padding = new Padding(10);
            panelRight.Controls.Add(methodPanel);
            panelRight.Controls.Add(labelMethods);
            panelRight.Controls.Add(parameterPanel);
            panelRight.Controls.Add(extensionInfoLabel);
            panelRight.Controls.Add(labelParameters);
            
            // 
            // labelAvailable
            // 
            labelAvailable.Dock = DockStyle.Top;
            labelAvailable.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelAvailable.Height = 22;
            labelAvailable.Text = "Available Extensions";
            labelAvailable.TextAlign = ContentAlignment.MiddleLeft;
            
            // 
            // availableTreeView
            // 
            availableTreeView.Dock = DockStyle.Fill;
            availableTreeView.ShowNodeToolTips = true;
            availableTreeView.AfterSelect += availableTreeView_AfterSelect;
            
            // 
            // buttonLoadExtension
            // 
            buttonLoadExtension.Dock = DockStyle.Top;
            buttonLoadExtension.Height = 28;
            buttonLoadExtension.Text = "Load Selected";
            buttonLoadExtension.Click += buttonLoadExtension_Click;
            
            // 
            // buttonRefresh
            // 
            buttonRefresh.Dock = DockStyle.Top;
            buttonRefresh.Height = 28;
            buttonRefresh.Text = "Refresh List";
            buttonRefresh.Click += buttonRefresh_Click;
            
            // 
            // labelActive
            // 
            labelActive.Dock = DockStyle.Top;
            labelActive.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelActive.Height = 22;
            labelActive.Text = "Active Extensions";
            labelActive.TextAlign = ContentAlignment.MiddleLeft;
            
            // 
            // activeListBox
            // 
            activeListBox.Dock = DockStyle.Fill;
            activeListBox.IntegralHeight = false;
            activeListBox.SelectedIndexChanged += activeListBox_SelectedIndexChanged;
            
            // 
            // buttonStart
            // 
            buttonStart.Dock = DockStyle.Top;
            buttonStart.Height = 28;
            buttonStart.Enabled = false;
            buttonStart.Text = "▶ Start";
            buttonStart.Click += buttonStart_Click;
            
            // 
            // buttonStop
            // 
            buttonStop.Dock = DockStyle.Top;
            buttonStop.Height = 28;
            buttonStop.Enabled = false;
            buttonStop.Text = "⏹ Stop";
            buttonStop.Click += buttonStop_Click;
            
            // 
            // buttonUnload
            // 
            buttonUnload.Dock = DockStyle.Top;
            buttonUnload.Height = 28;
            buttonUnload.Enabled = false;
            buttonUnload.Text = "Unload";
            buttonUnload.Click += buttonUnload_Click;
            
            // 
            // labelParameters
            // 
            labelParameters.Dock = DockStyle.Top;
            labelParameters.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelParameters.Height = 22;
            labelParameters.Text = "Parameters";
            labelParameters.TextAlign = ContentAlignment.MiddleLeft;
            
            // 
            // extensionInfoLabel
            // 
            extensionInfoLabel.Dock = DockStyle.Top;
            extensionInfoLabel.Height = 45;
            extensionInfoLabel.Text = "Select an extension to view details";
            extensionInfoLabel.ForeColor = SystemColors.GrayText;
            extensionInfoLabel.TextAlign = ContentAlignment.TopLeft;
            
            // 
            // parameterPanel
            // 
            parameterPanel.Dock = DockStyle.Top;
            parameterPanel.Height = 200;
            parameterPanel.AutoScroll = true;
            parameterPanel.BackColor = SystemColors.Window;
            parameterPanel.BorderStyle = BorderStyle.FixedSingle;
            
            // 
            // labelMethods
            // 
            labelMethods.Dock = DockStyle.Top;
            labelMethods.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelMethods.Height = 22;
            labelMethods.Text = "Methods";
            labelMethods.TextAlign = ContentAlignment.MiddleLeft;
            
            // 
            // methodPanel
            // 
            methodPanel.Dock = DockStyle.Fill;
            methodPanel.AutoScroll = true;
            methodPanel.BackColor = SystemColors.Window;
            methodPanel.BorderStyle = BorderStyle.FixedSingle;
            
            // 
            // ExtensionControlPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(700, 500);
            Controls.Add(panelRight);
            Controls.Add(panelLeft);
            MinimumSize = new Size(550, 450);
            Name = "ExtensionControlPanel";
            StartPosition = FormStartPosition.Manual;
            Text = "Extension Control Panel";
            
            panelLeft.ResumeLayout(false);
            panelRight.ResumeLayout(false);
            panelAvailable.ResumeLayout(false);
            panelActive.ResumeLayout(false);
            ResumeLayout(false);
        }
    }
}
