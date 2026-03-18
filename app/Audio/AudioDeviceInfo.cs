namespace DiscordVoiceTranslator.Audio;

/// <summary>
/// 오디오 캡처 디바이스 정보 모델.
/// </summary>
public sealed record AudioDeviceInfo(string? Id, string Name, bool IsVirtualCable)
{
    /// <summary>시스템 기본 루프백 (VB-Cable 없이도 동작)</summary>
    public static readonly AudioDeviceInfo SystemDefault =
        new(null, "시스템 기본 (Default Loopback)", false);

    public override string ToString() => IsVirtualCable ? $"★ {Name}" : Name;
}
