@echo off
rem 用独立运行器执行脚本：run-sample.cmd [脚本路径]，默认运行 samples\hello.block
setlocal
set SCRIPT=%~dp0samples\hello.block
if not "%~1"=="" set SCRIPT=%~1
"%~dp0bin\BlockRunner.exe" run "%SCRIPT%" %2 %3 %4 %5
echo.
echo 退出码：%ERRORLEVEL%
pause