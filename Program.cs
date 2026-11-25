using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inlayo.Launcher;

internal static class Program
{
    private const string PatcherUrl = "https://assets.inlayo.com/patcher/Inlayo-patcher.exe";
    private const string WindowTitle = "Inlayo Launcher";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static async Task<int> Main()
    {
        try
        {
            return await RunAsync();
        }
        catch (LauncherException exception)
        {
            ShowError(exception.Message);
            return 1;
        }
        catch (Exception)
        {
            ShowError("Oops! Something went wrong. Please try again.");
            return 1;
        }
    }

    private static async Task<int> RunAsync()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new LauncherException("Couldn't find your Local AppData folder.");

        var settingsDirectory = Path.Combine(localAppData, "Inlayo-launcher");
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        var osuPath = LoadOsuPath(settingsPath);

        if (!IsValidOsuPath(osuPath))
        {
            osuPath = SelectOsuPath();
            if (osuPath is null)
                return 0;

            Directory.CreateDirectory(settingsDirectory);
            SaveSettings(settingsPath, osuPath);
        }

        var osuStartInfo = new ProcessStartInfo
        {
            FileName = osuPath,
            WorkingDirectory = Path.GetDirectoryName(osuPath)!,
            UseShellExecute = false,
        };
        osuStartInfo.ArgumentList.Add("-devserver");
        osuStartInfo.ArgumentList.Add("inlayo.com");

        StartProcess(osuStartInfo, "Couldn't start osu!. Check that osu!.exe still exists.");

        var patcherPath = Path.Combine(Path.GetTempPath(), "Inlayo-patcher.exe");
        var launchDelay = Task.Delay(TimeSpan.FromSeconds(5));
        var patcherDownload = DownloadPatcherAsync(patcherPath);
        await Task.WhenAny(launchDelay, patcherDownload);
        if (patcherDownload.IsCompleted)
            await patcherDownload;

        await launchDelay;
        await patcherDownload;

        StartProcess(new ProcessStartInfo
        {
            FileName = patcherPath,
            UseShellExecute = true,
        }, "Couldn't start the patcher. Please try again.");

        return 0;
    }

    private static string? LoadOsuPath(string settingsPath)
    {
        if (!File.Exists(settingsPath))
            return null;

        try
        {
            var settings = JsonSerializer.Deserialize(
                File.ReadAllText(settingsPath),
                LauncherJsonContext.Default.LauncherSettings);
            return settings?.OsuPath;
        }
        catch (JsonException)
        {
            ShowError("Couldn't read your saved osu! path. Please select osu!.exe again.");
            return null;
        }
        catch (IOException exception)
        {
            throw new LauncherException("Couldn't read your saved settings. Please try again.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LauncherException("Couldn't read your saved settings. Please try again.", exception);
        }
    }

    private static bool IsValidOsuPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetFileName(path), "osu!.exe", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    private static string? SelectOsuPath()
    {
        while (true)
        {
            var selectedPath = NativeMethods.SelectOsuExecutable();
            if (selectedPath is null)
                return null;

            if (IsValidOsuPath(selectedPath))
                return Path.GetFullPath(selectedPath);

            ShowError("Please select osu!.exe.");
        }
    }

    private static void SaveSettings(string settingsPath, string osuPath)
    {
        var temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(
                new LauncherSettings { OsuPath = osuPath },
                LauncherJsonContext.Default.LauncherSettings);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        catch (IOException exception)
        {
            throw new LauncherException("Couldn't save your settings. Please try again.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LauncherException("Couldn't save your settings. Please try again.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task DownloadPatcherAsync(string patcherPath)
    {
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"Inlayo-patcher-{Guid.NewGuid():N}.download");
        try
        {
            using var response = await HttpClient.GetAsync(
                PatcherUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination);
            }

            File.Move(temporaryPath, patcherPath, overwrite: true);
        }
        catch (HttpRequestException exception)
        {
            throw new LauncherException(
                "Couldn't download the patcher. Check your internet connection and try again.",
                exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new LauncherException(
                "Couldn't download the patcher. Check your internet connection and try again.",
                exception);
        }
        catch (IOException exception)
        {
            throw new LauncherException("Couldn't save the patcher. Check your Temp folder and try again.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LauncherException("Couldn't save the patcher. Check your Temp folder and try again.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void StartProcess(ProcessStartInfo startInfo, string errorMessage)
    {
        try
        {
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException(errorMessage);
        }
        catch (Win32Exception exception)
        {
            throw new LauncherException(errorMessage, exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new LauncherException(errorMessage, exception);
        }
    }

    private static void ShowError(string message) =>
        NativeMethods.ShowMessage(message, WindowTitle, NativeMethods.MessageBoxError);

}

internal sealed class LauncherException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class LauncherSettings
{
    public string OsuPath { get; init; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LauncherSettings))]
internal partial class LauncherJsonContext : JsonSerializerContext;

internal static partial class NativeMethods
{
    internal const uint MessageBoxError = 0x10;

    private const uint OfnFileMustExist = 0x1000;
    private const uint OfnPathMustExist = 0x800;
    private const uint OfnNoChangeDir = 0x8;
    private const int FileNameCapacity = 32768;

    internal static unsafe string? SelectOsuExecutable()
    {
        char* fileName = stackalloc char[FileNameCapacity];

        fixed (char* filter = "osu!.exe\0osu!.exe\0Executable files (*.exe)\0*.exe\0\0")
        fixed (char* title = "Select osu!.exe")
        {
            var dialog = new OpenFileName
            {
                Size = (uint)sizeof(OpenFileName),
                Filter = filter,
                File = fileName,
                MaxFile = FileNameCapacity,
                Title = title,
                Flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir,
            };

            if (GetOpenFileNameW(&dialog) != 0)
                return new string(fileName);

            var error = CommDlgExtendedError();
            if (error != 0)
                throw new LauncherException("Couldn't open the file picker. Please try again.");

            return null;
        }
    }

    internal static void ShowMessage(string message, string title, uint type) =>
        _ = MessageBoxW(0, message, title, type);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct OpenFileName
    {
        internal uint Size;
        internal nint Owner;
        internal nint Instance;
        internal char* Filter;
        internal char* CustomFilter;
        internal uint MaxCustomFilter;
        internal uint FilterIndex;
        internal char* File;
        internal uint MaxFile;
        internal char* FileTitle;
        internal uint MaxFileTitle;
        internal char* InitialDirectory;
        internal char* Title;
        internal uint Flags;
        internal ushort FileOffset;
        internal ushort FileExtension;
        internal char* DefaultExtension;
        internal nint CustomData;
        internal nint Hook;
        internal char* TemplateName;
        internal nint Reserved;
        internal uint ReservedValue;
        internal uint ExtendedFlags;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    private static unsafe partial int GetOpenFileNameW(OpenFileName* dialog);

    [LibraryImport("comdlg32.dll", EntryPoint = "CommDlgExtendedError")]
    private static partial uint CommDlgExtendedError();

    [LibraryImport(
        "user32.dll",
        EntryPoint = "MessageBoxW",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint window, string message, string title, uint type);
}
