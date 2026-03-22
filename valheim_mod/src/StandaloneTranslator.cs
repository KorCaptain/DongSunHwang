using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ValheimTranslatorMod;

/// <summary>
/// PC 앱 없이 모드 단독 설치 시 사용하는 번역 엔진.
/// MyMemory 무료 API를 사용합니다 (인터넷 연결 필요, API 키 불필요).
///
/// API: https://api.mymemory.translated.net/get?q={text}&langpair={src}|{tgt}
/// 무료 한도: IP당 하루 5,000자 (충분한 게임 채팅 번역 지원)
/// </summary>
public sealed class StandaloneTranslator
{
    private const string ApiBase = "https://api.mymemory.translated.net/get";

    // 비동기 번역 요청 큐 (백그라운드 스레드에서 처리)
    private readonly ConcurrentQueue<TranslateRequest> _queue = new();
    private readonly Thread _workerThread;
    private volatile bool _running = true;

    public event Action<string, string, string, string>? TranslationComplete;
    // author, original, translated, request_id

    public StandaloneTranslator()
    {
        _workerThread = new Thread(ProcessQueue) { IsBackground = true, Name = "StandaloneTranslator" };
        _workerThread.Start();
    }

    /// <summary>번역 요청을 큐에 추가합니다.</summary>
    public void RequestTranslation(string text, string author, string targetLang, string requestId = "")
    {
        _queue.Enqueue(new TranslateRequest(text, author, targetLang, requestId));
    }

    private void ProcessQueue()
    {
        while (_running)
        {
            if (_queue.TryDequeue(out var req))
            {
                try
                {
                    string translated = Translate(req.Text, "auto", req.TargetLang);
                    TranslationComplete?.Invoke(req.Author, req.Text, translated, req.RequestId);
                }
                catch (Exception ex)
                {
                    Plugin.Instance.Logger.LogWarning($"[StandaloneTranslator] 번역 실패: {ex.Message}");
                    // 번역 실패 시 원문 표시
                    TranslationComplete?.Invoke(req.Author, req.Text, req.Text, req.RequestId);
                }
            }
            else
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string Translate(string text, string srcLang, string tgtLang)
    {
        // 텍스트가 너무 길면 잘라내기 (API 한도)
        if (text.Length > 500) text = text[..500];

        string langPair = $"{srcLang}|{tgtLang}";
        string url = $"{ApiBase}?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(langPair)}";

        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = 8000; // 8초 타임아웃
        request.UserAgent = "ValheimTranslatorMod/1.0";

        using var response = (HttpWebResponse)request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
        string json = reader.ReadToEnd();

        var obj = JObject.Parse(json);
        int responseStatus = obj["responseStatus"]?.Value<int>() ?? 0;

        if (responseStatus == 200)
        {
            return obj["responseData"]?["translatedText"]?.Value<string>() ?? text;
        }
        else
        {
            Plugin.Instance.Logger.LogWarning($"[StandaloneTranslator] API 응답 오류: {responseStatus}");
            return text;
        }
    }

    public void Dispose()
    {
        _running = false;
    }

    private record TranslateRequest(string Text, string Author, string TargetLang, string RequestId);
}
