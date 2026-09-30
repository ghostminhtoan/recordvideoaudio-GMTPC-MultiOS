using System;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordVideoAudio.GMTPC.Services;

public class WasapiAudioRecorder : IDisposable
{
    private WasapiRecorder? _loopbackRecorder;
    private WaveFileWriter? _loopbackWriter;
    private string? _loopbackPath;

    private WasapiPlayer? _silencePlayer;

    private WasapiRecorder? _micRecorder;
    private WaveFileWriter? _micWriter;
    private string? _micPath;

    public bool IsRecording { get; private set; }

    public string? StartRecording(bool recordSpeaker, float speakerVolume, bool recordMic, float micVolume, string outDir)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        StopRecording();

        try
        {
            var enumerator = new MMDeviceEnumerator();

            // 1. System Speaker Loopback Capture
            if (recordSpeaker)
            {
                try
                {
                    var renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (renderDevice != null)
                    {
                        _loopbackPath = Path.Combine(outDir, $"temp_speaker_{Guid.NewGuid():N}.wav");

                        _loopbackRecorder = new WasapiRecorderBuilder()
                            .WithDevice(renderDevice)
                            .WithLoopbackCapture()
                            .Build();

                        _loopbackWriter = new WaveFileWriter(_loopbackPath, _loopbackRecorder.WaveFormat);

                        _loopbackRecorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                        {
                            try
                            {
                                if (_loopbackWriter != null && !buffer.IsEmpty)
                                {
                                    _loopbackWriter.Write(buffer);
                                }
                            }
                            catch { }
                        };

                        _loopbackRecorder.StartRecording();

                        // Keep Windows Audio Engine active during silence using lightweight SilenceProvider
                        try
                        {
                            var silenceFormat = _loopbackRecorder.WaveFormat;
                            var silenceProvider = new SilenceProvider(silenceFormat);
                            _silencePlayer = new WasapiPlayerBuilder()
                                .WithDevice(renderDevice)
                                .Build();
                            _silencePlayer.Init(silenceProvider);
                            _silencePlayer.Play();
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 2. Microphone Capture
            if (recordMic)
            {
                try
                {
                    var captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                    if (captureDevice != null)
                    {
                        _micPath = Path.Combine(outDir, $"temp_mic_{Guid.NewGuid():N}.wav");

                        _micRecorder = new WasapiRecorderBuilder()
                            .WithDevice(captureDevice)
                            .Build();

                        _micWriter = new WaveFileWriter(_micPath, _micRecorder.WaveFormat);

                        _micRecorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                        {
                            try
                            {
                                if (_micWriter != null && !buffer.IsEmpty)
                                {
                                    _micWriter.Write(buffer);
                                }
                            }
                            catch { }
                        };

                        _micRecorder.StartRecording();
                    }
                }
                catch { }
            }

            IsRecording = true;
        }
        catch (Exception ex)
        {
            StopRecording();
            return ex.Message;
        }

        return null;
    }

    public (string? speakerWav, string? micWav) StopRecording()
    {
        if (!IsRecording) return (null, null);
        IsRecording = false;

        // Stop and flush speaker loopback
        try
        {
            if (_silencePlayer != null)
            {
                _silencePlayer.Stop();
                _silencePlayer.Dispose();
                _silencePlayer = null;
            }
        }
        catch { }

        try
        {
            if (_loopbackRecorder != null)
            {
                _loopbackRecorder.StopRecording();
                _loopbackRecorder.Dispose();
                _loopbackRecorder = null;
            }
        }
        catch { }

        try
        {
            if (_loopbackWriter != null)
            {
                _loopbackWriter.Flush();
                _loopbackWriter.Dispose();
                _loopbackWriter = null;
            }
        }
        catch { }

        // Stop and flush mic capture
        try
        {
            if (_micRecorder != null)
            {
                _micRecorder.StopRecording();
                _micRecorder.Dispose();
                _micRecorder = null;
            }
        }
        catch { }

        try
        {
            if (_micWriter != null)
            {
                _micWriter.Flush();
                _micWriter.Dispose();
                _micWriter = null;
            }
        }
        catch { }

        // Validate that WAV files exist and have data chunk beyond the standard 44-byte header
        string? speaker = (!string.IsNullOrEmpty(_loopbackPath) && File.Exists(_loopbackPath) && new FileInfo(_loopbackPath).Length > 44)
            ? _loopbackPath
            : null;

        string? mic = (!string.IsNullOrEmpty(_micPath) && File.Exists(_micPath) && new FileInfo(_micPath).Length > 44)
            ? _micPath
            : null;

        return (speaker, mic);
    }

    public void Dispose()
    {
        StopRecording();
    }
}
