namespace ModsDude.Client.WinForms;

partial class MainWindow
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
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
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        profileList = new ListBox();
        profilesTitleLabel = new Label();
        newProfileButton = new Button();
        repoSelector = new ComboBox();
        renameRepoButton = new Button();
        repoLabel = new Label();
        repoMembersButton = new Button();
        repoModsDialogButton = new Button();
        mainLayout = new TableLayoutPanel();
        profilesLayout = new TableLayoutPanel();
        repoLayout = new TableLayoutPanel();
        repoBodyPanel = new Panel();
        deleteRepoButton = new Button();
        profileBodyPanel = new Panel();
        deleteProfileButton = new Button();
        savegameGroupBox = new GroupBox();
        savegameStatusValueLabel = new Label();
        savegameStatusLabel = new Label();
        checkInSavegameButton = new Button();
        checkOutSavegameButton = new Button();
        profileModsButton = new Button();
        activateProfileButton = new Button();
        mainMenuStrip = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        exitToolStripMenuItem = new ToolStripMenuItem();
        outerLayout = new TableLayoutPanel();
        label1 = new Label();
        mainLayout.SuspendLayout();
        profilesLayout.SuspendLayout();
        repoLayout.SuspendLayout();
        repoBodyPanel.SuspendLayout();
        profileBodyPanel.SuspendLayout();
        savegameGroupBox.SuspendLayout();
        mainMenuStrip.SuspendLayout();
        outerLayout.SuspendLayout();
        SuspendLayout();
        // 
        // profileList
        // 
        profileList.BorderStyle = BorderStyle.FixedSingle;
        profilesLayout.SetColumnSpan(profileList, 2);
        profileList.Dock = DockStyle.Fill;
        profileList.FormattingEnabled = true;
        profileList.IntegralHeight = false;
        profileList.Items.AddRange(new object[] { "Profile 1", "Profile with long name" });
        profileList.Location = new Point(4, 44);
        profileList.Margin = new Padding(4);
        profileList.Name = "profileList";
        profileList.Size = new Size(242, 554);
        profileList.TabIndex = 2;
        // 
        // profilesTitleLabel
        // 
        profilesTitleLabel.Dock = DockStyle.Fill;
        profilesTitleLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
        profilesTitleLabel.Location = new Point(4, 4);
        profilesTitleLabel.Margin = new Padding(4);
        profilesTitleLabel.Name = "profilesTitleLabel";
        profilesTitleLabel.Size = new Size(170, 32);
        profilesTitleLabel.TabIndex = 7;
        profilesTitleLabel.Text = "Profiles";
        profilesTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // newProfileButton
        // 
        newProfileButton.Location = new Point(182, 4);
        newProfileButton.Margin = new Padding(4);
        newProfileButton.Name = "newProfileButton";
        newProfileButton.Size = new Size(64, 32);
        newProfileButton.TabIndex = 3;
        newProfileButton.Text = "New...";
        newProfileButton.UseVisualStyleBackColor = true;
        // 
        // repoSelector
        // 
        repoSelector.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        repoSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        repoSelector.FormattingEnabled = true;
        repoSelector.Items.AddRange(new object[] { "The Dudes - Farming Simulator 2025" });
        repoSelector.Location = new Point(65, 4);
        repoSelector.Margin = new Padding(4);
        repoSelector.MaxDropDownItems = 12;
        repoSelector.Name = "repoSelector";
        repoSelector.Size = new Size(281, 29);
        repoSelector.TabIndex = 8;
        // 
        // renameRepoButton
        // 
        renameRepoButton.Location = new Point(62, 84);
        renameRepoButton.Margin = new Padding(4);
        renameRepoButton.Name = "renameRepoButton";
        renameRepoButton.Size = new Size(128, 32);
        renameRepoButton.TabIndex = 9;
        renameRepoButton.Text = "Rename...";
        renameRepoButton.UseVisualStyleBackColor = true;
        // 
        // repoLabel
        // 
        repoLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        repoLabel.AutoSize = true;
        repoLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
        repoLabel.Location = new Point(4, 8);
        repoLabel.Margin = new Padding(4);
        repoLabel.Name = "repoLabel";
        repoLabel.Size = new Size(53, 21);
        repoLabel.TabIndex = 10;
        repoLabel.Text = "Repo:";
        // 
        // repoMembersButton
        // 
        repoMembersButton.Location = new Point(62, 4);
        repoMembersButton.Margin = new Padding(4);
        repoMembersButton.Name = "repoMembersButton";
        repoMembersButton.Size = new Size(128, 32);
        repoMembersButton.TabIndex = 11;
        repoMembersButton.Text = "Members...";
        repoMembersButton.UseVisualStyleBackColor = true;
        // 
        // repoModsDialogButton
        // 
        repoModsDialogButton.Location = new Point(62, 44);
        repoModsDialogButton.Margin = new Padding(4);
        repoModsDialogButton.Name = "repoModsDialogButton";
        repoModsDialogButton.Size = new Size(128, 32);
        repoModsDialogButton.TabIndex = 12;
        repoModsDialogButton.Text = "Mods...";
        repoModsDialogButton.UseVisualStyleBackColor = true;
        // 
        // mainLayout
        // 
        mainLayout.ColumnCount = 5;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(profilesLayout, 2, 0);
        mainLayout.Controls.Add(repoLayout, 0, 0);
        mainLayout.Controls.Add(profileBodyPanel, 4, 0);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Location = new Point(3, 27);
        mainLayout.Name = "mainLayout";
        mainLayout.RowCount = 1;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.Size = new Size(1090, 602);
        mainLayout.TabIndex = 13;
        // 
        // profilesLayout
        // 
        profilesLayout.ColumnCount = 2;
        profilesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        profilesLayout.ColumnStyles.Add(new ColumnStyle());
        profilesLayout.Controls.Add(newProfileButton, 1, 0);
        profilesLayout.Controls.Add(profilesTitleLabel, 0, 0);
        profilesLayout.Controls.Add(profileList, 0, 1);
        profilesLayout.Dock = DockStyle.Fill;
        profilesLayout.Location = new Point(382, 0);
        profilesLayout.Margin = new Padding(0);
        profilesLayout.Name = "profilesLayout";
        profilesLayout.RowCount = 2;
        profilesLayout.RowStyles.Add(new RowStyle());
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        profilesLayout.Size = new Size(250, 602);
        profilesLayout.TabIndex = 0;
        // 
        // repoLayout
        // 
        repoLayout.ColumnCount = 2;
        repoLayout.ColumnStyles.Add(new ColumnStyle());
        repoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        repoLayout.Controls.Add(repoLabel, 0, 0);
        repoLayout.Controls.Add(repoSelector, 1, 0);
        repoLayout.Controls.Add(repoBodyPanel, 0, 1);
        repoLayout.Dock = DockStyle.Fill;
        repoLayout.Location = new Point(0, 0);
        repoLayout.Margin = new Padding(0);
        repoLayout.Name = "repoLayout";
        repoLayout.RowCount = 2;
        repoLayout.RowStyles.Add(new RowStyle());
        repoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        repoLayout.Size = new Size(350, 602);
        repoLayout.TabIndex = 1;
        // 
        // repoBodyPanel
        // 
        repoLayout.SetColumnSpan(repoBodyPanel, 2);
        repoBodyPanel.Controls.Add(deleteRepoButton);
        repoBodyPanel.Controls.Add(repoMembersButton);
        repoBodyPanel.Controls.Add(renameRepoButton);
        repoBodyPanel.Controls.Add(repoModsDialogButton);
        repoBodyPanel.Dock = DockStyle.Fill;
        repoBodyPanel.Location = new Point(3, 40);
        repoBodyPanel.Name = "repoBodyPanel";
        repoBodyPanel.Size = new Size(344, 559);
        repoBodyPanel.TabIndex = 11;
        // 
        // deleteRepoButton
        // 
        deleteRepoButton.Location = new Point(276, 4);
        deleteRepoButton.Margin = new Padding(4);
        deleteRepoButton.Name = "deleteRepoButton";
        deleteRepoButton.Size = new Size(64, 32);
        deleteRepoButton.TabIndex = 13;
        deleteRepoButton.Text = "Delete";
        deleteRepoButton.UseVisualStyleBackColor = true;
        // 
        // profileBodyPanel
        // 
        profileBodyPanel.Controls.Add(label1);
        profileBodyPanel.Controls.Add(deleteProfileButton);
        profileBodyPanel.Controls.Add(savegameGroupBox);
        profileBodyPanel.Controls.Add(profileModsButton);
        profileBodyPanel.Controls.Add(activateProfileButton);
        profileBodyPanel.Dock = DockStyle.Fill;
        profileBodyPanel.Location = new Point(664, 0);
        profileBodyPanel.Margin = new Padding(0);
        profileBodyPanel.Name = "profileBodyPanel";
        profileBodyPanel.Size = new Size(426, 602);
        profileBodyPanel.TabIndex = 2;
        // 
        // deleteProfileButton
        // 
        deleteProfileButton.Location = new Point(352, 44);
        deleteProfileButton.Margin = new Padding(4);
        deleteProfileButton.Name = "deleteProfileButton";
        deleteProfileButton.Size = new Size(64, 32);
        deleteProfileButton.TabIndex = 16;
        deleteProfileButton.Text = "Delete";
        deleteProfileButton.UseVisualStyleBackColor = true;
        // 
        // savegameGroupBox
        // 
        savegameGroupBox.AutoSize = true;
        savegameGroupBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        savegameGroupBox.Controls.Add(savegameStatusValueLabel);
        savegameGroupBox.Controls.Add(savegameStatusLabel);
        savegameGroupBox.Controls.Add(checkInSavegameButton);
        savegameGroupBox.Controls.Add(checkOutSavegameButton);
        savegameGroupBox.Location = new Point(4, 168);
        savegameGroupBox.Name = "savegameGroupBox";
        savegameGroupBox.Size = new Size(278, 111);
        savegameGroupBox.TabIndex = 15;
        savegameGroupBox.TabStop = false;
        savegameGroupBox.Text = "Savegame";
        // 
        // savegameStatusValueLabel
        // 
        savegameStatusValueLabel.AutoSize = true;
        savegameStatusValueLabel.Location = new Point(68, 25);
        savegameStatusValueLabel.Name = "savegameStatusValueLabel";
        savegameStatusValueLabel.Size = new Size(194, 21);
        savegameStatusValueLabel.TabIndex = 17;
        savegameStatusValueLabel.Text = "Checked out (2026-03-11)";
        // 
        // savegameStatusLabel
        // 
        savegameStatusLabel.AutoSize = true;
        savegameStatusLabel.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
        savegameStatusLabel.Location = new Point(7, 25);
        savegameStatusLabel.Name = "savegameStatusLabel";
        savegameStatusLabel.Size = new Size(55, 21);
        savegameStatusLabel.TabIndex = 16;
        savegameStatusLabel.Text = "Status:";
        // 
        // checkInSavegameButton
        // 
        checkInSavegameButton.Location = new Point(7, 50);
        checkInSavegameButton.Margin = new Padding(4);
        checkInSavegameButton.Name = "checkInSavegameButton";
        checkInSavegameButton.Size = new Size(128, 32);
        checkInSavegameButton.TabIndex = 14;
        checkInSavegameButton.Text = "Check in";
        checkInSavegameButton.UseVisualStyleBackColor = true;
        // 
        // checkOutSavegameButton
        // 
        checkOutSavegameButton.Location = new Point(143, 50);
        checkOutSavegameButton.Margin = new Padding(4);
        checkOutSavegameButton.Name = "checkOutSavegameButton";
        checkOutSavegameButton.Size = new Size(128, 32);
        checkOutSavegameButton.TabIndex = 15;
        checkOutSavegameButton.Text = "Check out";
        checkOutSavegameButton.UseVisualStyleBackColor = true;
        // 
        // profileModsButton
        // 
        profileModsButton.Location = new Point(4, 98);
        profileModsButton.Margin = new Padding(4);
        profileModsButton.Name = "profileModsButton";
        profileModsButton.Size = new Size(128, 32);
        profileModsButton.TabIndex = 13;
        profileModsButton.Text = "Mods...";
        profileModsButton.UseVisualStyleBackColor = true;
        // 
        // activateProfileButton
        // 
        activateProfileButton.Location = new Point(4, 44);
        activateProfileButton.Margin = new Padding(4);
        activateProfileButton.Name = "activateProfileButton";
        activateProfileButton.Size = new Size(278, 48);
        activateProfileButton.TabIndex = 13;
        activateProfileButton.Text = "Activate selected profile";
        activateProfileButton.UseVisualStyleBackColor = true;
        // 
        // mainMenuStrip
        // 
        mainMenuStrip.Dock = DockStyle.Fill;
        mainMenuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
        mainMenuStrip.Location = new Point(0, 0);
        mainMenuStrip.Name = "mainMenuStrip";
        mainMenuStrip.Size = new Size(1096, 24);
        mainMenuStrip.TabIndex = 3;
        mainMenuStrip.Text = "menuStrip1";
        // 
        // fileToolStripMenuItem
        // 
        fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { exitToolStripMenuItem });
        fileToolStripMenuItem.Name = "fileToolStripMenuItem";
        fileToolStripMenuItem.Size = new Size(37, 20);
        fileToolStripMenuItem.Text = "&File";
        // 
        // exitToolStripMenuItem
        // 
        exitToolStripMenuItem.Name = "exitToolStripMenuItem";
        exitToolStripMenuItem.Size = new Size(92, 22);
        exitToolStripMenuItem.Text = "E&xit";
        // 
        // outerLayout
        // 
        outerLayout.ColumnCount = 1;
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        outerLayout.Controls.Add(mainMenuStrip, 0, 0);
        outerLayout.Controls.Add(mainLayout, 0, 1);
        outerLayout.Dock = DockStyle.Fill;
        outerLayout.Location = new Point(0, 0);
        outerLayout.Name = "outerLayout";
        outerLayout.RowCount = 2;
        outerLayout.RowStyles.Add(new RowStyle());
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        outerLayout.Size = new Size(1096, 632);
        outerLayout.TabIndex = 14;
        // 
        // label1
        // 
        label1.AutoSize = true;
        label1.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
        label1.Location = new Point(4, 10);
        label1.Margin = new Padding(4);
        label1.Name = "label1";
        label1.Size = new Size(108, 21);
        label1.TabIndex = 17;
        label1.Text = "Profile name";
        label1.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // MainWindow
        // 
        AutoScaleDimensions = new SizeF(9F, 21F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1096, 632);
        Controls.Add(outerLayout);
        Font = new Font("Segoe UI", 12F);
        MainMenuStrip = mainMenuStrip;
        Margin = new Padding(4);
        Name = "MainWindow";
        Text = " ModsDude";
        mainLayout.ResumeLayout(false);
        profilesLayout.ResumeLayout(false);
        repoLayout.ResumeLayout(false);
        repoLayout.PerformLayout();
        repoBodyPanel.ResumeLayout(false);
        profileBodyPanel.ResumeLayout(false);
        profileBodyPanel.PerformLayout();
        savegameGroupBox.ResumeLayout(false);
        savegameGroupBox.PerformLayout();
        mainMenuStrip.ResumeLayout(false);
        mainMenuStrip.PerformLayout();
        outerLayout.ResumeLayout(false);
        outerLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private ListBox repoList;
    private ListBox profileList;
    private Label profilesTitleLabel;
    private Button newProfileButton;
    private ComboBox repoSelector;
    private Button renameRepoButton;
    private Label repoLabel;
    private Button repoMembersButton;
    private Button repoModsDialogButton;
    private TableLayoutPanel mainLayout;
    private TableLayoutPanel profilesLayout;
    private TableLayoutPanel repoLayout;
    private Panel repoBodyPanel;
    private MenuStrip mainMenuStrip;
    private ToolStripMenuItem fileToolStripMenuItem;
    private ToolStripMenuItem exitToolStripMenuItem;
    private TableLayoutPanel outerLayout;
    private Panel profileBodyPanel;
    private GroupBox savegameGroupBox;
    private Label savegameStatusValueLabel;
    private Label savegameStatusLabel;
    private Button checkInSavegameButton;
    private Button checkOutSavegameButton;
    private Button profileModsButton;
    private Button activateProfileButton;
    private Button deleteRepoButton;
    private Button deleteProfileButton;
    private Label label1;
}