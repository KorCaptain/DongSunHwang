using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordVoiceTranslator.Audio;

/// <summary>
/// Manages the Python AI Engine subprocess and handles JSON protocol I/O.
/// </summary>
public sealed class AiEngineClient : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private Thread? _readerThread;
    private volatile bool _running;

    public event EventHandler<TranslationResult>? ResultReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler? EngineReady;

    public bool IsReady { get; private set; }

    public void Start(string pythonExe, string mainPyPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{mainPyPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Python process.");
        _stdin = _process.StandardInput;
        _running = true;

        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "AiEngineReader" };
        _readerThread.Start();

        // Forward stderr to debug output
        Task.Run(async () =>
        {
            while (!_process.StandardError.EndOfStream)
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line is not null)
                    Debug.WriteLine($"[AI Engine] {line}");
            }
        });
    }

    private void ReadLoop()
    {
        while (_running && _process is not null && !_process.HasExited)
        {
            try
            {
                var line = _process.StandardOutput.ReadLine();
                if (line is null) break;
                HandleMessage(line);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiEngineClient] ReadLoop error: {ex.Message}");
            }
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
                case "ready":
                    IsReady = true;
                    EngineReady?.Invoke(this, EventArgs.Empty);
                    break;

                case "result":
                    var result = new TranslationResult(
                        obj["text"]?.Value<string>() ?? string.Empty,
                        obj["translation"]?.Value<string>() ?? string.Empty,
                        obj["source_lang"]?.Value<string>() ?? string.Empty,
                        obj["target_lang"]?.Value<string>() ?? string.Empty
                    );
                    ResultReceived?.Invoke(this, result);
                    break;

                case "error":
                    ErrorReceived?.Invoke(this, obj["message"]?.Value<string>() ?? "Unknown error");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiEngineClient] JSON parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a PCM float32 audio chunk to the AI engine for processing.
    /// </summary>
    public void SendAudio(float[] samples, string sourceLang, string targetLang)
    {
        if (!IsReady || _stdin is null) return;

        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var b64 = Convert.ToBase64String(bytes);

        var msg = new JObject
        {
            ["type"] = "audio",
            ["data"] = b64,
            ["source_lang"] = sourceLang,
            ["target_lang"] = targetLang,
        };
        SendMessage(msg);
    }

    /// <summary>
    /// Send a config update to the AI engine.
    /// </summary>
    public void SendConfig(string sourceLang, string targetLang, float vadThreshold)
    {
        if (_stdin is null) return;
        var msg = new JObject
        {
            ["type"] = "config",
            ["source_lang"] = sourceLang,
            ["target_lang"] = targetLang,
            ["vad_threshold"] = vadThreshold,
        };
        SendMessage(msg);
    }

    private void SendMessage(JObject msg)
    {
        try
        {
            _stdin?.WriteLine(msg.ToString(Formatting.None));
            _stdin?.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiEngineClient] Send error: {ex.Message}");
        }
    }

    public void Shutdown()
    {
        _running = false;
        try
        {
            SendMessage(new JObject { ["type"] = "shutdown" });
            _process?.WaitForExit(3000);
            _process?.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        Shutdown();
        _process?.Dispose();
        _stdin?.Dispose();
    }
}

public record TranslationResult(string Text, string Translation, string SourceLang, string TargetLang);
