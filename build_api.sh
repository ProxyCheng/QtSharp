#!/usr/bin/env bash
# Builds the generated native binding (out/api/libQtSharp.Api.so): the generated C++ wrapper plus
# the hand-written runtime, in one shared object. This is the POSIX twin of build_api.bat;
# tools/regen-api.ps1 calls whichever one matches the host.
#
# Everything it needs can be pointed at from the environment, so a clone does not have to live in
# one particular place:
#   QT_DIR       a Qt kit's dev files (include/ and lib/)   default: this repo's Qt/6.12.0/gcc_64,
#                                                           or out/qt-kit/kit when that is absent
#   CXX          the C++ compiler                           default: g++
#   OPT          the optimisation flags                     default: -O2 (CI may use -O1)
#   QT_VER       the include/QtCore/<version> folder        default: discovered
#
# The shared object is linked with an $ORIGIN/lib rpath: the Qt libraries are deployed under lib/
# next to the application (see QtSharp.Api.props and the packages' buildTransitive), so no
# LD_LIBRARY_PATH is needed. -fvisibility=hidden keeps the dynamic symbol table to the exported
# C ABI (native/QtSharpApiExport.h) instead of every symbol the Qt headers bring in.
# -Wl,-z,defs makes a missing symbol a link error rather than a dlopen-time surprise, and the
# warnings-as-errors policy (the C# projects have TreatWarningsAsErrors) covers this compile too;
# Qt's own headers are -isystem, so only this repository's code has to be warning-free.
# Deprecation and uninitialized-value diagnostics are the suppressed classes. Deprecation: the
# glue calls exactly the Qt function the model declares, and 6.12 still declares many deprecated
# APIs. Uninitialized: Qt's value types have `Qt::Uninitialized` constructors whose whole purpose
# is to leave the storage untouched; the generated wrapper then reads the fields to build the POD,
# so GCC (correctly) reports the read. That is the bound API's own semantics, not a defect in this
# repository, and -Werror stays meaningful for everything else.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
cd "$root"

QT_DIR="${QT_DIR:-$root/Qt/6.12.0/gcc_64}"
# A clone staged with tools/build-qt-kit.sh has the kit under out/qt-kit.
if [ ! -f "$QT_DIR/include/QtCore/qobject.h" ] && [ -f "$root/out/qt-kit/kit/include/QtCore/qobject.h" ]; then
    QT_DIR="$root/out/qt-kit/kit"
fi
CXX="${CXX:-g++}"
OPT="${OPT:--O2}"
QT_INC="$QT_DIR/include"
QT_LIB="$QT_DIR/lib"
OUT="$root/out/api"

if [ ! -f "$QT_INC/QtCore/qobject.h" ]; then
    echo "[build_api] Qt headers not found under '$QT_INC'." >&2
    echo "[build_api] Set QT_DIR to a Qt 6.12.0 gcc_64 kit - see README.md." >&2
    exit 1
fi

# The private headers the runtime uses live under include/QtCore/<version>/QtCore; take whatever
# version this kit has rather than pinning the patch level.
if [ -z "${QT_VER:-}" ]; then
    QT_VER="$(basename "$(ls -d "$QT_INC"/QtCore/6.* 2>/dev/null | head -n 1)" 2>/dev/null || true)"
fi
if [ -z "$QT_VER" ]; then
    echo "[build_api] no include/QtCore/6.* folder under '$QT_INC' - is this a Qt 6 kit?" >&2
    exit 1
fi

mkdir -p "$OUT"

includes=(
    -isystem "$QT_INC"
    -isystem "$QT_INC/QtCore"
    -isystem "$QT_INC/QtGui"
    -isystem "$QT_INC/QtWidgets"
    -isystem "$QT_INC/QtNetwork"
    -isystem "$QT_INC/QtSvg"
    -isystem "$QT_INC/QtSvgWidgets"
    -isystem "$QT_INC/QtPrintSupport"
    -isystem "$QT_INC/QtOpenGLWidgets"
    -isystem "$QT_INC/QtOpenGL"
    -isystem "$QT_INC/QtCore/$QT_VER"
    -isystem "$QT_INC/QtCore/$QT_VER/QtCore"
    -I"$root/native"
)

libs=(
    -L"$QT_LIB"
    -lQt6Core -lQt6Gui -lQt6Widgets -lQt6Network -lQt6Svg -lQt6SvgWidgets
    -lQt6PrintSupport -lQt6OpenGL -lQt6OpenGLWidgets
)

echo "[build_api] Qt kit: $QT_DIR (QtCore $QT_VER)"
echo "[build_api] compiling native/QtSharpApiRuntime.cpp + out/api/QtSharp.Api.native.cpp"

"$CXX" -std=c++17 "$OPT" -Wall -Wextra -Werror \
    -Wno-deprecated-declarations -Wno-uninitialized -Wno-maybe-uninitialized \
    -shared -fPIC -pthread -fvisibility=hidden \
    -DQT_NO_DEBUG \
    -Wl,-z,defs -Wl,-rpath-link,"$QT_LIB" \
    -Wl,-soname,libQtSharp.Api.so -Wl,-rpath,'$ORIGIN/lib' \
    -o "$OUT/libQtSharp.Api.so" \
    "${includes[@]}" \
    native/QtSharpApiRuntime.cpp out/api/QtSharp.Api.native.cpp \
    "${libs[@]}"

size="$(du -h "$OUT/libQtSharp.Api.so" | cut -f1)"
echo "[build_api] wrote $OUT/libQtSharp.Api.so ($size)"
