// SPDX-License-Identifier: GPL-3.0-or-later
namespace ArdysaModsTools.Linux;

public sealed class LinuxPaths
{
    public string Config { get; }
    public string Data { get; }
    public string Cache { get; }
    public LinuxPaths(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Config = Root("XDG_CONFIG_HOME", Path.Combine(home, ".config"));
        Data = Root("XDG_DATA_HOME", Path.Combine(home, ".local", "share"));
        Cache = Root("XDG_CACHE_HOME", Path.Combine(home, ".cache"));
    }
    private static string Root(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return Path.Combine(value is not null && Path.IsPathFullyQualified(value) ? value : fallback,
            "ardysa-mods-tools");
    }
    public static string SafeChild(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\'))
            throw new InvalidDataException("Expected a relative Linux path.");
        var parts = relative.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.Contains('\0')))
            throw new InvalidDataException("Unsafe path component.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        EnsureNoLinks(full);
        return full;
    }
    public static void EnsureNoLinks(string path)
    {
        var full = Path.GetFullPath(path);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (new FileInfo(current).LinkTarget is not null)
                throw new IOException($"Symbolic links are not allowed in writable paths: {current}");
        }
    }
}
