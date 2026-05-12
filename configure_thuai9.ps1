Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ROOT        = Split-Path -Parent $MyInvocation.MyCommand.Path
$CONFIG_FILE = Join-Path $ROOT "thuai9_launch_config.json"

# ── Load saved config (or use defaults) ──────────────────────────────────────
$cfg = @{
    ServerPort   = 8888
    ServerIP     = "127.0.0.1"
    TeamCount    = 2
    CharacterNum = 6
    GameTime     = 120
    CheatMode    = $false
    StartUI      = $true
    CapiDebug    = $true
    CapiOutput   = $true
    CapiWarnOnly = $false
    PlaybackFile = "mygame"
    TeamCapiExes = @("","","","")
}
if (Test-Path $CONFIG_FILE)
{
    $saved = Get-Content $CONFIG_FILE -Raw | ConvertFrom-Json
    # Load array field separately
    if ($null -ne $saved.PSObject.Properties["TeamCapiExes"])
    {
        $loaded = @($saved.TeamCapiExes)
        for ($i = 0; $i -lt 4; $i++)
        {
            if ($i -lt $loaded.Count -and $null -ne $loaded[$i])
            {
                $cfg.TeamCapiExes[$i] = [string]$loaded[$i]
            }
        }
    }
    # Load scalar keys (excluding TeamCapiExes)
    foreach ($k in @($cfg.Keys | Where-Object { $_ -ne "TeamCapiExes" }))
    {
        if ($null -ne $saved.PSObject.Properties[$k])
        {
            $cfg[$k] = $saved.$k
        }
    }
}

# ── Helpers ───────────────────────────────────────────────────────────────────
function New-Label($text, $x, $y)
{
    $l = New-Object System.Windows.Forms.Label
    $l.Text     = $text
    $l.Location = [System.Drawing.Point]::new($x, ($y + 3))
    $l.AutoSize = $true
    return $l
}
function New-TextBox($value, $x, $y, $w = 90)
{
    $t = New-Object System.Windows.Forms.TextBox
    $t.Text     = "$value"
    $t.Location = [System.Drawing.Point]::new($x, $y)
    $t.Width    = $w
    return $t
}
function New-Check($text, $checked, $x, $y)
{
    $c = New-Object System.Windows.Forms.CheckBox
    $c.Text     = $text
    $c.Checked  = $checked
    $c.Location = [System.Drawing.Point]::new($x, $y)
    $c.AutoSize = $true
    return $c
}
function New-Group($text, $x, $y, $w, $h)
{
    $g = New-Object System.Windows.Forms.GroupBox
    $g.Text     = $text
    $g.Location = [System.Drawing.Point]::new($x, $y)
    $g.Size     = [System.Drawing.Size]::new($w, $h)
    return $g
}
function New-BrowseClick($txtTarget)
{
    return {
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*"
        $dlg.Title  = "Select CAPI executable"
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK)
        {
            $txtTarget.Text = $dlg.FileName
        }
    }.GetNewClosure()
}

# ── Form ──────────────────────────────────────────────────────────────────────
$form                  = New-Object System.Windows.Forms.Form
$form.Text             = "THUAI9  —  Launch Configuration"
$form.ClientSize       = [System.Drawing.Size]::new(480, 598)
$form.StartPosition    = "CenterScreen"
$form.FormBorderStyle  = "FixedDialog"
$form.MaximizeBox      = $false
$form.Font             = New-Object System.Drawing.Font("Segoe UI", 9)

# ── Group: Server ────────────────────────────────────────��────────────────────
$grpSrv = New-Group "Server" 12 10 456 165
$form.Controls.Add($grpSrv)

$LBL_W = 175   # label column right edge (= textbox left)
$TXT_X = $LBL_W

$grpSrv.Controls.AddRange(@(
    (New-Label "Server Port:"            10  25),  ($txtPort    = New-TextBox $cfg.ServerPort   $TXT_X  22  80),
    (New-Label "Team Count:"             10  57),  ($txtTeams   = New-TextBox $cfg.TeamCount    $TXT_X  54  80),
    (New-Label "Characters per Team:"    10  89),  ($txtCharNum = New-TextBox $cfg.CharacterNum $TXT_X  86  80),
    (New-Label "Game Time (seconds):"    10 121),  ($txtTime    = New-TextBox $cfg.GameTime     $TXT_X 118  80),
    ($cbCheat = New-Check "Cheat Mode" $cfg.CheatMode 310 25)
))

# ── Group: CAPI ───────────────────────────────────────────────────────────────
$grpCapi = New-Group "CAPI Debug Options" 12 185 456 100
$form.Controls.Add($grpCapi)

$grpCapi.Controls.AddRange(@(
    (New-Label "Server IP (CAPI connects to):" 10 25), ($txtIP = New-TextBox $cfg.ServerIP $TXT_X 22 120),
    ($cbDbg  = New-Check "File logging (-d)"   $cfg.CapiDebug    10 58),
    ($cbOut  = New-Check "Console output (-o)" $cfg.CapiOutput  155 58),
    ($cbWarn = New-Check "Warning only (-w)"   $cfg.CapiWarnOnly 310 58)
))

# ── Group: Team CAPI Executables ──────────────────────────────────────────────
$grpTeamCapi = New-Group "Team CAPI Executables" 12 295 456 163
$form.Controls.Add($grpTeamCapi)

$grpTeamCapi.Controls.Add((New-Label "Leave blank to use auto-detected CAPI exe" 10 20))

$txtTeamCapi = @()
for ($i = 0; $i -lt 4; $i++)
{
    $rowY = 42 + $i * 28
    $lbl  = New-Label "Team $($i+1):" 10 $rowY
    $txt  = New-TextBox $cfg.TeamCapiExes[$i] 70 $rowY 290
    $btn  = New-Object System.Windows.Forms.Button
    $btn.Text     = "..."
    $btn.Location = [System.Drawing.Point]::new(370, $rowY)
    $btn.Size     = [System.Drawing.Size]::new(72, 22)
    $btn.Add_Click((New-BrowseClick $txt))
    $grpTeamCapi.Controls.AddRange(@($lbl, $txt, $btn))
    $txtTeamCapi += $txt
}

# ── Group: Other ──────────────────────────────────────────────────────────────
$grpOther = New-Group "Other" 12 468 456 65
$form.Controls.Add($grpOther)

$grpOther.Controls.AddRange(@(
    (New-Label "Playback File Name:" 10 25), ($txtPB = New-TextBox $cfg.PlaybackFile $TXT_X 22 140),
    ($cbUI = New-Check "Start UI" $cfg.StartUI 360 22)
))

# ── Buttons ───────────────────────────────────────────────────────────────────
$btnStart          = New-Object System.Windows.Forms.Button
$btnStart.Text     = "Start Game"
$btnStart.Location = [System.Drawing.Point]::new(12, 543)
$btnStart.Size     = [System.Drawing.Size]::new(215, 40)
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 215)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.FlatStyle = "Flat"
$btnStart.FlatAppearance.BorderSize = 0
$form.Controls.Add($btnStart)

$btnCancel          = New-Object System.Windows.Forms.Button
$btnCancel.Text     = "Cancel"
$btnCancel.Location = [System.Drawing.Point]::new(253, 543)
$btnCancel.Size     = [System.Drawing.Size]::new(215, 40)
$form.Controls.Add($btnCancel)
$btnCancel.Add_Click({ $form.Close() })

# ── Start logic ───────────────────────────────────────────────────────────────
$btnStart.Add_Click({

    # -- Validate numeric fields -----------------------------------------------
    $port    = 0; $teams = 0; $charNum = 0; $time = 0
    if (-not [int]::TryParse($txtPort.Text,    [ref]$port)    -or $port    -lt 1 -or $port    -gt 65535) { [System.Windows.Forms.MessageBox]::Show("Invalid Server Port.",            "Validation", "OK", "Warning"); return }
    if (-not [int]::TryParse($txtTeams.Text,   [ref]$teams)   -or $teams   -lt 1 -or $teams   -gt 4)     { [System.Windows.Forms.MessageBox]::Show("Invalid Team Count (1-4).",       "Validation", "OK", "Warning"); return }
    if (-not [int]::TryParse($txtCharNum.Text, [ref]$charNum) -or $charNum -lt 1 -or $charNum -gt 20)    { [System.Windows.Forms.MessageBox]::Show("Invalid Characters per Team.",    "Validation", "OK", "Warning"); return }
    if (-not [int]::TryParse($txtTime.Text,    [ref]$time)    -or $time    -lt 10)                        { [System.Windows.Forms.MessageBox]::Show("Game Time must be >= 10 seconds.","Validation", "OK", "Warning"); return }

    $ip    = $txtIP.Text.Trim()
    $pbFile = $txtPB.Text.Trim()

    # -- Auto-detect default CAPI exe ------------------------------------------
    $capiExeDefault = ""
    foreach ($candidate in @(
        (Join-Path $ROOT "CAPI\cpp\x64\Debug\API.exe"),
        (Join-Path $ROOT "CAPI\cpp\x64\Release\API.exe")
    ))
    {
        if (Test-Path $candidate) { $capiExeDefault = $candidate; break }
    }

    # -- Build per-team exe array ----------------------------------------------
    $teamExes = @()
    for ($t = 1; $t -le $teams; $t++)
    {
        $specific = $txtTeamCapi[$t - 1].Text.Trim()
        if ($specific -ne "")
        {
            if (-not (Test-Path $specific))
            {
                [System.Windows.Forms.MessageBox]::Show("CAPI exe for Team $t not found:`n$specific", "Error", "OK", "Error")
                return
            }
            $teamExes += $specific
        }
        else
        {
            if (-not $capiExeDefault)
            {
                [System.Windows.Forms.MessageBox]::Show("No CAPI exe for Team $t and no default found.`nBuild API.sln in Visual Studio 2022 first.", "Error", "OK", "Error")
                return
            }
            $teamExes += $capiExeDefault
        }
    }

    # -- Save config -----------------------------------------------------------
    @{
        ServerPort   = $port
        ServerIP     = $ip
        TeamCount    = $teams
        CharacterNum = $charNum
        GameTime     = $time
        CheatMode    = $cbCheat.Checked
        StartUI      = $cbUI.Checked
        CapiDebug    = $cbDbg.Checked
        CapiOutput   = $cbOut.Checked
        CapiWarnOnly = $cbWarn.Checked
        PlaybackFile = $pbFile
        TeamCapiExes = @($txtTeamCapi | ForEach-Object { $_.Text.Trim() })
    } | ConvertTo-Json | Set-Content $CONFIG_FILE -Encoding UTF8

    $form.Close()

    # -- Build argument strings ------------------------------------------------
    $serverArgs = "--port $port --teamCount $teams --CharacterNum $charNum -g $time -f `"$pbFile`""
    if ($cbCheat.Checked) { $serverArgs += " --cheatMode" }

    $capiFlags = ""
    if ($cbDbg.Checked)  { $capiFlags += " -d" }
    if ($cbOut.Checked)  { $capiFlags += " -o" }
    if ($cbWarn.Checked) { $capiFlags += " -w" }

    # -- Start UI --------------------------------------------------------------
    if ($cbUI.Checked)
    {
        $uiDir = Join-Path $ROOT "interface\AvaloniaUI"
        if (Test-Path (Join-Path $uiDir "THUAI9_Avalonia.csproj"))
        {
            Start-Process "cmd" -ArgumentList "/k cd /d `"$uiDir`" && dotnet run"
            Start-Sleep -Seconds 2
        }
    }

    # -- Start Server ----------------------------------------------------------
    $srvDir = Join-Path $ROOT "logic\Server"
    Start-Process "cmd" -ArgumentList "/k cd /d `"$srvDir`" && dotnet run -- $serverArgs"

    # -- Wait for server -------------------------------------------------------
    Write-Host "[THUAI9] Waiting for server on ${ip}:${port}..."
    $deadline = (Get-Date).AddMinutes(2)
    $ready = $false
    while ((Get-Date) -lt $deadline)
    {
        try { $c = [System.Net.Sockets.TcpClient]::new($ip, $port); $c.Close(); $ready = $true; break }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready)
    {
        [System.Windows.Forms.MessageBox]::Show("Server did not become ready within 2 minutes.", "Error", "OK", "Error")
        return
    }

    # -- Start home clients (pid=0) for every team -----------------------------
    for ($t = 1; $t -le $teams; $t++)
    {
        $clientArgs = "-t $t -p 0 -I $ip -P $port$capiFlags"
        Start-Process "cmd" -ArgumentList "/k `"$($teamExes[$t-1])`" $clientArgs"
    }

    Write-Host "[THUAI9] All home clients launched."
    Write-Host "[THUAI9] Character CAPIs will be spawned automatically on BuildCharacter."
})

$form.ShowDialog() | Out-Null
