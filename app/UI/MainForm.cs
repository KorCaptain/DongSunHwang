using DiscordVoiceTranslator.Audio;
using DiscordVoiceTranslator.Overlay;
using Newtonsoft.Json;

namespace DiscordVoiceTranslator.UI;

/// <summary>
/// Main control panel: start/stop, language selection, status display.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _mainPyPath;

    private AudioCapture? _capture;
    private AiEngineClient? _engine;
    private SubtitleOverlay? _overlay;

    // Controls
    private Button _btnStart = null!;
    private Button _btnStop = null!;
    private ComboBox _cmbSource = null!;
    private ComboBox _cmbTarget = null!;
    private ComboBox _cmbCapture = null!;
    private Label _lblStatus = null!;
    private RichTextBox _rtbLog = null!;

    private static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("ko", "Korean"), ("ja", "Japanese"),
        ("zh", "Chinese"), ("de", "German"), ("fr", "French"),
        ("es", "Spanish"), ("ru", "Russian"), ("ar", "Arabic"),
        ("vi", "Vietnamese"), ("th", "Thai"), ("id", "Indonesian"),
    };

    public MainForm(AppSettings settings, string settingsPath, string mainPyPath)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _mainPyPath = mainPyPath;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Discord Voice Translator";
        Size = new Size(480, 420);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(32, 34, 37); // Discord dark
        ForeColor = Color.White;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12),
            AutoSize = true,
            BackColor = Color.Transparent,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        AddRow(panel, 0, "Source Language:", _cmbSource = LangCombo(_settings.SourceLang));
        AddRow(panel, 1, "Target Language:", _cmbTarget = LangCombo(_settings.TargetLang));
        AddRow(panel, 2, "Capture Mode:", _cmbCapture = CaptureCombo());

        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0),
        };
        _btnStart = MakeButton("▶ Start", Color.FromArgb(87, 166, 78));
        _btnStop = MakeButton("■ Stop", Color.FromArgb(220, 53, 69));
        _btnStop.Enabled = false;
        _btnStart.Click += OnStart;
        _btnStop.Click += OnStop;
        btnPanel.Controls.Add(_btnStart);
        btnPanel.Controls.Add(_btnStop);
        panel.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 3);
        panel.Controls.Add(btnPanel, 1, 3);

        _lblStatus = new Label
        {
            Text = "Status: Idle",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.LightGray,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 0, 0, 0),
        };

        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(47, 49, 54),
            ForeColor = Color.White,
            ReadOnly = true,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
        };

        Controls.Add(_rtbLog);
        Controls.Add(_lblStatus);
        Controls.Add(panel);

        FormClosing += (_, e) => { e.Cancel = false; CleanUp(); };
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        var lbl = new Label
        {
            Text = label,
            ForeColor = Color.LightGray,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
        };
        table.Controls.Add(lbl, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private ComboBox LangCombo(string selected)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        foreach (var (code, name) in Languages)
            cb.Items.Add(new LangItem(code, name));
        cb.SelectedIndex = Array.FindIndex(Languages, l => l.Code == selected);
        if (cb.SelectedIndex < 0) cb.SelectedIndex = 0;
        return cb;
    }

    private ComboBox CaptureCombo()
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        cb.Items.Add("System Audio (Loopback)");
        cb.Items.Add("Microphone");
        cb.SelectedIndex = _settings.CaptureMode == CaptureMode.Loopback ? 0 : 1;
        return cb;
    }

    private static Button MakeButton(string text, Color bg)
    {
        return new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Width = 100,
            Height = 32,
            Margin = new Padding(0, 0, 8, 0),
        };
    }

    private void OnStart(object? sender, EventArgs e)
    {
        try
        {
            string sourceLang = ((LangItem)_cmbSource.SelectedItem!).Code;
            string targetLang = ((LangItem)_cmbTarget.SelectedItem!).Code;
            bool loopback = _cmbCapture.SelectedIndex == 0;

            // Start overlay
            _overlay = new SubtitleOverlay
            {
                DisplayDurationMs = _settings.SubtitleDurationMs,
                ShowOriginal = _settings.ShowOriginal,
            };
            _overlay.Show();

            // Start AI engine
            _engine = new AiEngineClient();
            _engine.ResultReceived += (_, result) =>
            {
                Log($"[{result.SourceLang}] {result.Text}");
                Log($"[{result.TargetLang}] {result.Translation}");
                _overlay?.ShowSubtitle(result.Text, result.Translation);
            };
            _engine.ErrorReceived += (_, msg) => Log($"[ERROR] {msg}");
            _engine.EngineReady += (_, _) =>
            {
                SetStatus("Status: Running");
                _engine.SendConfig(sourceLang, targetLang, _settings.AiEngine.VadThreshold);
            };
            _engine.Start(_settings.AiEngine.PythonExe, _mainPyPath);

            // Start audio capture
            _capture = new AudioCapture();
            _capture.AudioChunkReady += (_, chunk) =>
                _engine.SendAudio(chunk, sourceLang, targetLang);

            if (loopback)
                _capture.StartLoopback();
            else
                _capture.StartMicrophone(_settings.MicrophoneDevice);

            SetStatus("Status: Starting AI engine...");
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            CleanUp();
        }
    }

    private void OnStop(object? sender, EventArgs e) => CleanUp();

    private void CleanUp()
    {
        _capture?.Dispose();
        _capture = null;
        _engine?.Dispose();
        _engine = null;
        _overlay?.Close();
        _overlay = null;
        SetStatus("Status: Idle");
        if (_btnStart is not null) _btnStart.Enabled = true;
        if (_btnStop is not null) _btnStop.Enabled = false;
    }

    private void Log(string message)
    {
        if (InvokeRequired) { Invoke(() => Log(message)); return; }
        _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(text)); return; }
        _lblStatus.Text = text;
    }

    private record LangItem(string Code, string Name)
    {
        public override string ToString() => Name;
    }
}
