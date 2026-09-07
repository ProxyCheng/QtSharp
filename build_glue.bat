@echo off
setlocal
call "D:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 -vcvars_ver=14.44
if errorlevel 1 exit /b 1

set QT_INC=D:\QtSharp\Qt\6.11.0\msvc2022_64\include
set QT_LIB=D:\QtSharp\Qt\6.11.0\msvc2022_64\lib

if not exist "out\native" mkdir "out\native"

cl /nologo /LD /EHsc /permissive- /std:c++17 /Zc:__cplusplus /DUNICODE /D_UNICODE /DQT_NO_DEBUG /MD /O2 /Fe"out\native\QtSharp.dll" /I"%QT_INC%" /I"%QT_INC%\QtCore" /I"%QT_INC%\QtGui" /I"%QT_INC%\QtWidgets" native\QtSharp_inline.cpp out\qt_min\Std-symbols.cpp /link /DEF:native\QtSharp.def /LIBPATH:"%QT_LIB%" Qt6Core.lib Qt6Gui.lib Qt6Widgets.lib

exit /b %errorlevel%
