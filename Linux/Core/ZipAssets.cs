// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Compression;

namespace ArdysaModsTools.Linux;

public static class ZipAssets
{
    public static async Task ExtractAsync(string archive, string destination, CancellationToken ct = default)
    {
        LinuxPaths.EnsureNoLinks(destination);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 100_000) throw new InvalidDataException("Too many ZIP entries.");
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (name.EndsWith('/')) { LinuxPaths.SafeChild(destination, name.TrimEnd('/')); continue; }
            var target = LinuxPaths.SafeChild(destination, name);
            // Reject UNIX symlinks and preserve the installed game's items_game as the only baseline.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("ZIP symbolic links are not allowed.");
            if (name == "scripts/items/items_game.txt")
                throw new InvalidDataException("A remote archive must not supply the game's items_game baseline.");
            total = checked(total + entry.Length);
            if (total > uint.MaxValue) throw new InvalidDataException("Extracted ZIP data exceeds 4 GiB.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew);
            var buffer = new byte[128 * 1024];
            long copied = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                copied += count;
                if (copied > entry.Length) throw new InvalidDataException("ZIP entry exceeded declared size.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (copied != entry.Length) throw new InvalidDataException("Truncated ZIP entry.");
        }
    }
}
