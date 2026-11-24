# Inlayo-launcher

A small Windows launcher for osu! that starts the game with the Inlayo dev server and then runs the Inlayo patcher.

## What it does

- Reads the osu! executable path from `%LOCALAPPDATA%\Inlayo-launcher\settings.json`.
- Opens a file picker to select `osu!.exe` when no valid saved path exists.
- Starts osu! with `-devserver inlayo.com`.
- Downloads a fresh copy of `Inlayo-patcher.exe` from `https://assets.inlayo.com/patcher/Inlayo-patcher.exe` into `%TEMP%` on every launch, replacing any existing copy, then runs it.
- Publishes as a framework-dependent single-file executable to keep the download small.
  The target PC needs the .NET 11 Runtime.

The launcher downloads and executes a remote executable on every run. Only use it if you trust `assets.inlayo.com`.

## Build

Install the .NET 11 SDK, then run from this directory:

```powershell
dotnet publish -c Release -r win-x86 --self-contained false
```

The resulting `Inlayo-launcher.exe` is in `bin\Release\net11.0\win-x86\publish\`. It is a single file, but requires the .NET 11 x86 Runtime on the target PC.
