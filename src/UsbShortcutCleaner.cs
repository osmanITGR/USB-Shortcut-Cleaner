// USB Shortcut Virus Cleaner & Restorer
// Build with build.bat (uses the C# compiler that ships with .NET Framework 4.x on every Windows PC).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

// Product name and version are checked by SignPath when signing; a release tag vX.Y.Z must match this version.
[assembly: AssemblyTitle("USB Shortcut Virus Cleaner & Restorer")]
[assembly: AssemblyProduct("USB Shortcut Cleaner")]
[assembly: AssemblyCompany("Md Osman")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Md Osman (WhatsApp: +8801625251930). MIT License.")]
[assembly: AssemblyVersion("2.1.1.0")]
[assembly: AssemblyFileVersion("2.1.1.0")]

namespace USBShortcutCleaner
{
    public class DriveItem
    {
        public string Root;
        public string Text;
        public override string ToString() { return Text; }
    }

    public class CleanResult
    {
        public int ProcessesStopped;
        public int PcItemsRemoved;
        public int ShortcutsRemoved;
        public int MalwareRemoved;
        public int ItemsRestored;
        public int ItemsUnhidden;
        public int ItemsScanned;
        public int Errors;
        public bool Cancelled;

        public bool FoundAnything
        {
            get { return ProcessesStopped + PcItemsRemoved + ShortcutsRemoved + MalwareRemoved + ItemsRestored + ItemsUnhidden > 0; }
        }
    }

    // Lists only USB pendrives, memory cards and USB hard disks. Internal disks are never offered,
    // so the cleaner can't be pointed at a big internal drive by accident.
    public static class DriveDetector
    {
        public static List<DriveItem> FindUsbDrives()
        {
            HashSet<char> usbLetters = GetUsbDriveLetters();
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            var list = new List<DriveItem>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    bool usb = d.DriveType == DriveType.Removable
                        || (d.DriveType == DriveType.Fixed && usbLetters.Contains(char.ToUpperInvariant(d.Name[0])));
                    if (!usb || !d.IsReady) continue;

                    string label = d.VolumeLabel;
                    if (string.IsNullOrEmpty(label)) label = d.DriveType == DriveType.Removable ? "Removable Disk" : "USB Disk";
                    list.Add(new DriveItem
                    {
                        Root = d.Name,
                        Text = string.Format(CultureInfo.InvariantCulture, "{0} [{1}] ({2:0.#} GB free / {3:0.#} GB)",
                            d.Name, label, d.TotalFreeSpace / 1073741824.0, d.TotalSize / 1073741824.0)
                    });
                }
                catch { }
            }
            return list;
        }

        static HashSet<char> GetUsbDriveLetters()
        {
            var letters = new HashSet<char>();
            try
            {
                // Windows 8+: the Storage API reports the real bus type (also catches UASP enclosures).
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                var usbDisks = new HashSet<uint>();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Number, BusType FROM MSFT_Disk")))
                    foreach (ManagementBaseObject disk in s.Get())
                        if (Convert.ToInt32(disk["BusType"]) == 7) usbDisks.Add(Convert.ToUInt32(disk["Number"]));

                if (usbDisks.Count > 0)
                    using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition")))
                        foreach (ManagementBaseObject part in s.Get())
                        {
                            object letter = part["DriveLetter"];
                            if (letter == null || !usbDisks.Contains(Convert.ToUInt32(part["DiskNumber"]))) continue;
                            char c = letter is char ? (char)letter : (char)Convert.ToUInt16(letter);
                            if (char.IsLetter(c)) letters.Add(char.ToUpperInvariant(c));
                        }
                return letters;
            }
            catch { }

            try
            {
                // Windows 7 fallback.
                using (var s = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                    foreach (ManagementObject disk in s.Get())
                        foreach (ManagementObject part in disk.GetRelated("Win32_DiskPartition"))
                            foreach (ManagementObject ld in part.GetRelated("Win32_LogicalDisk"))
                            {
                                string id = Convert.ToString(ld["DeviceID"]);
                                if (!string.IsNullOrEmpty(id)) letters.Add(char.ToUpperInvariant(id[0]));
                            }
            }
            catch { }
            return letters;
        }
    }

    // Win32 file APIs on \\?\ paths. System.IO silently trims trailing spaces, dots and U+00A0 from paths,
    // so "F:\<nbsp>" - the virus's favourite hiding folder - would turn into "F:\", the whole drive.
    public static class NativeFs
    {
        public class Entry
        {
            public string Name;
            public string FullPath; // \\?\ form
            public FileAttributes Attributes;
            public bool IsDirectory { get { return (Attributes & FileAttributes.Directory) != 0; } }
            public bool IsReparsePoint { get { return (Attributes & FileAttributes.ReparsePoint) != 0; } }
            public string Extension
            {
                get { int i = Name.LastIndexOf('.'); return i < 0 ? "" : Name.Substring(i).ToLowerInvariant(); }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FindData
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint SizeHigh, SizeLow, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string ShortName;
        }

        static readonly IntPtr InvalidHandle = new IntPtr(-1);
        const uint InvalidAttributes = 0xFFFFFFFF;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExW(string fileName, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool FindNextFileW(IntPtr handle, out FindData data);
        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFileAttributesW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetFileAttributesW(string path, uint attributes);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFileW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool RemoveDirectoryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileExW(string from, string to, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        public static string Long(string path)
        {
            if (path.StartsWith(@"\\?\")) return path;
            if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        public static string Display(string path)
        {
            if (path.StartsWith(@"\\?\UNC\")) return @"\\" + path.Substring(8);
            return path.StartsWith(@"\\?\") ? path.Substring(4) : path;
        }

        public static string Combine(string dir, string name)
        {
            return dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
        }

        static Win32Exception LastError() { return new Win32Exception(Marshal.GetLastWin32Error()); }

        public static List<Entry> List(string dir)
        {
            var list = new List<Entry>();
            FindData fd;
            IntPtr h = FindFirstFileExW(Combine(dir, "*"), 1 /* FindExInfoBasic */, out fd, 0, IntPtr.Zero, 2 /* LARGE_FETCH */);
            if (h == InvalidHandle)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 2 || err == 18) return list; // empty
                throw new Win32Exception(err);
            }
            try
            {
                do
                {
                    if (fd.Name == "." || fd.Name == "..") continue;
                    list.Add(new Entry { Name = fd.Name, FullPath = Combine(dir, fd.Name), Attributes = (FileAttributes)fd.Attributes });
                } while (FindNextFileW(h, out fd));
            }
            finally { FindClose(h); }
            return list;
        }

        public static bool Exists(string path) { return GetFileAttributesW(path) != InvalidAttributes; }

        public static FileAttributes GetAttributes(string path)
        {
            uint a = GetFileAttributesW(path);
            if (a == InvalidAttributes) throw LastError();
            return (FileAttributes)a;
        }

        public static void SetAttributes(string path, FileAttributes attributes)
        {
            if (!SetFileAttributesW(path, (uint)attributes)) throw LastError();
        }

        public static void DeleteFile(string path)
        {
            SetFileAttributesW(path, (uint)FileAttributes.Normal);
            if (!DeleteFileW(path)) throw LastError();
        }

        public static void RemoveDirectory(string path)
        {
            SetFileAttributesW(path, (uint)FileAttributes.Normal);
            if (!RemoveDirectoryW(path)) throw LastError();
        }

        public static void DeleteTree(string dir)
        {
            foreach (Entry e in List(dir))
            {
                if (!e.IsDirectory) DeleteFile(e.FullPath);
                else if (e.IsReparsePoint) RemoveDirectory(e.FullPath); // removes the link only, never its target
                else DeleteTree(e.FullPath);
            }
            RemoveDirectory(dir);
        }

        public static void Move(string from, string to)
        {
            if (!MoveFileExW(from, to, 0)) throw LastError();
        }

        public static byte[] ReadSmallFile(string path, int maxBytes)
        {
            SafeFileHandle h = CreateFileW(path, 0x80000000 /* GENERIC_READ */, 7 /* share all */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (h.IsInvalid) throw LastError();
            using (var fs = new FileStream(h, FileAccess.Read))
            {
                if (fs.Length > maxBytes) return null;
                var data = new byte[fs.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = fs.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return data;
            }
        }
    }

    public class UsbCleaner
    {
        static readonly string[] ScriptExts = { ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta" };
        static readonly string[] PayloadExts = { ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".exe", ".dll", ".bat", ".cmd", ".ps1", ".scr", ".pif", ".com", ".lnk" };
        static readonly string[] MalwareFolders = { "sysvolume", "WindowsServices", "drive", "Recycle", "RECYCLER" };
        static readonly string[] StrongMalwareFolders = { "sysvolume", "WindowsServices" };
        static readonly string[] SkipDirs = { "System Volume Information", "$RECYCLE.BIN", "RECYCLER", "RECYCLED", ".Trashes", ".Spotlight-V100", ".fseventsd", ".TemporaryItems" };
        static readonly string[] SkipFiles = { "desktop.ini", "thumbs.db", ".DS_Store", "IndexerVolumeGuid", "WPSettings.dat" };

        const FileAttributes HiddenOrSystem = FileAttributes.Hidden | FileAttributes.System;
        const FileAttributes Settable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive
            | FileAttributes.Temporary | FileAttributes.Offline | FileAttributes.NotContentIndexed;
        const int MaxLoggedErrors = 30;

        // Anything a shortcut virus uses to launch its payload from a .lnk.
        static readonly Regex VirusShortcutPattern = new Regex(
            @"wscript|cscript|mshta|rundll32|regsvr32|powershell|\bcmd(\.exe)?\b|%comspec%|javascript:|vbscript:|\.(vbs|vbe|jse?|wsf|wsh|hta)(?![a-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Stricter version for this PC's startup entries, where cmd/powershell/rundll32 are often legitimate.
        static readonly Regex VirusStartupPattern = new Regex(
            @"wscript|cscript|mshta|javascript:|vbscript:|\.(vbs|vbe|jse?|wsf|wsh|hta)(?![a-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Every script path inside a command line (lookahead so overlapping candidates are all tried).
        static readonly Regex ScriptPathPattern = new Regex(
            @"(?=([a-z]:\\[^""*?<>|\r\n]*?\.(?:vbs|vbe|jse?|wsf|wsh|hta)(?![a-z0-9])))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        public string VolumeLabel;

        readonly string root;        // \\?\F:\
        readonly string rootDisplay; // F:\
        readonly Func<bool> isCancelled;
        readonly Action<string> log;
        readonly Action<int, string> progress;
        readonly CleanResult result = new CleanResult();
        readonly HashSet<string> labelContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public UsbCleaner(string root, Func<bool> isCancelled, Action<string> log, Action<int, string> progress)
        {
            rootDisplay = root.EndsWith("\\") ? root : root + "\\";
            this.root = NativeFs.Long(rootDisplay);
            this.isCancelled = isCancelled;
            this.log = log;
            this.progress = progress;
            if (rootDisplay.Length == 3)
                try { VolumeLabel = new DriveInfo(rootDisplay).VolumeLabel; } catch { }
        }

        public static bool IsSuspiciousStartupCommand(string commandLine)
        {
            return VirusStartupPattern.IsMatch(commandLine);
        }

        bool Cancelled { get { return isCancelled != null && isCancelled(); } }

        void Log(string message) { if (log != null) log(message); }

        void Progress(int percent, string text) { if (progress != null) progress(percent, text); }

        void Fail(string message)
        {
            result.Errors++;
            if (result.Errors <= MaxLoggedErrors) Log(message);
            else if (result.Errors == MaxLoggedErrors + 1) Log("(More errors occurred - not all are shown.)");
        }

        public CleanResult Run(bool stopProcesses, bool cleanPc)
        {
            if (stopProcesses)
            {
                Progress(5, "Step 1/5: Stopping virus scripts running on this PC...");
                StopVirusProcesses();
            }
            if (cleanPc && !Cancelled)
            {
                Progress(15, "Step 2/5: Removing the virus from this PC's startup...");
                CleanPcStartup();
            }
            if (!Cancelled)
            {
                Progress(30, "Step 3/5: Removing fake shortcuts and virus files from the drive...");
                FindLabelContainers(); // must run before the shortcuts it relies on are deleted
                CleanDriveRoot();
            }
            if (!Cancelled)
            {
                Progress(45, "Step 4/5: Moving your files out of the virus's hidden folder...");
                RestoreTrappedFiles();
            }
            if (!Cancelled)
            {
                Progress(60, "Step 5/5: Unhiding files and folders...");
                UnhideAll();
            }
            result.Cancelled = Cancelled;
            try { SHChangeNotify(0x1000 /* SHCNE_UPDATEDIR */, 0x0005 /* SHCNF_PATHW */, rootDisplay, IntPtr.Zero); } catch { }
            return result;
        }

        // ---------- Step 1: running virus processes ----------

        void StopVirusProcesses()
        {
            int self = Process.GetCurrentProcess().Id;
            string rootLower = rootDisplay.ToLowerInvariant();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process"))
                    foreach (ManagementBaseObject p in s.Get())
                    {
                        int pid = Convert.ToInt32(p["ProcessId"]);
                        if (pid == self) continue;
                        string name = Convert.ToString(p["Name"]).ToLowerInvariant();
                        string exe = Convert.ToString(p["ExecutablePath"]);
                        string cmd = Convert.ToString(p["CommandLine"]).ToLowerInvariant();

                        string reason = null;
                        if (name == "wscript.exe" || name == "cscript.exe" || name == "mshta.exe")
                            reason = "script host";
                        else if (exe.StartsWith(rootDisplay, StringComparison.OrdinalIgnoreCase) && IsHiddenProgram(exe))
                            reason = "hidden program on the USB drive";
                        else if ((name == "cmd.exe" || name == "powershell.exe" || name == "rundll32.exe" || name == "regsvr32.exe")
                                 && cmd.Contains(rootLower) && VirusStartupPattern.IsMatch(cmd))
                            reason = "running a script from the USB drive";
                        if (reason != null) KillProcess(pid, name, reason);
                    }
                return;
            }
            catch (Exception ex)
            {
                Log("Detailed process scan unavailable (" + ex.Message + "), using basic check.");
            }
            foreach (string n in new[] { "wscript", "cscript", "mshta" })
                foreach (Process p in Process.GetProcessesByName(n))
                    KillProcess(p.Id, n + ".exe", "script host");
        }

        // True when the program, or any folder it sits in on the USB, is hidden or looks like a virus folder.
        bool IsHiddenProgram(string exePath)
        {
            string path = exePath;
            while (path.Length > rootDisplay.Length)
            {
                int cut = path.LastIndexOf('\\');
                if (cut < 0) break;
                string name = path.Substring(cut + 1);
                if (LooksBlank(name) || IsIn(StrongMalwareFolders, name)) return true;
                try { if ((NativeFs.GetAttributes(NativeFs.Long(path)) & HiddenOrSystem) != 0) return true; } catch { }
                path = path.Substring(0, cut);
            }
            return false;
        }

        void KillProcess(int pid, string name, string reason)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                result.ProcessesStopped++;
                Log("Stopped " + name + " (PID " + pid + ") - " + reason);
            }
            catch (ArgumentException) { } // already exited
            catch (Exception ex) { Fail("Could not stop " + name + " (PID " + pid + "): " + ex.Message + AdminHint()); }
        }

        // ---------- Step 2: this PC's startup (stops re-infection) ----------

        void CleanPcStartup()
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string runOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
            CleanRunKey(Registry.CurrentUser, "HKCU", run);
            CleanRunKey(Registry.CurrentUser, "HKCU", runOnce);
            CleanRunKey(Registry.LocalMachine, "HKLM", run);
            CleanRunKey(Registry.LocalMachine, "HKLM", runOnce);
            CleanRunKey(Registry.LocalMachine, "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
            CleanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            CleanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        }

        void CleanRunKey(RegistryKey hive, string hiveName, string path)
        {
            string where = hiveName + "\\" + path;
            RegistryKey key = null;
            bool writable = true;
            try { key = hive.OpenSubKey(path, true); } catch { }
            if (key == null)
            {
                writable = false;
                try { key = hive.OpenSubKey(path, false); } catch { }
            }
            if (key == null) return;

            using (key)
                foreach (string name in key.GetValueNames())
                {
                    string data = Convert.ToString(key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames));
                    if (!IsSuspiciousStartupCommand(data)) continue;
                    if (!writable)
                    {
                        Fail("Suspicious startup entry found, but administrator rights are needed to remove it: " + where + " -> " + name + " = " + data);
                        continue;
                    }
                    try
                    {
                        key.DeleteValue(name, false);
                        result.PcItemsRemoved++;
                        Log("Removed startup entry " + where + " -> " + name + " = " + data);
                        DeleteScriptsReferencedBy(data);
                    }
                    catch (Exception ex) { Fail("Could not remove startup entry " + name + ": " + ex.Message + AdminHint()); }
                }
        }

        void DeleteScriptsReferencedBy(string commandLine)
        {
            string expanded = Environment.ExpandEnvironmentVariables(commandLine);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (Match m in ScriptPathPattern.Matches(expanded))
            {
                string path;
                try { path = Path.GetFullPath(m.Groups[1].Value); } catch { continue; }
                if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || !NativeFs.Exists(NativeFs.Long(path))) continue;
                if (DeleteFile(NativeFs.Long(path))) Log("Deleted virus script on this PC: " + path);
            }
        }

        void CleanStartupFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            List<NativeFs.Entry> files;
            try { files = NativeFs.List(NativeFs.Long(folder)); } catch { return; }
            foreach (NativeFs.Entry f in files)
            {
                if (f.IsDirectory) continue;
                bool bad = IsIn(ScriptExts, f.Extension) || (f.Extension == ".lnk" && FileMatches(f.FullPath, VirusStartupPattern));
                if (bad && DeleteFile(f.FullPath))
                {
                    result.PcItemsRemoved++;
                    Log("Removed from Startup folder: " + NativeFs.Display(f.FullPath));
                }
            }
        }

        // ---------- Step 3: drive root ----------

        void CleanDriveRoot()
        {
            List<NativeFs.Entry> entries;
            try { entries = NativeFs.List(root); }
            catch (Exception ex) { Fail("Cannot read the drive: " + ex.Message); return; }

            foreach (NativeFs.Entry e in entries)
            {
                if (Cancelled) return;
                if (!e.IsDirectory)
                {
                    if (e.Extension == ".lnk")
                    {
                        if (DeleteFile(e.FullPath)) { result.ShortcutsRemoved++; Log("Deleted fake shortcut: " + e.Name); }
                    }
                    else if (IsIn(ScriptExts, e.Extension))
                    {
                        if (DeleteFile(e.FullPath)) { result.MalwareRemoved++; Log("Deleted virus script: " + e.Name); }
                    }
                    else if (e.Name.Equals("autorun.inf", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DeleteFile(e.FullPath)) { result.MalwareRemoved++; Log("Deleted autorun.inf"); }
                    }
                    continue;
                }

                if (IsMalwareFolder(e) && DeleteDirectory(e))
                {
                    result.MalwareRemoved++;
                    Log("Deleted virus folder: " + e.Name);
                }
            }
        }

        // Some variants move everything into a hidden folder named after the drive label ("Ventoy", "KINGSTON"...)
        // and leave "<label>.lnk" pointing at the virus script. Only trusted when the virus's traces are present.
        void FindLabelContainers()
        {
            var names = new List<string> { "USB Drive", "Removable Disk", "USB Disk" };
            if (!string.IsNullOrEmpty(VolumeLabel)) names.Add(VolumeLabel);
            List<NativeFs.Entry> entries;
            try { entries = NativeFs.List(root); } catch { return; }

            bool virusFolderPresent = entries.Exists(e => e.IsDirectory && IsIn(StrongMalwareFolders, e.Name));
            foreach (NativeFs.Entry e in entries)
            {
                if (!e.IsDirectory || e.IsReparsePoint || (e.Attributes & HiddenOrSystem) != HiddenOrSystem) continue;
                if (!names.Exists(n => n.Equals(e.Name, StringComparison.OrdinalIgnoreCase))) continue;
                string shortcut = root + e.Name + ".lnk";
                if (virusFolderPresent || (NativeFs.Exists(shortcut) && FileMatches(shortcut, VirusShortcutPattern)))
                    labelContainers.Add(e.Name);
            }
        }

        bool IsMalwareFolder(NativeFs.Entry dir)
        {
            if (!IsIn(MalwareFolders, dir.Name) || dir.IsReparsePoint) return false;
            bool hidden = (dir.Attributes & HiddenOrSystem) != 0;
            bool strong = IsIn(StrongMalwareFolders, dir.Name);
            // A hidden "sysvolume"/"WindowsServices" is always the virus, even after an antivirus emptied it.
            if (strong && hidden) return true;
            // "drive", "Recycle" etc. can be real user folders: only treat them as the virus when hidden.
            if (!strong && !hidden) return false;
            if (ContainsPayload(dir.FullPath)) return true;
            Log("Kept folder '" + dir.Name + "' (no virus files inside).");
            return false;
        }

        static bool ContainsPayload(string dir)
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            int seen = 0;
            while (stack.Count > 0)
            {
                List<NativeFs.Entry> items;
                try { items = NativeFs.List(stack.Pop()); } catch { continue; }
                foreach (NativeFs.Entry i in items)
                {
                    if (++seen > 5000) return false;
                    if (i.IsDirectory) { if (!i.IsReparsePoint) stack.Push(i.FullPath); }
                    else if (IsIn(PayloadExts, i.Extension)) return true;
                }
            }
            return false;
        }

        // ---------- Step 4: files trapped in the virus's blank-named folder ----------

        void RestoreTrappedFiles()
        {
            List<NativeFs.Entry> entries;
            try { entries = NativeFs.List(root); }
            catch (Exception ex) { Fail("Cannot read the drive: " + ex.Message); return; }

            foreach (NativeFs.Entry dir in entries)
            {
                if (Cancelled) return;
                if (!dir.IsDirectory || dir.IsReparsePoint) continue;
                bool hidden = (dir.Attributes & HiddenOrSystem) != 0;
                if (!LooksBlank(dir.Name) && !(dir.Name == "_" && hidden) && !labelContainers.Contains(dir.Name)) continue;

                Log("Found the virus's hidden folder (" + (LooksBlank(dir.Name) ? "blank name" : dir.Name) + ") - moving your files back...");
                List<NativeFs.Entry> items;
                try { items = NativeFs.List(dir.FullPath); }
                catch (Exception ex) { Fail("Cannot open the hidden folder: " + ex.Message); continue; }

                foreach (NativeFs.Entry item in items)
                {
                    if (Cancelled) return;
                    if (item.IsDirectory && IsIn(SkipDirs, item.Name)) continue;
                    string target = UniqueTarget(item.Name, item.IsDirectory);
                    try
                    {
                        NativeFs.Move(item.FullPath, target);
                        result.ItemsRestored++;
                        string newName = target.Substring(root.Length);
                        Log("Restored: " + newName + (newName == item.Name ? "" : "  (renamed - an item with that name already existed)"));
                    }
                    catch (Exception ex) { Fail("Could not restore " + item.Name + ": " + ex.Message); }
                }

                try
                {
                    if (NativeFs.List(dir.FullPath).Count == 0)
                    {
                        NativeFs.RemoveDirectory(dir.FullPath);
                        Log("Removed the now-empty hidden folder.");
                    }
                    else Log("Some items are still in the hidden folder, so it was left in place (it will be unhidden).");
                }
                catch (Exception ex) { Fail("Could not remove the empty hidden folder: " + ex.Message); }
            }
        }

        string UniqueTarget(string name, bool isDir)
        {
            string target = root + name;
            if (!NativeFs.Exists(target)) return target;
            int dot = isDir ? -1 : name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name;
            string ext = dot > 0 ? name.Substring(dot) : "";
            for (int i = 1; ; i++)
            {
                target = root + stem + (i == 1 ? " (restored)" : " (restored " + i + ")") + ext;
                if (!NativeFs.Exists(target)) return target;
            }
        }

        // ---------- Step 5: unhide everything ----------

        void UnhideAll()
        {
            var stack = new Stack<Tuple<string, int>>();
            stack.Push(Tuple.Create(root, 0));
            var timer = Stopwatch.StartNew();

            while (stack.Count > 0)
            {
                if (Cancelled) return;
                Tuple<string, int> next = stack.Pop();
                int depth = next.Item2;
                List<NativeFs.Entry> items;
                try { items = NativeFs.List(next.Item1); }
                catch (Exception ex) { Fail("Skipped folder " + Show(next.Item1) + ": " + ex.Message); continue; }

                foreach (NativeFs.Entry item in items)
                {
                    result.ItemsScanned++;
                    if (timer.ElapsedMilliseconds > 250)
                    {
                        timer.Restart();
                        if (Cancelled) return;
                        Progress(-1, string.Format("Step 5/5: Unhiding... {0:N0} items checked, {1:N0} unhidden", result.ItemsScanned, result.ItemsUnhidden));
                    }

                    FileAttributes a = item.Attributes;
                    if (item.IsDirectory ? IsIn(SkipDirs, item.Name) : (IsIn(SkipFiles, item.Name) || item.Name.StartsWith("._"))) continue;

                    // Root-level files were handled in step 3; deeper down, only remove things that are clearly the virus.
                    if (!item.IsDirectory && depth > 0)
                    {
                        if (item.Extension == ".lnk" && FileMatches(item.FullPath, VirusShortcutPattern))
                        {
                            if (DeleteFile(item.FullPath)) { result.ShortcutsRemoved++; Log("Deleted fake shortcut: " + Show(item.FullPath)); }
                            continue;
                        }
                        if ((a & FileAttributes.Hidden) != 0 && IsIn(ScriptExts, item.Extension))
                        {
                            if (DeleteFile(item.FullPath)) { result.MalwareRemoved++; Log("Deleted hidden virus script: " + Show(item.FullPath)); }
                            continue;
                        }
                    }

                    if ((a & HiddenOrSystem) != 0)
                    {
                        FileAttributes na = a & ~HiddenOrSystem;
                        // The virus often adds read-only together with hidden+system.
                        if ((a & HiddenOrSystem) == HiddenOrSystem) na &= ~FileAttributes.ReadOnly;
                        na &= Settable;
                        if (na == 0) na = FileAttributes.Normal;
                        try
                        {
                            NativeFs.SetAttributes(item.FullPath, na);
                            result.ItemsUnhidden++;
                            if (depth == 0) Log("Unhidden: " + item.Name);
                        }
                        catch (Exception ex) { Fail("Could not unhide " + Show(item.FullPath) + ": " + ex.Message); }
                    }

                    // Never follow junctions/symlinks: they can point to other drives or loop forever.
                    if (item.IsDirectory && !item.IsReparsePoint)
                        stack.Push(Tuple.Create(item.FullPath, depth + 1));
                }
            }
            Log(string.Format("Checked {0:N0} files and folders.", result.ItemsScanned));
        }

        // ---------- helpers ----------

        bool DeleteFile(string path)
        {
            try { NativeFs.DeleteFile(path); return true; }
            catch (Exception ex) { Fail("Could not delete " + Show(path) + ": " + ex.Message); return false; }
        }

        bool DeleteDirectory(NativeFs.Entry dir)
        {
            try { NativeFs.DeleteTree(dir.FullPath); return true; }
            catch (Exception ex) { Fail("Could not delete folder " + dir.Name + ": " + ex.Message); return false; }
        }

        string Show(string path)
        {
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : NativeFs.Display(path);
        }

        static bool FileMatches(string path, Regex pattern)
        {
            byte[] data;
            try { data = NativeFs.ReadSmallFile(path, 256 * 1024); }
            catch { return false; }
            if (data == null) return false;
            // Shortcut files store paths both as ANSI and UTF-16 (at either byte alignment).
            return pattern.IsMatch(Encoding.ASCII.GetString(data))
                || pattern.IsMatch(Encoding.Unicode.GetString(data))
                || (data.Length > 1 && pattern.IsMatch(Encoding.Unicode.GetString(data, 1, data.Length - 1)));
        }

        // Shortcut viruses hide everything in a folder whose name looks empty (non-breaking space and similar).
        public static bool LooksBlank(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                if (c == '\u2800' || c == '\u3164' || c == '\u115F' || c == '\u1160' || c == '\uFFA0' || c == '\u00AD') continue;
                return false;
            }
            return true;
        }

        static bool IsIn(string[] list, string value)
        {
            foreach (string s in list)
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string AdminHint()
        {
            return MainForm.IsAdministrator() ? "" : " (try 'Run as administrator')";
        }
    }

    public class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string DownloadUrl;
        public string Sha256; // lowercase hex from GitHub's asset digest, null if GitHub did not provide one
        public long Size;
    }

    // In-app updates from this project's GitHub releases, so users never need a browser.
    // The release must contain an asset named USB_Shortcut_Cleaner.exe (the build workflow does this).
    public static class Updater
    {
        public const string Repository = "osmanITGR/USB-Shortcut-Cleaner";
        public const string AssetName = "USB_Shortcut_Cleaner.exe";
        public const string ProductName = "USB Shortcut Cleaner";
        const string SettingsKey = @"Software\USB Shortcut Cleaner";
        const long MaxDownloadBytes = 50L * 1024 * 1024;

        public static Version CurrentVersion
        {
            get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static bool AutoCheckEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                        return k == null || Convert.ToInt32(k.GetValue("CheckForUpdates", 1)) != 0;
                }
                catch { return true; }
            }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                        k.SetValue("CheckForUpdates", value ? 1 : 0, RegistryValueKind.DWord);
                }
                catch { }
            }
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        public static Version ParseVersion(string text)
        {
            Version v;
            string s = (text ?? "").Trim().TrimStart('v', 'V');
            return Version.TryParse(s, out v) ? Normalize(v) : null;
        }

        static HttpWebRequest CreateRequest(string url)
        {
            // GitHub only accepts TLS 1.2+; apps built for .NET 4.0 don't enable it by default.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 768 | 192); } catch { }
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "USB-Shortcut-Cleaner/" + CurrentVersion.ToString(3);
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            return request;
        }

        // The latest published release (GitHub skips drafts and pre-releases), or null if it has no usable exe.
        public static UpdateInfo GetLatestRelease()
        {
            HttpWebRequest request = CreateRequest("https://api.github.com/repos/" + Repository + "/releases/latest");
            request.Accept = "application/vnd.github+json";
            string json;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                json = reader.ReadToEnd();

            var release = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (release == null || !release.ContainsKey("tag_name") || !release.ContainsKey("assets")) return null;
            var info = new UpdateInfo { Tag = release["tag_name"] as string };
            info.Version = ParseVersion(info.Tag);

            var assets = release["assets"] as object[];
            if (assets != null)
                foreach (object o in assets)
                {
                    var asset = o as Dictionary<string, object>;
                    if (asset == null || !AssetName.Equals(asset["name"] as string, StringComparison.OrdinalIgnoreCase)) continue;
                    info.DownloadUrl = asset["browser_download_url"] as string;
                    info.Size = Convert.ToInt64(asset["size"]);
                    string digest = asset.ContainsKey("digest") ? asset["digest"] as string : null;
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        info.Sha256 = digest.Substring(7).ToLowerInvariant();
                }
            return info.Version == null || info.DownloadUrl == null ? null : info;
        }

        // Downloads the update next to the running exe and verifies it. Returns the verified file's path.
        public static string Download(UpdateInfo info, string exePath, Action<int> progress)
        {
            if (!info.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Refusing to download the update over an insecure connection.");
            string target = exePath + ".new";
            try
            {
                using (var response = (HttpWebResponse)CreateRequest(info.DownloadUrl).GetResponse())
                using (Stream input = response.GetResponseStream())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = response.ContentLength > 0 ? response.ContentLength : info.Size;
                    var buffer = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, n);
                        done += n;
                        if (done > MaxDownloadBytes) throw new InvalidDataException("The download is unexpectedly large.");
                        if (progress != null && total > 0) progress((int)Math.Min(100, done * 100 / total));
                    }
                }
                Verify(target, info);
                return target;
            }
            catch
            {
                TryDelete(target);
                throw;
            }
        }

        public static void Verify(string file, UpdateInfo info)
        {
            if (info.Size > 0 && new FileInfo(file).Length != info.Size)
                throw new InvalidDataException("The downloaded file is incomplete.");
            if (info.Sha256 != null)
            {
                string hash;
                using (var sha = new SHA256CryptoServiceProvider())
                using (FileStream s = File.OpenRead(file))
                    hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                if (hash != info.Sha256) throw new InvalidDataException("The downloaded file is damaged (SHA-256 does not match).");
            }
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(file);
            if (v.ProductName != ProductName || ParseVersion(v.ProductVersion) != info.Version)
                throw new InvalidDataException("The downloaded file is not " + ProductName + " " + info.Tag + ".");
        }

        // A running exe cannot be overwritten, but it can be renamed: move it aside and put the new one in its place.
        public static void Swap(string newFile, string exePath)
        {
            string old = exePath + ".old";
            TryDelete(old);
            File.Move(exePath, old);
            try { File.Move(newFile, exePath); }
            catch
            {
                File.Move(old, exePath);
                throw;
            }
        }

        public static void UndoSwap(string newFile, string exePath)
        {
            File.Move(exePath, newFile);
            File.Move(exePath + ".old", exePath);
        }

        // Removes the previous version once it has exited (it may still be closing right after an update).
        // A ".new" file is only removed when stale, so another open window's fresh download is left alone.
        public static void DeleteLeftovers(string exePath)
        {
            string stale = exePath + ".new";
            try
            {
                if (File.Exists(stale) && DateTime.UtcNow - File.GetLastWriteTimeUtc(stale) > TimeSpan.FromHours(1)) TryDelete(stale);
            }
            catch { }

            for (int i = 0; i < 20 && File.Exists(exePath + ".old"); i++)
            {
                TryDelete(exePath + ".old");
                if (File.Exists(exePath + ".old")) System.Threading.Thread.Sleep(500);
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch { }
        }
    }

    public class MainForm : Form
    {
        static readonly Color CleanColor = Color.FromArgb(16, 185, 129);
        static readonly Color StopColor = Color.FromArgb(220, 38, 38);

        Label lblTitle, lblSub, lblSelect, lblStatus, lblCopyright;
        LinkLabel lnkUpdate;
        ComboBox cmbDrives;
        Button btnRefresh, btnClean, btnOpen;
        CheckBox chkCleanPc, chkAutoUpdate;
        ProgressBar progressBar;
        TextBox txtLog;
        BackgroundWorker worker;
        Timer uiTimer, deviceTimer;

        readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        readonly object statusLock = new object();
        readonly bool isAdmin = IsAdministrator();
        string pendingStatus;
        int pendingPercent = -1;
        bool loadingDrives, drivesChangedWhileBusy, closeWhenDone;
        UpdateInfo availableUpdate;
        bool checkingUpdate, updating;
        static bool startedAfterUpdate;

        [STAThread]
        static void Main()
        {
            try
            {
                startedAfterUpdate = Array.IndexOf(Environment.GetCommandLineArgs(), "--updated") > 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Startup error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static bool IsAdministrator()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public MainForm()
        {
            InitializeComponent();
        }

        void InitializeComponent()
        {
            Text = "USB Shortcut Virus Cleaner & Restorer v" + Updater.CurrentVersion.ToString(3) + (isAdmin ? " (Administrator)" : "");
            ClientSize = new Size(666, 610);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5f);

            lblTitle = new Label
            {
                Text = "USB Shortcut Virus Cleaner",
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(24, 18),
                Size = new Size(420, 32)
            };
            lnkUpdate = new LinkLabel
            {
                Text = "Check for updates",
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false,
                Location = new Point(444, 24),
                Size = new Size(198, 22)
            };
            lnkUpdate.LinkClicked += LnkUpdate_LinkClicked;
            lblSub = new Label
            {
                Text = "One-click tool to remove shortcut viruses and restore all hidden files.",
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(24, 50),
                Size = new Size(620, 24)
            };
            lblSelect = new Label
            {
                Text = "Select Target USB Drive:",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(24, 88),
                Size = new Size(300, 22)
            };
            cmbDrives = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10.5f),
                Location = new Point(24, 114),
                Size = new Size(480, 32)
            };
            cmbDrives.SelectedIndexChanged += delegate { UpdateButtons(); };

            btnRefresh = new Button
            {
                Text = "Refresh",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(516, 112),
                Size = new Size(125, 34),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnRefresh.Click += delegate { LoadDrives(); };

            chkCleanPc = new CheckBox
            {
                Text = "Also remove the virus from this computer's startup (stops it re-infecting the USB)",
                Checked = true,
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(26, 154),
                Size = new Size(615, 24)
            };

            btnClean = new Button
            {
                Text = "Scan && Clean USB Drive",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = CleanColor,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(24, 186),
                Size = new Size(330, 44),
                Cursor = Cursors.Hand
            };
            btnClean.FlatAppearance.BorderSize = 0;
            btnClean.Click += BtnClean_Click;

            btnOpen = new Button
            {
                Text = "Open in Explorer",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.FromArgb(226, 232, 240),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(370, 186),
                Size = new Size(271, 44),
                Cursor = Cursors.Hand
            };
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Click += BtnOpen_Click;

            lblStatus = new Label
            {
                Text = "Looking for USB drives...",
                UseMnemonic = false,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(24, 240),
                Size = new Size(618, 22)
            };
            progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Location = new Point(24, 264),
                Size = new Size(618, 14)
            };
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                MaxLength = 0,
                Font = new Font("Consolas", 9.5f),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(24, 290),
                Size = new Size(618, 260)
            };
            chkAutoUpdate = new CheckBox
            {
                Text = "Check for new versions when the program starts (contacts github.com)",
                Checked = Updater.AutoCheckEnabled,
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(26, 558),
                Size = new Size(615, 24)
            };
            chkAutoUpdate.CheckedChanged += delegate { Updater.AutoCheckEnabled = chkAutoUpdate.Checked; };
            lblCopyright = new Label
            {
                Text = "© 2026 Md Osman  ·  WhatsApp: +8801625251930",
                UseMnemonic = false,
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(24, 584),
                Size = new Size(618, 20)
            };

            Controls.AddRange(new Control[] { lblTitle, lnkUpdate, lblSub, lblSelect, cmbDrives, btnRefresh, chkCleanPc, btnClean, btnOpen, lblStatus, progressBar, txtLog, chkAutoUpdate, lblCopyright });

            worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            worker.DoWork += Worker_DoWork;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;

            // Log lines from the worker are batched and shown a few times per second so the UI never floods.
            uiTimer = new Timer { Interval = 150 };
            uiTimer.Tick += delegate { FlushUi(); };
            uiTimer.Start();

            // Debounces USB plug/unplug notifications.
            deviceTimer = new Timer { Interval = 1500 };
            deviceTimer.Tick += delegate
            {
                deviceTimer.Stop();
                if (worker.IsBusy) drivesChangedWhileBusy = true; else LoadDrives();
            };

            UpdateButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startedAfterUpdate) Log("Updated successfully to v" + Updater.CurrentVersion.ToString(3) + ".");
            Log("Ready. Only USB drives and memory cards are listed - internal disks are never touched.");
            if (!isAdmin) Log("Note: not running as administrator. Cleaning the USB works; some PC cleanup may need 'Run as administrator'.");
            LoadDrives();

            string exe = Updater.ExePath;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Updater.DeleteLeftovers(exe); });
            if (chkAutoUpdate.Checked) CheckForUpdates(false);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_DEVICECHANGE = 0x0219;
            if (m.Msg == WM_DEVICECHANGE && deviceTimer != null)
            {
                long ev = m.WParam.ToInt64();
                if (ev == 0x8000 /* DBT_DEVICEARRIVAL */ || ev == 0x8004 /* DBT_DEVICEREMOVECOMPLETE */)
                {
                    deviceTimer.Stop();
                    deviceTimer.Start();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (worker.IsBusy)
            {
                e.Cancel = true;
                closeWhenDone = true;
                worker.CancelAsync();
                lblStatus.Text = "Stopping, please wait...";
            }
        }

        void LoadDrives()
        {
            if (loadingDrives) return;
            loadingDrives = true;
            btnRefresh.Enabled = false;
            string previous = SelectedRoot();
            lblStatus.Text = "Looking for USB drives...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<DriveItem> drives;
                try { drives = DriveDetector.FindUsbDrives(); }
                catch { drives = new List<DriveItem>(); }
                try { BeginInvoke(new Action(() => ShowDrives(drives, previous))); }
                catch (InvalidOperationException) { } // form already closed
            });
        }

        void ShowDrives(List<DriveItem> drives, string previous)
        {
            loadingDrives = false;
            cmbDrives.Items.Clear();
            int select = 0;
            for (int i = 0; i < drives.Count; i++)
            {
                cmbDrives.Items.Add(drives[i]);
                if (string.Equals(drives[i].Root, previous, StringComparison.OrdinalIgnoreCase)) select = i;
            }
            if (drives.Count > 0) cmbDrives.SelectedIndex = select;

            if (!worker.IsBusy)
                lblStatus.Text = drives.Count == 0
                    ? "No USB drive found. Plug in a pendrive or memory card - it will appear here automatically."
                    : "Ready. Select a drive and click 'Scan & Clean USB Drive'.";
            UpdateButtons();
        }

        string SelectedRoot()
        {
            var item = cmbDrives.SelectedItem as DriveItem;
            return item == null ? null : item.Root;
        }

        void UpdateButtons()
        {
            bool cleaning = worker != null && worker.IsBusy;
            bool busy = cleaning || updating;
            bool hasDrive = cmbDrives.SelectedItem != null;
            cmbDrives.Enabled = !busy;
            chkCleanPc.Enabled = !busy;
            btnRefresh.Enabled = !busy && !loadingDrives;
            btnOpen.Enabled = hasDrive;
            if (!cleaning) btnClean.Enabled = hasDrive && !updating;
            if (lnkUpdate != null) lnkUpdate.Enabled = !busy;
        }

        // ---------- updates ----------

        void CheckForUpdates(bool userRequested)
        {
            if (checkingUpdate) return;
            checkingUpdate = true;
            if (userRequested) lnkUpdate.Text = "Checking...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                Exception error = null;
                try { info = Updater.GetLatestRelease(); }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new Action(() => UpdateCheckDone(info, error, userRequested))); }
                catch (InvalidOperationException) { } // form already closed
            });
        }

        void UpdateCheckDone(UpdateInfo info, Exception error, bool userRequested)
        {
            checkingUpdate = false;
            if (info != null && info.Version > Updater.CurrentVersion)
            {
                availableUpdate = info;
                lnkUpdate.Text = "Update available: " + info.Tag;
                lnkUpdate.Font = new Font(lnkUpdate.Font, FontStyle.Bold);
                lnkUpdate.LinkColor = CleanColor;
                Log("A new version " + info.Tag + " is available - click 'Update available' at the top right to install it.");
                if (userRequested) StartUpdate();
                return;
            }

            lnkUpdate.Text = "Check for updates";
            if (!userRequested) return;
            if (error != null)
                MessageBox.Show(this, "Could not check for updates: " + error.Message + "\n\nPlease check the internet connection and try again.",
                    "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, "You already have the latest version (v" + Updater.CurrentVersion.ToString(3) + ").",
                    "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void LnkUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (worker.IsBusy || updating) return;
            if (availableUpdate != null) StartUpdate(); else CheckForUpdates(true);
        }

        void StartUpdate()
        {
            UpdateInfo info = availableUpdate;
            string question = "Version " + info.Tag + " is available (you have v" + Updater.CurrentVersion.ToString(3) + ").\n\n"
                + "Download and install it now? The program will restart.";
            if (MessageBox.Show(this, question, "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            updating = true;
            UpdateButtons();
            progressBar.Value = 0;
            lblStatus.Text = "Downloading update...";
            Log("Downloading " + info.Tag + " from GitHub...");
            string exe = Updater.ExePath;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null;
                Exception error = null;
                try
                {
                    file = Updater.Download(info, exe, percent =>
                    {
                        lock (statusLock)
                        {
                            pendingPercent = percent;
                            pendingStatus = "Downloading update... " + percent + "%";
                        }
                    });
                }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new Action(() => FinishUpdate(info, exe, file, error))); }
                catch (InvalidOperationException) { } // form already closed; the leftover is removed on next start
            });
        }

        void FinishUpdate(UpdateInfo info, string exe, string file, Exception error)
        {
            FlushUi();
            if (error == null)
            {
                try
                {
                    Updater.Swap(file, exe);
                    try
                    {
                        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                    }
                    catch
                    {
                        Updater.UndoSwap(file, exe);
                        throw;
                    }
                    Close();
                    return;
                }
                catch (Exception ex) { error = ex; }
            }

            updating = false;
            UpdateButtons();
            progressBar.Value = 0;
            lblStatus.Text = "Update failed.";
            Log("Update failed: " + error.Message);
            string hint = error is UnauthorizedAccessException
                ? "\n\nThis folder is read-only for your account. Run the program as administrator, or move it to another folder (for example the Desktop) and try again."
                : "";
            MessageBox.Show(this, "Could not install the update: " + error.Message + hint, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Log(string message)
        {
            logQueue.Enqueue("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }

        void FlushUi()
        {
            var sb = new StringBuilder();
            string line;
            while (logQueue.TryDequeue(out line)) sb.Append(line).Append("\r\n");
            if (sb.Length > 0)
            {
                txtLog.AppendText(sb.ToString());
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }

            string status;
            int percent;
            lock (statusLock)
            {
                status = pendingStatus;
                percent = pendingPercent;
                pendingStatus = null;
                pendingPercent = -1;
            }
            if (status != null) lblStatus.Text = status;
            if (percent >= 0) progressBar.Value = Math.Max(0, Math.Min(100, percent));
        }

        void BtnOpen_Click(object sender, EventArgs e)
        {
            string root = SelectedRoot();
            if (root == null)
            {
                MessageBox.Show(this, "Please select a drive first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if (!Directory.Exists(root))
            {
                MessageBox.Show(this, "The drive " + root + " is no longer connected.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                LoadDrives();
                return;
            }
            Process.Start("explorer.exe", "\"" + root + "\"");
            Log("Opened " + root + " in Explorer.");
        }

        void BtnClean_Click(object sender, EventArgs e)
        {
            if (worker.IsBusy)
            {
                worker.CancelAsync();
                btnClean.Enabled = false;
                btnClean.Text = "Stopping...";
                return;
            }

            var item = cmbDrives.SelectedItem as DriveItem;
            if (item == null)
            {
                MessageBox.Show(this, "Please connect and select a USB drive first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            if (!Directory.Exists(item.Root))
            {
                MessageBox.Show(this, "The drive " + item.Root + " is no longer connected.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                LoadDrives();
                return;
            }

            btnClean.Text = "Stop";
            btnClean.BackColor = StopColor;
            progressBar.Value = 0;
            lblStatus.Text = "Cleaning drive " + item.Root + " ...";
            Log("==========================================");
            Log("Starting clean on " + item.Text);
            worker.RunWorkerAsync(new object[] { item.Root, chkCleanPc.Checked });
            UpdateButtons();
        }

        void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var args = (object[])e.Argument;
            var cleaner = new UsbCleaner((string)args[0], () => worker.CancellationPending, Log, (percent, text) =>
            {
                lock (statusLock)
                {
                    if (percent >= 0) pendingPercent = percent;
                    if (text != null) pendingStatus = text;
                }
            });
            e.Result = cleaner.Run(true, (bool)args[1]);
        }

        void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            FlushUi();
            btnClean.Text = "Scan && Clean USB Drive";
            btnClean.BackColor = CleanColor;
            UpdateButtons();

            if (closeWhenDone)
            {
                Close();
                return;
            }
            if (drivesChangedWhileBusy)
            {
                drivesChangedWhileBusy = false;
                LoadDrives();
            }

            if (e.Error != null)
            {
                Log("Fatal error: " + e.Error.Message);
                lblStatus.Text = "Cleaning failed.";
                MessageBox.Show(this, "Error during cleanup: " + e.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var r = (CleanResult)e.Result;
            string summary = string.Format(
                "Fake shortcuts removed: {0}\nVirus files/folders removed: {1}\nFiles restored from the hidden folder: {2}\nFiles/folders unhidden: {3}\nVirus items removed from this PC: {4}",
                r.ShortcutsRemoved, r.MalwareRemoved, r.ItemsRestored, r.ItemsUnhidden, r.ProcessesStopped + r.PcItemsRemoved);
            Log("==========================================");
            Log("SUMMARY: " + summary.Replace("\n", " | "));
            FlushUi();

            if (r.Cancelled)
            {
                lblStatus.Text = "Stopped by user.";
                MessageBox.Show(this, "Cleaning was stopped.\n\n" + summary, "Stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            progressBar.Value = 100;
            lblStatus.Text = r.Errors > 0 ? "Clean finished with " + r.Errors + " problem(s) - see the log." : "Clean completed successfully!";
            string message = r.FoundAnything ? "USB drive clean completed!\n\n" + summary : "No virus traces were found on this drive.\n\n" + summary;
            if (r.Errors > 0)
            {
                message += "\n\n" + r.Errors + " item(s) could not be processed - see the log for details.";
                if (!isAdmin) message += "\nTip: right-click the program and choose 'Run as administrator', then clean again.";
            }
            MessageBox.Show(this, message, r.Errors > 0 ? "Clean Finished" : "Clean Successful", MessageBoxButtons.OK,
                r.Errors > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }
}
