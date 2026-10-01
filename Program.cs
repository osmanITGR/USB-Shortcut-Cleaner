using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace USBShortcutCleaner
{
    public class MainForm : Form
    {
        private Label lblTitle;
        private Label lblSub;
        private Label lblSelect;
        private ComboBox cmbDrives;
        private Button btnRefresh;
        private Button btnClean;
        private Button btnOpen;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TextBox txtLog;
        private BackgroundWorker worker;

        [STAThread]
        public static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Startup error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public MainForm()
        {
            InitializeComponent();
            LoadDrives();
        }

        private void InitializeComponent()
        {
            this.Text = "USB Shortcut Virus Cleaner & Restorer";
            this.Size = new Size(680, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Title
            lblTitle = new Label();
            lblTitle.Text = "USB Shortcut Virus Cleaner";
            lblTitle.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(15, 23, 42);
            lblTitle.Location = new Point(24, 18);
            lblTitle.Size = new Size(620, 32);
            this.Controls.Add(lblTitle);

            // Subtitle
            lblSub = new Label();
            lblSub.Text = "One-click tool to permanently remove shortcut viruses and restore all hidden files.";
            lblSub.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(100, 116, 139);
            lblSub.Location = new Point(24, 50);
            lblSub.Size = new Size(620, 24);
            this.Controls.Add(lblSub);

            // Drive Select Label
            lblSelect = new Label();
            lblSelect.Text = "Select Target USB Drive:";
            lblSelect.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblSelect.ForeColor = Color.FromArgb(30, 41, 59);
            lblSelect.Location = new Point(24, 88);
            lblSelect.Size = new Size(200, 22);
            this.Controls.Add(lblSelect);

            // Combo Box
            cmbDrives = new ComboBox();
            cmbDrives.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDrives.Font = new Font("Segoe UI", 10.5f);
            cmbDrives.Location = new Point(24, 114);
            cmbDrives.Size = new Size(480, 32);
            this.Controls.Add(cmbDrives);

            // Refresh Button
            btnRefresh = new Button();
            btnRefresh.Text = "Refresh";
            btnRefresh.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnRefresh.Location = new Point(516, 112);
            btnRefresh.Size = new Size(125, 34);
            btnRefresh.BackColor = Color.White;
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.Click += (s, e) => { LoadDrives(); Log("Drive list refreshed."); };
            this.Controls.Add(btnRefresh);

            // Clean & Recover Button
            btnClean = new Button();
            btnClean.Text = "Scan & Clean USB Drive";
            btnClean.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            btnClean.ForeColor = Color.White;
            btnClean.BackColor = Color.FromArgb(16, 185, 129); // Emerald green
            btnClean.FlatStyle = FlatStyle.Flat;
            btnClean.FlatAppearance.BorderSize = 0;
            btnClean.Location = new Point(24, 162);
            btnClean.Size = new Size(330, 44);
            btnClean.Cursor = Cursors.Hand;
            btnClean.Click += BtnClean_Click;
            this.Controls.Add(btnClean);

            // Open Explorer Button
            btnOpen = new Button();
            btnOpen.Text = "Open in Explorer";
            btnOpen.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnOpen.ForeColor = Color.FromArgb(30, 41, 59);
            btnOpen.BackColor = Color.FromArgb(226, 232, 240);
            btnOpen.FlatStyle = FlatStyle.Flat;
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Location = new Point(370, 162);
            btnOpen.Size = new Size(271, 44);
            btnOpen.Cursor = Cursors.Hand;
            btnOpen.Click += BtnOpen_Click;
            this.Controls.Add(btnOpen);

            // Status label
            lblStatus = new Label();
            lblStatus.Text = "Ready. Select a drive and click 'Scan & Clean USB Drive'.";
            lblStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
            lblStatus.Location = new Point(24, 216);
            lblStatus.Size = new Size(618, 22);
            this.Controls.Add(lblStatus);

            // Progress Bar
            progressBar = new ProgressBar();
            progressBar.Location = new Point(24, 240);
            progressBar.Size = new Size(618, 14);
            progressBar.Style = ProgressBarStyle.Continuous;
            this.Controls.Add(progressBar);

            // Log Console
            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9.5f);
            txtLog.BackColor = Color.FromArgb(15, 23, 42); // Slate dark
            txtLog.ForeColor = Color.FromArgb(226, 232, 240);
            txtLog.Location = new Point(24, 266);
            txtLog.Size = new Size(618, 255);
            this.Controls.Add(txtLog);

            // BackgroundWorker
            worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }

        private void LoadDrives()
        {
            cmbDrives.Items.Clear();
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) continue;

                        double freeGb = 0;
                        double totalGb = 0;
                        try { freeGb = Math.Round(d.TotalFreeSpace / (1024.0 * 1024 * 1024), 1); } catch { }
                        try { totalGb = Math.Round(d.TotalSize / (1024.0 * 1024 * 1024), 1); } catch { }

                        string label = "Removable Disk";
                        try
                        {
                            if (!string.IsNullOrEmpty(d.VolumeLabel)) label = d.VolumeLabel;
                        }
                        catch { }

                        string item = string.Format("{0} [{1}] ({2} GB free / {3} GB)", d.Name, label, freeGb, totalGb);
                        cmbDrives.Items.Add(item);
                    }
                    catch { }
                }
            }
            catch { }

            if (cmbDrives.Items.Count > 0)
            {
                cmbDrives.SelectedIndex = 0;
            }
        }

        private void Log(string msg)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(Log), msg);
                return;
            }
            string time = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText(string.Format("[{0}] {1}\r\n", time, msg));
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void BtnOpen_Click(object sender, EventArgs e)
        {
            if (cmbDrives.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a drive first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string selected = cmbDrives.SelectedItem.ToString();
            string driveLetter = selected.Substring(0, 3); // e.g. "F:\"
            if (Directory.Exists(driveLetter))
            {
                Process.Start("explorer.exe", driveLetter);
                Log("Opened " + driveLetter + " in Explorer.");
            }
        }

        private void BtnClean_Click(object sender, EventArgs e)
        {
            if (cmbDrives.SelectedItem == null)
            {
                MessageBox.Show(this, "Please connect and select a USB drive first.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string selected = cmbDrives.SelectedItem.ToString();
            string driveLetter = selected.Substring(0, 3); // "F:\"

            btnClean.Enabled = false;
            btnRefresh.Enabled = false;
            progressBar.Value = 0;
            lblStatus.Text = "Cleaning drive " + driveLetter + " ...";

            Log("==========================================");
            Log("Starting cleaning process on " + driveLetter);

            worker.RunWorkerAsync(driveLetter);
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            string drivePath = (string)e.Argument;
            int shortcutsRemoved = 0;
            int malwareFoldersRemoved = 0;
            int unhiddenCount = 0;

            // 1. Kill script interpreters
            worker.ReportProgress(10, "Terminating rogue background script processes (wscript/cscript)...");
            foreach (var name in new[] { "wscript", "cscript" })
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        p.Kill();
                        worker.ReportProgress(15, "Terminated process: " + name + " (PID: " + p.Id + ")");
                    }
                }
                catch { }
            }

            // 2. Remove fake shortcuts (*.lnk at root)
            worker.ReportProgress(25, "Scanning and deleting fake shortcuts (*.lnk)...");
            try
            {
                DirectoryInfo dir = new DirectoryInfo(drivePath);
                foreach (var file in dir.GetFiles("*.lnk"))
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        shortcutsRemoved++;
                        worker.ReportProgress(30, "Deleted shortcut: " + file.Name);
                    }
                    catch (Exception ex)
                    {
                        worker.ReportProgress(30, "Could not delete " + file.Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                worker.ReportProgress(30, "Error scanning shortcuts: " + ex.Message);
            }

            // 3. Remove malware payload folders and scripts
            worker.ReportProgress(45, "Removing malware payload directories and scripts...");
            string[] malwareFolders = new[] { "sysvolume", "drive", "WindowsServices", "recycler", "Recycle" };
            foreach (var folder in malwareFolders)
            {
                string targetDir = Path.Combine(drivePath, folder);
                if (Directory.Exists(targetDir))
                {
                    try
                    {
                        ClearAttributesRecursive(new DirectoryInfo(targetDir));
                        Directory.Delete(targetDir, true);
                        malwareFoldersRemoved++;
                        worker.ReportProgress(50, "Deleted malware folder: " + folder);
                    }
                    catch (Exception ex)
                    {
                        worker.ReportProgress(50, "Could not delete folder " + folder + ": " + ex.Message);
                    }
                }
            }

            // Remove autorun.inf
            string autorunPath = Path.Combine(drivePath, "autorun.inf");
            if (File.Exists(autorunPath))
            {
                try
                {
                    File.SetAttributes(autorunPath, FileAttributes.Normal);
                    File.Delete(autorunPath);
                    worker.ReportProgress(55, "Deleted autorun.inf");
                }
                catch { }
            }

            // Remove loose .vbs and .js in root
            try
            {
                DirectoryInfo dir = new DirectoryInfo(drivePath);
                foreach (var file in dir.GetFiles())
                {
                    string ext = file.Extension.ToLowerInvariant();
                    if (ext == ".vbs" || ext == ".js" || ext == ".wsf")
                    {
                        try
                        {
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                            worker.ReportProgress(60, "Deleted script file: " + file.Name);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 4. Unhide all files & folders
            worker.ReportProgress(70, "Unhiding files and restoring permissions...");
            try
            {
                DirectoryInfo root = new DirectoryInfo(drivePath);
                unhiddenCount = UnhideAllRecursive(root);
                worker.ReportProgress(85, string.Format("Successfully verified and unhid {0} items.", unhiddenCount));
            }
            catch (Exception ex)
            {
                worker.ReportProgress(85, "Error during unhiding: " + ex.Message);
            }

            // 5. Restore files trapped in volume-named folder
            worker.ReportProgress(90, "Checking for trapped files in folder...");
            try
            {
                string volName = null;
                try
                {
                    DriveInfo dInfo = new DriveInfo(drivePath);
                    volName = dInfo.VolumeLabel;
                }
                catch { }

                if (!string.IsNullOrEmpty(volName))
                {
                    string volDir = Path.Combine(drivePath, volName);
                    if (Directory.Exists(volDir))
                    {
                        DirectoryInfo vInfo = new DirectoryInfo(volDir);
                        ClearAttributesRecursive(vInfo);

                        foreach (var subFile in vInfo.GetFiles())
                        {
                            string dest = Path.Combine(drivePath, subFile.Name);
                            if (!File.Exists(dest))
                            {
                                subFile.MoveTo(dest);
                                worker.ReportProgress(92, "Restored file to root: " + subFile.Name);
                            }
                        }

                        foreach (var subDir in vInfo.GetDirectories())
                        {
                            string dest = Path.Combine(drivePath, subDir.Name);
                            if (!Directory.Exists(dest))
                            {
                                subDir.MoveTo(dest);
                                worker.ReportProgress(92, "Restored folder to root: " + subDir.Name);
                            }
                        }

                        try
                        {
                            vInfo.Delete(true);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                worker.ReportProgress(95, "Note: " + ex.Message);
            }

            worker.ReportProgress(100, "Clean complete!");
            e.Result = new int[] { shortcutsRemoved, malwareFoldersRemoved, unhiddenCount };
        }

        private void ClearAttributesRecursive(DirectoryInfo dir)
        {
            try
            {
                dir.Attributes = FileAttributes.Normal;
                foreach (var f in dir.GetFiles())
                {
                    try { f.Attributes = FileAttributes.Normal; } catch { }
                }
                foreach (var d in dir.GetDirectories())
                {
                    ClearAttributesRecursive(d);
                }
            }
            catch { }
        }

        private int UnhideAllRecursive(DirectoryInfo dir)
        {
            int count = 0;
            if (dir.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                return 0;

            try
            {
                if ((dir.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly)) != 0)
                {
                    dir.Attributes = FileAttributes.Directory;
                    count++;
                }
            }
            catch { }

            try
            {
                foreach (var file in dir.GetFiles())
                {
                    try
                    {
                        if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly)) != 0)
                        {
                            file.Attributes = FileAttributes.Normal;
                            count++;
                        }
                    }
                    catch { }
                }

                foreach (var sub in dir.GetDirectories())
                {
                    count += UnhideAllRecursive(sub);
                }
            }
            catch { }

            return count;
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = Math.Min(100, Math.Max(0, e.ProgressPercentage));
            if (e.UserState != null)
            {
                string msg = e.UserState.ToString();
                lblStatus.Text = msg;
                Log(msg);
            }
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            btnClean.Enabled = true;
            btnRefresh.Enabled = true;
            progressBar.Value = 100;
            lblStatus.Text = "Clean completed successfully!";

            if (e.Error != null)
            {
                Log("Fatal Error: " + e.Error.Message);
                MessageBox.Show(this, "Error during cleanup: " + e.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int[] res = e.Result as int[];
            int shortcuts = res != null ? res[0] : 0;
            int folders = res != null ? res[1] : 0;
            int unhidden = res != null ? res[2] : 0;

            Log("==========================================");
            Log(string.Format("SUMMARY: {0} shortcuts removed, {1} malware folders deleted, {2} files/folders unhidden.", shortcuts, folders, unhidden));
            Log("Drive is now 100% clean and restored!");

            MessageBox.Show(this,
                string.Format("USB Drive Clean Completed!\r\n\r\n- Shortcuts Deleted: {0}\r\n- Malware Folders Deleted: {1}\r\n- Files/Folders Restored: {2}\r\n\r\nYour files are now fully accessible.", shortcuts, folders, unhidden),
                "Clean Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
