using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimTranslatorMod;

/// <summary>
/// 발헤임 번역 채팅 BepInEx 플러그인 진입점.
///
/// 동작 모드:
///   1. 앱 연결 모드: PC의 Valheim Voice Chat Translator 앱과 TCP로 연결
///      - 음성 번역, 정확도 높은 로컬 번역 엔진 사용
///   2. 스탠드얼론 모드 (앱 없이 모드만 설치):
///      - MyMemory 무료 웹 API로 자동 번역
///      - 인터넷 연결 필요, 하루 5,000자 무료
///
/// 설치:
///   - 이 DLL을 발헤임 BepInEx/plugins/ 폴더에 복사
///   - (선택) PC 앱 Valheim Voice Chat Translator 실행 → 연결 자동 감지
/// </summary>
[BepInPlugin("com.dongsunhwang.valheim-translator", "Valheim Translator", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;

    private Harmony _harmony = null!;
    private BridgeClient _bridge = null!;
    private TranslationChatUI _chatUI = null!;
    private StandaloneTranslator? _standaloneTranslator;

    // BepInEx 설정 (BepInEx/config/ValheimTranslatorMod.cfg)
    private ConfigEntry<int>    _cfgPort          = null!;
    private ConfigEntry<string> _cfgTargetLang    = null!;
    private ConfigEntry<bool>   _cfgAutoTranslate = null!;

    private void Awake()
    {
        Instance = this;

        // 설정 파일 정의
        _cfgPort          = Config.Bind("General", "BridgePort",      7891,  "PC 앱과 통신할 TCP 포트");
        _cfgTargetLang    = Config.Bind("General", "TargetLanguage",  "ko",  "번역 대상 언어 코드 (ko, en, ja, de, ru, pt, zh)");
        _cfgAutoTranslate = Config.Bind("General", "AutoTranslate",   true,  "앱 미연결 시 자동으로 웹 번역 API 사용");

        Logger.LogInfo($"Valheim Translator 로드됨. 포트:{_cfgPort.Value}, 언어:{_cfgTargetLang.Value}");

        // 번역 채팅 UI 초기화
        _chatUI = new TranslationChatUI();

        // TCP 브릿지 시작 (앱 연결 모드)
        _bridge = new BridgeClient(port: _cfgPort.Value);
        _bridge.SendChatQueued += text => ChatHook.EnqueueOutgoingChat(text);
        _bridge.TranslationResultQueued += args =>
        {
            // 앱이 보내온 번역 결과 → 인게임 번역 채팅창에 표시
            _chatUI.EnqueueTranslation(args);
        };
        _bridge.ConnectionChanged += connected =>
        {
            if (connected)
            {
                Logger.LogInfo("PC 앱 연결됨. 앱 번역 엔진 사용.");
                // 스탠드얼론 번역기 비활성화 (앱이 처리)
            }
            else
            {
                Logger.LogInfo("PC 앱 연결 끊김. 스탠드얼론 번역 모드로 전환.");
                EnsureStandaloneTranslator();
            }
        };
        _bridge.ChatCapturedWithoutApp += (author, text) =>
        {
            // 앱이 연결되지 않은 상태에서 채팅 수신 → 스탠드얼론 번역
            if (_cfgAutoTranslate.Value)
                EnsureStandaloneTranslator()?.RequestTranslation(text, author, _cfgTargetLang.Value);
        };
        _bridge.Connect();

        // Harmony 패치 적용
        _harmony = new Harmony("com.dongsunhwang.valheim-translator");
        _harmony.PatchAll();

        Logger.LogInfo("Harmony 패치 적용 완료.");
    }

    /// <summary>스탠드얼론 번역기를 필요 시 초기화합니다.</summary>
    private StandaloneTranslator? EnsureStandaloneTranslator()
    {
        if (!_cfgAutoTranslate.Value) return null;
        if (_standaloneTranslator != null) return _standaloneTranslator;

        _standaloneTranslator = new StandaloneTranslator();
        _standaloneTranslator.TranslationComplete += (author, original, translated, _) =>
        {
            var args = new TranslationArgs(author, original, translated);
            _chatUI.EnqueueTranslation(args);
        };
        Logger.LogInfo("스탠드얼론 번역기 초기화됨 (MyMemory 웹 API).");
        return _standaloneTranslator;
    }

    private void Update()
    {
        // Unity 메인 스레드에서 채팅 처리
        ChatHook.ProcessPendingChats();
    }

    private void OnGUI()
    {
        _chatUI?.DrawUI();
    }

    private void OnDestroy()
    {
        _standaloneTranslator?.Dispose();
        _bridge?.Dispose();
        _harmony?.UnpatchSelf();
    }

    internal BridgeClient Bridge => _bridge;
    internal string TargetLang => _cfgTargetLang.Value;
}
