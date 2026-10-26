#!/usr/bin/env bash
# Builds the native glue of an extension module - QtSharp.Charts and whatever follows it - into its
# own shared object, linked against that module's Qt library. This is the POSIX twin of
# tools/build-native-module.ps1, which does the same for Windows; tools/regen-api.ps1 calls the
# one that matches the host.
#
#   tools/build-native-module.sh <module> [qt-kit-dir]
#
# Everything about the module comes from the generator (`qtgen module <name>`), so adding a module
# stays "a row in the module table + a row in build-qt-kit.sh's list": no build script edits. The
# Qt kit defaults to Qt/6.12.0/gcc_64 (or out/qt-kit/kit) and can also be given with QT_DIR.
#
# The same warnings-as-errors and link-time strictness as build_api.sh: -isystem for Qt's headers,
# -Werror for this repository's code, and -Wl,-z,defs so an unresolved symbol is a link error.
# Deprecated declarations and Qt's intentionally uninitialized value-type constructors are the
# suppressed classes (the glue calls the API the model declares).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

module="${1:-}"
if [ -z "$module" ]; then
    echo "usage: tools/build-native-module.sh <module> [qt-kit-dir]" >&2
    exit 1
fi
QT_DIR="${2:-${QT_DIR:-$root/Qt/6.12.0/gcc_64}}"
if [ ! -f "$QT_DIR/include/QtCore/qobject.h" ] && [ -f "$root/out/qt-kit/kit/include/QtCore/qobject.h" ]; then
    QT_DIR="$root/out/qt-kit/kit"
fi
CXX="${CXX:-g++}"
OPT="${OPT:--O2}"
QT_INC="$QT_DIR/include"
QT_LIB="$QT_DIR/lib"

rid=linux-x64
gen="${QTGEN:-$root/gen/bin/Release/net10.0/$rid/qtgen}"
if [ ! -x "$gen" ]; then
    echo "The generator is missing - build it first: dotnet build gen/gen.csproj -c Release" >&2
    exit 1
fi

descriptor="$("$gen" module "$module")" || {
    echo "Could not read the '$module' module descriptor." >&2
    exit 1
}
field() { printf '%s\n' "$descriptor" | sed -n "s/^$1=//p" | head -n 1; }

namespace="$(field namespace)"
nativeLibrary="$(field nativeLibrary)"
qtModules="$(field qtModules | tr ',' ' ')"
qtLibs="$(field qtLibs | tr ',' ' ')"
qtDefines="$(field qtDefines | tr ',' ' ')"
if [ -z "$nativeLibrary" ] || [ -z "$namespace" ]; then
    echo "Could not read the '$module' module descriptor." >&2
    exit 1
fi
echo "[module] $module -> $namespace, modules $qtModules, links $qtLibs"

source_file="out/$module/$namespace.native.cpp"
if [ ! -f "$source_file" ]; then
    echo "'$source_file' is missing - generate it first: qtgen codegen model/qtsharp.$module.json out/$module --module $module" >&2
    exit 1
fi
for qtModule in $qtModules; do
    if [ ! -d "$QT_INC/$qtModule" ]; then
        echo "The Qt module '$qtModule' is not in the kit at $QT_INC - see README.md (Prerequisites)." >&2
        exit 1
    fi
done

out_dir="out/api-modules/$module"
mkdir -p "$out_dir"

# The module's own header directory plus the ones its headers pull in (QtCharts derives from
# QtWidgets, and every Qt module needs QtCore/QtGui).
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
)
for qtModule in $qtModules; do
    includes+=(-isystem "$QT_INC/$qtModule")
done
qt_ver_dir="$(ls -d "$QT_INC"/QtCore/6.* 2>/dev/null | head -n 1 || true)"
if [ -n "$qt_ver_dir" ]; then
    includes+=(-isystem "$qt_ver_dir" -isystem "$qt_ver_dir/QtCore")
fi
includes+=(-I"$root/native")

link_libs=()
for qtLib in $qtLibs; do
    link_libs+=("-l$qtLib")
done
link_libs+=(-lQt6Widgets -lQt6Gui -lQt6Core)

defines=()
for define in $qtDefines; do
    [ -n "$define" ] && defines+=("-D$define")
done

out="$out_dir/lib$nativeLibrary.so"
echo "[module] compiling $source_file"
"$CXX" -std=c++17 "$OPT" -Wall -Wextra -Werror \
    -Wno-deprecated-declarations -Wno-uninitialized -Wno-maybe-uninitialized \
    -shared -fPIC -pthread -fvisibility=hidden \
    -DQT_NO_DEBUG "${defines[@]}" \
    -Wl,-z,defs -Wl,-rpath-link,"$QT_LIB" \
    -Wl,-soname,"lib$nativeLibrary.so" -Wl,-rpath,'$ORIGIN/lib' \
    -o "$out" "${includes[@]}" "$source_file" \
    -L"$QT_LIB" "${link_libs[@]}"

size="$(du -h "$out" | cut -f1)"
echo "[module] wrote $out ($size)"
