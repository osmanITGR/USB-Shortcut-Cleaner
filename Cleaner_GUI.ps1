Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.Text = "USB Shortcut Virus Cleaner & Restorer"
$form.Size = New-Object System.Drawing.Size(650, 560)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(245, 247, 250)

# Header Label
$lblTitle = New-Object System.Windows.Forms.Label
$lblTitle.Text = "USB Shortcut Virus Cleaner & File Restorer"
$lblTitle.Font = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Bold)
$lblTitle.ForeColor = [System.Drawing.Color]::FromArgb(24, 43, 73)
$lblTitle.Size = New-Object System.Drawing.Size(600, 32)
$lblTitle.Location = New-Object System.Drawing.Point(20, 15)
$form.Controls.Add($lblTitle)

$lblSub = New-Object System.Windows.Forms.Label
$lblSub.Text = "Recover hidden files and remove fake shortcut viruses permanently."
$lblSub.Font = New-Object System.Drawing.Font("Segoe UI", 9.5)
$lblSub.ForeColor = [System.Drawing.Color]::FromArgb(100, 116, 139)
$lblSub.Size = New-Object System.Drawing.Size(600, 22)
$lblSub.Location = New-Object System.Drawing.Point(20, 48)
$form.Controls.Add($lblSub)

# Drive selection label
$lblSelect = New-Object System.Windows.Forms.Label
$lblSelect.Text = "Select USB Drive:"
$lblSelect.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$lblSelect.ForeColor = [System.Drawing.Color]::FromArgb(30, 41, 59)
$lblSelect.Size = New-Object System.Drawing.Size(180, 24)
$lblSelect.Location = New-Object System.Drawing.Point(20, 85)
$form.Controls.Add($lblSelect)

# Combo Box
$cmbDrives = New-Object System.Windows.Forms.ComboBox
$cmbDrives.Font = New-Object System.Drawing.Font("Segoe UI", 10.5)
$cmbDrives.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
$cmbDrives.Location = New-Object System.Drawing.Point(20, 112)
$cmbDrives.Size = New-Object System.Drawing.Size(460, 30)
$form.Controls.Add($cmbDrives)

# Refresh Button
$btnRefresh = New-Object System.Windows.Forms.Button
$btnRefresh.Text = "Refresh"
$btnRefresh.Font = New-Object System.Drawing.Font("Segoe UI", 9.5, [System.Drawing.FontStyle]::Bold)
$btnRefresh.Location = New-Object System.Drawing.Point(490, 110)
$btnRefresh.Size = New-Object System.Drawing.Size(120, 32)
$btnRefresh.BackColor = [System.Drawing.Color]::White
$btnRefresh.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$form.Controls.Add($btnRefresh)

# Clean & Fix Button
$btnClean = New-Object System.Windows.Forms.Button
$btnClean.Text = "Clean USB & Recover Files"
$btnClean.Font = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Bold)
$btnClean.ForeColor = [System.Drawing.Color]::White
$btnClean.BackColor = [System.Drawing.Color]::FromArgb(16, 185, 129)
$btnClean.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnClean.FlatAppearance.BorderSize = 0
$btnClean.Location = New-Object System.Drawing.Point(20, 160)
$btnClean.Size = New-Object System.Drawing.Size(320, 42)
$btnClean.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.Controls.Add($btnClean)

# Open Explorer Button
$btnOpen = New-Object System.Windows.Forms.Button
$btnOpen.Text = "Open USB Drive"
$btnOpen.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$btnOpen.ForeColor = [System.Drawing.Color]::FromArgb(30, 41, 59)
$btnOpen.BackColor = [System.Drawing.Color]::FromArgb(226, 232, 240)
$btnOpen.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnOpen.FlatAppearance.BorderSize = 0
$btnOpen.Location = New-Object System.Drawing.Point(360, 160)
$btnOpen.Size = New-Object System.Drawing.Size(250, 42)
$btnOpen.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.Controls.Add($btnOpen)

# Status/Log Box
$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 9.5)
$txtLog.BackColor = [System.Drawing.Color]::FromArgb(15, 23, 42)
$txtLog.ForeColor = [System.Drawing.Color]::FromArgb(226, 232, 240)
$txtLog.Location = New-Object System.Drawing.Point(20, 220)
$txtLog.Size = New-Object System.Drawing.Size(590, 280)
$form.Controls.Add($txtLog)

function Append-Log($text) {
    $time = (Get-Date).ToString("HH:mm:ss")
    $txtLog.AppendText("[$time] $text`r`n")
    $txtLog.SelectionStart = $txtLog.TextLength
    $txtLog.ScrollToCaret()
    [System.Windows.Forms.Application]::DoEvents()
}

function Load-Drives {
    $cmbDrives.Items.Clear()
    $drives = Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -eq 2 }
    
    if (-not $drives) {
        $drives = Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -notin @("C:","D:") }
    }

    foreach ($d in $drives) {
        $sizeGB = if ($d.Size) { [math]::Round($d.Size / 1GB, 1) } else { 0 }
        $freeGB = if ($d.FreeSpace) { [math]::Round($d.FreeSpace / 1GB, 1) } else { 0 }
        $volName = if ($d.VolumeName) { $d.VolumeName } else { "Removable Disk" }
        $displayText = "$($d.DeviceID) [$volName] ($freeGB GB free / $sizeGB GB)"
        $cmbDrives.Items.Add($displayText) | Out-Null
    }

    if ($cmbDrives.Items.Count -gt 0) {
        $cmbDrives.SelectedIndex = 0
    }
}

$btnRefresh.Add_Click({
    Load-Drives
    Append-Log "Drive list refreshed."
})

$btnOpen.Add_Click({
    if ($cmbDrives.SelectedItem) {
        $driveLetter = $cmbDrives.SelectedItem.ToString().Substring(0, 2)
        if (Test-Path "$driveLetter\") {
            Start-Process "explorer.exe" "$driveLetter\"
            Append-Log "Opened $driveLetter\"
        }
    } else {
        [System.Windows.Forms.MessageBox]::Show("Please select a drive first.", "Notice", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)
    }
})

$btnClean.Add_Click({
    if (-not $cmbDrives.SelectedItem) {
        [System.Windows.Forms.MessageBox]::Show("No drive selected!", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $driveLetter = $cmbDrives.SelectedItem.ToString().Substring(0, 2)
    $drivePath = "$driveLetter\"
    
    if (-not (Test-Path $drivePath)) {
        Append-Log "Error: Drive $driveLetter not found!"
        return
    }

    $btnClean.Enabled = $false
    $btnRefresh.Enabled = $false
    Append-Log "------------------------------------------"
    Append-Log "Starting cleaning process for drive $driveLetter ..."

    try {
        # Step 1: Kill running wscript/cscript processes that could lock files
        Append-Log "Step 1: Terminating any running malware script engines (wscript/cscript)..."
        Get-Process -Name wscript, cscript -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            Append-Log "Terminated process: $($_.ProcessName) (PID: $($_.Id))"
        }

        # Step 2: Delete malicious shortcuts (*.lnk at root)
        Append-Log "Step 2: Removing fake shortcut files (*.lnk)..."
        $shortcuts = Get-ChildItem -Path $drivePath -Filter "*.lnk" -Force -ErrorAction SilentlyContinue
        foreach ($lnk in $shortcuts) {
            Append-Log "Deleted shortcut: $($lnk.Name)"
            Remove-Item -LiteralPath $lnk.FullName -Force -ErrorAction SilentlyContinue
        }

        # Step 3: Delete common malware payload folders & files
        Append-Log "Step 3: Removing malware payload folders and scripts..."
        $malwareFolders = @("sysvolume", "WindowsServices", "drive", "recycler", "Recycle")
        foreach ($folder in $malwareFolders) {
            $fPath = Join-Path $drivePath $folder
            if (Test-Path $fPath) {
                Append-Log "Deleted malware folder: $folder"
                attrib.exe -h -r -s $fPath
                Remove-Item -LiteralPath $fPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        # Delete autorun.inf if present
        $autorun = Join-Path $drivePath "autorun.inf"
        if (Test-Path $autorun) {
            Append-Log "Deleted autorun.inf"
            attrib.exe -h -r -s $autorun
            Remove-Item -LiteralPath $autorun -Force -ErrorAction SilentlyContinue
        }

        # Delete loose .vbs, .js in root if created by worm
        $looseScripts = Get-ChildItem -Path $drivePath -Include "*.vbs", "*.js", "*.wsf" -File -Force -ErrorAction SilentlyContinue
        foreach ($s in $looseScripts) {
            Append-Log "Deleted script: $($s.Name)"
            attrib.exe -h -r -s $s.FullName
            Remove-Item -LiteralPath $s.FullName -Force -ErrorAction SilentlyContinue
        }

        # Step 4: Unhide all files & folders
        Append-Log "Step 4: Unhiding all files and folders (running attrib -h -r -s)..."
        $targetWildcard = "$driveLetter\*.*"
        attrib.exe -h -r -s /s /d $targetWildcard

        # Step 5: Check if root files were trapped in a volume-named or dummy folder
        Append-Log "Step 5: Checking for trapped files in volume folder..."
        $vol = (Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq $driveLetter }).VolumeName
        if ($vol -and (Test-Path (Join-Path $drivePath $vol))) {
            $subFolder = Join-Path $drivePath $vol
            $items = Get-ChildItem -LiteralPath $subFolder -Force -ErrorAction SilentlyContinue
            if ($items) {
                Append-Log "Restoring files from folder '$vol' to drive root..."
                attrib.exe -h -r -s $subFolder
                Get-ChildItem -LiteralPath $subFolder | Move-Item -Destination $drivePath -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $subFolder -Force -ErrorAction SilentlyContinue
            }
        }

        Append-Log "SUCCESS: USB drive cleaned and all files successfully restored!"
        [System.Windows.Forms.MessageBox]::Show("USB Drive cleaned and all files successfully restored!", "Success", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
    }
    catch {
        Append-Log "Error occurred: $($_.Exception.Message)"
    }
    finally {
        $btnClean.Enabled = $true
        $btnRefresh.Enabled = $true
    }
})

# Initial load
Load-Drives
Append-Log "Ready. Select your drive and click 'Clean USB & Recover Files'."

$form.ShowDialog() | Out-Null
