$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 C# 编译器：$csc" }

$shared = Get-ChildItem (Join-Path $root 'src\Shared') -Filter *.cs | ForEach-Object { $_.FullName }
$editor = Get-ChildItem (Join-Path $root 'src\Editor') -Filter *.cs | ForEach-Object { $_.FullName }
$runner = Get-ChildItem (Join-Path $root 'src\Runner') -Filter *.cs | ForEach-Object { $_.FullName }
$tests  = Get-ChildItem (Join-Path $root 'src\Tests')  -Filter *.cs | ForEach-Object { $_.FullName }

$bin = Join-Path $root 'bin'
if (-not (Test-Path $bin)) { New-Item -ItemType Directory -Path $bin | Out-Null }

$refs = @(
  '/r:System.dll',
  '/r:System.Core.dll',
  '/r:System.Drawing.dll',
  '/r:System.Windows.Forms.dll',
  '/r:System.IO.Compression.dll',
  '/r:System.IO.Compression.FileSystem.dll'
)

$common = @('/nologo', '/codepage:65001', '/unsafe+', '/platform:anycpu', '/optimize+', '/debug-') + $refs

Write-Host '==> 编译 BlockAuto.exe (编辑器)' -ForegroundColor Cyan
& $csc $common '/target:winexe' ('/out:' + (Join-Path $bin 'BlockAuto.exe')) ($shared + $editor)
if ($LASTEXITCODE -ne 0) { throw "编辑器编译失败 ($LASTEXITCODE)" }

Write-Host '==> 编译 BlockRunner.exe (独立运行器)' -ForegroundColor Cyan
& $csc $common '/target:exe' ('/out:' + (Join-Path $bin 'BlockRunner.exe')) ($shared + $runner)
if ($LASTEXITCODE -ne 0) { throw "运行器编译失败 ($LASTEXITCODE)" }

if ($tests.Count -gt 0) {
  Write-Host '==> 编译 BlockAuto.Tests.exe (自测)' -ForegroundColor Cyan
  & $csc $common '/target:exe' ('/out:' + (Join-Path $bin 'BlockAuto.Tests.exe')) ($shared + $tests)
  if ($LASTEXITCODE -ne 0) { throw "测试程序编译失败 ($LASTEXITCODE)" }
}

Write-Host ''
Write-Host '编译完成：' -ForegroundColor Green
Get-ChildItem $bin -Filter *.exe | ForEach-Object { Write-Host ('  ' + $_.FullName) }
