using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using SnipR;

// Real filesystem/ZIP/hash operations; only the external HTTP boundary is replaced.
var root = Path.Combine(Path.GetTempPath(), "SnipR-setup-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failures = 0;
try
{
    await Check("verified download installs executable and notices; cached launch works offline", async dir =>
    {
        var zip = Archive();
        using var client = Client(zip);
        var installer = new EncoderInstaller(Path.Combine(dir, "app"), Path.Combine(dir, "cache"), client);
        var path = await installer.EnsureAsync(null, default);
        Require(await File.ReadAllTextAsync(path) == "test executable", "Executable contents");
        Require(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "FFmpeg-LICENSE.txt")), "License retained");
        Require(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "FFmpeg-README.txt")), "README retained");
        Require(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "FFmpeg-archive.sha256")), "Checksum retained");
        using var offline = new HttpClient(new FixtureHandler(_ => throw new HttpRequestException("offline")));
        var again = new EncoderInstaller(Path.Combine(dir, "app"), Path.Combine(dir, "cache"), offline);
        Require(await again.EnsureAsync(null, default) == path, "Offline reuse");
    });
    await Check("bundled encoder is preferred without internet or cache writes", async dir =>
    {
        var tools = Directory.CreateDirectory(Path.Combine(dir, "app", "tools")).FullName;
        var path = Path.Combine(tools, "ffmpeg.exe");
        await File.WriteAllTextAsync(path, "provided encoder");
        using var client = new HttpClient(new FixtureHandler(_ => throw new HttpRequestException("offline")));
        var installer = new EncoderInstaller(Path.Combine(dir, "app"), Path.Combine(dir, "cache"), client);
        Require(await installer.EnsureAsync(null, default) == path, "Bundled encoder preference");
        Require(!Directory.Exists(Path.Combine(dir, "cache")), "No unnecessary install");
    });
    await Check("checksum mismatch never publishes an executable; retry succeeds", async dir =>
    {
        using var bad = Client(Archive(), new string('0', 64));
        var installer = new EncoderInstaller(Path.Combine(dir, "app"), Path.Combine(dir, "cache"), bad);
        await Reject<InvalidDataException>(() => installer.EnsureAsync(null, default));
        Require(installer.FindInstalled() == null, "Bad hash must not install");
        Require(!Directory.EnumerateFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories).Any(), "No unverified executable");
        using var good = Client(Archive());
        Require(File.Exists(await new EncoderInstaller(Path.Combine(dir, "app"), Path.Combine(dir, "cache"), good).EnsureAsync(null, default)), "Retry");
    });
    await Check("malformed publisher checksum is rejected", async dir =>
    {
        using var client = Client(Archive(), "not a checksum");
        var installer = new EncoderInstaller(dir, Path.Combine(dir, "cache"), client);
        await Reject<InvalidDataException>(() => installer.EnsureAsync(null, default));
        Require(installer.FindInstalled() == null, "Invalid checksum must not install");
    });
    await Check("missing license leaves no partial installation", async dir =>
    {
        using var client = Client(Archive(includeLicense: false));
        var installer = new EncoderInstaller(dir, Path.Combine(dir, "cache"), client);
        await Reject<InvalidDataException>(() => installer.EnsureAsync(null, default));
        Require(installer.FindInstalled() == null, "Incomplete package must not install");
    });
    await Check("cancel during download removes temporary files", async dir =>
    {
        using var cancel = new CancellationTokenSource();
        using var client = Client(Archive());
        var installer = new EncoderInstaller(dir, Path.Combine(dir, "cache"), client);
        await Reject<OperationCanceledException>(() => installer.EnsureAsync(new InlineProgress(p =>
        {
            if (p.Percent > 0) cancel.Cancel();
        }), cancel.Token));
        Require(installer.FindInstalled() == null, "Cancellation must not install");
        Require(!Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(), "Cancelled data cleaned");
    });
    await Check("stalled response body times out and leaves no partial installation", async dir =>
    {
        using var client = new HttpClient(new FixtureHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".sha256")
            ? new(HttpStatusCode.OK) { Content = new StringContent(new string('0', 64)) }
            : new(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
        var installer = new EncoderInstaller(dir, Path.Combine(dir, "cache"), client, TimeSpan.FromMilliseconds(100));
        await Reject<OperationCanceledException>(() => installer.EnsureAsync(null, default).WaitAsync(TimeSpan.FromSeconds(2)));
        Require(installer.FindInstalled() == null, "Timeout must not install");
        Require(!Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(), "Timed-out data cleaned");
    });
    await Check("network failure can be retried", async dir =>
    {
        using var offline = new HttpClient(new FixtureHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var installer = new EncoderInstaller(dir, Path.Combine(dir, "cache"), offline);
        await Reject<HttpRequestException>(() => installer.EnsureAsync(null, default));
        Require(installer.FindInstalled() == null, "Network failure must not install");
        using var client = Client(Archive());
        Require(File.Exists(await new EncoderInstaller(dir, Path.Combine(dir, "cache"), client).EnsureAsync(null, default)), "Retry after failure");
    });
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"Setup checks: {failures} failures.");
return failures == 0 ? 0 : 1;

async Task Check(string name, Func<string, Task> test)
{
    var dir = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
    try { await test(dir); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject<T>(Func<Task<string>> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static byte[] Archive(bool includeLicense = true)
{
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
    {
        foreach (var (name, content) in new[] { ("bin/ffmpeg.exe", "test executable"), ("LICENSE", "test license"), ("README.txt", "test readme") })
        {
            if (!includeLicense && name == "LICENSE") continue;
            using var writer = new StreamWriter(zip.CreateEntry("ffmpeg-fixture/" + name).Open());
            writer.Write(content);
        }
    }
    return stream.ToArray();
}
static HttpClient Client(byte[] zip, string? checksum = null) => new(new FixtureHandler(request =>
{
    var url = request.RequestUri!.AbsoluteUri;
    if (url == "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256")
        return new(HttpStatusCode.OK) { Content = new StringContent(checksum ?? Convert.ToHexString(SHA256.HashData(zip))) };
    if (url == "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip")
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
    throw new Exception("Unexpected URL: " + url);
}));
sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
}
sealed class InlineProgress(Action<EncoderProgress> report) : IProgress<EncoderProgress>
{
    public void Report(EncoderProgress value) => report(value);
}
sealed class StalledStream : MemoryStream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}
