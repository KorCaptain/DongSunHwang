using DiscordVoiceTranslator.Audio;
using DiscordVoiceTranslator.Overlay;
using Newtonsoft.Json;

namespace DiscordVoiceTranslator.UI;

/// <summary>
/// 메인 컨트롤 패널.
/// - 입력 언어: 자동감지(Whisper) 또는 7개 핵심 언어 수동 선택
/// - 출력 언어: OS 자동감지 또는 7개 핵심 언어 수동 선택
/// - 캡처 장치: VB-Audio Virtual Cable 포함 WASAPI 루프백 디바이스 목록
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _mainPyPath;

    private AudioCapture? _capture;
    private AiEngineClient? _engine;
    private SubtitleOverlay? _overlay;

    // ── 컨트롤 ──────────────────────────────────────────────────────────────
    private ComboBox _cmbSourceLang = null!;
    private ComboBox _cmbTargetLang = null!;
    private ComboBox _cmbDevice = null!;
    private Button _btnRefreshDevices = null!;
    private Button _btnStart = null!;
    private Button _btnStop = null!;
    private Label _lblStatus = null!;
    private Label _lblDetectedLang = null!;
    private RichTextBox _rtbLog = null!;

    // 캡처 디바이스 목록 (device combo 인덱스와 1:1)
    private List<AudioDeviceInfo> _deviceList = new();

    // ── 색상 팔레트 (Discord 스타일) ─────────────────────────────────────
    private static readonly Color BgDark    = Color.FromArgb(32,  34,  37);
    private static readonly Color BgMid     = Color.FromArgb(47,  49,  54);
    private static readonly Color BgLight   = Color.FromArgb(54,  57,  63);
    private static readonly Color AccentGreen  = Color.FromArgb(87,  166, 78);
    private static readonly Color AccentRed    = Color.FromArgb(220, 53,  69);
    private static readonly Color AccentBlue   = Color.FromArgb(88,  101, 242);
    private static readonly Color TextMain  = Color.FromArgb(220, 221, 222);
    private static readonly Color TextSub   = Color.FromArgb(148, 155, 164);

    public MainForm(AppSettings settings, string settingsPath, string mainPyPath)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _mainPyPath = mainPyPath;
        BuildUI();
        RefreshDeviceList();
        ApplyStoredSettings();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI 빌드
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        Text = "Discord Voice Translator";
        Size = new Size(520, 500);
        MinimumSize = new Size(520, 500);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = TextMain;

        // ── 최상단 헤더 ──────────────────────────────────────────────────
        var header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = BgMid };
        var lblTitle = new Label
        {
            Text = "🎙 Discord Voice Translator",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = AccentBlue,
            AutoSize = true,
            Location = new Point(16, 12),
        };
        header.Controls.Add(lblTitle);

        // ── 설정 패널 ─────────────────────────────────────────────────────
        var settingsPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 240,
            BackColor = BgDark,
            Padding = new Padding(16, 12, 16, 8),
        };

        // 말하는 언어 (입력 / Source)
        var lblSrc = MakeLabel("🗣  말하는 언어 (입력)", bold: true);
        lblSrc.Location = new Point(16, 14);
        var lblSrcHint = MakeLabel("말하는 사람의 언어를 선택하세요. 자동 감지 시 Whisper가 언어를 판별합니다.", sub: true);
        lblSrcHint.Location = new Point(16, 34);
        lblSrcHint.Width = 470;
        _cmbSourceLang = MakeLangCombo();
        _cmbSourceLang.Location = new Point(16, 55);
        _cmbSourceLang.Width = 460;

        // 듣는 언어 (출력 / Target)
        var lblTgt = MakeLabel("👂  듣는 언어 (출력)", bold: true);
        lblTgt.Location = new Point(16, 94);
        var lblTgtHint = MakeLabel("내가 받아볼 번역 언어입니다. 'OS 자동' 선택 시 시스템 언어로 자동 설정됩니다.", sub: true);
        lblTgtHint.Location = new Point(16, 114);
        lblTgtHint.Width = 470;
        _cmbTargetLang = MakeLangCombo(includeOsAuto: true);
        _cmbTargetLang.Location = new Point(16, 135);
        _cmbTargetLang.Width = 460;

        // 캡처 장치
        var lblDev = MakeLabel("🔊  캡처 장치", bold: true);
        lblDev.Location = new Point(16, 174);
        var lblDevHint = MakeLabel("VB-Audio Virtual Cable 사용 시 'CABLE Output' 장치를 선택하세요.", sub: true);
        lblDevHint.Location = new Point(16, 194);
        lblDevHint.Width = 470;
        _cmbDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(16, 215),
            Width = 390,
            BackColor = BgLight,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
        };
        _btnRefreshDevices = new Button
        {
            Text = "↺",
            Location = new Point(412, 213),
            Size = new Size(64, 26),
            BackColor = BgLight,
            ForeColor = TextSub,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ToolTipText = "장치 목록 새로고침",
        };
        _btnRefreshDevices.FlatAppearance.BorderColor = BgLight;
        _btnRefreshDevices.Click += (_, _) => { RefreshDeviceList(); Log("[장치 목록 새로고침]"); };

        settingsPanel.Controls.AddRange(new Control[]
        {
            lblSrc, lblSrcHint, _cmbSourceLang,
            lblTgt, lblTgtHint, _cmbTargetLang,
            lblDev, lblDevHint, _cmbDevice, _btnRefreshDevices,
        });

        // ── 버튼 패널 ─────────────────────────────────────────────────────
        var btnPanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = BgDark, Padding = new Padding(16, 8, 16, 0) };
        _btnStart = MakeButton("▶  번역 시작", AccentGreen);
        _btnStop  = MakeButton("■  중지",      AccentRed);
        _btnStop.Enabled = false;
        _btnStart.Location = new Point(16, 10);
        _btnStop.Location  = new Point(144, 10);
        _btnStart.Click += OnStart;
        _btnStop.Click  += OnStop;
        btnPanel.Controls.Add(_btnStart);
        btnPanel.Controls.Add(_btnStop);

        // ── 상태 표시줄 ───────────────────────────────────────────────────
        var statusPanel = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = BgMid };
        _lblStatus = new Label
        {
            Text = "⬤  대기 중",
            ForeColor = TextSub,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 8),
            AutoSize = true,
        };
        _lblDetectedLang = new Label
        {
            Text = "",
            ForeColor = AccentBlue,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Location = new Point(200, 8),
            AutoSize = true,
        };
        statusPanel.Controls.Add(_lblStatus);
        statusPanel.Controls.Add(_lblDetectedLang);

        // ── 로그 ─────────────────────────────────────────────────────────
        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = BgMid,
            ForeColor = TextMain,
            ReadOnly = true,
            Font = new Font("Consolas", 8.5f),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        // Controls 추가 순서 = 역순 (Dock.Top은 마지막 추가가 맨 위)
        Controls.Add(_rtbLog);
        Controls.Add(statusPanel);
        Controls.Add(btnPanel);
        Controls.Add(settingsPanel);
        Controls.Add(header);

        FormClosing += (_, _) => { SaveSettings(); CleanUp(); };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  디바이스 목록 관리
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshDeviceList()
    {
        _deviceList = AudioCapture.GetLoopbackDevices();

        _cmbDevice.Items.Clear();
        foreach (var dev in _deviceList)
            _cmbDevice.Items.Add(dev.ToString());

        // VB-Cable이 있으면 자동 선택
        int vbIdx = _deviceList.FindIndex(d => d.IsVirtualCable);
        if (vbIdx >= 0)
        {
            _cmbDevice.SelectedIndex = vbIdx;
            Log($"[VB-Audio] VB-Cable 자동 감지: {_deviceList[vbIdx].Name}");
        }
        else
        {
            _cmbDevice.SelectedIndex = 0; // 시스템 기본
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  언어 콤보박스 구성
    // ─────────────────────────────────────────────────────────────────────────

    /// <param name="includeOsAuto">
    /// true면 "OS 자동" 항목 추가 (target용).
    /// false면 "자동 감지 (Whisper)" 항목 추가 (source용).
    /// </param>
    private ComboBox MakeLangCombo(bool includeOsAuto = false)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgLight,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            Height = 26,
        };

        if (includeOsAuto)
        {
            // target: OS 자동 + 7개 언어
            cb.Items.Add(new LangItem("auto_os", "🖥  OS 언어 자동 설정", ""));
        }
        else
        {
            // source: Whisper 자동감지 + 7개 언어
            cb.Items.Add(SupportedLanguages.AutoDetect);
        }

        foreach (var lang in SupportedLanguages.CoreLanguages)
            cb.Items.Add(lang);

        cb.SelectedIndex = 0;
        return cb;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  저장된 설정 적용 (초기화 시)
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyStoredSettings()
    {
        SelectLang(_cmbSourceLang, _settings.SourceLang);
        SelectLang(_cmbTargetLang, _settings.TargetLang == "auto" ? "auto_os" : _settings.TargetLang);

        // 저장된 캡처 디바이스 복원
        if (_settings.CaptureDeviceId is not null)
        {
            int idx = _deviceList.FindIndex(d => d.Id == _settings.CaptureDeviceId);
            if (idx >= 0) _cmbDevice.SelectedIndex = idx;
        }
    }

    private static void SelectLang(ComboBox cb, string code)
    {
        for (int i = 0; i < cb.Items.Count; i++)
        {
            var item = cb.Items[i] as LangItem;
            if (item?.Code == code) { cb.SelectedIndex = i; return; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Start / Stop
    // ─────────────────────────────────────────────────────────────────────────

    private void OnStart(object? sender, EventArgs e)
    {
        try
        {
            string sourceLang = GetSelectedLangCode(_cmbSourceLang);
            string targetLang = ResolveTargetLang();
            var selectedDevice = _deviceList.Count > 0
                ? _deviceList[Math.Max(0, _cmbDevice.SelectedIndex)]
                : AudioDeviceInfo.SystemDefault;

            Log($"[시작] 입력: {sourceLang} / 출력: {targetLang} / 장치: {selectedDevice.Name}");

            // 오버레이
            _overlay = new SubtitleOverlay
            {
                DisplayDurationMs = _settings.SubtitleDurationMs,
                ShowOriginal = _settings.ShowOriginal,
            };
            _overlay.Show();

            // AI 엔진
            _engine = new AiEngineClient();
            _engine.ResultReceived += (_, result) =>
            {
                // Whisper가 실제 감지한 언어를 UI에 표시
                string detectedLabel = result.DetectedLang != null && result.DetectedLang != sourceLang
                    ? $"[감지: {result.DetectedLang}]"
                    : string.Empty;

                Log($"  원문 [{result.SourceLang}]: {result.Text}");
                Log($"  번역 [{result.TargetLang}]: {result.Translation}");

                _overlay?.ShowSubtitle(result.Text, result.Translation);
                SetDetectedLang(detectedLabel);
            };
            _engine.ErrorReceived += (_, msg) => Log($"[오류] {msg}");
            _engine.EngineReady += (_, _) =>
            {
                SetStatus("⬤  실행 중", AccentGreen);
                _engine.SendConfig(sourceLang, targetLang, _settings.AiEngine.VadThreshold);
            };
            _engine.Start(_settings.AiEngine.PythonExe, _mainPyPath);

            // 오디오 캡처
            _capture = new AudioCapture();
            _capture.AudioChunkReady += (_, chunk) =>
                _engine.SendAudio(chunk, sourceLang, targetLang);

            StartCapture(selectedDevice);

            SetStatus("⬤  AI 엔진 초기화 중...", TextSub);
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;

            // 설정 저장
            _settings.SourceLang = sourceLang;
            _settings.TargetLang = targetLang;
            _settings.CaptureDeviceId = selectedDevice.Id;
            _settings.CaptureDeviceName = selectedDevice.Name;
            SaveSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            CleanUp();
        }
    }

    private void StartCapture(AudioDeviceInfo device)
    {
        if (device.Id is null)
        {
            // 시스템 기본 루프백
            _capture!.StartLoopback();
        }
        else if (device.IsVirtualCable || _settings.CaptureMode != CaptureMode.Microphone)
        {
            // VB-Cable 또는 특정 WASAPI 루프백
            _capture!.StartLoopbackOnDevice(device.Id);
        }
        else
        {
            int devNum = int.TryParse(device.Id, out int n) ? n : 0;
            _capture!.StartMicrophone(devNum);
        }
    }

    /// <summary>target combo에서 실제 언어 코드를 반환합니다. "auto_os"이면 OS 언어를 감지.</summary>
    private string ResolveTargetLang()
    {
        string code = GetSelectedLangCode(_cmbTargetLang);
        if (code == "auto_os")
        {
            string detected = SupportedLanguages.DetectOsLanguage();
            Log($"[OS 자동] 시스템 언어 감지: {detected}");
            return detected;
        }
        return code;
    }

    private static string GetSelectedLangCode(ComboBox cb)
    {
        return (cb.SelectedItem as LangItem)?.Code ?? "auto";
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
        SetStatus("⬤  대기 중", TextSub);
        SetDetectedLang("");
        if (_btnStart is not null) _btnStart.Enabled = true;
        if (_btnStop  is not null) _btnStop.Enabled  = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  설정 저장
    // ─────────────────────────────────────────────────────────────────────────

    private void SaveSettings()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] 설정 저장 실패: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  헬퍼
    // ─────────────────────────────────────────────────────────────────────────

    private void Log(string message)
    {
        if (InvokeRequired) { Invoke(() => Log(message)); return; }
        _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }

    private void SetStatus(string text, Color? color = null)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(text, color)); return; }
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color ?? TextSub;
    }

    private void SetDetectedLang(string text)
    {
        if (InvokeRequired) { Invoke(() => SetDetectedLang(text)); return; }
        _lblDetectedLang.Text = text;
    }

    private static Label MakeLabel(string text, bool bold = false, bool sub = false)
    {
        return new Label
        {
            Text = text,
            ForeColor = sub ? TextSub : TextMain,
            Font = bold
                ? new Font("Segoe UI", 9.5f, FontStyle.Bold)
                : new Font("Segoe UI", 8.5f),
            AutoSize = true,
            BackColor = Color.Transparent,
        };
    }

    private static Button MakeButton(string text, Color bg)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Width = 120,
            Height = 32,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}
