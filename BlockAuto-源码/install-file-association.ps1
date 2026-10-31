# 将 .block 文件关联到 BlockRunner（当前用户，无需管理员）
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$runner = Join-Path $root 'bin\BlockRunner.exe'
$ext = '.block'

if ($Uninstall) {
    if (Test-Path "HKCU:\Software\Classes\$ext") { Remove-Item "HKCU:\Software\Classes\$ext" -Recurse -Force }
    if (Test-Path 'HKCU:\Software\Classes\BlockAuto.Script') { Remove-Item 'HKCU:\Software\Classes\BlockAuto.Script' -Recurse -Force }
    Write-Host '已移除 .block 文件关联。' -ForegroundColor Green
    return
}

if (-not (Test-Path $runner)) {
    throw "找不到 BlockRunner.exe：$runner，请先运行 build.ps1"
}

$cmd = "`"$runner`" run `"%1`""
New-Item -Path "HKCU:\Software\Classes\$ext" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$ext" -Name '(Default)' -Value 'BlockAuto.Script'

New-Item -Path 'HKCU:\Software\Classes\BlockAuto.Script' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Classes\BlockAuto.Script' -Name '(Default)' -Value 'BlockAuto 积木脚本'

New-Item -Path 'HKCU:\Software\Classes\BlockAuto.Script\shell\open\command' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Classes\BlockAuto.Script\shell\open\command' -Name '(Default)' -Value $cmd

Write-Host "已关联 $ext → $runner run `"%1`"" -ForegroundColor Green
Write-Host '卸载：install-file-association.ps1 -Uninstall'