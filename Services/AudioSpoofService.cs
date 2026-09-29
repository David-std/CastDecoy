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
    private float _saturationGain = 12f;
    private bool _isActive = false;

    private readonly Random _random = new();

    private int _choppySampleCount = 0;
    private bool _choppyMuted = false;
    private const int ChoppyCycleSamples = 5292;

    private readonly short[] _echoBuffer = new short[11025];
    private int _echoIndex = 0;

    public event Action<float>? PeakLevelChanged;
    public bool IsActive => _isActive;
    public AudioSpoofMode Mode => _mode;

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

            _monitorProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 1))
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

    public void SetMonitoring(bool enable)
    {
        _isMonitoring = enable;
        if (_waveOut != null)
        {
            if (enable)
            {
                if (_waveOut.PlaybackState != PlaybackState.Playing)
                    _waveOut.Play();
            }
            else
            {
                if (_waveOut.PlaybackState == PlaybackState.Playing)
                    _waveOut.Stop();
            }
        }
    }

    public void SetSaturationGain(float gain)
    {
        _saturationGain = Math.Clamp(gain, 1f, 30f);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytesRecorded = e.BytesRecorded;
        if (bytesRecorded <= 0) return;

        byte[] processed = new byte[bytesRecorded];
        Array.Copy(e.Buffer, processed, bytesRecorded);

        int sampleCount = bytesRecorded / 2;
        float maxPeak = 0f;

        switch (_mode)
        {
            case AudioSpoofMode.Normal:
                for (int i = 0; i < sampleCount; i++)
                {
                    short s = BitConverter.ToInt16(processed, i * 2);
                    float abs = Math.Abs(s) / 32768f;
                    if (abs > maxPeak) maxPeak = abs;
                }
                break;

            case AudioSpoofMode.FakeMute:
                Array.Clear(processed, 0, processed.Length);
                maxPeak = 0f;
                break;

            case AudioSpoofMode.Choppy:
                for (int i = 0; i < sampleCount; i++)
                {
                    _choppySampleCount++;
                    if (_choppySampleCount >= ChoppyCycleSamples)
                    {
                        _choppySampleCount = 0;
                        _choppyMuted = !_choppyMuted;
                    }

                    if (_choppyMuted)
                    {
                        processed[i * 2] = 0;
                        processed[i * 2 + 1] = 0;
                    }
                    else
                    {
                        short s = BitConverter.ToInt16(processed, i * 2);
                        float abs = Math.Abs(s) / 32768f;
                        if (abs > maxPeak) maxPeak = abs;
                    }
                }
                break;

            case AudioSpoofMode.Echo:
                for (int i = 0; i < sampleCount; i++)
                {
                    short orig = BitConverter.ToInt16(processed, i * 2);
                    short delayed = _echoBuffer[_echoIndex];
                    int mixed = orig + (int)(delayed * 0.60f);
                    if (mixed > 32767) mixed = 32767;
                    if (mixed < -32768) mixed = -32768;
                    _echoBuffer[_echoIndex] = (short)mixed;
                    _echoIndex++;
                    if (_echoIndex >= _echoBuffer.Length) _echoIndex = 0;

                    byte[] b = BitConverter.GetBytes((short)mixed);
                    processed[i * 2] = b[0];
                    processed[i * 2 + 1] = b[1];

                    float abs = Math.Abs(mixed) / 32768f;
                    if (abs > maxPeak) maxPeak = abs;
                }
                break;

            case AudioSpoofMode.StaticNoise:
                for (int i = 0; i < sampleCount; i++)
                {
                    short noise = (short)_random.Next(-10000, 10000);
                    byte[] b = BitConverter.GetBytes(noise);
                    processed[i * 2] = b[0];
                    processed[i * 2 + 1] = b[1];
                    float abs = Math.Abs(noise) / 32768f;
                    if (abs > maxPeak) maxPeak = abs;
                }
                break;

            case AudioSpoofMode.Saturation:
                for (int i = 0; i < sampleCount; i++)
                {
                    short s = BitConverter.ToInt16(processed, i * 2);
                    int amplified = (int)(s * _saturationGain);
                    if (amplified > 32767) amplified = 32767;
                    if (amplified < -32768) amplified = -32768;

                    byte[] b = BitConverter.GetBytes((short)amplified);
                    processed[i * 2] = b[0];
                    processed[i * 2 + 1] = b[1];

                    float abs = Math.Abs(amplified) / 32768f;
                    if (abs > maxPeak) maxPeak = abs;
                }
                break;
        }

        if (_isMonitoring && _monitorProvider != null)
        {
            _monitorProvider.AddSamples(processed, 0, processed.Length);
        }

        PeakLevelChanged?.Invoke(Math.Min(1f, maxPeak));
    }

    public void Dispose()
    {
        Stop();
    }
}
