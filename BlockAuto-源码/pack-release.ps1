# 打包发布目录：BlockAuto-v1.0-键鼠/
# 用法：powershell -ExecutionPolicy Bypass -File .\pack-release.ps1
$ErrorActionPreference = 'Stop'
$srcRoot = $PSScriptRoot                                   # ...\BlockAuto-键鼠\BlockAuto-源码
$root    = Split-Path -Parent $srcRoot                     # ...\BlockAuto-键鼠
$out     = Join-Path $root 'BlockAuto-v1.0-键鼠'
$app     = Join-Path $out 'BlockAuto'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $app -Force | Out-Null

Copy-Item (Join-Path $srcRoot 'bin\BlockAuto.exe')        $app -Force
Copy-Item (Join-Path $srcRoot 'bin\BlockRunner.exe')      $app -Force
Copy-Item (Join-Path $srcRoot 'bin\BlockAuto.Tests.exe')  $app -Force
Copy-Item (Join-Path $srcRoot 'README.md')                $app -Force
Copy-Item (Join-Path $srcRoot 'install-file-association.ps1') $app -Force

foreach ($d in @('samples', 'templates', 'docs')) {
  Copy-Item (Join-Path $srcRoot $d) (Join-Path $app $d) -Recurse -Force
}

# 发布目录里的 exe 在根上，两个 bat 指向自己
$start = "@echo off`r`nrem 启动积木编辑器`r`nstart `"`" `"%~dp0BlockAuto.exe`""
[IO.File]::WriteAllText((Join-Path $app 'start-editor.cmd'), $start, [Text.Encoding]::Default)
$run = "@echo off`r`nrem 用独立运行器执行脚本：run-sample.cmd [脚本路径]，默认运行 samples\hello.block`r`nsetlocal`r`nset SCRIPT=%~dp0samples\hello.block`r`nif not `"%~1`"==`"`" set SCRIPT=%~1`r`n`"%~dp0BlockRunner.exe`" run `"%SCRIPT%`" %2 %3 %4 %5`r`necho.`r`necho 退出码：%ERRORLEVEL%`r`npause"
[IO.File]::WriteAllText((Join-Path $app 'run-sample.cmd'), $run, [Text.Encoding]::Default)

Copy-Item (Join-Path $srcRoot '发布说明.txt') $out -Force

Write-Host '== 发布目录 =='
Get-ChildItem $out -Recurse -File |
  Select-Object @{n='rel';e={$_.FullName.Substring($out.Length+1)}}, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} |
  Sort-Object rel | Format-Table -AutoSize | Out-String -Width 200 | Write-Host