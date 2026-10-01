# USB Shortcut Virus Cleaner & Restorer

A lightweight, portable, native Windows GUI application to permanently remove USB shortcut viruses (`.vbs`, `.lnk`, `sysvolume`, `autorun.inf`) and instantly restore all hidden/system files and directories.

---

## 🌟 Features

- **100% Native & Portable**: Single standalone executable (`.exe`), zero external dependencies, no installation needed.
- **Universal Windows Support**: Runs on Windows 7, 8, 8.1, 10, and 11 (supports both 32-bit and 64-bit systems).
- **Auto-Detection**: Automatically detects plugged-in removable/USB drives with free space and volume names.
- **Rogue Script Killer**: Automatically terminates malicious active background script engines (`wscript.exe`, `cscript.exe`) locking USB files.
- **Malware Purging**: Completely deletes fake shortcut files (`*.lnk`), payload directories (`sysvolume`, `drive`, `WindowsServices`, `recycler`), and `autorun.inf`.
- **Deep File Recovery**: Recursively unhides all hidden, system-protected, and read-only files and folders.
- **Volume Directory Restorer**: Automatically detects if files were hidden inside a subfolder matching the drive volume label and restores them directly to the drive root.
- **Responsive UI**: Multi-threaded with real-time progress bar, detailed log console, and one-click Explorer button.

---

## 🚀 How to Run

1. Download or run **`USB_Shortcut_Cleaner.exe`**.
2. Select your USB drive from the dropdown.
3. Click **"Scan & Clean USB Drive"**.
4. Once completed, click **"Open in Explorer"** to view your recovered files.

---

## 🛠️ How to Build from Source

This project requires only the built-in Microsoft C# compiler (`csc.exe`) included with any Windows .NET Framework 4.0+.

Run `build.bat` or execute in Command Prompt:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll /out:USB_Shortcut_Cleaner.exe Program.cs
```

---

## 📄 License
Open source under the [MIT License](LICENSE).
