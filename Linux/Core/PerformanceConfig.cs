// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArdysaModsTools.Linux;

public sealed class PerformanceConfig
{
    private const string Relative = "game/dota/cfg/autoexec.cfg";
    private const string Begin = "// BEGIN ArdysaModsTools Linux FPS settings";
    private const string End = "// END ArdysaModsTools Linux FPS settings";
    private static readonly Regex Block = new(@"(?m)^// BEGIN ArdysaModsTools Linux FPS settings\r?\n[\s\S]*?^// END ArdysaModsTools Linux FPS settings(?:\r?\n|$)");
    public async Task SaveAsync(string root, int? fpsMax, int? menuFps, CancellationToken ct = default)
    {
        if (!SteamDetector.Validate(root)) throw new IOException("Select a native Dota installation.");
        if (SteamDetector.GameRunning()) throw new IOException("Close Dota 2 before editing autoexec.");
        if (fpsMax is < 0 or > 1000 || menuFps is < 0 or > 1000 || (fpsMax is null) != (menuFps is null))
            throw new ArgumentException("Both FPS limits must be between 0 and 1000, or both null to remove the block.");
        using var transaction = new FileTransaction(root);
        var file = LinuxPaths.SafeChild(root, Relative);
        var existed = File.Exists(file);
        var original = existed ? await File.ReadAllTextAsync(file, ct) : "";
        var matches = Block.Matches(original);
        if (matches.Count > 1 || (original.Contains(Begin, StringComparison.Ordinal) && matches.Count != 1)
            || (original.Contains(End, StringComparison.Ordinal) && matches.Count != 1))
            throw new IOException("FPS settings block is damaged; inspect autoexec.cfg first.");
        var text = Block.Replace(original, "");
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (fpsMax is not null)
        {
            if (text.Length > 0 && !text.EndsWith('\n')) text += newline;
            text += Begin + newline + "fps_max \"" + fpsMax.Value.ToString(CultureInfo.InvariantCulture) + "\"" + newline
                + "fps_max_ui \"" + menuFps!.Value.ToString(CultureInfo.InvariantCulture) + "\"" + newline + End + newline;
        }
        var temporary = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), ct);
            await transaction.ApplyAsync([new(Relative, text.Length == 0 && fpsMax is null ? null : temporary)], ct);
        }
        finally { File.Delete(temporary); }
    }
}
