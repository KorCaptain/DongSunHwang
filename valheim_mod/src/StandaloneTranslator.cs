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
/// 비공식 Google Translate API를 사용합니다 (완전 무료, 글자 수 제한 없음, API 키 불필요).
///
/// 엔드포인트:
///   https://translate.googleapis.com/translate_a/single
///     ?client=gtx&sl={source}&tl={target}&dt=t&q={text}
///
/// 응답 형식: JSON 배열, [0][0][0]에 번역 결과
/// </summary>
public sealed class StandaloneTranslator
{
    private const string ApiBase = "https://translate.googleapis.com/translate_a/single";

    private readonly ConcurrentQueue<TranslateRequest> _queue = new();
    private readonly Thread _workerThread;
    private volatile bool _running = true;

    /// <summary>번역 완료 시 발생 (author, original, translated, requestId).</summary>
    public event Action<string, string, string, string>? TranslationComplete;

    public StandaloneTranslator()
    {
        _workerThread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "StandaloneTranslator",
        };
        _workerThread.Start();
        Plugin.Instance.Logger.LogInfo("스탠드얼론 번역기 초기화됨 (비공식 Google Translate API, 무료 무제한).");
    }

    /// <summary>번역 요청을 큐에 추가합니다 (비동기).</summary>
    public void RequestTranslation(string text, string author, string targetLang, string requestId = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
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
                    // 번역 실패 시 원문 그대로 표시
                    TranslationComplete?.Invoke(req.Author, req.Text, req.Text, req.RequestId);
                }
            }
            else
            {
                Thread.Sleep(30);
            }
        }
    }

    private static string Translate(string text, string srcLang, string tgtLang)
    {
        // 너무 긴 텍스트는 잘라내기 (단일 채팅은 일반적으로 짧음)
        if (text.Length > 800) text = text[..800];

        string url = string.Format(
            "{0}?client=gtx&sl={1}&tl={2}&dt=t&q={3}",
            ApiBase,
            Uri.EscapeDataString(srcLang),
            Uri.EscapeDataString(tgtLang),
            Uri.EscapeDataString(text)
        );

        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = 8000;
        request.UserAgent = "Mozilla/5.0";
        request.Headers.Add("Accept-Language", "ko,en;q=0.9");

        using var response = (HttpWebResponse)request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
        string json = reader.ReadToEnd();

        // 응답 파싱: [[["번역결과","원문",null,null,10],...],...]
        var arr = JArray.Parse(json);
        var translationParts = arr[0] as JArray;
        if (translationParts == null) return text;

        var sb = new StringBuilder();
        foreach (var part in translationParts)
        {
            var translated = part[0]?.Value<string>();
            if (!string.IsNullOrEmpty(translated))
                sb.Append(translated);
        }

        string result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? text : result;
    }

    public void Dispose()
    {
        _running = false;
    }

    private record TranslateRequest(string Text, string Author, string TargetLang, string RequestId);
}
