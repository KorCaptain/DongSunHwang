using System.Speech.Synthesis;
using DiscordVoiceTranslator.Audio;
using DiscordVoiceTranslator.Network;
using DiscordVoiceTranslator.Overlay;
using Newtonsoft.Json;

namespace DiscordVoiceTranslator.UI;

/// <summary>
/// 발헤임 음성 번역 채팅 자동화 메인 폼.
///
/// 기능:
/// - 마이크 음성 → STT → 번역 → 발헤임 공개 채팅 자동 전송
/// - 발헤임 채팅 수신 → 번역 → PC 창에 표시 + 플레이어별 TTS 읽기
/// - 번역 엔진 선택: 빠른(argostranslate) / 정확(NLLB)
/// - 발헤임 인게임 번역 채팅창 연동 (ValheimBridge via TCP:7891)
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _mainPyPath;

    private AudioCapture? _capture;
    private AiEngineClient? _engine;
    private ValheimBridge? _bridge;
    private SubtitleOverlay? _overlay;

    // 플레이어별 TTS (플레이어명 → SpeechSynthesizer)
    private readonly Dictionary<string, SpeechSynthesizer> _playerSynths = new();
    private List<System.Speech.Synthesis.VoiceInfo> _availableVoices = new();

    // ── 컨트롤 ──────────────────────────────────────────────────────────────
    // 섹션1: 음성 → 발헤임
    private ComboBox _cmbMic = null!;
    private Button _btnRefreshMic = null!;
    private ComboBox _cmbVoiceSrcLang = null!;
    private ComboBox _cmbVoiceTgtLang = null!;
    private Button _btnStartVoice = null!;
    private Button _btnStopVoice = null!;

    // 섹션2: 발헤임 수신 → 번역
    private ComboBox _cmbReceiveLang = null!;
    private Button _btnToggleTts = null!;
    private Label _lblValheimStatus = null!;
    private RadioButton _rbFast = null!;
    private RadioButton _rbAccurate = null!;

    // 섹션3: 번역 채팅 로그
    private RichTextBox _rtbChat = null!;

    // 섹션4: 플레이어 TTS 음성 설정
    private ListBox _lstPlayers = null!;
    private ComboBox _cmbVoice = null!;
    private Button _btnTestVoice = null!;

    // 섹션5: 디버그 로그
    private RichTextBox _rtbLog = null!;

    // 마이크 장치 목록
    private List<AudioDeviceInfo> _micList = new();

    // 플레이어별 색상 (채팅 로그 구분)
    private readonly Color[] _playerColors =
    {
        Color.FromArgb(116, 185, 255),
        Color.FromArgb(253, 203, 110),
        Color.FromArgb(85,  239, 196),
        Color.FromArgb(255, 118, 117),
        Color.FromArgb(162, 155, 254),
        Color.FromArgb(250, 177, 160),
    };
    private readonly Dictionary<string, Color> _playerColorMap = new();
    private int _colorIndex;

    // ── 색상 팔레트 ──────────────────────────────────────────────────────────
    private static readonly Color BgDark   = Color.FromArgb(32,  34,  37);
    private static readonly Color BgMid    = Color.FromArgb(47,  49,  54);
    private static readonly Color BgLight  = Color.FromArgb(54,  57,  63);
    private static readonly Color AccentGreen = Color.FromArgb(87,  166, 78);
    private static readonly Color AccentRed   = Color.FromArgb(220, 53,  69);
    private static readonly Color AccentBlue  = Color.FromArgb(88,  101, 242);
    private static readonly Color AccentOrange = Color.FromArgb(230, 126, 34);
    private static readonly Color TextMain = Color.FromArgb(220, 221, 222);
    private static readonly Color TextSub  = Color.FromArgb(148, 155, 164);

    public MainForm(AppSettings settings, string settingsPath, string mainPyPath)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _mainPyPath = mainPyPath;
        BuildUI();
        RefreshMicList();
        LoadAvailableVoices();
        ApplyStoredSettings();
        StartValheimBridge();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI 빌드
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        Text = "Valheim Voice Chat Translator";
        Size = new Size(780, 760);
        MinimumSize = new Size(780, 760);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = TextMain;

        // ── 헤더 ──────────────────────────────────────────────────────────
        var header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = BgMid };
        var lblTitle = new Label
        {
            Text = "⚔  Valheim Voice Chat Translator",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = AccentOrange,
            AutoSize = true,
            Location = new Point(16, 12),
        };
        header.Controls.Add(lblTitle);

        // ── 탭 컨트롤 (전체 레이아웃) ──────────────────────────────────
        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            Font = new Font("Segoe UI", 9.5f),
        };

        var tabMain = new TabPage("번역 채팅") { BackColor = BgDark, ForeColor = TextMain };
        var tabVoice = new TabPage("음성 설정") { BackColor = BgDark, ForeColor = TextMain };
        var tabLog = new TabPage("디버그 로그") { BackColor = BgDark, ForeColor = TextMain };

        tabControl.TabPages.Add(tabMain);
        tabControl.TabPages.Add(tabVoice);
        tabControl.TabPages.Add(tabLog);

        BuildMainTab(tabMain);
        BuildVoiceTab(tabVoice);
        BuildLogTab(tabLog);

        Controls.Add(tabControl);
        Controls.Add(header);

        FormClosing += (_, _) => { SaveSettings(); CleanUp(); };
    }

    /// <summary>탭1: 번역 채팅 (수신 설정 + 채팅 로그 + 플레이어 TTS)</summary>
    private void BuildMainTab(TabPage tab)
    {
        int y = 10;

        // ── 섹션2: 발헤임 채팅 수신 → 번역 ─────────────────────────────
        var lblSec2 = MakeLabel("📡  발헤임 채팅 수신 → 번역", bold: true);
        lblSec2.Location = new Point(12, y);
        tab.Controls.Add(lblSec2);
        y += 22;

        // 수신 번역 언어
        var lblRcvLang = MakeLabel("번역 언어 (수신 채팅을 이 언어로 번역):", sub: true);
        lblRcvLang.Location = new Point(12, y);
        tab.Controls.Add(lblRcvLang);
        y += 18;

        _cmbReceiveLang = MakeLangCombo();
        _cmbReceiveLang.Location = new Point(12, y);
        _cmbReceiveLang.Width = 220;
        tab.Controls.Add(_cmbReceiveLang);

        // TTS 토글
        _btnToggleTts = new Button
        {
            Text = "🔊 TTS 켜기",
            Location = new Point(242, y - 2),
            Size = new Size(110, 26),
            BackColor = _settings.TtsEnabled ? AccentGreen : BgLight,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _btnToggleTts.FlatAppearance.BorderSize = 0;
        _btnToggleTts.Click += OnToggleTts;
        tab.Controls.Add(_btnToggleTts);
        UpdateTtsButton();

        // 발헤임 연결 상태
        _lblValheimStatus = new Label
        {
            Text = "⬤  발헤임 미연결",
            ForeColor = TextSub,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(364, y),
            AutoSize = true,
        };
        tab.Controls.Add(_lblValheimStatus);
        y += 34;

        // 번역 엔진 선택
        var lblEngine = MakeLabel("번역 엔진:", sub: true);
        lblEngine.Location = new Point(12, y);
        tab.Controls.Add(lblEngine);

        _rbFast = new RadioButton
        {
            Text = "⚡ 빠른 응답 (argostranslate, 오프라인)",
            Location = new Point(80, y - 2),
            AutoSize = true,
            ForeColor = TextMain,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f),
        };
        _rbAccurate = new RadioButton
        {
            Text = "🎯 정확도 우선 (NLLB-200)",
            Location = new Point(350, y - 2),
            AutoSize = true,
            ForeColor = TextMain,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f),
        };
        _rbFast.CheckedChanged += OnEngineChanged;
        _rbAccurate.CheckedChanged += OnEngineChanged;
        tab.Controls.Add(_rbFast);
        tab.Controls.Add(_rbAccurate);
        y += 28;

        // ── 구분선 ────────────────────────────────────────────────────────
        var sep1 = new Panel { Location = new Point(12, y), Size = new Size(740, 1), BackColor = BgLight };
        tab.Controls.Add(sep1);
        y += 8;

        // ── 섹션3: 번역 채팅 로그 ────────────────────────────────────────
        var lblSec3 = MakeLabel("💬  번역 채팅 로그", bold: true);
        lblSec3.Location = new Point(12, y);
        tab.Controls.Add(lblSec3);
        y += 22;

        _rtbChat = new RichTextBox
        {
            Location = new Point(12, y),
            Size = new Size(740, 220),
            BackColor = BgMid,
            ForeColor = TextMain,
            ReadOnly = true,
            Font = new Font("Malgun Gothic", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        tab.Controls.Add(_rtbChat);
        y += 228;

        // ── 구분선 ────────────────────────────────────────────────────────
        var sep2 = new Panel { Location = new Point(12, y), Size = new Size(740, 1), BackColor = BgLight };
        tab.Controls.Add(sep2);
        y += 8;

        // ── 섹션4: 플레이어별 TTS 음성 ───────────────────────────────────
        var lblSec4 = MakeLabel("🎭  플레이어별 TTS 음성", bold: true);
        lblSec4.Location = new Point(12, y);
        tab.Controls.Add(lblSec4);
        y += 22;

        var lblPlayers = MakeLabel("접속 플레이어:", sub: true);
        lblPlayers.Location = new Point(12, y);
        tab.Controls.Add(lblPlayers);

        var lblVoiceSel = MakeLabel("배정 음성:", sub: true);
        lblVoiceSel.Location = new Point(230, y);
        tab.Controls.Add(lblVoiceSel);
        y += 18;

        _lstPlayers = new ListBox
        {
            Location = new Point(12, y),
            Size = new Size(200, 100),
            BackColor = BgLight,
            ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
        };
        _lstPlayers.SelectedIndexChanged += OnPlayerSelected;
        tab.Controls.Add(_lstPlayers);

        _cmbVoice = new ComboBox
        {
            Location = new Point(222, y),
            Size = new Size(340, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgLight,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
        };
        _cmbVoice.SelectedIndexChanged += OnVoiceSelected;
        tab.Controls.Add(_cmbVoice);

        _btnTestVoice = new Button
        {
            Text = "▶ 테스트",
            Location = new Point(572, y - 2),
            Size = new Size(80, 28),
            BackColor = AccentBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _btnTestVoice.FlatAppearance.BorderSize = 0;
        _btnTestVoice.Click += OnTestVoice;
        tab.Controls.Add(_btnTestVoice);
    }

    /// <summary>탭2: 음성 설정 (마이크 → 발헤임 채팅 전송)</summary>
    private void BuildVoiceTab(TabPage tab)
    {
        int y = 10;

        var lblSec1 = MakeLabel("🎙  내 음성 → 발헤임 채팅 전송", bold: true);
        lblSec1.Location = new Point(12, y);
        tab.Controls.Add(lblSec1);
        y += 22;

        // 마이크 장치
        var lblMic = MakeLabel("마이크 장치:", sub: true);
        lblMic.Location = new Point(12, y);
        tab.Controls.Add(lblMic);
        y += 18;

        _cmbMic = new ComboBox
        {
            Location = new Point(12, y),
            Width = 560,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgLight,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
        };
        tab.Controls.Add(_cmbMic);

        _btnRefreshMic = new Button
        {
            Text = "↺",
            Location = new Point(582, y - 2),
            Size = new Size(64, 28),
            BackColor = BgLight,
            ForeColor = TextSub,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
        };
        _btnRefreshMic.FlatAppearance.BorderColor = BgLight;
        _btnRefreshMic.Click += (_, _) => { RefreshMicList(); Log("[마이크 목록 새로고침]"); };
        tab.Controls.Add(_btnRefreshMic);
        y += 34;

        // 음성 입력 언어
        var lblSrcLang = MakeLabel("내가 말하는 언어:", sub: true);
        lblSrcLang.Location = new Point(12, y);
        tab.Controls.Add(lblSrcLang);

        var lblTgtLang = MakeLabel("발헤임 전송 언어:", sub: true);
        lblTgtLang.Location = new Point(340, y);
        tab.Controls.Add(lblTgtLang);
        y += 18;

        _cmbVoiceSrcLang = MakeLangCombo(includeAutoDetect: true);
        _cmbVoiceSrcLang.Location = new Point(12, y);
        _cmbVoiceSrcLang.Width = 310;
        tab.Controls.Add(_cmbVoiceSrcLang);

        _cmbVoiceTgtLang = MakeLangCombo();
        _cmbVoiceTgtLang.Location = new Point(340, y);
        _cmbVoiceTgtLang.Width = 310;
        tab.Controls.Add(_cmbVoiceTgtLang);
        y += 36;

        // 시작/중지
        _btnStartVoice = MakeButton("▶  음성 번역 시작", AccentGreen);
        _btnStopVoice  = MakeButton("■  중지",          AccentRed);
        _btnStopVoice.Enabled = false;
        _btnStartVoice.Location = new Point(12, y);
        _btnStopVoice.Location  = new Point(142, y);
        _btnStartVoice.Click += OnStartVoice;
        _btnStopVoice.Click  += OnStopVoice;
        tab.Controls.Add(_btnStartVoice);
        tab.Controls.Add(_btnStopVoice);
        y += 44;

        // 안내
        var lblHint = MakeLabel(
            "※ 시작하면 마이크 음성을 인식하여 선택한 언어로 번역 후\n" +
            "   발헤임 공개 채팅에 자동 전송합니다. 발헤임 BepInEx 모드 필요.",
            sub: true);
        lblHint.Location = new Point(12, y);
        lblHint.Width = 650;
        lblHint.AutoSize = false;
        lblHint.Height = 36;
        tab.Controls.Add(lblHint);
    }

    /// <summary>탭3: 디버그 로그</summary>
    private void BuildLogTab(TabPage tab)
    {
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
        tab.Controls.Add(_rtbLog);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  초기화
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshMicList()
    {
        _micList = AudioCapture.GetMicrophoneDevices();
        _cmbMic.Items.Clear();
        foreach (var dev in _micList)
            _cmbMic.Items.Add(dev.ToString());
        if (_cmbMic.Items.Count > 0)
            _cmbMic.SelectedIndex = Math.Max(0, _settings.MicrophoneDevice);
    }

    private void LoadAvailableVoices()
    {
        using var synth = new SpeechSynthesizer();
        _availableVoices = synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .ToList();

        _cmbVoice.Items.Clear();
        foreach (var v in _availableVoices)
            _cmbVoice.Items.Add($"{v.Name} ({v.Culture.TwoLetterISOLanguageName})");
    }

    private void ApplyStoredSettings()
    {
        SelectLang(_cmbReceiveLang, _settings.ChatReceiveLang);
        SelectLang(_cmbVoiceSrcLang, _settings.VoiceSourceLang);
        SelectLang(_cmbVoiceTgtLang, _settings.VoiceTargetLang);

        _rbFast.Checked     = _settings.TranslationEngine == "argos";
        _rbAccurate.Checked = _settings.TranslationEngine == "nllb";
        if (!_rbFast.Checked && !_rbAccurate.Checked) _rbFast.Checked = true;

        UpdateTtsButton();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ValheimBridge 시작
    // ─────────────────────────────────────────────────────────────────────────

    private void StartValheimBridge()
    {
        _bridge = new ValheimBridge(_settings.ValheimBridgePort);
        _bridge.ConnectionChanged += (_, connected) =>
        {
            if (InvokeRequired) { Invoke(() => OnBridgeConnectionChanged(connected)); return; }
            OnBridgeConnectionChanged(connected);
        };
        _bridge.ChatReceived += OnValheimChatReceived;
        _bridge.Start();
        Log($"[ValheimBridge] TCP 서버 시작됨 (포트 {_settings.ValheimBridgePort})");
    }

    private void OnBridgeConnectionChanged(bool connected)
    {
        _lblValheimStatus.Text = connected ? "⬤  발헤임 연결됨" : "⬤  발헤임 미연결";
        _lblValheimStatus.ForeColor = connected ? AccentGreen : TextSub;
        Log(connected ? "[발헤임] 모드 연결됨" : "[발헤임] 모드 연결 끊김");
    }

    private void OnValheimChatReceived(object? sender, ChatReceivedArgs e)
    {
        // 수신 채팅 → 번역 요청
        if (_engine is null || !_engine.IsReady) return;

        string targetLang = GetSelectedLangCode(_cmbReceiveLang);
        string requestId = Guid.NewGuid().ToString("N")[..8];
        _engine.SendTextTranslation(e.Text, "auto", targetLang, requestId, e.Author);
        Log($"[수신] [{e.Author}] {e.Text}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  음성 번역 시작/중지
    // ─────────────────────────────────────────────────────────────────────────

    private void OnStartVoice(object? sender, EventArgs e)
    {
        try
        {
            string voiceSrc = GetSelectedLangCode(_cmbVoiceSrcLang);
            string voiceTgt = GetSelectedLangCode(_cmbVoiceTgtLang);
            int micIdx = _cmbMic.SelectedIndex >= 0 ? _cmbMic.SelectedIndex : 0;

            Log($"[음성 시작] 입력: {voiceSrc} / 출력: {voiceTgt} / 마이크: {micIdx}");

            _overlay = new SubtitleOverlay
            {
                DisplayDurationMs = _settings.SubtitleDurationMs,
                ShowOriginal = _settings.ShowOriginal,
            };
            _overlay.Show();

            _engine = new AiEngineClient();

            // 음성 STT 결과 → 발헤임 전송
            _engine.ResultReceived += (_, result) =>
            {
                Log($"  [STT] {result.Text}  →  {result.Translation}");
                _overlay?.ShowSubtitle(result.Text, result.Translation);
                _bridge?.SendChat(result.Translation);
            };

            // 텍스트 번역 결과 (발헤임 채팅 수신) → 로그 + TTS
            _engine.TextResultReceived += OnTextTranslationResult;

            _engine.ErrorReceived += (_, msg) => Log($"[오류] {msg}");
            _engine.EngineReady += (_, _) =>
            {
                string engine = _settings.TranslationEngine;
                _engine.SendConfig(voiceSrc, voiceTgt, _settings.AiEngine.VadThreshold);
                // 번역 엔진 설정도 전달
                _engine.SendEngineConfig(engine);
            };
            _engine.Start(_settings.AiEngine.PythonExe, _mainPyPath);

            _capture = new AudioCapture();
            _capture.AudioChunkReady += (_, chunk) =>
                _engine.SendAudio(chunk, voiceSrc, voiceTgt);
            _capture.StartMicrophone(micIdx);

            _btnStartVoice.Enabled = false;
            _btnStopVoice.Enabled = true;

            _settings.VoiceSourceLang = voiceSrc;
            _settings.VoiceTargetLang = voiceTgt;
            _settings.MicrophoneDevice = micIdx;
            SaveSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"시작 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            CleanUpVoice();
        }
    }

    private void OnStopVoice(object? sender, EventArgs e) => CleanUpVoice();

    private void CleanUpVoice()
    {
        _capture?.Dispose();
        _capture = null;
        _engine?.Dispose();
        _engine = null;
        _overlay?.Close();
        _overlay = null;
        if (_btnStartVoice is not null) _btnStartVoice.Enabled = true;
        if (_btnStopVoice  is not null) _btnStopVoice.Enabled  = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  텍스트 번역 결과 처리 (발헤임 채팅 수신)
    // ─────────────────────────────────────────────────────────────────────────

    private void OnTextTranslationResult(object? sender, TextTranslationResult result)
    {
        if (InvokeRequired) { Invoke(() => OnTextTranslationResult(sender, result)); return; }

        // 번역 채팅 로그에 표시
        AppendChatLog(result.Author, result.Text, result.Translation);

        // 인게임 번역 채팅창에도 전송
        _bridge?.SendTranslationResult(result.Author, result.Text, result.Translation);

        // TTS
        if (_settings.TtsEnabled)
            SpeakForPlayer(result.Author, result.Translation);
    }

    private void AppendChatLog(string author, string original, string translated)
    {
        if (!_playerColorMap.ContainsKey(author))
        {
            _playerColorMap[author] = _playerColors[_colorIndex % _playerColors.Length];
            _colorIndex++;
            AddPlayerToList(author);
        }

        var color = _playerColorMap[author];

        _rtbChat.SelectionStart = _rtbChat.TextLength;
        _rtbChat.SelectionLength = 0;
        _rtbChat.SelectionColor = color;
        _rtbChat.AppendText($"[{author}] ");
        _rtbChat.SelectionColor = TextMain;
        _rtbChat.AppendText($"{translated}");
        _rtbChat.SelectionColor = TextSub;
        _rtbChat.AppendText($"  ← {original}\n");
        _rtbChat.SelectionColor = TextMain;
        _rtbChat.ScrollToCaret();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  플레이어 TTS 음성 관리
    // ─────────────────────────────────────────────────────────────────────────

    private void AddPlayerToList(string playerName)
    {
        if (_lstPlayers.Items.Contains(playerName)) return;
        _lstPlayers.Items.Add(playerName);

        // 저장된 음성 복원 또는 자동 배정
        string? savedVoice = _settings.PlayerVoiceMap.TryGetValue(playerName, out var v) ? v : null;
        var synth = new SpeechSynthesizer();

        if (savedVoice != null && _availableVoices.Any(x => x.Name == savedVoice))
        {
            synth.SelectVoice(savedVoice);
        }
        else
        {
            // 아직 사용되지 않은 음성 자동 배정
            var usedNames = _playerSynths.Values.Select(s => s.Voice.Name).ToHashSet();
            var next = _availableVoices.FirstOrDefault(x => !usedNames.Contains(x.Name));
            if (next != null) synth.SelectVoice(next.Name);
        }
        _playerSynths[playerName] = synth;
    }

    private void SpeakForPlayer(string playerName, string text)
    {
        if (!_playerSynths.TryGetValue(playerName, out var synth)) return;
        synth.SpeakAsyncCancelAll();
        synth.SpeakAsync(text);
    }

    // ── 플레이어 목록 선택 시 콤보박스에 해당 음성 표시 ───────────────────
    private void OnPlayerSelected(object? sender, EventArgs e)
    {
        if (_lstPlayers.SelectedItem is not string playerName) return;
        if (!_playerSynths.TryGetValue(playerName, out var synth)) return;

        string currentVoiceName = synth.Voice.Name;
        for (int i = 0; i < _availableVoices.Count; i++)
        {
            if (_availableVoices[i].Name == currentVoiceName)
            {
                _cmbVoice.SelectedIndex = i;
                break;
            }
        }
    }

    private void OnVoiceSelected(object? sender, EventArgs e)
    {
        if (_lstPlayers.SelectedItem is not string playerName) return;
        if (_cmbVoice.SelectedIndex < 0 || _cmbVoice.SelectedIndex >= _availableVoices.Count) return;
        if (!_playerSynths.TryGetValue(playerName, out var synth)) return;

        var newVoice = _availableVoices[_cmbVoice.SelectedIndex];
        synth.SelectVoice(newVoice.Name);
        _settings.PlayerVoiceMap[playerName] = newVoice.Name;
        SaveSettings();
    }

    private void OnTestVoice(object? sender, EventArgs e)
    {
        if (_lstPlayers.SelectedItem is not string playerName) return;
        SpeakForPlayer(playerName, $"안녕하세요, 저는 {playerName}입니다.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TTS 토글 / 번역 엔진 변경
    // ─────────────────────────────────────────────────────────────────────────

    private void OnToggleTts(object? sender, EventArgs e)
    {
        _settings.TtsEnabled = !_settings.TtsEnabled;
        UpdateTtsButton();
        SaveSettings();
    }

    private void UpdateTtsButton()
    {
        if (_btnToggleTts is null) return;
        _btnToggleTts.Text = _settings.TtsEnabled ? "🔊 TTS 켜짐" : "🔇 TTS 꺼짐";
        _btnToggleTts.BackColor = _settings.TtsEnabled ? AccentGreen : BgLight;
    }

    private void OnEngineChanged(object? sender, EventArgs e)
    {
        if (sender is RadioButton rb && rb.Checked)
        {
            _settings.TranslationEngine = _rbFast.Checked ? "argos" : "nllb";
            SaveSettings();
            // 엔진이 실행 중이면 설정 전달
            _engine?.SendEngineConfig(_settings.TranslationEngine);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  설정 저장
    // ─────────────────────────────────────────────────────────────────────────

    private void SaveSettings()
    {
        try
        {
            _settings.ChatReceiveLang = GetSelectedLangCode(_cmbReceiveLang);
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] 설정 저장 실패: {ex.Message}");
        }
    }

    private void CleanUp()
    {
        CleanUpVoice();
        _bridge?.Dispose();
        _bridge = null;
        foreach (var s in _playerSynths.Values) s.Dispose();
        _playerSynths.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  헬퍼
    // ─────────────────────────────────────────────────────────────────────────

    private ComboBox MakeLangCombo(bool includeAutoDetect = false)
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
        if (includeAutoDetect)
            cb.Items.Add(SupportedLanguages.AutoDetect);
        foreach (var lang in SupportedLanguages.CoreLanguages)
            cb.Items.Add(lang);
        cb.SelectedIndex = 0;
        return cb;
    }

    private static string GetSelectedLangCode(ComboBox cb)
        => (cb.SelectedItem as LangItem)?.Code ?? "ko";

    private static void SelectLang(ComboBox cb, string code)
    {
        for (int i = 0; i < cb.Items.Count; i++)
        {
            if ((cb.Items[i] as LangItem)?.Code == code) { cb.SelectedIndex = i; return; }
        }
    }

    private void Log(string message)
    {
        if (_rtbLog is null) return;
        if (InvokeRequired) { Invoke(() => Log(message)); return; }
        _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _rtbLog.ScrollToCaret();
    }

    private static Label MakeLabel(string text, bool bold = false, bool sub = false)
        => new Label
        {
            Text = text,
            ForeColor = sub ? TextSub : TextMain,
            Font = bold ? new Font("Segoe UI", 9.5f, FontStyle.Bold) : new Font("Segoe UI", 8.5f),
            AutoSize = true,
            BackColor = Color.Transparent,
        };

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
