using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PortAudioSharp;
using PortAudioStream = PortAudioSharp.Stream;

namespace LanMountainDesktop.Services;

public enum AudioRecorderRuntimeState
{
    Unsupported = 0,
    Ready = 1,
    Recording = 2,
    Paused = 3,
    Error = 4
}

public sealed record AudioRecorderSnapshot(
    AudioRecorderRuntimeState State,
    TimeSpan Duration,
    double InputLevel,
    string LastSavedFilePath,
    string LastError)
{
    public bool IsSupported => State != AudioRecorderRuntimeState.Unsupported;
}

public interface IAudioRecorderService : IDisposable
{
    AudioRecorderSnapshot GetSnapshot();

    bool StartOrResume();

    bool Pause();

    string? StopAndSave(string? outputPath = null);

    void Discard();
}

public static class AudioRecorderServiceFactory
{
    private static readonly Lazy<IAudioRecorderService> SharedRecorderService = new(
        () =>
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                return new NoOpAudioRecorderService("Unsupported platform");
            }

            return new PortAudioRecorderService();
        },
        isThreadSafe: true);

    private static readonly Lazy<IAudioRecorderService> SharedStudyMonitoringService = new(
        () =>
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                return new NoOpAudioRecorderService("Unsupported platform");
            }

            return new PortAudioRecorderService();
        },
        isThreadSafe: true);

    public static IAudioRecorderService CreateRecorder()
    {
        return SharedRecorderService.Value;
    }

    public static IAudioRecorderService CreateStudyMonitoring()
    {
        return SharedStudyMonitoringService.Value;
    }

    public static IAudioRecorderService CreateDefault()
    {
        return CreateRecorder();
    }

    public static void DisposeSharedServices()
    {
        if (SharedRecorderService.IsValueCreated)
        {
            SharedRecorderService.Value.Dispose();
        }

        if (SharedStudyMonitoringService.IsValueCreated)
        {
            SharedStudyMonitoringService.Value.Dispose();
        }
    }
}

internal sealed class NoOpAudioRecorderService(string reason) : IAudioRecorderService
{
    private readonly AudioRecorderSnapshot _snapshot = new(
        AudioRecorderRuntimeState.Unsupported,
        TimeSpan.Zero,
        0,
        string.Empty,
        reason);

    public AudioRecorderSnapshot GetSnapshot()
    {
        return _snapshot;
    }

    public bool StartOrResume()
    {
        return false;
    }

    public bool Pause()
    {
        return false;
    }

    public string? StopAndSave(string? outputPath = null)
    {
        return null;
    }

    public void Discard()
    {
    }

    public void Dispose()
    {
    }
}

public sealed class PortAudioRecorderService : IAudioRecorderService
{
    private const int ChannelCount = 1;
    private const int BitsPerSample = 16;
    private const int BytesPerSample = BitsPerSample / 8;
    private const int PreferredSampleRate = 16000;
    private const uint FramesPerBuffer = 320;

    private readonly object _syncRoot = new();

    private PortAudioStream? _stream;
    private PortAudioStream.Callback? _streamCallback;
    private MemoryStream? _pcmBuffer;

    private AudioRecorderRuntimeState _state = AudioRecorderRuntimeState.Unsupported;
    private string _lastSavedFilePath = string.Empty;
    private string _lastError = string.Empty;
    private int _inputDeviceIndex = -1;
    private int _sampleRate = PreferredSampleRate;
    private double _deviceDefaultSampleRate = PreferredSampleRate;
    private long _capturedFrames;
    private double _inputLevel;
    private bool _isPortAudioInitialized;
    private bool _isDisposed;

    public PortAudioRecorderService()
    {
        InitializeRuntime();
    }

    public AudioRecorderSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            var level = _state == AudioRecorderRuntimeState.Recording
                ? Math.Clamp(_inputLevel, 0, 1)
                : 0;

            var duration = _capturedFrames <= 0 || _sampleRate <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(_capturedFrames / (double)_sampleRate);

            return new AudioRecorderSnapshot(
                State: _state,
                Duration: duration,
                InputLevel: level,
                LastSavedFilePath: _lastSavedFilePath,
                LastError: _lastError);
        }
    }

    public bool StartOrResume()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (_state == AudioRecorderRuntimeState.Unsupported)
            {
                return false;
            }

            if (_state == AudioRecorderRuntimeState.Error)
            {
                _state = AudioRecorderRuntimeState.Ready;
            }

            if (_state == AudioRecorderRuntimeState.Recording)
            {
                return true;
            }

            if (_state == AudioRecorderRuntimeState.Paused && _stream is not null)
            {
                try
                {
                    _stream.Start();
                    _state = AudioRecorderRuntimeState.Recording;
                    _lastError = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    SetErrorLocked(ex);
                    return false;
                }
            }

            EnsureBufferLocked();
            ResetCaptureStateLocked();
            if (!TryOpenInputStreamLocked())
            {
                return false;
            }

            _state = AudioRecorderRuntimeState.Recording;
            _lastError = string.Empty;
            return true;
        }
    }

    public bool Pause()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _state != AudioRecorderRuntimeState.Recording || _stream is null)
            {
                return false;
            }

            try
            {
                _stream.Stop();
                _state = AudioRecorderRuntimeState.Paused;
                _inputLevel = 0;
                _lastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                SetErrorLocked(ex);
                return false;
            }
        }
    }

    public string? StopAndSave(string? outputPath = null)
    {
        byte[] pcmData;
        int sampleRate;

        lock (_syncRoot)
        {
            if (_isDisposed ||
                (_state != AudioRecorderRuntimeState.Recording && _state != AudioRecorderRuntimeState.Paused))
            {
                return null;
            }

            StopStreamLocked();

            pcmData = _pcmBuffer?.ToArray() ?? Array.Empty<byte>();
            sampleRate = _sampleRate;

            ResetCaptureStateLocked();
            _state = AudioRecorderRuntimeState.Ready;
            _inputLevel = 0;
        }

        if (pcmData.Length == 0)
        {
            return null;
        }

        var resolvedOutputPath = ResolveOutputPath(outputPath);
        try
        {
            WriteWaveFile(resolvedOutputPath, pcmData, sampleRate, ChannelCount, BitsPerSample);
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                SetErrorLocked(ex);
            }

            return null;
        }

        lock (_syncRoot)
        {
            _lastSavedFilePath = resolvedOutputPath;
            _lastError = string.Empty;
        }

        return resolvedOutputPath;
    }

    public void Discard()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            StopStreamLocked();
            ResetCaptureStateLocked();
            _inputLevel = 0;
            _lastError = string.Empty;

            if (_state != AudioRecorderRuntimeState.Unsupported)
            {
                _state = AudioRecorderRuntimeState.Ready;
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            StopStreamLocked();
            _pcmBuffer?.Dispose();
            _pcmBuffer = null;

            if (_isPortAudioInitialized)
            {
                try
                {
                    PortAudio.Terminate();
                }
                catch
                {
                    // Ignore shutdown failures.
                }

                _isPortAudioInitialized = false;
            }
        }
    }

    private void InitializeRuntime()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || _isPortAudioInitialized)
            {
                return;
            }

            try
            {
                PortAudio.LoadNativeLibrary();
                PortAudio.Initialize();
                _isPortAudioInitialized = true;
            }
            catch (Exception ex)
            {
                _state = AudioRecorderRuntimeState.Unsupported;
                _lastError = ResolveErrorMessage(ex);
                return;
            }

            try
            {
                _inputDeviceIndex = PortAudio.DefaultInputDevice;
                if (_inputDeviceIndex < 0)
                {
                    _state = AudioRecorderRuntimeState.Unsupported;
                    _lastError = "No input device";
                    return;
                }

                var deviceInfo = PortAudio.GetDeviceInfo(_inputDeviceIndex);
                if (deviceInfo.maxInputChannels < 1)
                {
                    _state = AudioRecorderRuntimeState.Unsupported;
                    _lastError = "Input channels unavailable";
                    return;
                }

                _deviceDefaultSampleRate = deviceInfo.defaultSampleRate > 0
                    ? deviceInfo.defaultSampleRate
                    : PreferredSampleRate;
                _state = AudioRecorderRuntimeState.Ready;
                _lastError = string.Empty;
            }
            catch (Exception ex)
            {
                _state = AudioRecorderRuntimeState.Unsupported;
                _lastError = ResolveErrorMessage(ex);
            }
        }
    }

    private bool TryOpenInputStreamLocked()
    {
        if (!_isPortAudioInitialized || _inputDeviceIndex < 0)
        {
            _state = AudioRecorderRuntimeState.Unsupported;
            return false;
        }

        var inputParameters = new StreamParameters
        {
            device = _inputDeviceIndex,
            channelCount = ChannelCount,
            sampleFormat = SampleFormat.Int16,
            suggestedLatency = ResolveSuggestedLatency(),
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        _streamCallback ??= OnStreamCallback;
        foreach (var candidateRate in BuildSampleRateCandidates())
        {
            try
            {
                _stream?.Dispose();
                _stream = new PortAudioStream(
                    inputParameters,
                    null,
                    candidateRate,
                    FramesPerBuffer,
                    StreamFlags.ClipOff,
                    _streamCallback,
                    this);
                _sampleRate = Math.Clamp((int)Math.Round(candidateRate), 8000, 96000);
                _stream.Start();
                return true;
            }
            catch (Exception ex)
            {
                _stream?.Dispose();
                _stream = null;
                _lastError = ResolveErrorMessage(ex);
            }
        }

        _state = AudioRecorderRuntimeState.Error;
        return false;
    }

    private StreamCallbackResult OnStreamCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        _ = output;
        _ = timeInfo;
        _ = statusFlags;
        _ = userData;

        if (frameCount == 0 || input == IntPtr.Zero)
        {
            return StreamCallbackResult.Continue;
        }

        var byteCount = checked((int)(frameCount * ChannelCount * BytesPerSample));
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            Marshal.Copy(input, buffer, 0, byteCount);
            var peak = CalculatePeak(buffer, byteCount);

            lock (_syncRoot)
            {
                if (_state != AudioRecorderRuntimeState.Recording)
                {
                    return StreamCallbackResult.Continue;
                }

                _pcmBuffer?.Write(buffer, 0, byteCount);
                _capturedFrames += frameCount;
                _inputLevel = (_inputLevel * 0.72) + (peak * 0.28);
            }
        }
        catch
        {
            // Keep callback resilient to transient IO/interop errors.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return StreamCallbackResult.Continue;
    }

    private void StopStreamLocked()
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            if (_stream.IsActive)
            {
                _stream.Stop();
            }
        }
        catch
        {
            // Ignore stop errors.
        }

        try
        {
            _stream.Close();
        }
        catch
        {
            // Ignore close errors.
        }

        _stream.Dispose();
        _stream = null;
    }

    private void ResetCaptureStateLocked()
    {
        _capturedFrames = 0;
        _sampleRate = Math.Clamp(_sampleRate, 8000, 96000);
        _inputLevel = 0;
        _pcmBuffer?.SetLength(0);
    }

    private void EnsureBufferLocked()
    {
        if (_pcmBuffer is not null)
        {
            return;
        }

        _pcmBuffer = new MemoryStream(capacity: 128 * 1024);
    }

    private double ResolveSuggestedLatency()
    {
        try
        {
            var info = PortAudio.GetDeviceInfo(_inputDeviceIndex);
            if (info.defaultLowInputLatency > 0)
            {
                return info.defaultLowInputLatency;
            }

            if (info.defaultHighInputLatency > 0)
            {
                return info.defaultHighInputLatency;
            }
        }
        catch
        {
            // Fall through to default latency.
        }

        return 0.04;
    }

    private double[] BuildSampleRateCandidates()
    {
        var ordered = new[] { PreferredSampleRate, _deviceDefaultSampleRate, 44100d, 48000d };
        var unique = new HashSet<int>();
        var list = new List<double>(ordered.Length);
        foreach (var rate in ordered)
        {
            var rounded = (int)Math.Round(rate);
            if (rounded < 8000 || rounded > 96000 || !unique.Add(rounded))
            {
                continue;
            }

            list.Add(rounded);
        }

        return list.ToArray();
    }

    private static double CalculatePeak(byte[] buffer, int byteCount)
    {
        double peak = 0;
        for (var i = 0; i + 1 < byteCount; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            var normalized = Math.Abs(sample) / 32768d;
            if (normalized > peak)
            {
                peak = normalized;
            }
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return BuildDefaultOutputPath();
        }

        var normalizedPath = outputPath.Trim();
        if (!string.Equals(Path.GetExtension(normalizedPath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = Path.ChangeExtension(normalizedPath, ".wav");
        }

        var directory = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.CurrentDirectory;
            normalizedPath = Path.Combine(directory, Path.GetFileName(normalizedPath));
        }

        Directory.CreateDirectory(directory);
        return normalizedPath;
    }

    private static string BuildDefaultOutputPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        var folder = Path.Combine(root, "LanMountainDesktop", "Recordings");
        Directory.CreateDirectory(folder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        return Path.Combine(folder, $"recording_{timestamp}.wav");
    }

    private static void WriteWaveFile(string path, byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var blockAlign = channels * (bitsPerSample / 8);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        writer.Write(36 + pcmData.Length);
        writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        writer.Write(pcmData.Length);
        writer.Write(pcmData);
    }

    private void SetErrorLocked(Exception ex)
    {
        _lastError = ResolveErrorMessage(ex);
        _state = AudioRecorderRuntimeState.Error;
    }

    private static string ResolveErrorMessage(Exception ex)
    {
        return ex.Message.Trim().Length > 0
            ? ex.Message.Trim()
            : ex.GetType().Name;
    }
}
