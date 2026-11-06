using LaughDlc.Controls;

namespace LaughDlc;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    private Label titleLabel = null!;
    private GroupBox audioGroup = null!;
    private Label audioPathLabel = null!;
    private TextBox audioPathTextBox = null!;
    private Button browseButton = null!;
    private Button playButton = null!;
    private Button stopButton = null!;
    private Label playbackStatusLabel = null!;
    private GroupBox hotkeyGroup = null!;
    private Label hotkeyLabel = null!;
    private HotkeyRecorderControl hotkeyRecorder = null!;
    private CheckBox hotkeyEnabledCheckBox = null!;
    private Label hotkeyStatusLabel = null!;
    private Label trayHintLabel = null!;
    private NotifyIcon trayIcon = null!;
    private ContextMenuStrip trayMenu = null!;
    private ToolStripMenuItem openMenuItem = null!;
    private ToolStripSeparator traySeparatorOne = null!;
    private ToolStripMenuItem playMenuItem = null!;
    private ToolStripMenuItem stopMenuItem = null!;
    private ToolStripMenuItem toggleHotkeyMenuItem = null!;
    private ToolStripSeparator traySeparatorTwo = null!;
    private ToolStripMenuItem exitMenuItem = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        titleLabel = new Label();
        audioGroup = new GroupBox();
        audioPathLabel = new Label();
        audioPathTextBox = new TextBox();
        browseButton = new Button();
        playButton = new Button();
        stopButton = new Button();
        playbackStatusLabel = new Label();
        hotkeyGroup = new GroupBox();
        hotkeyLabel = new Label();
        hotkeyRecorder = new HotkeyRecorderControl();
        hotkeyEnabledCheckBox = new CheckBox();
        hotkeyStatusLabel = new Label();
        trayHintLabel = new Label();
        trayMenu = new ContextMenuStrip(components);
        openMenuItem = new ToolStripMenuItem();
        traySeparatorOne = new ToolStripSeparator();
        playMenuItem = new ToolStripMenuItem();
        stopMenuItem = new ToolStripMenuItem();
        toggleHotkeyMenuItem = new ToolStripMenuItem();
        traySeparatorTwo = new ToolStripSeparator();
        exitMenuItem = new ToolStripMenuItem();
        trayIcon = new NotifyIcon(components);

        audioGroup.SuspendLayout();
        hotkeyGroup.SuspendLayout();
        trayMenu.SuspendLayout();
        SuspendLayout();

        titleLabel.AutoSize = true;
        titleLabel.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        titleLabel.Location = new Point(18, 14);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(100, 30);
        titleLabel.TabIndex = 0;
        titleLabel.Text = "笑声DLC";

        audioGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        audioGroup.Controls.Add(audioPathLabel);
        audioGroup.Controls.Add(audioPathTextBox);
        audioGroup.Controls.Add(browseButton);
        audioGroup.Controls.Add(playButton);
        audioGroup.Controls.Add(stopButton);
        audioGroup.Controls.Add(playbackStatusLabel);
        audioGroup.Location = new Point(18, 54);
        audioGroup.Name = "audioGroup";
        audioGroup.Size = new Size(644, 182);
        audioGroup.TabIndex = 1;
        audioGroup.TabStop = false;
        audioGroup.Text = "音频";

        audioPathLabel.AutoSize = true;
        audioPathLabel.Location = new Point(14, 30);
        audioPathLabel.Name = "audioPathLabel";
        audioPathLabel.Size = new Size(63, 15);
        audioPathLabel.TabIndex = 0;
        audioPathLabel.Text = "音频文件：";

        audioPathTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        audioPathTextBox.BackColor = SystemColors.Window;
        audioPathTextBox.Location = new Point(83, 27);
        audioPathTextBox.Name = "audioPathTextBox";
        audioPathTextBox.ReadOnly = true;
        audioPathTextBox.Size = new Size(438, 23);
        audioPathTextBox.TabIndex = 1;
        audioPathTextBox.TabStop = false;

        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.Location = new Point(531, 25);
        browseButton.Name = "browseButton";
        browseButton.Size = new Size(99, 28);
        browseButton.TabIndex = 2;
        browseButton.Text = "选择音频…";
        browseButton.UseVisualStyleBackColor = true;

        playButton.Location = new Point(83, 66);
        playButton.Name = "playButton";
        playButton.Size = new Size(100, 32);
        playButton.TabIndex = 3;
        playButton.Text = "试听";
        playButton.UseVisualStyleBackColor = true;

        stopButton.Enabled = false;
        stopButton.Location = new Point(191, 66);
        stopButton.Name = "stopButton";
        stopButton.Size = new Size(100, 32);
        stopButton.TabIndex = 4;
        stopButton.Text = "停止";
        stopButton.UseVisualStyleBackColor = true;

        playbackStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        playbackStatusLabel.AutoEllipsis = true;
        playbackStatusLabel.ForeColor = SystemColors.GrayText;
        playbackStatusLabel.Location = new Point(83, 108);
        playbackStatusLabel.Name = "playbackStatusLabel";
        playbackStatusLabel.Size = new Size(547, 56);
        playbackStatusLabel.TabIndex = 5;
        playbackStatusLabel.Text = "尚未选择音频。支持 WAV、MP3。";
        playbackStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

        hotkeyGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        hotkeyGroup.Controls.Add(hotkeyLabel);
        hotkeyGroup.Controls.Add(hotkeyRecorder);
        hotkeyGroup.Controls.Add(hotkeyEnabledCheckBox);
        hotkeyGroup.Controls.Add(hotkeyStatusLabel);
        hotkeyGroup.Location = new Point(18, 246);
        hotkeyGroup.Name = "hotkeyGroup";
        hotkeyGroup.Size = new Size(644, 118);
        hotkeyGroup.TabIndex = 2;
        hotkeyGroup.TabStop = false;
        hotkeyGroup.Text = "全局快捷键";

        hotkeyLabel.AutoSize = true;
        hotkeyLabel.Location = new Point(14, 32);
        hotkeyLabel.Name = "hotkeyLabel";
        hotkeyLabel.Size = new Size(63, 15);
        hotkeyLabel.TabIndex = 0;
        hotkeyLabel.Text = "当前快捷键：";

        hotkeyRecorder.Location = new Point(83, 26);
        hotkeyRecorder.Name = "hotkeyRecorder";
        hotkeyRecorder.Size = new Size(300, 32);
        hotkeyRecorder.TabIndex = 1;
        hotkeyRecorder.UseVisualStyleBackColor = true;

        hotkeyEnabledCheckBox.AutoSize = true;
        hotkeyEnabledCheckBox.Location = new Point(395, 32);
        hotkeyEnabledCheckBox.Name = "hotkeyEnabledCheckBox";
        hotkeyEnabledCheckBox.Size = new Size(111, 19);
        hotkeyEnabledCheckBox.TabIndex = 2;
        hotkeyEnabledCheckBox.Text = "启用全局快捷键";
        hotkeyEnabledCheckBox.UseVisualStyleBackColor = true;

        hotkeyStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        hotkeyStatusLabel.AutoEllipsis = true;
        hotkeyStatusLabel.Location = new Point(83, 67);
        hotkeyStatusLabel.Name = "hotkeyStatusLabel";
        hotkeyStatusLabel.Size = new Size(547, 41);
        hotkeyStatusLabel.TabIndex = 3;
        hotkeyStatusLabel.Text = "状态：已禁用";
        hotkeyStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

        trayHintLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        trayHintLabel.AutoEllipsis = true;
        trayHintLabel.ForeColor = SystemColors.GrayText;
        trayHintLabel.Location = new Point(18, 378);
        trayHintLabel.Name = "trayHintLabel";
        trayHintLabel.Size = new Size(644, 32);
        trayHintLabel.TabIndex = 3;
        trayHintLabel.Text = "关闭窗口只会隐藏到系统托盘。音量由 Windows 默认播放设备控制。";
        trayHintLabel.TextAlign = ContentAlignment.MiddleLeft;

        trayMenu.Items.AddRange(new ToolStripItem[]
        {
            openMenuItem,
            traySeparatorOne,
            playMenuItem,
            stopMenuItem,
            toggleHotkeyMenuItem,
            traySeparatorTwo,
            exitMenuItem
        });
        trayMenu.Name = "trayMenu";
        trayMenu.Size = new Size(161, 126);

        openMenuItem.Name = "openMenuItem";
        openMenuItem.Size = new Size(160, 22);
        openMenuItem.Text = "打开窗口(&O)";

        traySeparatorOne.Name = "traySeparatorOne";
        traySeparatorOne.Size = new Size(157, 6);

        playMenuItem.Name = "playMenuItem";
        playMenuItem.Size = new Size(160, 22);
        playMenuItem.Text = "试听/播放(&P)";

        stopMenuItem.Enabled = false;
        stopMenuItem.Name = "stopMenuItem";
        stopMenuItem.Size = new Size(160, 22);
        stopMenuItem.Text = "停止(&S)";

        toggleHotkeyMenuItem.Name = "toggleHotkeyMenuItem";
        toggleHotkeyMenuItem.Size = new Size(160, 22);
        toggleHotkeyMenuItem.Text = "禁用快捷键(&H)";

        traySeparatorTwo.Name = "traySeparatorTwo";
        traySeparatorTwo.Size = new Size(157, 6);

        exitMenuItem.Name = "exitMenuItem";
        exitMenuItem.Size = new Size(160, 22);
        exitMenuItem.Text = "退出程序(&X)";

        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Text = "笑声DLC";
        trayIcon.Visible = true;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = SystemColors.Control;
        ClientSize = new Size(680, 422);
        Controls.Add(trayHintLabel);
        Controls.Add(hotkeyGroup);
        Controls.Add(audioGroup);
        Controls.Add(titleLabel);
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(650, 400);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "笑声DLC";

        hotkeyGroup.ResumeLayout(false);
        hotkeyGroup.PerformLayout();
        audioGroup.ResumeLayout(false);
        audioGroup.PerformLayout();
        trayMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
