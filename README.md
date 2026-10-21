# MemCleaner

A tiny Windows tray app that shows how much RAM is in use and frees memory on demand — handy right before
starting something RAM- or cache-hungry, like a large game or a build.

It's a single native exe (no .NET runtime needed) that stays light while it sits in the tray.

## Installing

Download `MemCleaner.exe` from the [latest release](../../releases/latest), put it in a folder under
`C:\Program Files`, and run it. It asks for administrator rights (UAC), which cleaning memory needs.

## Using it

The tray icon shows the percentage of RAM in use: green below 60%, amber up to 79%, red from 80%. Hover it for
used / total memory, the file cache size and when you last cleaned.

To clean, press **Ctrl+Alt+M**, right-click the icon → **Clean memory**, or launch the exe again. A notification
then shows what was freed, for example:

> **Memory cleaned in 1.4 s**  
> In use −1.2 GB · Cache −3.4 GB · Combined 85 MB  
> RAM 72% → 41%

**Start with Windows** (right-click menu) starts the tray at logon without a UAC prompt. Because it then runs as
administrator, it only works when the exe is in Program Files, where other programs can't replace it.

## What a clean does

1. Trims every process's working set
2. Empties the system file cache
3. Writes modified pages to disk
4. Purges the standby list (cached file data)
5. Combines identical memory pages
6. Flushes the registry's in-memory changes

Freed cache has to be read from disk again later, so MemCleaner never cleans on its own — run it right before you
need the room.

## Problems

Problems show as a notification and are written to `%LocalAppData%\MemCleaner\error.log` and the Windows Event Log
(source **MemCleaner**).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). The native exe also needs Visual Studio's
**Desktop development with C++** workload.

```shell
dotnet publish src/MemCleaner/MemCleaner.csproj -c Release -r win-x64 -o publish
```

GitHub Actions builds the exe for pushes and pull requests to `master`, and a version tag (`v1.2.3` or `1.2.3`) publishes a release. Release
exes carry a build attestation, which you can check with `gh attestation verify MemCleaner.exe --repo <owner>/MemCleaner`.

## License

[MIT](LICENSE)
