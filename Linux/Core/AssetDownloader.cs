// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace ArdysaModsTools.Linux;

public sealed class AssetDownloader : IDisposable
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    { "cdn.ardysamods.my.id", "cdn2.ardysamods.my.id", "raw.githubusercontent.com", "cdn.jsdelivr.net" };
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(30) };
    public async Task DownloadAsync(IReadOnlyList<string> urls, string expectedSha256, string destination,
        CancellationToken ct = default)
    {
        if (expectedSha256.Length != 64 || expectedSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("A SHA-256 digest from a trusted manifest is required.");
        if (urls.Count is < 1 or > 8) throw new InvalidDataException("Provide one to eight mirrors.");
        LinuxPaths.EnsureNoLinks(destination);
        if (File.Exists(destination)) throw new IOException("Download destination already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var errors = new List<string>();
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !uri.IsDefaultPort || !AllowedHosts.Contains(uri.Host) || uri.UserInfo.Length != 0)
                throw new InvalidDataException("Download host is not an approved upstream HTTPS host.");
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                const long limit = uint.MaxValue;
                if (response.Content.Headers.ContentLength > limit) throw new IOException("Asset exceeds 4 GiB.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using (var output = new FileStream(temporary, FileMode.CreateNew))
                {
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[128 * 1024];
                    long total = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        total += count;
                        if (total > limit) throw new IOException("Asset exceeds 4 GiB.");
                        digest.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    }
                    var actual = Convert.ToHexString(digest.GetHashAndReset());
                    if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SHA-256 mismatch; download rejected.");
                    output.Flush(true);
                }
                File.Move(temporary, destination);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            { errors.Add($"{uri.Host}: {ex.Message}"); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        throw new IOException("Every mirror failed: " + string.Join("; ", errors));
    }
    public void Dispose() => client.Dispose();
}
