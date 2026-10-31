# 打包 v2.5 发布目录
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\31371\Desktop\键鼠BlockAuto-积木自动化平台'
$src  = Join-Path $root 'BlockAuto-源码'
$out  = Join-Path $root 'BlockAuto-v2.5-鼠标点击自动识图版'
$app  = Join-Path $out 'BlockAuto'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $app -Force | Out-Null

Copy-Item (Join-Path $src 'bin\BlockAuto.exe')       $app -Force
Copy-Item (Join-Path $src 'bin\BlockRunner.exe')     $app -Force
Copy-Item (Join-Path $src 'bin\BlockAuto.Tests.exe') $app -Force
Copy-Item (Join-Path $src 'README.md')               $app -Force
Copy-Item (Join-Path $src 'install-file-association.ps1') $app -Force

foreach ($d in @('samples', 'templates', 'docs')) {
  Copy-Item (Join-Path $src $d) (Join-Path $app $d) -Recurse -Force
}
Remove-Item (Join-Path $app 'docs\shot')  -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $app 'docs\shot2') -Recurse -Force -ErrorAction SilentlyContinue

# 发布目录里的 exe 在根上，改写两个 bat 指向自己
$start = "@echo off`r`nrem 启动积木编辑器`r`nstart `"`" `"%~dp0BlockAuto.exe`""
[IO.File]::WriteAllText((Join-Path $app 'start-editor.cmd'), $start, [Text.Encoding]::Default)
$run = "@echo off`r`nrem 用独立运行器执行脚本：run-sample.cmd [脚本路径]，默认运行 samples\hello.block`r`nsetlocal`r`nset SCRIPT=%~dp0samples\hello.block`r`nif not `"%~1`"==`"`" set SCRIPT=%~1`r`n`"%~dp0BlockRunner.exe`" run `"%SCRIPT%`" %2 %3 %4 %5`r`necho.`r`necho 退出码：%ERRORLEVEL%`r`npause"
[IO.File]::WriteAllText((Join-Path $app 'run-sample.cmd'), $run, [Text.Encoding]::Default)

Copy-Item (Join-Path $src '发布说明.txt') $out -Force

Write-Host '== 发布目录 =='
Get-ChildItem $out -Recurse | Where-Object { -not $_.PSIsContainer } |
  Select-Object @{n='rel';e={$_.FullName.Substring($out.Length+1)}}, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} |
  Format-Table -AutoSize | Out-String -Width 200 | Write-Host