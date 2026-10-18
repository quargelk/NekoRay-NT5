namespace NekoRayNT5.UI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.menuMain = new System.Windows.Forms.MenuStrip();
            this.panelTop = new System.Windows.Forms.Panel();
            this.checkSystemProxy = new System.Windows.Forms.CheckBox();
            this.textSearch = new System.Windows.Forms.TextBox();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.tabGroups = new System.Windows.Forms.TabControl();
            this.tabGroupDefault = new System.Windows.Forms.TabPage();
            this.listProfiles = new System.Windows.Forms.ListView();
            this.colType = new System.Windows.Forms.ColumnHeader();
            this.colAddress = new System.Windows.Forms.ColumnHeader();
            this.colName = new System.Windows.Forms.ColumnHeader();
            this.colTest = new System.Windows.Forms.ColumnHeader();
            this.colTraffic = new System.Windows.Forms.ColumnHeader();
            this.tabBottom = new System.Windows.Forms.TabControl();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.textLog = new System.Windows.Forms.TextBox();
            this.tabConnections = new System.Windows.Forms.TabPage();
            this.listConnections = new System.Windows.Forms.ListView();
            this.colConnStatus = new System.Windows.Forms.ColumnHeader();
            this.colConnOutbound = new System.Windows.Forms.ColumnHeader();
            this.colConnDestination = new System.Windows.Forms.ColumnHeader();
            this.statusMain = new System.Windows.Forms.StatusStrip();
            this.labelRunning = new System.Windows.Forms.ToolStripStatusLabel();
            this.labelInbound = new System.Windows.Forms.ToolStripStatusLabel();
            this.labelSpeed = new System.Windows.Forms.ToolStripStatusLabel();
            this.panelTop.SuspendLayout();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.tabGroups.SuspendLayout();
            this.tabGroupDefault.SuspendLayout();
            this.tabBottom.SuspendLayout();
            this.tabLog.SuspendLayout();
            this.tabConnections.SuspendLayout();
            this.statusMain.SuspendLayout();
            this.SuspendLayout();
            //
            // menuMain
            //
            this.menuMain.Location = new System.Drawing.Point(0, 0);
            this.menuMain.Name = "menuMain";
            this.menuMain.Size = new System.Drawing.Size(784, 24);
            this.menuMain.TabIndex = 0;
            //
            // panelTop
            //
            this.panelTop.Controls.Add(this.textSearch);
            this.panelTop.Controls.Add(this.checkSystemProxy);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 24);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(784, 30);
            this.panelTop.TabIndex = 1;
            //
            // checkSystemProxy
            //
            this.checkSystemProxy.AutoCheck = false;
            this.checkSystemProxy.AutoSize = true;
            this.checkSystemProxy.Location = new System.Drawing.Point(8, 7);
            this.checkSystemProxy.Name = "checkSystemProxy";
            this.checkSystemProxy.Size = new System.Drawing.Size(92, 17);
            this.checkSystemProxy.TabIndex = 0;
            this.checkSystemProxy.Text = "System Proxy";
            this.checkSystemProxy.UseVisualStyleBackColor = true;
            //
            // textSearch
            //
            this.textSearch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.textSearch.Location = new System.Drawing.Point(576, 5);
            this.textSearch.Name = "textSearch";
            this.textSearch.Size = new System.Drawing.Size(200, 20);
            this.textSearch.TabIndex = 2;
            //
            // splitMain
            //
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(0, 54);
            this.splitMain.Name = "splitMain";
            this.splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            //
            // splitMain.Panel1
            //
            this.splitMain.Panel1.Controls.Add(this.tabGroups);
            //
            // splitMain.Panel2
            //
            this.splitMain.Panel2.Controls.Add(this.tabBottom);
            this.splitMain.Size = new System.Drawing.Size(784, 465);
            this.splitMain.SplitterDistance = 290;
            this.splitMain.TabIndex = 2;
            //
            // tabGroups
            //
            this.tabGroups.Controls.Add(this.tabGroupDefault);
            this.tabGroups.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabGroups.Location = new System.Drawing.Point(0, 0);
            this.tabGroups.Name = "tabGroups";
            this.tabGroups.SelectedIndex = 0;
            this.tabGroups.Size = new System.Drawing.Size(784, 290);
            this.tabGroups.TabIndex = 0;
            //
            // tabGroupDefault
            //
            this.tabGroupDefault.Controls.Add(this.listProfiles);
            this.tabGroupDefault.Location = new System.Drawing.Point(4, 22);
            this.tabGroupDefault.Name = "tabGroupDefault";
            this.tabGroupDefault.Size = new System.Drawing.Size(776, 264);
            this.tabGroupDefault.TabIndex = 0;
            this.tabGroupDefault.Text = "Default";
            this.tabGroupDefault.UseVisualStyleBackColor = true;
            //
            // listProfiles
            //
            this.listProfiles.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colType,
            this.colAddress,
            this.colName,
            this.colTest,
            this.colTraffic});
            this.listProfiles.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listProfiles.FullRowSelect = true;
            this.listProfiles.HideSelection = false;
            this.listProfiles.Location = new System.Drawing.Point(0, 0);
            this.listProfiles.Name = "listProfiles";
            this.listProfiles.Size = new System.Drawing.Size(776, 264);
            this.listProfiles.TabIndex = 0;
            this.listProfiles.UseCompatibleStateImageBehavior = false;
            this.listProfiles.View = System.Windows.Forms.View.Details;
            //
            // colType
            //
            this.colType.Text = "Type";
            this.colType.Width = 90;
            //
            // colAddress
            //
            this.colAddress.Text = "Address";
            this.colAddress.Width = 180;
            //
            // colName
            //
            this.colName.Text = "Name";
            this.colName.Width = 220;
            //
            // colTest
            //
            this.colTest.Text = "Test Result";
            this.colTest.Width = 110;
            //
            // colTraffic
            //
            this.colTraffic.Text = "Traffic";
            this.colTraffic.Width = 140;
            //
            // tabBottom
            //
            this.tabBottom.Controls.Add(this.tabLog);
            this.tabBottom.Controls.Add(this.tabConnections);
            this.tabBottom.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabBottom.Location = new System.Drawing.Point(0, 0);
            this.tabBottom.Name = "tabBottom";
            this.tabBottom.SelectedIndex = 0;
            this.tabBottom.Size = new System.Drawing.Size(784, 171);
            this.tabBottom.TabIndex = 0;
            //
            // tabLog
            //
            this.tabLog.Controls.Add(this.textLog);
            this.tabLog.Location = new System.Drawing.Point(4, 22);
            this.tabLog.Name = "tabLog";
            this.tabLog.Size = new System.Drawing.Size(776, 145);
            this.tabLog.TabIndex = 0;
            this.tabLog.Text = "Log";
            this.tabLog.UseVisualStyleBackColor = true;
            //
            // textLog
            //
            this.textLog.BackColor = System.Drawing.SystemColors.Window;
            this.textLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textLog.Font = new System.Drawing.Font("Courier New", 8.25F);
            this.textLog.Location = new System.Drawing.Point(0, 0);
            this.textLog.Multiline = true;
            this.textLog.Name = "textLog";
            this.textLog.ReadOnly = true;
            this.textLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textLog.Size = new System.Drawing.Size(776, 145);
            this.textLog.TabIndex = 0;
            this.textLog.WordWrap = false;
            //
            // tabConnections
            //
            this.tabConnections.Controls.Add(this.listConnections);
            this.tabConnections.Location = new System.Drawing.Point(4, 22);
            this.tabConnections.Name = "tabConnections";
            this.tabConnections.Size = new System.Drawing.Size(776, 145);
            this.tabConnections.TabIndex = 1;
            this.tabConnections.Text = "Connection";
            this.tabConnections.UseVisualStyleBackColor = true;
            //
            // listConnections
            //
            this.listConnections.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colConnStatus,
            this.colConnOutbound,
            this.colConnDestination});
            this.listConnections.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listConnections.FullRowSelect = true;
            this.listConnections.Location = new System.Drawing.Point(0, 0);
            this.listConnections.Name = "listConnections";
            this.listConnections.Size = new System.Drawing.Size(776, 145);
            this.listConnections.TabIndex = 0;
            this.listConnections.UseCompatibleStateImageBehavior = false;
            this.listConnections.View = System.Windows.Forms.View.Details;
            //
            // colConnStatus
            //
            this.colConnStatus.Text = "Status";
            this.colConnStatus.Width = 100;
            //
            // colConnOutbound
            //
            this.colConnOutbound.Text = "Outbound";
            this.colConnOutbound.Width = 200;
            //
            // colConnDestination
            //
            this.colConnDestination.Text = "Destination";
            this.colConnDestination.Width = 400;
            //
            // statusMain
            //
            this.statusMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.labelRunning,
            this.labelInbound,
            this.labelSpeed});
            this.statusMain.Location = new System.Drawing.Point(0, 519);
            this.statusMain.Name = "statusMain";
            this.statusMain.Size = new System.Drawing.Size(784, 22);
            this.statusMain.TabIndex = 3;
            //
            // labelRunning
            //
            this.labelRunning.Name = "labelRunning";
            this.labelRunning.Size = new System.Drawing.Size(500, 17);
            this.labelRunning.Spring = true;
            this.labelRunning.Text = "Not Running";
            this.labelRunning.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // labelInbound
            //
            this.labelInbound.Name = "labelInbound";
            this.labelInbound.Size = new System.Drawing.Size(0, 17);
            //
            // labelSpeed
            //
            this.labelSpeed.Name = "labelSpeed";
            this.labelSpeed.Size = new System.Drawing.Size(0, 17);
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 541);
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.panelTop);
            this.Controls.Add(this.statusMain);
            this.Controls.Add(this.menuMain);
            this.MainMenuStrip = this.menuMain;
            this.MinimumSize = new System.Drawing.Size(480, 360);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "NekoRay NT5";
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            this.splitMain.ResumeLayout(false);
            this.tabGroups.ResumeLayout(false);
            this.tabGroupDefault.ResumeLayout(false);
            this.tabBottom.ResumeLayout(false);
            this.tabLog.ResumeLayout(false);
            this.tabLog.PerformLayout();
            this.tabConnections.ResumeLayout(false);
            this.statusMain.ResumeLayout(false);
            this.statusMain.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.MenuStrip menuMain;
        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.CheckBox checkSystemProxy;
        private System.Windows.Forms.TextBox textSearch;
        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.TabControl tabGroups;
        private System.Windows.Forms.TabPage tabGroupDefault;
        private System.Windows.Forms.ListView listProfiles;
        private System.Windows.Forms.ColumnHeader colType;
        private System.Windows.Forms.ColumnHeader colAddress;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colTest;
        private System.Windows.Forms.ColumnHeader colTraffic;
        private System.Windows.Forms.TabControl tabBottom;
        private System.Windows.Forms.TabPage tabLog;
        private System.Windows.Forms.TextBox textLog;
        private System.Windows.Forms.TabPage tabConnections;
        private System.Windows.Forms.ListView listConnections;
        private System.Windows.Forms.ColumnHeader colConnStatus;
        private System.Windows.Forms.ColumnHeader colConnOutbound;
        private System.Windows.Forms.ColumnHeader colConnDestination;
        private System.Windows.Forms.StatusStrip statusMain;
        private System.Windows.Forms.ToolStripStatusLabel labelRunning;
        private System.Windows.Forms.ToolStripStatusLabel labelInbound;
        private System.Windows.Forms.ToolStripStatusLabel labelSpeed;
    }
}
