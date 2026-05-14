param(
    [string]$Root = $PSScriptRoot,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
$rootPathWithSeparator = $rootPath + '\'
$currentPid = $PID
$workspaceProcessNames = @('Server', 'ClientTest', 'ClientTest2', 'THUAI9_Avalonia', 'API')
$managedMarkers = @(
    'THUAI9_Avalonia.csproj',
    'logic\Server\Server.csproj',
    'logic\ClientTest\ClientTest.csproj',
    'logic\ClientTest2\ClientTest2.csproj'
)
$pythonMarkers = @('PyAPI.main', 'CAPI\python')
function Test-ContainsMarker {
    param(
        [AllowNull()][string]$Text,
        [string[]]$Markers
    )
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }
    foreach ($marker in $Markers) {
        if ($Text.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}
function Test-IsWorkspacePath {
    param([AllowNull()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }
    return $Path.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($rootPathWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}
$allProcesses = @(Get-CimInstance Win32_Process)
$targets = @()
foreach ($process in $allProcesses) {
    if ($process.ProcessId -eq $currentPid) {
        continue
    }
    $name = [System.IO.Path]::GetFileNameWithoutExtension($process.Name)
    $exePath = $process.ExecutablePath
    $commandLine = $process.CommandLine
    $isWorkspaceApp = ($workspaceProcessNames -contains $name) -and (Test-IsWorkspacePath $exePath)
    $isDotnetSmoke = ($name -ieq 'dotnet') -and (Test-ContainsMarker $commandLine $managedMarkers)
    $isPythonSmoke = (($name -ieq 'python') -or ($name -ieq 'python3') -or ($name -ieq 'py')) -and (Test-ContainsMarker $commandLine $pythonMarkers)
    if ($isWorkspaceApp -or $isDotnetSmoke -or $isPythonSmoke) {
        $targets += $process
    }
}
$targets = @($targets | Sort-Object ProcessId -Unique)
if ($targets.Count -eq 0) {
    if (-not $Quiet) {
        Write-Host '[THUAI9] No old smoke processes found.'
    }
    exit 0
}
if (-not $Quiet) {
    Write-Host '[THUAI9] Stopping old smoke processes from this workspace:'
    foreach ($target in $targets) {
        $displayPath = if ($target.ExecutablePath) { $target.ExecutablePath } else { $target.CommandLine }
        Write-Host ('  - {0} ({1}) {2}' -f $target.Name, $target.ProcessId, $displayPath)
    }
}
foreach ($target in $targets) {
    try {
        Stop-Process -Id $target.ProcessId -Force -ErrorAction Stop
    }
    catch {
        Write-Warning ('Failed to stop process {0} ({1}): {2}' -f $target.Name, $target.ProcessId, $_.Exception.Message)
    }
}
Start-Sleep -Milliseconds 500
