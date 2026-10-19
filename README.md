# USB Shortcut Cleaner

A small, portable Windows tool that removes USB "shortcut viruses" from pendrives and memory cards and brings back the files the virus hid.

বাংলা ব্যবহার নির্দেশিকা: [User_Guide.txt](User_Guide.txt)

## What it does

A shortcut virus hides everything on a pendrive and replaces it with shortcuts (`.lnk`) that run the virus every time you open them. One click on **Scan & Clean USB Drive**:

1. Stops running virus scripts (`wscript`, `cscript`, `mshta`, and hidden programs started from the USB drive).
2. Optionally removes the virus from this PC's startup (`Run`/`RunOnce` registry entries and Startup folder items that launch scripts), so it cannot re-infect the drive. Every removed entry is written to the log.
3. Deletes the fake shortcuts, `autorun.inf`, virus scripts and virus folders (`sysvolume`, `WindowsServices`, and hidden `drive`/`Recycle`/`RECYCLER` folders that contain scripts or programs).
4. Moves your files back out of the virus's hiding folder: a folder with an invisible name (non-breaking space and similar) or a hidden folder named after the drive label. If a file with the same name already exists, the restored one is renamed to `name (restored)`. Nothing is overwritten.
5. Unhides all files and folders. Junctions and symlinks are never followed.

Only USB pendrives, memory cards and USB hard disks are listed. Internal disks are never offered.

## Download

Get `USB_Shortcut_Cleaner.exe` from the [Releases](https://github.com/osmanITGR/USB-Shortcut-Cleaner/releases) page. It needs no installation and runs on Windows 7 SP1 (with .NET Framework 4), 8, 8.1, 10 and 11.

The program asks for administrator rights when the account allows it. They are needed to stop elevated virus processes and to clean machine-wide startup entries. Cleaning the USB drive itself also works without them.

## Build from source

Run `build.bat`. It uses the C# compiler that ships with the .NET Framework on every Windows PC, so nothing needs to be installed. The source is in [src/UsbShortcutCleaner.cs](src/UsbShortcutCleaner.cs).

Release builds are made by [GitHub Actions](.github/workflows/build.yml) from the tagged source, and code signing runs in the same workflow (see below). To publish a release, set the version in `src/UsbShortcutCleaner.cs` and push a matching tag, for example `v2.0.0`.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Committers and reviewers: [osmanITGR](https://github.com/osmanITGR)
- Approvers: [osmanITGR](https://github.com/osmanITGR)

Only binaries built by GitHub Actions from this repository's source are signed, and every signing request is approved manually.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License

[MIT](LICENSE)
