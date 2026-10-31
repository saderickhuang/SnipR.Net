using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Drawing;

namespace SnipR;

internal sealed class RecordingSession : IAsyncDisposable
{
    private readonly List<AudioTrack> tracks = [];
    private readonly Stopwatch clock = new();
    private readonly StringBuilder log = new();
    private Process? process;
    private string workingFolder = "";
    private string outputPath = "";
    private double videoOffset;
    private Task? logTask;
    private Task? progressTask;
    private bool finished;
    private EncoderJob? job;
    public TimeSpan Elapsed => clock.Elapsed;
    public bool HasExited => process?.HasExited == true;
    public Exception? AudioFailure => tracks.Select(x => x.Failure).FirstOrDefault(x => x != null);
    public string RecoveryFolder => workingFolder;
    public static string EncoderPath => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");

    public async Task StartAsync(Rectangle region, Settings settings)
    {
        if (!File.Exists(EncoderPath)) throw new FileNotFoundException("缺少录像组件。请运行 scripts/setup-ffmpeg.ps1 后重新构建。", EncoderPath);
        if (region.Width < 16 || region.Height < 16 || (region.Width & 1) != 0 || (region.Height & 1) != 0)
            throw new ArgumentException("录制区域必须至少为 16 × 16，且宽高为偶数。");
        Directory.CreateDirectory(settings.OutputFolder);
        var stem = $"SnipR_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}";
        outputPath = Path.Combine(settings.OutputFolder, stem + ".mp4");
        workingFolder = Path.Combine(settings.OutputFolder, ".sessions", stem);
        Directory.CreateDirectory(workingFolder);
        try
        {
            job = new EncoderJob();
            try
            {
                if (settings.SystemAudio) tracks.Add(new(true, Path.Combine(workingFolder, "system.wav"), clock));
                if (settings.Microphone) tracks.Add(new(false, Path.Combine(workingFolder, "microphone.wav"), clock));
                clock.Start();
                foreach (var track in tracks) track.Start();
            }
            catch (Exception ex)
            {
                throw new IOException("无法访问音频设备。请检查 Windows 默认扬声器、默认麦克风和麦克风权限，或关闭对应声音来源后重试。\n" + ex.Message, ex);
            }
            var info = CreateStartInfo();
            string[] args = ["-hide_banner", "-loglevel", "warning", "-y", "-f", "gdigrab", "-framerate", settings.Fps.ToString(),
                "-draw_mouse", settings.ShowCursor ? "1" : "0", "-offset_x", region.X.ToString(), "-offset_y", region.Y.ToString(),
                "-video_size", $"{region.Width}x{region.Height}", "-i", "desktop", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "22",
                "-pix_fmt", "yuv420p", "-movflags", "+frag_keyframe+empty_moov", "-g", settings.Fps.ToString(),
                "-progress", "pipe:1", "-stats_period", "0.1", Path.Combine(workingFolder, "video.mp4")];
            foreach (var arg in args) info.ArgumentList.Add(arg);
            process = Process.Start(info) ?? throw new IOException("无法启动录像组件。");
            job.Attach(process);
            logTask = ReadLogAsync(process);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            progressTask = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (!ready.Task.IsCompleted && line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var microseconds) && microseconds > 0)
                    {
                        videoOffset = Math.Max(0, clock.Elapsed.TotalSeconds - microseconds / 1_000_000d);
                        ready.TrySetResult();
                    }
                }
                ready.TrySetException(new IOException("录像组件未能启动：" + log));
            });
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task<string> StopAsync()
    {
        if (finished || process == null) throw new InvalidOperationException("当前没有录像。");
        finished = true;
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("q");
                await process.StandardInput.FlushAsync();
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            clock.Stop();
            foreach (var track in tracks) await track.DisposeAsync();
            await (logTask ?? Task.CompletedTask);
            await (progressTask ?? Task.CompletedTask);
            await File.WriteAllTextAsync(Path.Combine(workingFolder, "encoder.log"), log.ToString());
            if (process.ExitCode != 0) throw new IOException("视频录制异常：" + log);
            var info = CreateStartInfo();
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-n", "-i", Path.Combine(workingFolder, "video.mp4") }) info.ArgumentList.Add(arg);
            foreach (var track in tracks) { info.ArgumentList.Add("-i"); info.ArgumentList.Add(track.Path); }
            info.ArgumentList.Add("-map"); info.ArgumentList.Add("0:v:0");
            if (tracks.Count > 0)
            {
                var offset = videoOffset.ToString("F6", CultureInfo.InvariantCulture);
                var filters = string.Join(";", tracks.Select((_, i) => $"[{i + 1}:a]atrim=start={offset},asetpts=PTS-STARTPTS,aresample=48000,apad[a{i}]"));
                filters += ";" + string.Concat(tracks.Select((_, i) => $"[a{i}]")) + $"amix=inputs={tracks.Count}:duration=longest:normalize=0,alimiter=limit=0.95:latency=1[mix]";
                info.ArgumentList.Add("-filter_complex"); info.ArgumentList.Add(filters);
                foreach (var arg in new[] { "-map", "[mix]", "-c:a", "aac", "-b:a", "192k", "-shortest" }) info.ArgumentList.Add(arg);
            }
            foreach (var arg in new[] { "-c:v", "copy", "-movflags", "+faststart", outputPath }) info.ArgumentList.Add(arg);
            using var merge = Process.Start(info) ?? throw new IOException("无法保存视频。");
            job!.Attach(merge);
            var errors = merge.StandardError.ReadToEndAsync();
            try { await merge.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(30)); }
            catch { if (!merge.HasExited) merge.Kill(true); throw; }
            var error = await errors;
            if (merge.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0) throw new IOException("保存失败：" + error);
            if (AudioFailure != null) throw new IOException($"视频已保存，但音频设备中断，声音可能不完整：{AudioFailure.Message}\n视频：{outputPath}");
            // Only discard this session's scratch files after a successful final output.
            try { Directory.Delete(workingFolder, true); } catch { /* A scanner may still hold a scratch file. */ }
            return outputPath;
        }
        catch (Exception ex)
        {
            throw new IOException($"{ex.Message}\n原始录制保留在：{workingFolder}", ex);
        }
        finally { await DisposeAsync(); }
    }

    private static ProcessStartInfo CreateStartInfo() => new(EncoderPath)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true
    };

    private async Task ReadLogAsync(Process source)
    {
        while (await source.StandardError.ReadLineAsync() is { } line)
            lock (log) { if (log.Length > 16000) log.Remove(0, 8000); log.AppendLine(line); }
    }

    public async ValueTask DisposeAsync()
    {
        if (process != null)
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (progressTask != null) await progressTask;
            if (logTask != null) await logTask;
            process.Dispose();
            process = null;
        }
        clock.Stop();
        foreach (var track in tracks) await track.DisposeAsync();
        job?.Dispose();
        job = null;
    }
}
