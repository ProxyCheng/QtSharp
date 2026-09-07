@echo off
setlocal
call "D:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 -vcvars_ver=14.44
if errorlevel 1 exit /b 1

set RT=D:\QtSharp\Qt\6.11.0\runtime

dumpbin /exports "%RT%\Qt6Core.dll" > "%RT%\exports_core.txt" 2>&1
dumpbin /exports "%RT%\Qt6Gui.dll" > "%RT%\exports_gui.txt" 2>&1
dumpbin /exports "%RT%\Qt6Widgets.dll" > "%RT%\exports_widgets.txt" 2>&1

echo done
