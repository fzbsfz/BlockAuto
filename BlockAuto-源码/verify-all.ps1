# 一次性验收：自测 + 命令行打开脚本 + 运行器 banner + 示例耗时
$ErrorActionPreference = 'Stop'
$src = 'C:\Users\31371\Desktop\BlockAuto-键鼠\BlockAuto-源码'
$exe = Join-Path $src 'bin\BlockAuto.exe'
$runner = Join-Path $src 'bin\BlockRunner.exe'
$tests = Join-Path $src 'bin\BlockAuto.Tests.exe'

Write-Host '=== 1. 单元自测 ==='
$out = & $tests 2>&1 | Select-Object -Last 1
Write-Host $out

Write-Host ''
Write-Host '=== 2. 命令行打开脚本（黑盒）==='
$script = Join-Path $src 'samples\keymouse_demo.block'
$p = Start-Process -FilePath $exe -ArgumentList ('"' + $script + '"') -PassThru
Start-Sleep -Seconds 7
$p.Refresh()
$title = $p.MainWindowTitle
Write-Host ('窗口标题 = ' + $title)
Write-Host $(if ($title -like '*keymouse_demo*') { 'OK 命令行打开了指定脚本' } else { 'FAIL 未打开指定脚本' })
Stop-Process -Id $p.Id -Force
Start-Sleep -Milliseconds 400

Write-Host ''
Write-Host '=== 3. 运行器 banner ==='
& $runner 2>&1 | Select-Object -First 1 | Write-Host

Write-Host ''
Write-Host '=== 4. 示例脚本耗时 ==='
foreach ($f in @('hello', 'logic_demo', 'recursive_find')) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $runner run (Join-Path $src ("samples\" + $f + ".block")) --silent 2>&1 | Out-Null
    $sw.Stop()
    Write-Host ("{0,-16} {1,5:0.00} 秒" -f $f, $sw.Elapsed.TotalSeconds)
}