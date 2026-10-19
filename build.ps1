$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$assetsDir = Join-Path $root 'assets'
$distDir = Join-Path $root 'dist'
$iconPath = Join-Path $assetsDir 'app.ico'
$manifestPath = Join-Path $assetsDir 'app.manifest'

$framework64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$framework32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$frameworkDir = $null
if (Test-Path (Join-Path $framework64 'csc.exe')) {
    $frameworkDir = $framework64
} elseif (Test-Path (Join-Path $framework32 'csc.exe')) {
    $frameworkDir = $framework32
} else {
    throw 'Cannot find the .NET Framework 4.x C# compiler (csc.exe).'
}

$csc = Join-Path $frameworkDir 'csc.exe'

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw 'Missing assets\app.ico. Run: python tools\make_icon.py'
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Missing assets\app.manifest.'
}

$sourceFiles = @(Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' |
    Sort-Object Name |
    ForEach-Object { $_.FullName })
if ($sourceFiles.Count -eq 0) {
    throw 'No C# source files were found in src\.'
}

if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

# Build the Chinese output name without relying on the script file's ANSI/UTF-8
# interpretation by Windows PowerShell 5.1.
$exeName = (-join @(
    [char]0x684C,
    [char]0x9762,
    [char]0x5DE5,
    [char]0x5177
)) + '.exe'
$outputPath = Join-Path $distDir $exeName

$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    ('/out:' + $outputPath),
    ('/win32icon:' + $iconPath),
    ('/win32manifest:' + $manifestPath),
    ('/reference:' + (Join-Path $frameworkDir 'System.dll')),
    ('/reference:' + (Join-Path $frameworkDir 'System.Core.dll')),
    ('/reference:' + (Join-Path $frameworkDir 'System.Drawing.dll')),
    ('/reference:' + (Join-Path $frameworkDir 'System.Windows.Forms.dll')),
    ('/reference:' + (Join-Path $frameworkDir 'Accessibility.dll'))
) + $sourceFiles

$pdbPath = [System.IO.Path]::ChangeExtension($outputPath, '.pdb')
if (Test-Path -LiteralPath $pdbPath) {
    Remove-Item -LiteralPath $pdbPath -Force
}

& $csc $compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw ('Compilation failed with exit code ' + $LASTEXITCODE + '.')
}

$item = Get-Item -LiteralPath $outputPath
$sizeKb = [math]::Round($item.Length / 1024.0, 1)
Write-Host ('Built: ' + $item.FullName)
Write-Host ('Size:  ' + $sizeKb + ' KB')
