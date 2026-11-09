<#
Makes the release zip for one mod in dist\.

  powershell -File tools\package.ps1 HarvestAllButton

A BepInEx mod is built in Release and zipped as BepInEx\plugins\<Mod>\ (DLL and
README), so players unzip it into the game folder. Resource Ledger, which has no
.csproj, is zipped from its committed files with its version from VERSION.

The mod's folder must have no uncommitted changes, so a zip always matches a
commit. Packaging stops if the DLL contains a local user path, or if the
repository or the DLL matches a line of tools\private-names.txt (one regular
expression per line, matched without case; git ignores that file).
#>
param([Parameter(Mandatory)][string]$Mod)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$namesFile = Join-Path $PSScriptRoot 'private-names.txt'
$names = @()
if (Test-Path $namesFile) { $names = @(Get-Content $namesFile | Where-Object { $_.Trim() -and -not $_.StartsWith('#') }) }
$Private = if ($names) { ($names | ForEach-Object { "(?:$($_.Trim()))" }) -join '|' } else { '(?!)' }

if (-not (Test-Path $Mod -PathType Container)) { throw "No folder named $Mod" }
$dirty = git status --porcelain -- $Mod Directory.Build.props
if ($dirty) { throw "Commit these first:`n$($dirty -join "`n")" }

if ($names) {
    $hits = git grep -n -i -P $Private HEAD -- .
    if ($hits) { throw "Private names in committed files:`n$($hits -join "`n")" }
} else {
    Write-Warning "No $namesFile, so only local user paths are checked"
}

New-Item -ItemType Directory -Force dist | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$csproj = Join-Path $Mod "$Mod.csproj"

if (-not (Test-Path $csproj)) {
    $version = (Get-Content (Join-Path $Mod VERSION) -TotalCount 1).Trim()
    $zip = Join-Path $root "dist\$Mod-$version.zip"
    git archive --format=zip --prefix="$Mod/" -o $zip "HEAD:$Mod"
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
    Write-Output $zip
    return
}

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $csproj" }

# Build under a time limit that kills the whole process tree.
$log = Join-Path $env:TEMP "package-$Mod.log"
$p = Start-Process dotnet -ArgumentList "build `"$csproj`" -c Release -nodeReuse:false -v:minimal" `
    -NoNewWindow -PassThru -RedirectStandardOutput $log
$null = $p.Handle   # makes ExitCode readable after the process ends
if (-not $p.WaitForExit(300000)) {
    taskkill /PID $p.Id /T /F | Out-Null
    throw "Build timed out after 5 minutes and was killed; see $log"
}
if ($p.ExitCode -ne 0) { Get-Content $log -Tail 30; throw "Build failed; see $log" }
dotnet build-server shutdown | Out-Null

$dll = Join-Path $root "$Mod\bin\Release\$Mod.dll"
$bytes = [IO.File]::ReadAllBytes($dll)
$text = [Text.Encoding]::ASCII.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
if ($text -match "(?i)$Private|[A-Z]:\\Users\\") { throw "The DLL contains '$($Matches[0])'" }

# Zip entries use '/', which every unzip tool understands.
$zip = Join-Path $root "dist\$Mod-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($f in @($dll, (Join-Path $root "$Mod\README.md"))) {
        $name = "BepInEx/plugins/$Mod/" + (Split-Path $f -Leaf)
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f, $name) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Output $zip
