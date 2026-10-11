// SPDX-License-Identifier: GPL-3.0-or-later
using ArdysaModsTools.Linux;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This entry point requires Linux. Use the upstream app on Windows.");
    return 1;
}
try { return await LinuxCommand.RunAsync(args); }
catch (OperationCanceledException) { Console.Error.WriteLine("Operation cancelled."); return 130; }
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
{ Console.Error.WriteLine(ex.Message); return 1; }
