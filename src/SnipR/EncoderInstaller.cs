using System.Net.Http;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SnipR;

internal sealed record EncoderProgress(string Message, double? Percent = null);

internal sealed class EncoderInstaller(string applicationDirectory, string cacheDirectory, HttpClient client, TimeSpan? timeout = null)
{
    private const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(15) };
    public static EncoderInstaller Default { get; } = new(AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipR", "components"), DownloadClient);
    private readonly SemaphoreSlim installLock = new(1, 1);
    private string InstalledDirectory => Path.Combine(cacheDirectory, "ffmpeg");

    public string? FindInstalled()
    {
        foreach (var path in new[] { Path.Combine(applicationDirectory, "tools", "ffmpeg.exe"), Path.Combine(InstalledDirectory, "ffmpeg.exe") })
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        return null;
    }

    public async Task<string> EnsureAsync(IProgress<EncoderProgress>? progress, CancellationToken cancellationToken)
    {
        // ResponseHeadersRead only applies HttpClient.Timeout through response headers.
        // Keep the response body, checksum and extraction under the same deadline too.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromMinutes(15));
        cancellationToken = deadline.Token;
        await installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (FindInstalled() is { } existing) return existing;
            progress?.Report(new("正在连接 FFmpeg 下载站…"));
            var checksumText = await client.GetStringAsync(DownloadUrl + ".sha256", cancellationToken).ConfigureAwait(false);
            var expected = checksumText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!Regex.IsMatch(expected, "\\A[0-9a-fA-F]{64}\\z")) throw new InvalidDataException("下载站返回的校验信息无效，请稍后重试。");

            Directory.CreateDirectory(cacheDirectory);
            temporary = Path.Combine(cacheDirectory, ".download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            var archivePath = Path.Combine(temporary, "ffmpeg.zip");
            using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long received = 0;
                var total = response.Content.Headers.ContentLength;
                var clock = Stopwatch.StartNew();
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    received += count;
                    if (clock.ElapsedMilliseconds >= 100 || received == total)
                    {
                        var percent = total > 0 ? Math.Min(100, 100.0 * received / total.Value) : (double?)null;
                        var size = total > 0 ? $"{received / 1048576.0:F1} / {total / 1048576.0:F1} MB" : $"{received / 1048576.0:F1} MB";
                        progress?.Report(new("正在下载 FFmpeg · " + size, percent));
                        clock.Restart();
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("正在校验下载文件…"));
            await using (var file = File.OpenRead(archivePath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
                if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("FFmpeg 下载文件校验失败，未安装该文件。请重试。");
            }
            progress?.Report(new("正在安装录像组件…"));
            var staged = Directory.CreateDirectory(Path.Combine(temporary, "ready")).FullName;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var (source, target) in new[] { ("ffmpeg.exe", "ffmpeg.exe"), ("LICENSE", "FFmpeg-LICENSE.txt"), ("README.txt", "FFmpeg-README.txt") })
                {
                    var entries = archive.Entries.Where(e => e.Name.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (entries.Length != 1 || entries[0].Length == 0) throw new InvalidDataException("FFmpeg 下载包缺少有效文件：" + source);
                    // Fixed destination names: never trust paths supplied by a ZIP entry.
                    await using var input = entries[0].Open();
                    await using var output = File.Create(Path.Combine(staged, target));
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(staged, "FFmpeg-archive.sha256"), expected.ToLowerInvariant() + "  ffmpeg-release-essentials.zip\n", cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Publish the complete directory at once. An interrupted download is never usable.
            var previous = Path.Combine(temporary, "previous");
            if (Directory.Exists(InstalledDirectory)) Directory.Move(InstalledDirectory, previous);
            try { Directory.Move(staged, InstalledDirectory); }
            catch
            {
                if (Directory.Exists(previous)) Directory.Move(previous, InstalledDirectory);
                throw;
            }
            return Path.Combine(InstalledDirectory, "ffmpeg.exe");
        }
        finally
        {
            if (temporary != null)
            {
                try { Directory.Delete(temporary, true); }
                catch (IOException) { /* A scanner may still hold a temporary file. */ }
                catch (UnauthorizedAccessException) { }
            }
            installLock.Release();
        }
    }
}
