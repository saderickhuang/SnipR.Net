using System.Diagnostics;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace SnipR;

internal sealed class AudioTrack : IAsyncDisposable
{
    private readonly WasapiCapture capture;
    private readonly WaveFileWriter writer;
    private readonly Stopwatch timeline;
    private readonly object gate = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WasapiOut? silence;
    private MMDevice? device;
    private long bytesWritten;
    private bool stopping;
    private bool disposed;
    public Exception? Failure { get; private set; }
    public string Path { get; }

    public AudioTrack(bool loopback, string path, Stopwatch timeline)
    {
        Path = path;
        this.timeline = timeline;
        if (loopback)
        {
            using var devices = new MMDeviceEnumerator();
            device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            capture = new WasapiLoopbackCapture(device);
        }
        else capture = new WasapiCapture();
        try { writer = new WaveFileWriter(path, capture.WaveFormat); }
        catch { capture.Dispose(); device?.Dispose(); throw; }
        capture.DataAvailable += (_, args) =>
        {
            lock (gate)
            {
                if (disposed || Failure != null) return;
                try
                {
                    var expectedStart = (long)(timeline.Elapsed.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond) - args.BytesRecorded;
                    if (expectedStart - bytesWritten > writer.WaveFormat.AverageBytesPerSecond / 10)
                        PadTo(expectedStart);
                    writer.Write(args.Buffer, 0, args.BytesRecorded);
                    bytesWritten += args.BytesRecorded;
                }
                catch (Exception ex) { Failure = ex; }
            }
        };
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception != null) Failure = args.Exception;
            else if (!stopping) Failure = new IOException("音频设备意外停止，请检查设备连接。");
            stopped.TrySetResult();
        };
    }

    public void Start()
    {
        if (device != null)
        {
            // Keep loopback producing silent packets when no app is playing audio.
            silence = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
            silence.Init(new SilenceProvider(capture.WaveFormat));
            silence.Play();
        }
        capture.StartRecording();
    }

    private void PadTo(long targetBytes)
    {
        targetBytes -= targetBytes % writer.WaveFormat.BlockAlign;
        var zeros = new byte[8192 - 8192 % writer.WaveFormat.BlockAlign];
        while (bytesWritten < targetBytes)
        {
            var count = (int)Math.Min(zeros.Length, targetBytes - bytesWritten);
            writer.Write(zeros, 0, count);
            bytesWritten += count;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        stopping = true;
        capture.StopRecording();
        if (capture.CaptureState != CaptureState.Stopped)
        {
            try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException ex) { Failure ??= ex; }
        }
        lock (gate)
        {
            disposed = true;
            try { PadTo((long)(timeline.Elapsed.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond)); }
            finally { writer.Dispose(); }
        }
        capture.Dispose();
        silence?.Dispose();
        device?.Dispose();
    }
}
