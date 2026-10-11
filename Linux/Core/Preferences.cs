// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace ArdysaModsTools.Linux;

public sealed record Preferences(string? DotaRoot = null, bool ExperimentalMount = false);
public sealed class PreferenceStore(LinuxPaths paths)
{
    private readonly string file = Path.Combine(paths.Config, "preferences.json");
    public Preferences Load()
    {
        LinuxPaths.EnsureNoLinks(file);
        return File.Exists(file) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file))
            ?? throw new InvalidDataException("Invalid preferences.") : new();
    }
    public void Save(Preferences value)
    {
        if (value.DotaRoot is not null && !SteamDetector.Validate(value.DotaRoot))
            throw new IOException("Dota root must point to a native Linux installation.");
        LinuxPaths.EnsureNoLinks(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew))
        { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
        File.Move(temporary, file, true);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
