using Newtonsoft.Json;

namespace DiscordVoiceTranslator.UI;

/// <summary>
/// Application settings (mirrors config/settings.json structure).
/// </summary>
public class AppSettings
{
    [JsonProperty("source_lang")]
    public string SourceLang { get; set; } = "en";

    [JsonProperty("target_lang")]
    public string TargetLang { get; set; } = "ko";

    [JsonProperty("capture_mode")]
    public CaptureMode CaptureMode { get; set; } = CaptureMode.Loopback;

    [JsonProperty("microphone_device")]
    public int MicrophoneDevice { get; set; } = 0;

    [JsonProperty("subtitle_duration_ms")]
    public int SubtitleDurationMs { get; set; } = 5000;

    [JsonProperty("show_original")]
    public bool ShowOriginal { get; set; } = true;

    [JsonProperty("ai_engine")]
    public AiEngineSettings AiEngine { get; set; } = new();
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

    [JsonProperty("source_lang")]
    public string SourceLang { get; set; } = "en";

    [JsonProperty("target_lang")]
    public string TargetLang { get; set; } = "ko";
}

public enum CaptureMode
{
    Loopback,
    Microphone,
}
