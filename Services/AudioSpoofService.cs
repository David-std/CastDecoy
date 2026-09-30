using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;

namespace CastDecoy.Services;

public enum AudioSpoofMode
{
    Normal,
    FakeMute,
    Choppy,
    Echo,
    StaticNoise,
    Saturation
}

public class AudioSpoofService : IDisposable
{
    private WaveIn? _waveIn;
    private WaveOut? _waveOut;
    private BufferedWaveProvider? _monitorProvider;

    private AudioSpoofMode _mode = AudioSpoofMode.Normal;
    private bool _isMonitoring = false;
    private float _saturationGain = 15f;
    private float _masterVolume = 1.0f;
    private bool _isActive = false;

    private readonly Random _random = new();

    private int _choppySampleCount = 0;
    private bool _choppyMuted = false;
    private int _choppyRate = 1;

    private readonly short[] _echoBuffer = new short[44100];
    private int _echoIndex = 0;
    private int _echoMode = 0;

    private int _staticType = 0;
    private long _sampleCountGlobal = 0;

    private readonly float[] _bandPeaks = new float[12];

    public event Action<float>? PeakLevelChanged;
    public event Action<float, float[]>? SpectrumDataChanged;
    public bool IsActive => _isActive;
    public AudioSpoofMode Mode => _mode;
    public float MasterVolume => _masterVolume;

    public static List<string> GetInputDevices()
    {
        var list = new List<string>();
        int count = WaveIn.DeviceCount;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var caps = WaveIn.GetCapabilities(i);
                list.Add(caps.ProductName);
            }
            catch
            {
                list.Add($"Dispositivo {i + 1}");
            }
        }
        return list;
    }

    public void Start(int deviceIndex)
    {
        Stop();

        try
        {
            _waveIn = new WaveIn
            {
                DeviceNumber = Math.Max(0, deviceIndex),
                WaveFormat = new WaveFormat(44100, 16, 1),
                BufferMilliseconds = 25
            };

            _monitorProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 1), TimeSpan.FromMilliseconds(200))
            {
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOut();
            _waveOut.Init(_monitorProvider);
            if (_isMonitoring)
            {
                _waveOut.Play();
            }

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            _isActive = true;

            Array.Clear(_echoBuffer, 0, _echoBuffer.Length);
            _echoIndex = 0;
            _choppySampleCount = 0;
            _choppyMuted = false;
        }
        catch
        {
            Stop();
        }
    }

    public void Stop()
    {
        _isActive = false;

        if (_waveIn != null)
        {
            try
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.StopRecording();
                _waveIn.Dispose();
            }
            catch { }
            _waveIn = null;
        }

        if (_waveOut != null)
        {
            try
            {
                _waveOut.Stop();
                _waveOut.Dispose();
            }
            catch { }
            _waveOut = null;
        }

        _monitorProvider = null;
        PeakLevelChanged?.Invoke(0f);
        Array.Clear(_bandPeaks, 0, _bandPeaks.Length);
        SpectrumDataChanged?.Invoke(0f, _bandPeaks);
    }

    public void SetMode(AudioSpoofMode mode)
    {
        _mode = mode;
        if (mode == AudioSpoofMode.Echo)
        {
            Array.Clear(_echoBuffer, 0, _echoBuffer.Length);
            _echoIndex = 0;
        }
        if (mode == AudioSpoofMode.Choppy)
        {
            _choppySampleCount = 0;
            _choppyMuted = false;
        }
    }

    public void SetMasterVolume(float volume)
    {
        _masterVolume = Math.Clamp(volume, 0f, 1f);
    }

    public void SetEchoMode(int modeIndex)
    {
        _echoMode = Math.Clamp(modeIndex, 0, 3);
        Array.Clear(_echoBuffer, 0, _echoBuffer.Length);
        _echoIndex = 0;
    }

    public void SetStaticType(int typeIndex)
    {
        _staticType = Math.Clamp(typeIndex, 0, 2);
    }

    public void SetChoppyRate(int rateIndex)
    {
        _choppyRate = Math.Clamp(rateIndex, 0, 2);
        _choppySampleCount = 0;
        _choppyMuted = false;
    }

    public void SetMonitoring(bool enable)
    {
        _isMonitoring = enable;
        if (_waveOut != null)
        {
            if (enable)
            {
                _monitorProvider?.ClearBuffer();
                if (_waveOut.PlaybackState != PlaybackState.Playing)
                    _waveOut.Play();
            }
            else
            {
                if (_waveOut.PlaybackState == PlaybackState.Playing)
                    _waveOut.Stop();
                _monitorProvider?.ClearBuffer();
            }
        }
    }

    public void SetSaturationGain(float gain)
    {
        _saturationGain = Math.Clamp(gain, 1f, 30f);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            int bytesRecorded = e.BytesRecorded;
            if (bytesRecorded <= 0) return;

            byte[] processed = new byte[bytesRecorded];
            Array.Copy(e.Buffer, processed, bytesRecorded);

            int sampleCount = bytesRecorded / 2;
            float maxPeak = 0f;

            int choppyCycle = _choppyRate == 0 ? 6000 : (_choppyRate == 2 ? 3000 : 4410);
            int choppyMuteDuration = _choppyRate == 0 ? (int)(choppyCycle * 0.25f) : (_choppyRate == 2 ? (int)(choppyCycle * 0.75f) : (int)(choppyCycle * 0.50f));

            int multipleDelay = _echoMode switch
            {
                1 => 5292,
                2 => 7938,
                3 => 11466,
                _ => 12348
            };

            for (int i = 0; i < sampleCount; i++)
            {
                _sampleCountGlobal = (_sampleCountGlobal + 1) % 4410000;
                short orig = BitConverter.ToInt16(e.Buffer, i * 2);
                int mixed = orig;

                switch (_mode)
                {
                    case AudioSpoofMode.Normal:
                        mixed = orig;
                        break;

                    case AudioSpoofMode.FakeMute:
                        mixed = 0;
                        break;

                    case AudioSpoofMode.Choppy:
                        _choppySampleCount++;
                        if (_choppySampleCount >= choppyCycle)
                        {
                            _choppySampleCount = 0;
                        }
                        _choppyMuted = _choppySampleCount < choppyMuteDuration;
                        mixed = _choppyMuted ? 0 : orig;
                        break;

                    case AudioSpoofMode.Echo:
                        if (_echoMode == 0)
                        {
                            const int tap1Delay = 12348;
                            const int tap2Delay = 24696;
                            int idx1 = (_echoIndex - tap1Delay + 44100) % 44100;
                            int idx2 = (_echoIndex - tap2Delay + 44100) % 44100;
                            short tap1 = _echoBuffer[idx1];
                            short tap2 = _echoBuffer[idx2];

                            mixed = orig + (int)(tap1 * 0.55f + tap2 * 0.38f);
                            int writeBack = orig + (int)(tap1 * 0.40f + tap2 * 0.25f);
                            _echoBuffer[_echoIndex] = (short)Math.Clamp(writeBack, -32767, 32767);
                            _echoIndex = (_echoIndex + 1) % 44100;
                        }
                        else
                        {
                            int readIdx = (_echoIndex - multipleDelay + 44100) % 44100;
                            short delayed = _echoBuffer[readIdx];
                            mixed = orig + (int)(delayed * 0.65f);
                            _echoBuffer[_echoIndex] = (short)Math.Clamp(mixed, -32767, 32767);
                            _echoIndex = (_echoIndex + 1) % 44100;
                        }
                        break;

                    case AudioSpoofMode.StaticNoise:
                        if (_staticType == 0)
                        {
                            double t = (double)_sampleCountGlobal / 44100.0;
                            double hum55 = Math.Sin(2.0 * Math.PI * 55.0 * t) * 6000.0;
                            double hum110 = Math.Sin(2.0 * Math.PI * 110.0 * t) * 2500.0;
                            double humBuzz = Math.Sin(2.0 * Math.PI * 165.0 * t) * 1200.0;
                            double buzz = hum55 + hum110 + humBuzz + _random.Next(-1000, 1000);
                            mixed = (int)(orig * 0.65f + buzz);
                        }
                        else if (_staticType == 1)
                        {
                            short white = (short)_random.Next(-6500, 6500);
                            mixed = (int)(orig * 0.60f + white);
                        }
                        else
                        {
                            int crackle = _random.NextDouble() < 0.012 ? _random.Next(-18000, 18000) : 0;
                            int hiss = _random.Next(-3500, 3500) + crackle;
                            mixed = (int)(orig * 0.55f + hiss);
                        }
                        break;

                    case AudioSpoofMode.Saturation:
                        double drive = 1.0 + (_saturationGain * 0.4);
                        double norm = (double)orig / 32768.0;
                        double outputScale = 32000.0 / Math.Pow(drive, 0.4);
                        mixed = (int)(Math.Tanh(norm * drive) * outputScale);
                        break;
                }

                mixed = (int)(mixed * _masterVolume);
                short finalSample = (short)Math.Clamp(mixed, -32767, 32767);

                byte[] b = BitConverter.GetBytes(finalSample);
                processed[i * 2] = b[0];
                processed[i * 2 + 1] = b[1];

                float abs = Math.Abs((int)finalSample) / 32767f;
                if (abs > maxPeak) maxPeak = abs;
            }

            if (_isMonitoring && _monitorProvider != null)
            {
                _monitorProvider.AddSamples(processed, 0, processed.Length);
            }

            PeakLevelChanged?.Invoke(Math.Min(1f, maxPeak));
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}
