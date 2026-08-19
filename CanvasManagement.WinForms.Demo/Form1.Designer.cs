namespace CanvasManagement.WinForms.Demo
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            skglControl1 = new SkiaSharp.Views.Desktop.SKGLControl();
            
            // Top toolbar buttons
            buttonStart = new Button();
            buttonFilterPanel = new Button();
            buttonExtensionPanel = new Button();
            buttonStop = new Button();
            
            // Scale controls
            scaleTrackBar = new TrackBar();
            scaleLabel = new Label();
            
            // Left panel - Canvas management
            panelCanvasControls = new Panel();
            labelCanvases = new Label();
            canvasListBox = new ListBox();
            buttonAddCanvas = new Button();
            buttonRemoveCanvas = new Button();
            labelCanvasProperties = new Label();
            labelOpacity = new Label();
            opacityTrackBar = new TrackBar();
            labelBrightness = new Label();
            brightnessTrackBar = new TrackBar();
            labelZOrder = new Label();
            buttonBringToFront = new Button();
            buttonSendToBack = new Button();
            checkBoxHidden = new CheckBox();
            
            // Right panel - Quick stats
            panelStats = new Panel();
            labelStats = new Label();
            labelStatsContent = new Label();
            
            // Global controls
            labelGlobalBrightness = new Label();
            globalBrightnessTrackBar = new TrackBar();
            
            ((System.ComponentModel.ISupportInitialize)scaleTrackBar).BeginInit();
            ((System.ComponentModel.ISupportInitialize)opacityTrackBar).BeginInit();
            ((System.ComponentModel.ISupportInitialize)brightnessTrackBar).BeginInit();
            ((System.ComponentModel.ISupportInitialize)globalBrightnessTrackBar).BeginInit();
            panelCanvasControls.SuspendLayout();
            panelStats.SuspendLayout();
            SuspendLayout();
            
            // 
            // buttonStart
            // 
            buttonStart.Location = new Point(12, 12);
            buttonStart.Name = "buttonStart";
            buttonStart.Size = new Size(90, 30);
            buttonStart.TabIndex = 0;
            buttonStart.Text = "▶ Start";
            buttonStart.UseVisualStyleBackColor = true;
            buttonStart.Click += button1_Click;
            
            // 
            // buttonStop
            // 
            buttonStop.Location = new Point(108, 12);
            buttonStop.Name = "buttonStop";
            buttonStop.Size = new Size(90, 30);
            buttonStop.TabIndex = 1;
            buttonStop.Text = "⏹ Stop";
            buttonStop.UseVisualStyleBackColor = true;
            buttonStop.Enabled = false;
            buttonStop.Click += buttonStop_Click;
            
            // 
            // buttonFilterPanel
            // 
            buttonFilterPanel.Location = new Point(220, 12);
            buttonFilterPanel.Name = "buttonFilterPanel";
            buttonFilterPanel.Size = new Size(110, 30);
            buttonFilterPanel.TabIndex = 2;
            buttonFilterPanel.Text = "🎨 Filters";
            buttonFilterPanel.UseVisualStyleBackColor = true;
            buttonFilterPanel.Click += buttonFilterPanel_Click;
            
            // 
            // buttonExtensionPanel
            // 
            buttonExtensionPanel.Location = new Point(336, 12);
            buttonExtensionPanel.Name = "buttonExtensionPanel";
            buttonExtensionPanel.Size = new Size(110, 30);
            buttonExtensionPanel.TabIndex = 3;
            buttonExtensionPanel.Text = "🧩 Extensions";
            buttonExtensionPanel.UseVisualStyleBackColor = true;
            buttonExtensionPanel.Click += buttonExtensionPanel_Click;
            
            // 
            // panelCanvasControls - Left side canvas management
            // 
            panelCanvasControls.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            panelCanvasControls.BorderStyle = BorderStyle.FixedSingle;
            panelCanvasControls.Location = new Point(12, 50);
            panelCanvasControls.Name = "panelCanvasControls";
            panelCanvasControls.Size = new Size(200, 480);
            panelCanvasControls.TabIndex = 4;
            panelCanvasControls.Controls.Add(labelCanvases);
            panelCanvasControls.Controls.Add(canvasListBox);
            panelCanvasControls.Controls.Add(buttonAddCanvas);
            panelCanvasControls.Controls.Add(buttonRemoveCanvas);
            panelCanvasControls.Controls.Add(labelCanvasProperties);
            panelCanvasControls.Controls.Add(labelOpacity);
            panelCanvasControls.Controls.Add(opacityTrackBar);
            panelCanvasControls.Controls.Add(labelBrightness);
            panelCanvasControls.Controls.Add(brightnessTrackBar);
            panelCanvasControls.Controls.Add(labelZOrder);
            panelCanvasControls.Controls.Add(buttonBringToFront);
            panelCanvasControls.Controls.Add(buttonSendToBack);
            panelCanvasControls.Controls.Add(checkBoxHidden);
            
            // 
            // labelCanvases
            // 
            labelCanvases.AutoSize = true;
            labelCanvases.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelCanvases.Location = new Point(8, 8);
            labelCanvases.Name = "labelCanvases";
            labelCanvases.Text = "Canvas Layers";
            
            // 
            // canvasListBox
            // 
            canvasListBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            canvasListBox.FormattingEnabled = true;
            canvasListBox.ItemHeight = 15;
            canvasListBox.Location = new Point(8, 28);
            canvasListBox.Name = "canvasListBox";
            canvasListBox.Size = new Size(182, 109);
            canvasListBox.TabIndex = 0;
            canvasListBox.SelectedIndexChanged += canvasListBox_SelectedIndexChanged;
            
            // 
            // buttonAddCanvas
            // 
            buttonAddCanvas.Location = new Point(8, 143);
            buttonAddCanvas.Name = "buttonAddCanvas";
            buttonAddCanvas.Size = new Size(88, 25);
            buttonAddCanvas.TabIndex = 1;
            buttonAddCanvas.Text = "Add Canvas";
            buttonAddCanvas.UseVisualStyleBackColor = true;
            buttonAddCanvas.Click += buttonAddCanvas_Click;
            
            // 
            // buttonRemoveCanvas
            // 
            buttonRemoveCanvas.Location = new Point(102, 143);
            buttonRemoveCanvas.Name = "buttonRemoveCanvas";
            buttonRemoveCanvas.Size = new Size(88, 25);
            buttonRemoveCanvas.TabIndex = 2;
            buttonRemoveCanvas.Text = "Remove";
            buttonRemoveCanvas.UseVisualStyleBackColor = true;
            buttonRemoveCanvas.Click += buttonRemoveCanvas_Click;
            
            // 
            // labelCanvasProperties
            // 
            labelCanvasProperties.AutoSize = true;
            labelCanvasProperties.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelCanvasProperties.Location = new Point(8, 178);
            labelCanvasProperties.Name = "labelCanvasProperties";
            labelCanvasProperties.Text = "Canvas Properties";
            
            // 
            // labelOpacity
            // 
            labelOpacity.AutoSize = true;
            labelOpacity.Location = new Point(8, 200);
            labelOpacity.Name = "labelOpacity";
            labelOpacity.Text = "Opacity: 100%";
            
            // 
            // opacityTrackBar
            // 
            opacityTrackBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            opacityTrackBar.Location = new Point(8, 218);
            opacityTrackBar.Maximum = 100;
            opacityTrackBar.Name = "opacityTrackBar";
            opacityTrackBar.Size = new Size(182, 45);
            opacityTrackBar.TabIndex = 3;
            opacityTrackBar.TickFrequency = 10;
            opacityTrackBar.Value = 100;
            opacityTrackBar.Scroll += opacityTrackBar_Scroll;
            
            // 
            // labelBrightness
            // 
            labelBrightness.AutoSize = true;
            labelBrightness.Location = new Point(8, 260);
            labelBrightness.Name = "labelBrightness";
            labelBrightness.Text = "Brightness: 100%";
            
            // 
            // brightnessTrackBar
            // 
            brightnessTrackBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            brightnessTrackBar.Location = new Point(8, 278);
            brightnessTrackBar.Maximum = 100;
            brightnessTrackBar.Name = "brightnessTrackBar";
            brightnessTrackBar.Size = new Size(182, 45);
            brightnessTrackBar.TabIndex = 4;
            brightnessTrackBar.TickFrequency = 10;
            brightnessTrackBar.Value = 100;
            brightnessTrackBar.Scroll += brightnessTrackBar_Scroll;
            
            // 
            // labelZOrder
            // 
            labelZOrder.AutoSize = true;
            labelZOrder.Location = new Point(8, 320);
            labelZOrder.Name = "labelZOrder";
            labelZOrder.Text = "Z-Order:";
            
            // 
            // buttonBringToFront
            // 
            buttonBringToFront.Location = new Point(8, 340);
            buttonBringToFront.Name = "buttonBringToFront";
            buttonBringToFront.Size = new Size(88, 25);
            buttonBringToFront.TabIndex = 5;
            buttonBringToFront.Text = "↑ To Front";
            buttonBringToFront.UseVisualStyleBackColor = true;
            buttonBringToFront.Click += buttonBringToFront_Click;
            
            // 
            // buttonSendToBack
            // 
            buttonSendToBack.Location = new Point(102, 340);
            buttonSendToBack.Name = "buttonSendToBack";
            buttonSendToBack.Size = new Size(88, 25);
            buttonSendToBack.TabIndex = 6;
            buttonSendToBack.Text = "↓ To Back";
            buttonSendToBack.UseVisualStyleBackColor = true;
            buttonSendToBack.Click += buttonSendToBack_Click;
            
            // 
            // checkBoxHidden
            // 
            checkBoxHidden.AutoSize = true;
            checkBoxHidden.Location = new Point(8, 375);
            checkBoxHidden.Name = "checkBoxHidden";
            checkBoxHidden.Size = new Size(100, 19);
            checkBoxHidden.TabIndex = 7;
            checkBoxHidden.Text = "Hide Canvas";
            checkBoxHidden.UseVisualStyleBackColor = true;
            checkBoxHidden.CheckedChanged += checkBoxHidden_CheckedChanged;
            
            // 
            // panelStats - Right side quick stats
            // 
            panelStats.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            panelStats.BorderStyle = BorderStyle.FixedSingle;
            panelStats.Location = new Point(830, 50);
            panelStats.Name = "panelStats";
            panelStats.Size = new Size(180, 300);
            panelStats.TabIndex = 8;
            panelStats.Controls.Add(labelStats);
            panelStats.Controls.Add(labelStatsContent);
            
            // 
            // labelStats
            // 
            labelStats.AutoSize = true;
            labelStats.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            labelStats.Location = new Point(8, 8);
            labelStats.Name = "labelStats";
            labelStats.Text = "System Info";
            
            // 
            // labelStatsContent
            // 
            labelStatsContent.Location = new Point(8, 30);
            labelStatsContent.Name = "labelStatsContent";
            labelStatsContent.Size = new Size(162, 260);
            labelStatsContent.TabIndex = 1;
            labelStatsContent.Text = "Resolution: 384x192\nCanvases: 0\nFilters: 0\nExtensions: Loaded";
            
            // 
            // labelGlobalBrightness
            // 
            labelGlobalBrightness.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            labelGlobalBrightness.AutoSize = true;
            labelGlobalBrightness.Location = new Point(830, 360);
            labelGlobalBrightness.Name = "labelGlobalBrightness";
            labelGlobalBrightness.Text = "Global Brightness: 100%";
            
            // 
            // globalBrightnessTrackBar
            // 
            globalBrightnessTrackBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            globalBrightnessTrackBar.Location = new Point(830, 380);
            globalBrightnessTrackBar.Maximum = 100;
            globalBrightnessTrackBar.Name = "globalBrightnessTrackBar";
            globalBrightnessTrackBar.Size = new Size(180, 45);
            globalBrightnessTrackBar.TabIndex = 9;
            globalBrightnessTrackBar.TickFrequency = 10;
            globalBrightnessTrackBar.Value = 100;
            globalBrightnessTrackBar.Scroll += globalBrightnessTrackBar_Scroll;
            
            // 
            // scaleLabel
            // 
            scaleLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            scaleLabel.AutoSize = true;
            scaleLabel.Location = new Point(830, 440);
            scaleLabel.Name = "scaleLabel";
            scaleLabel.Size = new Size(68, 15);
            scaleLabel.TabIndex = 10;
            scaleLabel.Text = "Scale: 100%";
            
            // 
            // scaleTrackBar
            // 
            scaleTrackBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            scaleTrackBar.Location = new Point(830, 460);
            scaleTrackBar.Maximum = 40;
            scaleTrackBar.Minimum = 5;
            scaleTrackBar.Name = "scaleTrackBar";
            scaleTrackBar.Size = new Size(180, 45);
            scaleTrackBar.TabIndex = 11;
            scaleTrackBar.TickFrequency = 5;
            scaleTrackBar.Value = 10;
            scaleTrackBar.Scroll += scaleTrackBar_Scroll;
            
            // 
            // skglControl1
            // 
            skglControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            skglControl1.BackColor = Color.Black;
            skglControl1.Location = new Point(220, 50);
            skglControl1.Margin = new Padding(4, 3, 4, 3);
            skglControl1.Name = "skglControl1";
            skglControl1.Size = new Size(600, 480);
            skglControl1.TabIndex = 12;
            skglControl1.VSync = true;
            skglControl1.PaintSurface += skglControl1_PaintSurface;
            
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1024, 540);
            Controls.Add(skglControl1);
            Controls.Add(scaleTrackBar);
            Controls.Add(scaleLabel);
            Controls.Add(globalBrightnessTrackBar);
            Controls.Add(labelGlobalBrightness);
            Controls.Add(panelStats);
            Controls.Add(panelCanvasControls);
            Controls.Add(buttonExtensionPanel);
            Controls.Add(buttonFilterPanel);
            Controls.Add(buttonStop);
            Controls.Add(buttonStart);
            MinimumSize = new Size(900, 500);
            Name = "Form1";
            Text = "Canvas Management Demo - Full Feature Showcase";
            ((System.ComponentModel.ISupportInitialize)scaleTrackBar).EndInit();
            ((System.ComponentModel.ISupportInitialize)opacityTrackBar).EndInit();
            ((System.ComponentModel.ISupportInitialize)brightnessTrackBar).EndInit();
            ((System.ComponentModel.ISupportInitialize)globalBrightnessTrackBar).EndInit();
            panelCanvasControls.ResumeLayout(false);
            panelCanvasControls.PerformLayout();
            panelStats.ResumeLayout(false);
            panelStats.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private SkiaSharp.Views.Desktop.SKGLControl skglControl1;
        private Button buttonStart;
        private Button buttonStop;
        private Button buttonFilterPanel;
        private Button buttonExtensionPanel;
        private TrackBar scaleTrackBar;
        private Label scaleLabel;
        
        // Canvas management controls
        private Panel panelCanvasControls;
        private Label labelCanvases;
        private ListBox canvasListBox;
        private Button buttonAddCanvas;
        private Button buttonRemoveCanvas;
        private Label labelCanvasProperties;
        private Label labelOpacity;
        private TrackBar opacityTrackBar;
        private Label labelBrightness;
        private TrackBar brightnessTrackBar;
        private Label labelZOrder;
        private Button buttonBringToFront;
        private Button buttonSendToBack;
        private CheckBox checkBoxHidden;
        
        // Stats panel
        private Panel panelStats;
        private Label labelStats;
        private Label labelStatsContent;
        
        // Global controls
        private Label labelGlobalBrightness;
        private TrackBar globalBrightnessTrackBar;
    }
}
