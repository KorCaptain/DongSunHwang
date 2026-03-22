using System.Collections.Concurrent;
using HarmonyLib;
using UnityEngine;

namespace ValheimTranslatorMod;

/// <summary>
/// 발헤임 Chat 클래스를 Harmony로 패치합니다.
/// - Chat.AddString 후킹: 다른 플레이어의 공개 채팅 캡처 → PC 앱으로 전송
/// - Plugin.Update()에서 아웃고잉 큐 처리: 번역된 텍스트 → 게임 채팅 전송
/// </summary>
public static class ChatHook
{
    // PC 앱이 보내온 번역 텍스트를 Unity 메인 스레드에서 전송하기 위한 큐
    private static readonly ConcurrentQueue<string> _outgoingQueue = new();

    // 자신이 보낸 텍스트 (루프 방지용)
    private static readonly System.Collections.Generic.HashSet<string> _ownMessages = new();

    public static void EnqueueOutgoingChat(string text)
    {
        _outgoingQueue.Enqueue(text);
        _ownMessages.Add(text); // 자신이 보낸 텍스트로 등록
    }

    /// <summary>Plugin.Update()에서 호출: 큐에 쌓인 아웃고잉 채팅 전송.</summary>
    public static void ProcessPendingChats()
    {
        // 브릿지 큐 처리
        Plugin.Instance.Bridge.ProcessMainThreadQueue();

        // 아웃고잉 채팅 전송
        while (_outgoingQueue.TryDequeue(out var text))
        {
            if (Chat.instance != null)
                Chat.instance.SendText(Talker.Type.Normal, text);
        }
    }

    // ── Harmony 패치 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Chat.AddString 패치: 다른 플레이어의 공개 채팅을 캡처합니다.
    /// 발헤임에서 채팅이 UI에 추가될 때 호출됩니다.
    /// </summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.AddString), typeof(string), typeof(string), typeof(Talker.Type), typeof(bool))]
    [HarmonyPostfix]
    public static void AddString_Postfix(string user, string text, Talker.Type type)
    {
        // 공개 채팅만 처리
        if (type != Talker.Type.Normal) return;

        // 자신이 번역 앱을 통해 보낸 텍스트는 무시 (루프 방지)
        if (_ownMessages.Contains(text))
        {
            _ownMessages.Remove(text);
            return;
        }

        // 자신의 캐릭터 이름도 무시
        if (Player.m_localPlayer != null && user == Player.m_localPlayer.GetPlayerName())
            return;

        // 앱 연결 시 → 앱으로 전송 (앱이 번역)
        // 앱 미연결 시 → 스탠드얼론 번역기 사용
        Plugin.Instance.Bridge.HandleIncomingChat(user, text);
    }
}
