<#
.SYNOPSIS
    Verifies that a published PersonaEngine.exe loads only DLLs bundled inside its
    own publish directory (plus baseline Windows + NVIDIA driver components).

.DESCRIPTION
    .NET self-contained publishes can still pull DLLs from outside the publish dir
    if a P/Invoke target name resolves to a system path, or if a native dep
    dynamically does LoadLibrary against an absolute path. A clean static check
    plus a runtime snapshot catches both:

      Static  - walks every .dll/.exe under the publish dir and dumps its IMPORT
                table via dumpbin. Any imported DLL whose base name is not in the
                allowlist OR is not present alongside the binary is flagged.
      Runtime - launches the exe, waits a few seconds, snapshots its loaded
                modules with Get-Process -Module, and flags any module path that
                lives outside the publish dir or the approved Windows/NVIDIA
                system roots.

.PARAMETER PublishDir
    Path to the publish-XYZ folder produced by build-release.ps1.

.PARAMETER ExeName
    Executable to launch for the runtime check (default: PersonaEngine.exe).

.PARAMETER SkipRuntime
    Skip the runtime snapshot (useful in CI where we cannot launch the app).

.PARAMETER RuntimeSeconds
    How long to let the app run before snapshotting (default: 8).
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [string] $ExeName        = 'PersonaEngine.exe',
    [switch] $SkipRuntime,
    [int]    $RuntimeSeconds = 8
)

$ErrorActionPreference = 'Stop'

$PublishDir = (Resolve-Path -LiteralPath $PublishDir).ProviderPath
if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) {
    throw "Publish directory not found: $PublishDir"
}

$exePath = Join-Path $PublishDir $ExeName
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Executable not found in publish dir: $exePath"
}

# DLLs that legitimately live outside the publish dir on every Windows + NVIDIA
# box. Anything else flagged as external is a real risk for shipping.
$systemAllowlist = @(
    'ntdll.dll', 'kernel32.dll', 'kernelbase.dll', 'user32.dll', 'gdi32.dll',
    'gdi32full.dll', 'msvcrt.dll', 'rpcrt4.dll', 'sechost.dll', 'advapi32.dll',
    'combase.dll', 'ole32.dll', 'oleaut32.dll', 'shell32.dll', 'shlwapi.dll',
    'shcore.dll', 'win32u.dll', 'imm32.dll', 'ws2_32.dll', 'crypt32.dll',
    'msasn1.dll', 'bcrypt.dll', 'bcryptprimitives.dll', 'ncrypt.dll',
    'cryptbase.dll', 'cryptsp.dll', 'wintrust.dll', 'mscoree.dll',
    'iphlpapi.dll', 'dnsapi.dll', 'nsi.dll', 'winhttp.dll', 'winmm.dll',
    'mfplat.dll', 'mf.dll', 'avrt.dll', 'mmdevapi.dll', 'audioses.dll',
    'opengl32.dll', 'glu32.dll', 'd3d9.dll', 'd3d11.dll', 'd3d12.dll',
    'dxgi.dll', 'directxdatabasehelper.dll', 'd3dcompiler_47.dll',
    'dwmapi.dll', 'uxtheme.dll', 'uiautomationcore.dll',
    'powrprof.dll', 'profapi.dll', 'wtsapi32.dll', 'version.dll',
    'setupapi.dll', 'cfgmgr32.dll', 'devobj.dll', 'wldp.dll',
    'kernel.appcore.dll', 'gdiplus.dll', 'msvcp_win.dll', 'ucrtbase.dll',
    'webio.dll', 'ws2help.dll', 'normaliz.dll', 'usp10.dll', 'comdlg32.dll',
    'comctl32.dll', 'msctf.dll', 'oleacc.dll', 'urlmon.dll',
    'twinapi.appcore.dll', 'windows.storage.dll', 'wininet.dll',
    'sspicli.dll', 'secur32.dll', 'fwpuclnt.dll', 'mswsock.dll',
    'rasapi32.dll', 'rasman.dll', 'rtutils.dll',
    'nvcuda.dll', 'nvfatbinaryloader.dll', 'nvapi64.dll',
    'nvml.dll', 'nvoglv64.dll', 'nvinit.dll', 'nvopencl.dll'
)
$systemAllowlist = $systemAllowlist | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique

function Get-DumpbinPath {
    $cmd = Get-Command dumpbin -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $vsroot = & $vswhere -latest -property installationPath 2>$null
        if ($LASTEXITCODE -eq 0 -and $vsroot) {
            $candidates = Get-ChildItem -LiteralPath $vsroot -Recurse -Filter dumpbin.exe -ErrorAction SilentlyContinue
            if ($candidates) {
                $hostX64 = $candidates | Where-Object FullName -match 'Hostx64\\x64\\dumpbin.exe' | Select-Object -First 1
                if ($hostX64) { return $hostX64.FullName }
                return $candidates[0].FullName
            }
        }
    }
    return $null
}

# === STATIC CHECK ===

Write-Host "=== Static import check ===" -ForegroundColor Cyan
Write-Host "Publish dir : $PublishDir"

$dumpbin = Get-DumpbinPath
if (-not $dumpbin) {
    Write-Host "[SKIP] dumpbin.exe not found - install VS Build Tools (Desktop C++) for static analysis. Continuing with runtime check only." -ForegroundColor Yellow
    $skipStatic = $true
} else {
    Write-Host "dumpbin     : $dumpbin"
    $skipStatic = $false
}

$bundled = @{}
Get-ChildItem -LiteralPath $PublishDir -Recurse -Include *.dll, *.exe -File |
    ForEach-Object { $bundled[$_.Name.ToLowerInvariant()] = $_.FullName }

Write-Host "Bundled DLL/EXE: $($bundled.Count)"
Write-Host ""

if ($skipStatic) {
    # Skip the dumpbin loop entirely.
    $staticIssues = $null
} else {

$staticIssues = New-Object System.Collections.Generic.List[object]
$binaries = Get-ChildItem -LiteralPath $PublishDir -Recurse -Include *.dll, *.exe -File

foreach ($bin in $binaries) {
    $output = & $dumpbin /imports $bin.FullName 2>&1
    if ($LASTEXITCODE -ne 0) { continue }

    $imports = $output |
        Select-String -Pattern '^\s{4}([A-Za-z0-9_.\-]+\.dll)\s*$' -AllMatches |
        ForEach-Object { $_.Matches.Groups[1].Value.ToLowerInvariant() } |
        Sort-Object -Unique

    foreach ($imp in $imports) {
        if ($systemAllowlist -contains $imp) { continue }
        if ($bundled.ContainsKey($imp))      { continue }

        $staticIssues.Add([pscustomobject]@{
            Source     = $bin.FullName.Substring($PublishDir.Length).TrimStart('\','/')
            MissingDep = $imp
        })
    }
}

}

if ($skipStatic) {
    # Already reported above.
} elseif ($staticIssues.Count -eq 0) {
    Write-Host "[OK] Every imported DLL is either bundled or on the system allowlist." -ForegroundColor Green
} else {
    Write-Host "[WARN] $($staticIssues.Count) imports point at DLLs that are not bundled and not on the system allowlist:" -ForegroundColor Yellow
    $staticIssues | Sort-Object MissingDep, Source | Format-Table -AutoSize | Out-String | Write-Host
}

# === RUNTIME CHECK ===

if ($SkipRuntime) {
    Write-Host ""
    Write-Host "Skipping runtime snapshot (-SkipRuntime)." -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "=== Runtime module snapshot ===" -ForegroundColor Cyan
Write-Host "Launching $exePath ..."

$proc = Start-Process -FilePath $exePath -WorkingDirectory $PublishDir -PassThru
try {
    Start-Sleep -Seconds $RuntimeSeconds
    if ($proc.HasExited) {
        Write-Host "[WARN] Process exited before snapshot (exit $($proc.ExitCode)). Modules below may be incomplete." -ForegroundColor Yellow
    } else {
        $proc.Refresh()
    }

    $modules = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Modules

    $publishLower = $PublishDir.ToLowerInvariant()
    $approvedSystemRoots = @(
        "$env:windir\system32".ToLowerInvariant(),
        "$env:windir\syswow64".ToLowerInvariant(),
        "$env:windir\winsxs".ToLowerInvariant(),
        "$env:ProgramFiles\NVIDIA Corporation".ToLowerInvariant(),
        "$env:ProgramFiles\NVIDIA GPU Computing Toolkit".ToLowerInvariant(),
        "${env:ProgramFiles(x86)}\NVIDIA Corporation".ToLowerInvariant()
    )

    $runtimeIssues = New-Object System.Collections.Generic.List[object]
    foreach ($m in $modules) {
        $path = $m.FileName
        if (-not $path) { continue }
        $pl = $path.ToLowerInvariant()
        if ($pl.StartsWith($publishLower)) { continue }
        $isApproved = $false
        foreach ($root in $approvedSystemRoots) {
            if ($root -and $pl.StartsWith($root)) { $isApproved = $true; break }
        }
        if ($isApproved) { continue }

        $runtimeIssues.Add([pscustomobject]@{
            Module = [System.IO.Path]::GetFileName($path)
            Path   = $path
        })
    }

    if ($runtimeIssues.Count -eq 0) {
        Write-Host "[OK] Every loaded module is from the publish dir or an approved system path." -ForegroundColor Green
    } else {
        Write-Host "[WARN] $($runtimeIssues.Count) loaded modules are outside the publish dir and not in an approved system path:" -ForegroundColor Yellow
        $runtimeIssues | Sort-Object Module | Format-Table -AutoSize | Out-String | Write-Host
        Write-Host "These DLLs would need to exist on every target machine - investigate or bundle." -ForegroundColor Yellow
    }
} finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
