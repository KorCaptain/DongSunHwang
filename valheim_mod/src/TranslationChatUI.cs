using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

namespace ValheimTranslatorMod;

/// <summary>
/// 발헤임 인게임에 별도 번역 채팅창을 추가합니다.
/// IMGUI를 사용하며, 기존 Chat UI 아래에 렌더링됩니다.
///
/// UI 구조:
///   [번역 채팅] [일반 채팅]   ← 탭 선택기
///   ┌──────────────────────┐
///   │ [Erik] Hello there!  │  ← 원문 표시
///   │ [Sven] Let me trade  │
///   └──────────────────────┘
///
/// - "번역 채팅" 탭: PC 앱에서 받은 translation_result 표시
/// - "일반 채팅" 탭: 기존 Chat UI와 동일한 영역 (단순 안내)
/// </summary>
public class TranslationChatUI
{
    private enum Tab { Translation, Normal }
    private Tab _activeTab = Tab.Translation;

    private readonly List<ChatEntry> _entries = new();
    private readonly ConcurrentQueue<TranslationArgs> _pendingResults = new();

    private Vector2 _scrollPos;

    // IMGUI 스타일
    private GUIStyle? _windowStyle;
    private GUIStyle? _labelStyle;
    private GUIStyle? _tabStyle;
    private GUIStyle? _activeTabStyle;
    private bool _stylesInitialized;

    // 창 위치/크기
    private Rect _windowRect = new Rect(10, Screen.height - 320, 480, 280);
    private const int WindowId = 47891;

    // 최대 표시 줄 수
    private const int MaxEntries = 60;

    // 플레이어별 색상
    private static readonly Color[] PlayerColors =
    {
        new Color(0.45f, 0.73f, 1f),
        new Color(1f, 0.80f, 0.43f),
        new Color(0.33f, 0.94f, 0.77f),
        new Color(1f, 0.46f, 0.46f),
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
        // 발헤임 UI가 열려있을 때만 표시 (인벤토리 등 열리면 숨김)
        if (InventoryGui.IsVisible()) return;
        if (Menu.IsVisible()) return;

        // 대기 중인 번역 결과 처리
        while (_pendingResults.TryDequeue(out var args))
            AddEntry(args.Author, args.Original);

        InitStyles();

        _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "");
    }

    private void DrawWindow(int id)
    {
        InitStyles();

        // ── 탭 버튼 ──
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("번역 채팅", _activeTab == Tab.Translation ? _activeTabStyle : _tabStyle, GUILayout.Width(100)))
            _activeTab = Tab.Translation;
        if (GUILayout.Button("일반 채팅", _activeTab == Tab.Normal ? _activeTabStyle : _tabStyle, GUILayout.Width(100)))
            _activeTab = Tab.Normal;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        if (_activeTab == Tab.Translation)
            DrawTranslationTab();
        else
            DrawNormalTab();

        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 24));
    }

    private void DrawTranslationTab()
    {
        _scrollPos = GUILayout.BeginScrollView(_scrollPos,
            GUILayout.Width(_windowRect.width - 16),
            GUILayout.Height(_windowRect.height - 60));

        foreach (var entry in _entries)
        {
            Color savedColor = GUI.color;
            GUI.color = entry.Color;
            GUILayout.Label($"[{entry.Author}]", _labelStyle!);
            GUI.color = savedColor;
            GUILayout.Label($"  {entry.Text}", _labelStyle!);
            GUILayout.Space(2);
        }

        GUILayout.EndScrollView();
    }

    private void DrawNormalTab()
    {
        GUILayout.Label("일반 채팅은 발헤임 기본 채팅창(Enter)을 사용하세요.", _labelStyle!);
        GUILayout.Label("(번역 앱의 음성 번역 시작 시 자동으로 채팅이 전송됩니다)", _labelStyle!);
    }

    private void AddEntry(string author, string text)
    {
        if (!_playerColors.ContainsKey(author))
        {
            _playerColors[author] = PlayerColors[_colorIdx % PlayerColors.Length];
            _colorIdx++;
        }

        _entries.Add(new ChatEntry(author, text, _playerColors[author]));

        // 최대 개수 초과 시 오래된 것 제거
        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);

        // 스크롤 맨 아래로
        _scrollPos = new Vector2(0, float.MaxValue);
    }

    private void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            normal = { background = MakeTex(2, 2, new Color(0.12f, 0.13f, 0.15f, 0.92f)) },
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.86f, 0.87f, 0.87f) },
            wordWrap = true,
        };

        _tabStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            normal  = { background = MakeTex(2, 2, new Color(0.2f, 0.21f, 0.23f, 1f)) },
            hover   = { background = MakeTex(2, 2, new Color(0.25f, 0.26f, 0.28f, 1f)) },
        };

        _activeTabStyle = new GUIStyle(_tabStyle)
        {
            normal = { background = MakeTex(2, 2, new Color(0.34f, 0.40f, 0.95f, 1f)) },
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
