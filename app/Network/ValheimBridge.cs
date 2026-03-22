using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ValheimVoiceTranslator.Network;

/// <summary>
/// 발헤임 BepInEx 모드와 TCP로 통신하는 브릿지 서버.
/// 포트 7891(기본)에서 단일 클라이언트 연결을 수락합니다.
///
/// 프로토콜 (줄바꿈 구분 JSON):
///   서버 → 모드: {"type":"send_chat","text":"..."}
///   서버 → 모드: {"type":"translation_result","author":"...","original":"...","translated":"..."}
///   모드 → 서버: {"type":"chat_received","author":"...","text":"..."}
/// </summary>
public sealed class ValheimBridge : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private TcpClient? _client;
    private StreamWriter? _writer;
    private Thread? _acceptThread;
    private Thread? _readerThread;
    private volatile bool _running;

    public event EventHandler<ChatReceivedArgs>? ChatReceived;
    public event EventHandler<bool>? ConnectionChanged;

    public bool IsConnected => _client?.Connected == true;

    public ValheimBridge(int port = 7891)
    {
        _port = port;
    }

    public void Start()
    {
        _running = true;
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "ValheimBridgeAccept" };
        _acceptThread.Start();
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                // 기존 클라이언트 정리
                CloseClient();

                var client = _listener!.AcceptTcpClient();
                _client = client;
                _writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };

                ConnectionChanged?.Invoke(this, true);

                _readerThread = new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "ValheimBridgeReader" };
                _readerThread.Start();
                _readerThread.Join(); // 연결이 끊길 때까지 대기

                ConnectionChanged?.Invoke(this, false);
            }
            catch (Exception ex) when (_running)
            {
                System.Diagnostics.Debug.WriteLine($"[ValheimBridge] AcceptLoop error: {ex.Message}");
                Thread.Sleep(1000);
            }
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
                if (line is null) break;
                HandleMessage(line);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ValheimBridge] ReadLoop error: {ex.Message}");
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var obj = JObject.Parse(json);
            var type = obj["type"]?.Value<string>();

            if (type == "chat_received")
            {
                var author = obj["author"]?.Value<string>() ?? "Unknown";
                var text = obj["text"]?.Value<string>() ?? string.Empty;
                ChatReceived?.Invoke(this, new ChatReceivedArgs(author, text));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ValheimBridge] JSON parse error: {ex.Message}");
        }
    }

    /// <summary>번역된 텍스트를 발헤임 공개 채팅으로 전송하도록 모드에 지시합니다.</summary>
    public void SendChat(string text)
    {
        Send(new JObject { ["type"] = "send_chat", ["text"] = text });
    }

    /// <summary>번역 결과를 모드의 인게임 번역 채팅창에 표시하도록 전송합니다.</summary>
    public void SendTranslationResult(string author, string original, string translated)
    {
        Send(new JObject
        {
            ["type"] = "translation_result",
            ["author"] = author,
            ["original"] = original,
            ["translated"] = translated,
        });
    }

    private void Send(JObject obj)
    {
        try
        {
            _writer?.WriteLine(obj.ToString(Formatting.None));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ValheimBridge] Send error: {ex.Message}");
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
        try { _listener?.Stop(); } catch { }
    }
}

public record ChatReceivedArgs(string Author, string Text);
