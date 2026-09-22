@echo off
setlocal
rem Builds the generated native binding (out\api\QtSharp.Api.dll): the generated C++ wrapper plus
rem the hand-written runtime, in one translation unit. tools\regen-api.ps1 calls this; it is also
rem fine to run by hand.
rem
rem Everything it needs can be pointed at from the environment, so a clone does not have to live in
rem D:\QtSharp:
rem   QT_DIR       a Qt kit's dev files (include\ and lib\)   default: this repo's Qt\6.11.0\msvc2022_64
rem   VCVARS       vcvarsall.bat of a Visual Studio with C++  default: the one used here
rem   VCVARS_VER   the toolset version for -vcvars_ver         default: 14.44
if "%QT_DIR%"=="" set QT_DIR=D:\QtSharp\Qt\6.11.0\msvc2022_64
if "%VCVARS%"=="" set VCVARS=D:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat
if "%VCVARS_VER%"=="" set VCVARS_VER=14.44

rem Run in this script's directory (the repository root), whatever the caller's working directory is.
cd /d "%~dp0"

set QT_INC=%QT_DIR%\include
set QT_LIB=%QT_DIR%\lib

if not exist "%VCVARS%" (
    echo [build_api] vcvarsall.bat not found at "%VCVARS%".
    echo [build_api] Set VCVARS to your Visual Studio's one, and VCVARS_VER if the toolset differs.
    exit /b 1
)
if not exist "%QT_INC%\QtCore\qobject.h" (
    echo [build_api] Qt headers not found under "%QT_INC%".
    echo [build_api] Set QT_DIR to a Qt 6.11.0 msvc2022_64 kit - see README.md.
    exit /b 1
)

rem The private headers the runtime uses live under include\QtCore\<version>\QtCore; take whatever
rem version this kit has rather than pinning the patch level.
set QT_VER=
for /d %%V in ("%QT_INC%\QtCore\6.*") do set QT_VER=%%~nxV
if "%QT_VER%"=="" (
    echo [build_api] no include\QtCore\6.* folder under "%QT_INC%" - is this a Qt 6 kit?
    exit /b 1
)

call "%VCVARS%" x64 -vcvars_ver=%VCVARS_VER%
if errorlevel 1 exit /b 1

if not exist "out\api" mkdir "out\api"

cl /nologo /LD /EHsc /permissive- /std:c++17 /Zc:__cplusplus /DUNICODE /D_UNICODE /DQT_NO_DEBUG /MD /O2 /Fe"out\api\QtSharp.Api.dll" /I"%QT_INC%" /I"%QT_INC%\QtCore" /I"%QT_INC%\QtGui" /I"%QT_INC%\QtWidgets" /I"%QT_INC%\QtCore\%QT_VER%" /I"%QT_INC%\QtCore\%QT_VER%\QtCore" /Inative native\QtSharpApiRuntime.cpp out\api\QtSharp.Api.native.cpp /link /LIBPATH:"%QT_LIB%" Qt6Core.lib Qt6Gui.lib Qt6Widgets.lib

exit /b %errorlevel%
