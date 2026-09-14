@echo off
setlocal
call "D:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 -vcvars_ver=14.44
if errorlevel 1 exit /b 1
cl /nologo /LD /EHsc /O2 /MD /Fe"testbind\bin\Release\net10.0\CrashTracer.dll" /Fo"D:\QtSharp\out\native\CrashTracer.obj" native\CrashTracer.cpp /link dbghelp.lib
exit /b %errorlevel%
