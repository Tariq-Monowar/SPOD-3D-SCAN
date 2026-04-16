namespace Scantool
{
    partial class MainForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            loadingPictureBox = new PictureBox();
            saveInProgressLabel = new Label();
            scanInProgressLabel = new Label();
            pnlLoadingContainer = new Panel();
            ((System.ComponentModel.ISupportInitialize)webView21).BeginInit();
            ((System.ComponentModel.ISupportInitialize)loadingPictureBox).BeginInit();
            pnlLoadingContainer.SuspendLayout();
            SuspendLayout();
            // 
            // webView21
            // 
            webView21.AllowExternalDrop = true;
            webView21.CreationProperties = null;
            webView21.DefaultBackgroundColor = Color.White;
            webView21.Dock = DockStyle.Fill;
            webView21.Location = new Point(0, 0);
            webView21.Margin = new Padding(3, 2, 3, 2);
            webView21.Name = "webView21";
            webView21.Size = new Size(1540, 796);
            webView21.Source = new Uri("about:blank", UriKind.Absolute);
            webView21.TabIndex = 0;
            webView21.ZoomFactor = 1D;
            webView21.SourceChanged += WebView21_SourceChanged;
            // 
            // loadingPictureBox
            // 
            loadingPictureBox.Image = (Image)resources.GetObject("loadingPictureBox.Image");
            loadingPictureBox.Location = new Point(0, 0);
            loadingPictureBox.Margin = new Padding(3, 2, 3, 2);
            loadingPictureBox.Name = "loadingPictureBox";
            loadingPictureBox.Size = new Size(400, 300);
            loadingPictureBox.SizeMode = PictureBoxSizeMode.AutoSize;
            loadingPictureBox.TabIndex = 1;
            loadingPictureBox.TabStop = false;
            loadingPictureBox.WaitOnLoad = true;
            // 
            // saveInProgressLabel
            // 
            saveInProgressLabel.AutoSize = true;
            saveInProgressLabel.BackColor = Color.FromArgb(28, 39, 58);
            saveInProgressLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            saveInProgressLabel.ForeColor = Color.White;
            saveInProgressLabel.Location = new Point(116, 15);
            saveInProgressLabel.Name = "saveInProgressLabel";
            saveInProgressLabel.Size = new Size(164, 20);
            saveInProgressLabel.TabIndex = 6;
            saveInProgressLabel.Text = "Speichervorgang läuft";
            saveInProgressLabel.Visible = false;
            // 
            // scanInProgressLabel
            // 
            scanInProgressLabel.AutoSize = true;
            scanInProgressLabel.BackColor = Color.FromArgb(28, 39, 58);
            scanInProgressLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            scanInProgressLabel.ForeColor = Color.White;
            scanInProgressLabel.Location = new Point(122, 15);
            scanInProgressLabel.Name = "scanInProgressLabel";
            scanInProgressLabel.Size = new Size(150, 21);
            scanInProgressLabel.TabIndex = 7;
            scanInProgressLabel.Text = "Scanvorgang läuft";
            scanInProgressLabel.Visible = false;
            // 
            // pnlLoadingContainer
            // 
            pnlLoadingContainer.Anchor = AnchorStyles.None;
            pnlLoadingContainer.BackColor = Color.Transparent;
            pnlLoadingContainer.Controls.Add(saveInProgressLabel);
            pnlLoadingContainer.Controls.Add(scanInProgressLabel);
            pnlLoadingContainer.Controls.Add(loadingPictureBox);
            pnlLoadingContainer.Location = new Point(558, 248);
            pnlLoadingContainer.Name = "pnlLoadingContainer";
            pnlLoadingContainer.Size = new Size(399, 298);
            pnlLoadingContainer.TabIndex = 8;
            pnlLoadingContainer.Visible = false;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1540, 796);
            Controls.Add(pnlLoadingContainer);
            Controls.Add(webView21);
            Margin = new Padding(3, 2, 3, 2);
            Name = "MainForm";
            Text = "FeetF1rst Scantool";
            WindowState = FormWindowState.Maximized;
            FormClosing += MainForm_FormClosing;
            ((System.ComponentModel.ISupportInitialize)webView21).EndInit();
            ((System.ComponentModel.ISupportInitialize)loadingPictureBox).EndInit();
            pnlLoadingContainer.ResumeLayout(false);
            pnlLoadingContainer.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private PictureBox loadingPictureBox;
        private Label saveInProgressLabel;
        private Label scanInProgressLabel;
        private Panel pnlLoadingContainer;
    }
}
