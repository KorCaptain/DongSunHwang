using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ValheimTranslatorMod;

/// <summary>
/// PC 앱(ValheimBridge TCP 서버)과 통신하는 클라이언트.
/// - 연결 끊김 시 3초마다 재연결 시도
/// - 앱 연결 여부에 따라 스탠드얼론 모드 자동 전환 지원
/// </summary>
public sealed class BridgeClient : IDisposable
{
    private readonly int _port;
    private TcpClient? _client;
    private StreamWriter? _writer;
    private Thread? _connectThread;
    private volatile bool _running = true;
    private volatile bool _connected;

    // ── 이벤트 ────────────────────────────────────────────────────────────────

    /// <summary>앱이 send_chat 메시지를 보내왔을 때. Unity 메인 스레드에서 처리.</summary>
    public event Action<string>? SendChatQueued;

    /// <summary>앱이 translation_result 메시지를 보내왔을 때. Unity 메인 스레드에서 처리.</summary>
    public event Action<TranslationArgs>? TranslationResultQueued;

    /// <summary>앱 연결 상태 변경 (true=연결, false=끊김).</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>
    /// 앱이 연결되지 않은 상태에서 채팅이 캡처됨.
    /// 스탠드얼론 번역을 트리거하는 데 사용.
    /// </summary>
    public event Action<string, string>? ChatCapturedWithoutApp;  // (author, text)

    public bool IsConnected => _connected;

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    public BridgeClient(int port = 7891)
    {
        _port = port;
    }

    public void Connect()
    {
        _connectThread = new Thread(ConnectLoop) { IsBackground = true, Name = "ValheimBridgeConnect" };
        _connectThread.Start();
    }

    private void ConnectLoop()
    {
        while (_running)
        {
            try
            {
                Plugin.Instance.Logger.LogInfo($"PC 앱 연결 시도 중... (127.0.0.1:{_port})");
                var client = new TcpClient();
                client.Connect("127.0.0.1", _port);
                _client = client;
                _writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };
                _connected = true;

                _mainThreadQueue.Enqueue(() => ConnectionChanged?.Invoke(true));

                ReadLoop(client);

                _connected = false;
                _mainThreadQueue.Enqueue(() => ConnectionChanged?.Invoke(false));
                Plugin.Instance.Logger.LogInfo("PC 앱 연결 끊김. 재연결 대기...");
            }
            catch (Exception ex) when (_running)
            {
                // 첫 연결 실패는 조용히 (앱 없이 사용 가능)
                Plugin.Instance.Logger.LogInfo($"PC 앱 연결 실패 (스탠드얼론 모드): {ex.Message}");
            }

            CloseClient();
            if (_running) Thread.Sleep(3000);
        }
    }

    private void ReadLoop(TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
            while (_running && client.Connected)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                HandleMessage(line);
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance.Logger.LogWarning($"ReadLoop error: {ex.Message}");
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var obj = JObject.Parse(json);
            var type = obj["type"]?.Value<string>();

            switch (type)
            {
                case "send_chat":
                {
                    var text = obj["text"]?.Value<string>() ?? string.Empty;
                    _mainThreadQueue.Enqueue(() => SendChatQueued?.Invoke(text));
                    break;
                }
                case "translation_result":
                {
                    var author     = obj["author"]?.Value<string>()     ?? "Unknown";
                    var original   = obj["original"]?.Value<string>()   ?? string.Empty;
                    var translated = obj["translated"]?.Value<string>() ?? string.Empty;
                    var args = new TranslationArgs(author, original, translated);
                    _mainThreadQueue.Enqueue(() => TranslationResultQueued?.Invoke(args));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance.Logger.LogWarning($"JSON parse error: {ex.Message}");
        }
    }

    /// <summary>Unity 메인 스레드에서 처리할 큐를 소비합니다.</summary>
    public void ProcessMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
            action?.Invoke();
    }

    /// <summary>
    /// 발헤임 채팅 수신 내용을 처리합니다.
    /// - 앱 연결 시: PC 앱으로 전송 (앱이 번역 후 translation_result 응답)
    /// - 앱 미연결 시: ChatCapturedWithoutApp 이벤트 발생 (스탠드얼론 번역 트리거)
    /// </summary>
    public void HandleIncomingChat(string author, string text)
    {
        if (_connected)
        {
            // 앱 연결 모드: 앱으로 전송
            Send(new JObject
            {
                ["type"] = "chat_received",
                ["author"] = author,
                ["text"] = text,
            });
        }
        else
        {
            // 스탠드얼론 모드: 로컬/웹 번역 사용
            _mainThreadQueue.Enqueue(() => ChatCapturedWithoutApp?.Invoke(author, text));
        }
    }

    private void Send(JObject obj)
    {
        if (!_connected || _writer == null) return;
        try
        {
            _writer.WriteLine(obj.ToString(Formatting.None));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Logger.LogWarning($"Send error: {ex.Message}");
            _connected = false;
        }
    }

    private void CloseClient()
    {
        try { _writer?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
        _writer = null;
        _client = null;
    }

    public void Dispose()
    {
        _running = false;
        CloseClient();
    }
}

public record TranslationArgs(string Author, string Original, string Translated);
