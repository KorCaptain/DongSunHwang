using NAudio.Wave;

namespace DiscordVoiceTranslator.Audio;

/// <summary>
/// Captures audio from the default loopback (WASAPI) or microphone device
/// and fires events with raw PCM float32 chunks at 16kHz mono.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int ChunkMs = 30; // VAD chunk size in milliseconds

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

    /// <summary>
    /// Start capturing from the system loopback (what you hear).
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
    /// Start capturing from the default microphone.
    /// </summary>
    public void StartMicrophone(int deviceNumber = 0)
    {
        StopCapture();
        var format = new WaveFormat(SampleRate, 16, Channels);
        _waveIn = new WaveInEvent
        {
            WaveFormat = format,
            DeviceNumber = deviceNumber,
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
        _waveIn.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.Dispose();
        _waveIn = null;
        _resampleBuffer.Clear();
        IsCapturing = false;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_captureFormat is null) return;

        // Convert bytes to float32 samples, then resample/downmix to 16kHz mono
        var floats = BytesToFloat32(e.Buffer, e.BytesRecorded, _captureFormat);
        var mono = DownmixToMono(floats, _captureFormat.Channels);
        var resampled = Resample(mono, _captureFormat.SampleRate, SampleRate);

        _resampleBuffer.AddRange(resampled);

        // Fire chunks of exactly _chunkSamples
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
