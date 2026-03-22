using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ValheimVoiceTranslator.Audio;

/// <summary>
/// Captures audio from a WASAPI loopback device (system audio or VB-Audio Virtual Cable)
/// or from a microphone, and fires events with PCM float32 chunks at 16kHz mono.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int ChunkMs = 30; // VAD 청크 크기 (ms)

    private readonly int _chunkSamples;
    private IWaveIn? _waveIn;
    private WaveFormat? _captureFormat;
    private readonly List<float> _resampleBuffer = new();

    public event EventHandler<float[]>? AudioChunkReady;
    public bool IsCapturing { get; private set; }

    public AudioCapture()
    {
        _chunkSamples = SampleRate * ChunkMs / 1000;
    }

    // ─────────────────────────────────────────────────────
    //  디바이스 열거
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 현재 활성화된 모든 WASAPI 재생(Render) 디바이스 목록을 반환합니다.
    /// VB-Audio Virtual Cable은 "CABLE Output (VB-Audio Virtual Cable)"으로 표시됩니다.
    /// </summary>
    public static List<AudioDeviceInfo> GetLoopbackDevices()
    {
        var list = new List<AudioDeviceInfo>
        {
            AudioDeviceInfo.SystemDefault, // 시스템 기본 루프백
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                bool isVbCable = dev.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                              || dev.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
                              || dev.FriendlyName.Contains("Virtual Cable", StringComparison.OrdinalIgnoreCase);
                list.Add(new AudioDeviceInfo(dev.ID, dev.FriendlyName, isVbCable));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCapture] Device enumeration failed: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// 마이크(입력) 디바이스 목록을 반환합니다.
    /// </summary>
    public static List<AudioDeviceInfo> GetMicrophoneDevices()
    {
        var list = new List<AudioDeviceInfo>
        {
            new(null, "기본 마이크 (Default Microphone)", false),
        };

        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            list.Add(new AudioDeviceInfo(i.ToString(), caps.ProductName, false));
        }

        return list;
    }

    // ─────────────────────────────────────────────────────
    //  캡처 시작
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 시스템 기본 루프백으로 캡처를 시작합니다.
    /// </summary>
    public void StartLoopback()
    {
        StopCapture();
        _waveIn = new WasapiLoopbackCapture();
        _captureFormat = _waveIn.WaveFormat;
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        IsCapturing = true;
    }

    /// <summary>
    /// 특정 WASAPI 재생 디바이스(VB-Audio Virtual Cable 등)의 루프백으로 캡처를 시작합니다.
    /// deviceId: MMDevice.ID (null이면 시스템 기본)
    /// </summary>
    public void StartLoopbackOnDevice(string? deviceId)
    {
        if (deviceId is null)
        {
            StartLoopback();
            return;
        }

        StopCapture();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            _waveIn = new WasapiLoopbackCapture(device);
            _captureFormat = _waveIn.WaveFormat;
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsCapturing = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"디바이스 '{deviceId}' 캡처 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 마이크 입력으로 캡처를 시작합니다.
    /// deviceNumber: WaveIn 디바이스 번호 (-1이면 기본)
    /// </summary>
    public void StartMicrophone(int deviceNumber = 0)
    {
        StopCapture();
        var format = new WaveFormat(SampleRate, 16, Channels);
        _waveIn = new WaveInEvent
        {
            WaveFormat = format,
            DeviceNumber = Math.Max(0, deviceNumber),
            BufferMilliseconds = ChunkMs,
        };
        _captureFormat = format;
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        IsCapturing = true;
    }

    public void StopCapture()
    {
        if (_waveIn is null) return;
        try
        {
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
        }
        catch { /* 이미 중지된 경우 무시 */ }
        finally
        {
            _waveIn = null;
            _resampleBuffer.Clear();
            IsCapturing = false;
        }
    }

    // ─────────────────────────────────────────────────────
    //  오디오 처리
    // ─────────────────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_captureFormat is null || e.BytesRecorded == 0) return;

        // 바이트 → float32 → 모노 다운믹스 → 16kHz 리샘플
        var floats = BytesToFloat32(e.Buffer, e.BytesRecorded, _captureFormat);
        var mono = DownmixToMono(floats, _captureFormat.Channels);
        var resampled = Resample(mono, _captureFormat.SampleRate, SampleRate);

        _resampleBuffer.AddRange(resampled);

        // _chunkSamples 단위로 이벤트 발생
        while (_resampleBuffer.Count >= _chunkSamples)
        {
            var chunk = _resampleBuffer.GetRange(0, _chunkSamples).ToArray();
            _resampleBuffer.RemoveRange(0, _chunkSamples);
            AudioChunkReady?.Invoke(this, chunk);
        }
    }

    private static float[] BytesToFloat32(byte[] buffer, int count, WaveFormat fmt)
    {
        int bytesPerSample = fmt.BitsPerSample / 8;
        int sampleCount = count / bytesPerSample;
        var result = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = i * bytesPerSample;
            result[i] = fmt.BitsPerSample switch
            {
                16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                32 when fmt.Encoding == WaveFormatEncoding.IeeeFloat
                    => BitConverter.ToSingle(buffer, offset),
                32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => 0f,
            };
        }
        return result;
    }

    private static float[] DownmixToMono(float[] samples, int channels)
    {
        if (channels == 1) return samples;
        int monoCount = samples.Length / channels;
        var mono = new float[monoCount];
        for (int i = 0; i < monoCount; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += samples[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    private static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate) return input;
        double ratio = (double)toRate / fromRate;
        int outLen = (int)(input.Length * ratio);
        var output = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double srcIdx = i / ratio;
            int lo = (int)srcIdx;
            int hi = Math.Min(lo + 1, input.Length - 1);
            double frac = srcIdx - lo;
            output[i] = (float)(input[lo] * (1 - frac) + input[hi] * frac);
        }
        return output;
    }

    public void Dispose() => StopCapture();
}
