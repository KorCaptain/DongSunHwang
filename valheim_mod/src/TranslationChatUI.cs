using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

namespace ValheimTranslatorMod;

/// <summary>
/// 발헤임 인게임에 독립적인 번역 채팅창을 추가합니다.
/// IMGUI를 사용하며, 화면 어디서나 드래그로 이동 가능한 별도 창입니다.
///
/// 창 구조:
///   ┌────────────────────────────────┐
///   │ ⚔ 번역 채팅  [─]              │  ← 헤더 (드래그 이동)
///   │ [Erik] Hello there!            │  ← 원문 채팅 표시
///   │ [Sven] Let me trade            │
///   ├────────────────────────────────┤
///   │ 채팅 전송: ◉ 일반  ○ 외침     │  ← 채팅 타입 선택
///   └────────────────────────────────┘
/// </summary>
public class TranslationChatUI
{
    // 채팅 전송 타입
    public enum ChatSendType { Normal, Shout }
    public static ChatSendType SelectedSendType { get; private set; } = ChatSendType.Normal;

    private readonly List<ChatEntry> _entries = new();
    private readonly ConcurrentQueue<TranslationArgs> _pendingResults = new();

    private Vector2 _scrollPos;
    private bool _isMinimized;

    // IMGUI 스타일
    private GUIStyle? _windowStyle;
    private GUIStyle? _headerStyle;
    private GUIStyle? _labelStyle;
    private GUIStyle? _radioStyle;
    private GUIStyle? _miniButtonStyle;
    private bool _stylesInitialized;

    // 창 위치/크기
    private Rect _windowRect;
    private bool _positionInitialized;
    private const int WindowId = 47891;
    private const float WindowW = 500f;
    private const float WindowH = 300f;
    private const float WindowHMin = 48f;

    // 최대 표시 줄 수
    private const int MaxEntries = 80;

    // 플레이어별 색상
    private static readonly Color[] PlayerColors =
    {
        new Color(0.45f, 0.73f, 1f),
        new Color(1f,    0.80f, 0.43f),
        new Color(0.33f, 0.94f, 0.77f),
        new Color(1f,    0.46f, 0.46f),
        new Color(0.64f, 0.61f, 1f),
        new Color(0.98f, 0.69f, 0.63f),
    };
    private readonly Dictionary<string, Color> _playerColors = new();
    private int _colorIdx;

    public void EnqueueTranslation(TranslationArgs args)
    {
        _pendingResults.Enqueue(args);
    }

    public void DrawUI()
    {
        // 발헤임 일부 UI 오버레이와 겹치지 않도록 기본 조건 유지
        // (인벤토리나 메뉴는 닫아도 번역창은 유지 - 독립 창이므로)

        // 초기 위치 설정 (화면 우하단)
        if (!_positionInitialized)
        {
            _windowRect = new Rect(
                Screen.width - WindowW - 20,
                Screen.height - WindowH - 20,
                WindowW,
                _isMinimized ? WindowHMin : WindowH
            );
            _positionInitialized = true;
        }

        // 대기 중인 번역 결과 처리
        while (_pendingResults.TryDequeue(out var args))
            AddEntry(args.Author, args.Original);

        InitStyles();

        _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "");
    }

    private void DrawWindow(int id)
    {
        InitStyles();

        // ── 헤더 ────────────────────────────────────────────────────────────
        GUILayout.BeginHorizontal(_headerStyle!);
        GUILayout.Label("⚔ 번역 채팅", _headerStyle!);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(_isMinimized ? "▲" : "▼", _miniButtonStyle!, GUILayout.Width(24), GUILayout.Height(22)))
        {
            _isMinimized = !_isMinimized;
            _windowRect.height = _isMinimized ? WindowHMin : WindowH;
        }
        GUILayout.EndHorizontal();

        if (_isMinimized)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, _windowRect.height));
            return;
        }

        GUILayout.Space(2);

        // ── 채팅 로그 영역 ───────────────────────────────────────────────────
        float chatAreaH = _windowRect.height - 90f;
        _scrollPos = GUILayout.BeginScrollView(_scrollPos,
            GUILayout.Width(_windowRect.width - 12),
            GUILayout.Height(chatAreaH));

        foreach (var entry in _entries)
        {
            GUILayout.BeginHorizontal();

            // 플레이어명 (색상)
            var savedColor = GUI.contentColor;
            GUI.contentColor = entry.Color;
            GUILayout.Label($"[{entry.Author}]", _labelStyle!, GUILayout.Width(90));
            GUI.contentColor = savedColor;

            // 원문
            GUILayout.Label(entry.Text, _labelStyle!);
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        // ── 구분선 ────────────────────────────────────────────────────────────
        GUILayout.Space(4);
        var sepRect = GUILayoutUtility.GetRect(_windowRect.width - 12, 1);
        GUI.DrawTexture(sepRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true,
            0, new Color(0.3f, 0.3f, 0.35f, 0.8f), 0, 0);
        GUILayout.Space(4);

        // ── 채팅 전송 타입 선택 ────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        GUILayout.Label("채팅 전송:", _labelStyle!, GUILayout.Width(70));

        bool isNormal = SelectedSendType == ChatSendType.Normal;
        bool isShout  = SelectedSendType == ChatSendType.Shout;

        // 일반 채팅 라디오
        var normalBg = isNormal ? new Color(0.34f, 0.40f, 0.95f, 0.9f) : new Color(0.2f, 0.21f, 0.23f, 0.9f);
        var oldBg = GUI.backgroundColor;
        GUI.backgroundColor = normalBg;
        if (GUILayout.Button("◉ 일반", _radioStyle!, GUILayout.Width(72), GUILayout.Height(24)))
            SelectedSendType = ChatSendType.Normal;

        // 외침 라디오
        var shoutBg = isShout ? new Color(0.85f, 0.36f, 0.13f, 0.9f) : new Color(0.2f, 0.21f, 0.23f, 0.9f);
        GUI.backgroundColor = shoutBg;
        if (GUILayout.Button("📢 외침", _radioStyle!, GUILayout.Width(72), GUILayout.Height(24)))
            SelectedSendType = ChatSendType.Shout;

        GUI.backgroundColor = oldBg;
        GUILayout.FlexibleSpace();
        GUILayout.Label($"({_entries.Count}줄)", _labelStyle!);
        GUILayout.EndHorizontal();

        // 드래그 핸들 (헤더 영역)
        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 30));
    }

    private void AddEntry(string author, string text)
    {
        if (!_playerColors.ContainsKey(author))
        {
            _playerColors[author] = PlayerColors[_colorIdx % PlayerColors.Length];
            _colorIdx++;
        }

        _entries.Add(new ChatEntry(author, text, _playerColors[author]));

        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);

        // 스크롤 맨 아래로
        _scrollPos = new Vector2(0, float.MaxValue);
    }

    private void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        var darkBg = MakeTex(2, 2, new Color(0.10f, 0.11f, 0.13f, 0.93f));
        var headerBg = MakeTex(2, 2, new Color(0.16f, 0.17f, 0.20f, 0.97f));

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(6, 6, 6, 6),
        };
        _windowStyle.normal.background = darkBg;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.65f, 0.20f) },
            padding = new RectOffset(4, 4, 2, 2),
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.88f, 0.88f, 0.90f) },
            wordWrap = true,
            padding = new RectOffset(2, 2, 1, 1),
        };

        _radioStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
        };

        _miniButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            padding = new RectOffset(2, 2, 1, 1),
        };
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var tex = new Texture2D(w, h);
        var pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private record ChatEntry(string Author, string Text, Color Color);
}
