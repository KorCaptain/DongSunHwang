using Newtonsoft.Json;
using System.Globalization;

namespace ValheimVoiceTranslator.UI;

/// <summary>
/// 지원 언어 정의. 핵심 7개 언어 + 자동감지.
/// </summary>
public static class SupportedLanguages
{
    public static readonly LangItem AutoDetect = new("auto", "자동 감지 (Auto Detect)", "🌐");

    public static readonly LangItem[] CoreLanguages =
    {
        new("ko", "한국어",          "🇰🇷"),
        new("en", "English",         "🇺🇸"),
        new("de", "Deutsch",         "🇩🇪"),
        new("ja", "日本語",           "🇯🇵"),
        new("ru", "Русский",         "🇷🇺"),
        new("pt", "Português-BR",    "🇧🇷"),
        new("zh", "中文 (简体)",       "🇨🇳"),
    };

    /// <summary>운영체제 UI 언어를 지원 언어 코드로 매핑합니다.</summary>
    public static string DetectOsLanguage()
    {
        string twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        // pt-BR 계열 처리
        if (twoLetter == "pt") return "pt";
        return Array.Exists(CoreLanguages, l => l.Code == twoLetter) ? twoLetter : "en";
    }
}

/// <summary>언어 항목 (ComboBox 표시용)</summary>
public record LangItem(string Code, string DisplayName, string Flag = "")
{
    public override string ToString() => string.IsNullOrEmpty(Flag)
        ? DisplayName
        : $"{Flag}  {DisplayName}";
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 앱 전체 설정 (config/settings.json 구조 반영).
/// </summary>
public class AppSettings
{
    // ── 언어 ────────────────────────────────────────────────────────────────

    /// <summary>입력(말하는 사람) 언어 코드. "auto" = Whisper 자동감지.</summary>
    [JsonProperty("source_lang")]
    public string SourceLang { get; set; } = "auto";

    /// <summary>출력(듣는 사람) 언어 코드. "auto" = OS 언어 자동감지.</summary>
    [JsonProperty("target_lang")]
    public string TargetLang { get; set; } = "auto";

    // ── 캡처 ────────────────────────────────────────────────────────────────

    [JsonProperty("capture_mode")]
    public CaptureMode CaptureMode { get; set; } = CaptureMode.Loopback;

    /// <summary>
    /// WASAPI 루프백 디바이스 ID (null = 시스템 기본).
    /// VB-Audio Virtual Cable 선택 시 해당 MMDevice.ID가 저장됨.
    /// </summary>
    [JsonProperty("capture_device_id")]
    public string? CaptureDeviceId { get; set; }

    /// <summary>사람이 읽을 수 있는 캡처 디바이스 이름 (저장/표시용).</summary>
    [JsonProperty("capture_device_name")]
    public string? CaptureDeviceName { get; set; }

    /// <summary>마이크 WaveIn 디바이스 번호.</summary>
    [JsonProperty("microphone_device")]
    public int MicrophoneDevice { get; set; } = 0;

    // ── 자막 ────────────────────────────────────────────────────────────────

    [JsonProperty("subtitle_duration_ms")]
    public int SubtitleDurationMs { get; set; } = 5000;

    [JsonProperty("show_original")]
    public bool ShowOriginal { get; set; } = true;

    // ── AI 엔진 ─────────────────────────────────────────────────────────────

    [JsonProperty("ai_engine")]
    public AiEngineSettings AiEngine { get; set; } = new();

    // ── 발헤임 연동 ──────────────────────────────────────────────────────────

    /// <summary>ValheimBridge TCP 서버 포트.</summary>
    [JsonProperty("valheim_bridge_port")]
    public int ValheimBridgePort { get; set; } = 7891;

    /// <summary>TTS 활성화 여부.</summary>
    [JsonProperty("tts_enabled")]
    public bool TtsEnabled { get; set; } = true;

    /// <summary>음성 입력 언어 (마이크 모드 발화 언어).</summary>
    [JsonProperty("voice_source_lang")]
    public string VoiceSourceLang { get; set; } = "ko";

    /// <summary>발헤임 채팅 전송 번역 언어.</summary>
    [JsonProperty("voice_target_lang")]
    public string VoiceTargetLang { get; set; } = "en";

    /// <summary>수신 채팅 번역 언어.</summary>
    [JsonProperty("chat_receive_lang")]
    public string ChatReceiveLang { get; set; } = "ko";

    /// <summary>번역 엔진: "argos" (빠름) or "nllb" (정확).</summary>
    [JsonProperty("translation_engine")]
    public string TranslationEngine { get; set; } = "argos";

    /// <summary>플레이어명 → TTS 음성 이름 매핑 (재시작 시 유지).</summary>
    [JsonProperty("player_voice_map")]
    public Dictionary<string, string> PlayerVoiceMap { get; set; } = new();
}

public class AiEngineSettings
{
    [JsonProperty("python_exe")]
    public string PythonExe { get; set; } = "python";

    [JsonProperty("whisper_model_size")]
    public string WhisperModelSize { get; set; } = "medium";

    [JsonProperty("whisper_model_path")]
    public string? WhisperModelPath { get; set; }

    [JsonProperty("nllb_model_name")]
    public string NllbModelName { get; set; } = "facebook/nllb-200-distilled-600M";

    [JsonProperty("nllb_model_path")]
    public string? NllbModelPath { get; set; }

    [JsonProperty("vad_threshold")]
    public float VadThreshold { get; set; } = 0.5f;
}

public enum CaptureMode
{
    /// <summary>시스템 기본 루프백</summary>
    Loopback,
    /// <summary>특정 WASAPI 디바이스 루프백 (VB-Audio Virtual Cable)</summary>
    DeviceLoopback,
    /// <summary>마이크 입력</summary>
    Microphone,
}
