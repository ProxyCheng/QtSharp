@echo off
setlocal
rem Builds the crash tracer (native\CrashTracer.cpp) into the folder the test app runs from. It is a
rem debugging aid, not part of the build: call it when something crashes, and run the app with the
rem tracer installed.
rem
rem   VCVARS / VCVARS_VER  the Visual Studio environment to use (see build_api.bat).
if "%VCVARS%"=="" set VCVARS=D:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat
if "%VCVARS_VER%"=="" set VCVARS_VER=14.44

cd /d "%~dp0"

if not exist "%VCVARS%" (
    echo [build_crashtracer] vcvarsall.bat not found at "%VCVARS%".
    echo [build_crashtracer] Set VCVARS to your Visual Studio's one.
    exit /b 1
)

call "%VCVARS%" x64 -vcvars_ver=%VCVARS_VER%
if errorlevel 1 exit /b 1

if not exist "out\native" mkdir "out\native"
if not exist "testbind\bin\Release\net10.0" mkdir "testbind\bin\Release\net10.0"

cl /nologo /LD /EHsc /O2 /MD /Fe"testbind\bin\Release\net10.0\CrashTracer.dll" /Fo"out\native\CrashTracer.obj" native\CrashTracer.cpp /link dbghelp.lib

exit /b %errorlevel%
