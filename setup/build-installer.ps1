# Builds Installer\Planora-Setup-<version>.exe: publishes Planora as a self-contained Windows program,
# zips it, and compiles setup\Setup.cs around the zip with the C# compiler that ships with Windows.
#
#   powershell -ExecutionPolicy Bypass -File setup\build-installer.ps1
#
# Planora Setup is developed by Adeeb.

$ErrorActionPreference = 'Stop'
$version = '1.1.0'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$work = Join-Path $env:TEMP 'planora-installer-build'
$publish = Join-Path $work 'publish'
$payload = Join-Path $work 'payload.zip'
$outDir = Join-Path $root 'Installer'
$out = Join-Path $outDir "Planora-Setup-$version.exe"

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $publish | Out-Null
New-Item -ItemType Directory -Force $outDir | Out-Null

Write-Host 'Publishing Planora (self-contained, win-x64)...'
& $dotnet publish (Join-Path $root 'src\Planora.App\Planora.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:Version=$version -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $publish

Write-Host 'Packing...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host 'Compiling Setup...'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" `
    "/win32icon:$(Join-Path $root 'src\Planora.App\Assets\Planora.ico')" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'Setup.manifest')" `
    "/resource:$payload,payload.zip" `
    "/reference:$(Join-Path $framework 'System.IO.Compression.dll')" `
    "/reference:$(Join-Path $framework 'System.IO.Compression.FileSystem.dll')" `
    "/reference:$(Join-Path $framework 'System.Windows.Forms.dll')" `
    "/reference:$(Join-Path $framework 'System.Drawing.dll')" `
    "/reference:$(Join-Path $framework 'System.Core.dll')" `
    (Join-Path $PSScriptRoot 'Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compiling Setup failed' }

Remove-Item $work -Recurse -Force
$size = [math]::Round((Get-Item $out).Length / 1MB, 1)
Write-Host "Created $out ($size MB)"
