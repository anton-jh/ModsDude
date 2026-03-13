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
        profilesTitleLabel = new Label();
        newProfileButton = new Button();
        repoSelector = new ComboBox();
        renameRepoButton = new Button();
        reposTitleLabel = new Label();
        repoMembersButton = new Button();
        repoModsDialogButton = new Button();
        mainLayout = new TableLayoutPanel();
        profilesLayout = new TableLayoutPanel();
        savegameGroupBox = new GroupBox();
        savegameStatusValueLabel = new Label();
        savegameStatusLabel = new Label();
        checkInSavegameButton = new Button();
        checkOutSavegameButton = new Button();
        groupBox1 = new GroupBox();
        profileModsButton = new Button();
        activateProfileButton = new Button();
        comboBox1 = new ComboBox();
        repoLayout = new TableLayoutPanel();
        newRepoButton = new Button();
        repoBodyPanel = new Panel();
        deleteRepoButton = new Button();
        mainMenuStrip = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        exitToolStripMenuItem = new ToolStripMenuItem();
        outerLayout = new TableLayoutPanel();
        mainLayout.SuspendLayout();
        profilesLayout.SuspendLayout();
        savegameGroupBox.SuspendLayout();
        groupBox1.SuspendLayout();
        repoLayout.SuspendLayout();
        repoBodyPanel.SuspendLayout();
        mainMenuStrip.SuspendLayout();
        outerLayout.SuspendLayout();
        SuspendLayout();
        // 
        // profilesTitleLabel
        // 
        profilesTitleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        profilesTitleLabel.AutoSize = true;
        profilesTitleLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
        profilesTitleLabel.Location = new Point(4, 9);
        profilesTitleLabel.Margin = new Padding(4);
        profilesTitleLabel.Name = "profilesTitleLabel";
        profilesTitleLabel.Size = new Size(68, 21);
        profilesTitleLabel.TabIndex = 7;
        profilesTitleLabel.Text = "Profiles";
        profilesTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // newProfileButton
        // 
        newProfileButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        newProfileButton.Location = new Point(432, 4);
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
        repoSelector.ItemHeight = 21;
        repoSelector.Location = new Point(68, 8);
        repoSelector.Margin = new Padding(4);
        repoSelector.MaxDropDownItems = 12;
        repoSelector.Name = "repoSelector";
        repoSelector.Size = new Size(356, 29);
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
        // reposTitleLabel
        // 
        reposTitleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        reposTitleLabel.AutoSize = true;
        reposTitleLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
        reposTitleLabel.Location = new Point(4, 9);
        reposTitleLabel.Margin = new Padding(4);
        reposTitleLabel.Name = "reposTitleLabel";
        reposTitleLabel.Size = new Size(56, 21);
        reposTitleLabel.TabIndex = 10;
        reposTitleLabel.Text = "Repos";
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
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        mainLayout.Controls.Add(profilesLayout, 3, 0);
        mainLayout.Controls.Add(repoLayout, 1, 0);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Location = new Point(3, 27);
        mainLayout.Name = "mainLayout";
        mainLayout.RowCount = 1;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.Size = new Size(1143, 633);
        mainLayout.TabIndex = 13;
        // 
        // profilesLayout
        // 
        profilesLayout.ColumnCount = 3;
        profilesLayout.ColumnStyles.Add(new ColumnStyle());
        profilesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        profilesLayout.ColumnStyles.Add(new ColumnStyle());
        profilesLayout.Controls.Add(savegameGroupBox, 1, 3);
        profilesLayout.Controls.Add(newProfileButton, 2, 0);
        profilesLayout.Controls.Add(groupBox1, 1, 2);
        profilesLayout.Controls.Add(profilesTitleLabel, 0, 0);
        profilesLayout.Controls.Add(activateProfileButton, 1, 1);
        profilesLayout.Controls.Add(comboBox1, 1, 0);
        profilesLayout.Dock = DockStyle.Fill;
        profilesLayout.Location = new Point(599, 0);
        profilesLayout.Margin = new Padding(0);
        profilesLayout.Name = "profilesLayout";
        profilesLayout.RowCount = 5;
        profilesLayout.RowStyles.Add(new RowStyle());
        profilesLayout.RowStyles.Add(new RowStyle());
        profilesLayout.RowStyles.Add(new RowStyle());
        profilesLayout.RowStyles.Add(new RowStyle());
        profilesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        profilesLayout.Size = new Size(500, 633);
        profilesLayout.TabIndex = 0;
        // 
        // savegameGroupBox
        // 
        savegameGroupBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        savegameGroupBox.Controls.Add(savegameStatusValueLabel);
        savegameGroupBox.Controls.Add(savegameStatusLabel);
        savegameGroupBox.Controls.Add(checkInSavegameButton);
        savegameGroupBox.Controls.Add(checkOutSavegameButton);
        savegameGroupBox.Dock = DockStyle.Fill;
        savegameGroupBox.Location = new Point(80, 264);
        savegameGroupBox.Margin = new Padding(4, 32, 4, 4);
        savegameGroupBox.Name = "savegameGroupBox";
        savegameGroupBox.Size = new Size(344, 111);
        savegameGroupBox.TabIndex = 15;
        savegameGroupBox.TabStop = false;
        savegameGroupBox.Text = "Savegame";
        // 
        // savegameStatusValueLabel
        // 
        savegameStatusValueLabel.AutoSize = true;
        savegameStatusValueLabel.Location = new Point(68, 25);
        savegameStatusValueLabel.Margin = new Padding(4, 0, 4, 0);
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
        savegameStatusLabel.Margin = new Padding(4, 0, 4, 0);
        savegameStatusLabel.Name = "savegameStatusLabel";
        savegameStatusLabel.Size = new Size(55, 21);
        savegameStatusLabel.TabIndex = 16;
        savegameStatusLabel.Text = "Status:";
        // 
        // checkInSavegameButton
        // 
        checkInSavegameButton.Location = new Point(7, 62);
        checkInSavegameButton.Margin = new Padding(4, 16, 4, 4);
        checkInSavegameButton.Name = "checkInSavegameButton";
        checkInSavegameButton.Size = new Size(128, 32);
        checkInSavegameButton.TabIndex = 14;
        checkInSavegameButton.Text = "Check in";
        checkInSavegameButton.UseVisualStyleBackColor = true;
        // 
        // checkOutSavegameButton
        // 
        checkOutSavegameButton.Location = new Point(143, 62);
        checkOutSavegameButton.Margin = new Padding(4, 16, 4, 4);
        checkOutSavegameButton.Name = "checkOutSavegameButton";
        checkOutSavegameButton.Size = new Size(128, 32);
        checkOutSavegameButton.TabIndex = 15;
        checkOutSavegameButton.Text = "Check out";
        checkOutSavegameButton.UseVisualStyleBackColor = true;
        // 
        // groupBox1
        // 
        groupBox1.Controls.Add(profileModsButton);
        groupBox1.Dock = DockStyle.Fill;
        groupBox1.Location = new Point(80, 128);
        groupBox1.Margin = new Padding(4, 32, 4, 4);
        groupBox1.Name = "groupBox1";
        groupBox1.Size = new Size(344, 100);
        groupBox1.TabIndex = 16;
        groupBox1.TabStop = false;
        groupBox1.Text = "Mods";
        // 
        // profileModsButton
        // 
        profileModsButton.Location = new Point(7, 29);
        profileModsButton.Margin = new Padding(4);
        profileModsButton.Name = "profileModsButton";
        profileModsButton.Size = new Size(128, 32);
        profileModsButton.TabIndex = 13;
        profileModsButton.Text = "Edit...";
        profileModsButton.UseVisualStyleBackColor = true;
        // 
        // activateProfileButton
        // 
        activateProfileButton.Location = new Point(80, 44);
        activateProfileButton.Margin = new Padding(4);
        activateProfileButton.Name = "activateProfileButton";
        activateProfileButton.Size = new Size(336, 48);
        activateProfileButton.TabIndex = 13;
        activateProfileButton.Text = "Activate";
        activateProfileButton.UseVisualStyleBackColor = true;
        // 
        // comboBox1
        // 
        comboBox1.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox1.FormattingEnabled = true;
        comboBox1.ItemHeight = 21;
        comboBox1.Location = new Point(80, 8);
        comboBox1.Margin = new Padding(4);
        comboBox1.Name = "comboBox1";
        comboBox1.Size = new Size(344, 29);
        comboBox1.TabIndex = 8;
        // 
        // repoLayout
        // 
        repoLayout.ColumnCount = 3;
        repoLayout.ColumnStyles.Add(new ColumnStyle());
        repoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        repoLayout.ColumnStyles.Add(new ColumnStyle());
        repoLayout.Controls.Add(reposTitleLabel, 0, 0);
        repoLayout.Controls.Add(repoSelector, 1, 0);
        repoLayout.Controls.Add(newRepoButton, 2, 0);
        repoLayout.Controls.Add(repoBodyPanel, 0, 1);
        repoLayout.Dock = DockStyle.Fill;
        repoLayout.Location = new Point(42, 0);
        repoLayout.Margin = new Padding(0);
        repoLayout.Name = "repoLayout";
        repoLayout.RowCount = 2;
        repoLayout.RowStyles.Add(new RowStyle());
        repoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        repoLayout.Size = new Size(500, 633);
        repoLayout.TabIndex = 1;
        // 
        // newRepoButton
        // 
        newRepoButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        newRepoButton.Location = new Point(432, 4);
        newRepoButton.Margin = new Padding(4);
        newRepoButton.Name = "newRepoButton";
        newRepoButton.Size = new Size(64, 32);
        newRepoButton.TabIndex = 14;
        newRepoButton.Text = "New...";
        newRepoButton.UseVisualStyleBackColor = true;
        // 
        // repoBodyPanel
        // 
        repoLayout.SetColumnSpan(repoBodyPanel, 3);
        repoBodyPanel.Controls.Add(deleteRepoButton);
        repoBodyPanel.Controls.Add(repoMembersButton);
        repoBodyPanel.Controls.Add(renameRepoButton);
        repoBodyPanel.Controls.Add(repoModsDialogButton);
        repoBodyPanel.Dock = DockStyle.Fill;
        repoBodyPanel.Location = new Point(3, 43);
        repoBodyPanel.Name = "repoBodyPanel";
        repoBodyPanel.Size = new Size(494, 587);
        repoBodyPanel.TabIndex = 11;
        // 
        // deleteRepoButton
        // 
        deleteRepoButton.Location = new Point(139, 274);
        deleteRepoButton.Margin = new Padding(4);
        deleteRepoButton.Name = "deleteRepoButton";
        deleteRepoButton.Size = new Size(64, 32);
        deleteRepoButton.TabIndex = 13;
        deleteRepoButton.Text = "Delete";
        deleteRepoButton.UseVisualStyleBackColor = true;
        // 
        // mainMenuStrip
        // 
        mainMenuStrip.Dock = DockStyle.Fill;
        mainMenuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
        mainMenuStrip.Location = new Point(0, 0);
        mainMenuStrip.Name = "mainMenuStrip";
        mainMenuStrip.Size = new Size(1149, 24);
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
        outerLayout.Size = new Size(1149, 663);
        outerLayout.TabIndex = 14;
        // 
        // MainWindow
        // 
        AutoScaleDimensions = new SizeF(9F, 21F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1149, 663);
        Controls.Add(outerLayout);
        Font = new Font("Segoe UI", 12F);
        MainMenuStrip = mainMenuStrip;
        Margin = new Padding(4);
        Name = "MainWindow";
        Text = " ModsDude";
        Load += MainWindow_Load;
        mainLayout.ResumeLayout(false);
        profilesLayout.ResumeLayout(false);
        profilesLayout.PerformLayout();
        savegameGroupBox.ResumeLayout(false);
        savegameGroupBox.PerformLayout();
        groupBox1.ResumeLayout(false);
        repoLayout.ResumeLayout(false);
        repoLayout.PerformLayout();
        repoBodyPanel.ResumeLayout(false);
        mainMenuStrip.ResumeLayout(false);
        mainMenuStrip.PerformLayout();
        outerLayout.ResumeLayout(false);
        outerLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private ListBox repoList;
    private Label profilesTitleLabel;
    private Button newProfileButton;
    private ComboBox repoSelector;
    private Button renameRepoButton;
    private Label reposTitleLabel;
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
    private GroupBox savegameGroupBox;
    private Label savegameStatusValueLabel;
    private Label savegameStatusLabel;
    private Button checkInSavegameButton;
    private Button checkOutSavegameButton;
    private Button profileModsButton;
    private Button activateProfileButton;
    private Button deleteRepoButton;
    private ComboBox comboBox1;
    private Button newRepoButton;
    private GroupBox groupBox1;
}