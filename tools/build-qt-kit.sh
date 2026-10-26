#!/usr/bin/env bash
# Stages the Qt 6.12.0 pieces the binding needs on Linux, without a Qt installer:
#
#   <dest>/kit      dev files (include/ + lib/ + plugins/)   -> QT_DIR
#   <dest>/runtime  the prefix-shaped runtime a built app loads:
#                   lib/*.so.6 + plugins/ + qt.conf          -> QT_RUNTIME_DIR
#
# The archives come from Qt's own online repository, the same ones the official installer fetches.
# This is the POSIX twin of tools/build-qt-kit.ps1 (which does the same for the MSVC kit); it
# parses the repository's directory listings instead of using aqt, because the 6.12 repository no
# longer publishes the Updates.xml aqt needs (see README.md).
#
#   tools/build-qt-kit.sh [--version 6.12.0] [--dest out/qt-kit] [--kit-dir /path/to/gcc_64] [--force]
#
# Requires curl and 7z (p7zip-full). The runtime is relinked for deployment with patchelf: every
# Qt library gets $ORIGIN, every plugin $ORIGIN/../../lib, so the application directory needs no
# LD_LIBRARY_PATH.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

version=6.12.0
dest="$root/out/qt-kit"
kit_source=""
force=0
while [ $# -gt 0 ]; do
    case "$1" in
        --version) version="$2"; shift 2 ;;
        --dest)    dest="$2"; shift 2 ;;
        --kit-dir) kit_source="$2"; shift 2 ;;
        --force)   force=1; shift ;;
        *) echo "unknown argument: $1" >&2; exit 1 ;;
    esac
done

short="${version//./}"
kit="$dest/kit"
runtime="$dest/runtime"
cache="$root/out/qt-cache"

fetch() {
    local url="$1" out="$2"
    curl -fsSL --ssl-no-revoke --retry 3 --retry-delay 5 -o "$out" "$url"
}

listing() {
    local url="$1"
    curl -fsSL --ssl-no-revoke --retry 3 --retry-delay 5 "$url"
}

# The package archives in a listing, as the href basename for one package prefix.
archive_of() {
    local html="$1" pkg="$2"
    printf '%s' "$html" |
        grep -oE "href=\"[^\"]*${pkg}-[^\"]*-[Xx]86_64\.7z\"" |
        head -n 1 | sed 's/^href="//; s/"$//'
}

# Addon package names in the Qt online repository (the same set tools/build-qt-kit.ps1 lists).
addons="qtcharts qt3d qtshadertools qtmultimedia qtpositioning"

# ------------------------------------------------------------------ the dev kit
if [ -n "$kit_source" ]; then
    kit="$(cd "$kit_source" && pwd)"
    echo "[qt-kit] using the dev files already at $kit"
elif [ "$force" = 0 ] && [ -f "$kit/include/QtCore/qobject.h" ]; then
    echo "[qt-kit] using the kit already staged at $kit"
else
    if ! command -v 7z >/dev/null 2>&1; then
        echo "[qt-kit] 7z was not found. Install p7zip-full, or pass --kit-dir." >&2
        exit 1
    fi
    mkdir -p "$kit" "$cache"
    repo_base="https://download.qt.io/online/qtsdkrepository/linux_x64/desktop/qt6_$short"

    # The repository groups the kits one level down; for 6.8 the folder is qt6_680/qt6_680, the
    # 6.12 Windows kit uses qt6_6120/qt6_6120_msvc2022_64. Probe the plausible Linux names rather
    # than assuming one, so a repository reshuffle fails here with a clear message.
    kit_level=""
    # Qt groups each kit one level down and the folder name has changed between versions
    # (qt6_680/qt6_680, qt6_6120/qt6_6120_msvc2022_64). Probe the plausible Linux names, then
    # whatever the version group actually lists, so a repository reshuffle fails here with a
    # clear message.
    candidates="qt6_${short}_gcc_64 qt6_$short qt6_${short}_linux_gcc_64
qt6_$short/qt6_$short qt6_${short}_gcc_64/qt6_${short}_gcc_64 qt6_${short}_gcc_64/qt6_$short"
    group_listing="$(listing "$repo_base/" 2>/dev/null || true)"
    if [ -n "$group_listing" ]; then
        discovered="$(printf '%s' "$group_listing" |
            grep -oE 'href="qt6_[^"/]+/"' | sed 's/^href="//; s/"$//; s:/$::' | sort -u)"
        for dir in $discovered; do
            candidates="$candidates $dir $dir/$dir"
        done
    fi
    for candidate in $candidates; do
        if listing "$repo_base/$candidate/qt.qt6.$short.linux_gcc_64/" >/dev/null 2>&1; then
            kit_level="$candidate"
            break
        fi
    done
    if [ -z "$kit_level" ]; then
        echo "[qt-kit] could not find the Linux kit under $repo_base (tried: $candidates)." >&2
        exit 1
    fi
    echo "[qt-kit] repository: $repo_base/$kit_level"

    sources="$repo_base/$kit_level/qt.qt6.$short.linux_gcc_64"
    for addon in $addons; do
        sources="$sources $repo_base/$kit_level/qt.qt6.$short.addons.$addon.linux_gcc_64"
    done

    archives=""
    for source in $sources; do
        echo "[qt-kit] listing $source"
        html="$(listing "$source/")" || { echo "[qt-kit] cannot list $source" >&2; exit 1; }
        if [ "$source" = "$repo_base/$kit_level/qt.qt6.$short.linux_gcc_64" ]; then
            # qtdeclarative is not bound, but QtMultimedia's ffmpeg backend links Qt6Qml/Qt6Quick.
            packages="qtbase qtsvg icu qtdeclarative"
        else
            # qt.qt6.6120.addons.qtcharts.linux_gcc_64 -> qtcharts
            packages="$(basename "$source" | sed -E "s/^qt\.qt6\.${short}\.addons\.//; s/\.linux_gcc_64$//")"
        fi
        for package in $packages; do
            archive="$(archive_of "$html" "$package" || true)"
            if [ -z "$archive" ]; then
                echo "[qt-kit] no $package archive is published under $source" >&2
                exit 1
            fi
            archives="$archives $source/$archive"
        done
    done

    for url in $archives; do
        file="$cache/$(basename "$url")"
        if [ "$force" = 1 ] || [ ! -f "$file" ]; then
            echo "[qt-kit] downloading $(basename "$url")"
            fetch "$url" "$file"
        fi
        echo "[qt-kit] extracting $(basename "$file")"
        7z x "$file" "-o$kit" -y -bso0 -bsp0 >/dev/null
    done

    qobject="$(find "$kit" -path '*/include/QtCore/qobject.h' -print -quit)"
    if [ -z "$qobject" ]; then
        echo "[qt-kit] the extracted archives do not contain include/QtCore/qobject.h under $kit." >&2
        exit 1
    fi
    kit_root="$(cd "$(dirname "$qobject")/../.." && pwd)"
    if [ "$kit_root" != "$kit" ]; then
        echo "[qt-kit] discovered the Qt kit root: $kit_root"
        kit="$kit_root"
    fi

    # An addon archive can extract beside the kit root. Merge the pieces the binding needs into
    # the kit; the addon table mirrors tools/build-qt-kit.ps1.
    merge_addon() {
        local marker="$1" includes="$2" libs="$3" plugins="$4"
        local found root_dir include_dir
        if [ -e "$kit/$marker" ]; then return; fi
        found="$(find "$search_root" -path "*/$marker" -print -quit)"
        if [ -z "$found" ]; then
            echo "[qt-kit] an addon archive did not provide $marker under $kit" >&2
            exit 1
        fi
        include_dir="$(dirname "$(dirname "$found")")"
        root_dir="$(dirname "$include_dir")"
        echo "[qt-kit] merging addon pieces from $root_dir"
        for dir in $includes; do
            for match in "$root_dir/include"/$dir; do
                [ -d "$match" ] && cp -a "$match" "$kit/include/"
            done
        done
        for pattern in $libs; do
            for lib in "$root_dir"/lib/$pattern*; do
                [ -e "$lib" ] && cp -aL "$lib" "$kit/lib/"
            done
        done
        for plugin_dir in $plugins; do
            [ -d "$root_dir/plugins/$plugin_dir" ] || continue
            mkdir -p "$kit/plugins/$plugin_dir"
            cp -aL "$root_dir/plugins/$plugin_dir"/*.so "$kit/plugins/$plugin_dir/" 2>/dev/null || true
        done
    }

    merge_addon include/QtCharts/qchart.h        "QtCharts"              "libQt6Charts"                          ""
    merge_addon include/Qt3DCore/qentity.h       "Qt3D*"                 "libQt63D*"                             "renderers geometryloaders sceneparsers"
    merge_addon include/QtShaderTools/qtshadertoolsexports.h "QtShaderTools" "libQt6ShaderTools"                  ""
    merge_addon include/QtMultimedia/qmediaplayer.h "QtMultimedia QtMultimediaWidgets" "libQt6Multimedia*"     "multimedia"
    merge_addon include/QtPositioning/qgeocoordinate.h "QtPositioning"    "libQt6Positioning*"                    ""
fi

# ------------------------------------------------------------------ validate the dev kit
for marker in include/QtCore/qobject.h \
              include/QtCharts/qchart.h \
              include/Qt3DCore/qentity.h \
              include/QtShaderTools/qtshadertoolsexports.h \
              include/QtMultimedia/qmediaplayer.h \
              include/QtPositioning/qgeocoordinate.h; do
    if [ ! -e "$kit/$marker" ]; then
        echo "[qt-kit] the kit at '$kit' is missing $marker." >&2
        exit 1
    fi
done
# The ffmpeg backend plugin links these; the checker catches a kit that lacks qtdeclarative.
if [ ! -e "$kit/lib/libQt6Qml.so.6" ] || [ ! -e "$kit/lib/libQt6Quick.so.6" ]; then
    echo "[qt-kit] Qt6Qml/Qt6Quick are missing under '$kit/lib' - QtMultimedia's ffmpeg backend needs them." >&2
    exit 1
fi
if ! find "$kit/include/QtCore" -name qobject_p.h -print -quit | grep -q .; then
    echo "[qt-kit] the kit at '$kit' has no QtCore private headers - the runtime needs them (see README.md)." >&2
    exit 1
fi
if [ ! -e "$kit/lib/libQt6Core.so.6" ]; then
    echo "[qt-kit] no libQt6Core.so.6 under '$kit/lib' - is this a shared Qt kit?" >&2
    exit 1
fi
echo "[qt-kit] kit: $kit"

# Where the archives were unpacked. An archive can put its pieces beside the discovered kit root
# (the ICU archive and the addons do on some versions), so searches for "anything the kit
# provides" look at the whole extraction tree rather than only the discovered root.
if [ -n "$kit_source" ]; then
    search_root="$kit"
else
    search_root="$dest/kit"
fi

# ------------------------------------------------------------------ the runtime
# A cached kit usually comes with the runtime it was staged from (the CI cache holds out/qt-kit),
# so only restage when it is missing or --force asked for it: the copy and the patchelf pass over
# ~100 MB of libraries are the slow part of an otherwise warm cache.
if [ "$force" = 0 ] && [ -f "$runtime/qt.conf" ] && [ -e "$runtime/lib/libQt6Core.so.6" ]; then
    echo "[qt-kit] using the runtime already staged at $runtime"
else
rm -rf "$runtime"
mkdir -p "$runtime/lib" "$runtime/plugins"

# Everything the glues and plugins can load: Qt's own modules (addons included), the libraries Qt
# bundles (ICU, OpenSSL for the TLS backend, FFmpeg for Multimedia) and the X11/GL integration
# libraries that ship with the kit. System libraries stay system libraries.
copied=0
for lib in "$kit"/lib/libQt6*.so.6 "$kit"/lib/libicu*.so.* "$kit"/lib/libav*.so.* \
           "$kit"/lib/libsw*.so.* "$kit"/lib/libssl*.so.* "$kit"/lib/libcrypto*.so.*; do
    [ -e "$lib" ] || continue
    cp -L "$lib" "$runtime/lib/"
    copied=$((copied + 1))
done
if [ "$copied" -eq 0 ]; then
    echo "[qt-kit] no runtime libraries were staged from '$kit/lib'." >&2
    exit 1
fi

# Dependency closure: Qt6Core and the platform plugins need libraries the kit bundles elsewhere
# (ICU is a separate archive, and a kit may ship its own OpenSSL/FFmpeg). Walk the NEEDED entries
# and copy every one the kit provides; system libraries are left to the system.
if command -v readelf >/dev/null 2>&1; then
    for _ in 1 2 3 4; do
        added=0
        for lib in "$runtime"/lib/*.so*; do
            [ -e "$lib" ] || continue
            while IFS= read -r need; do
                [ -z "$need" ] && continue
                [ -e "$runtime/lib/$need" ] && continue
                candidate="$(find "$search_root" -name "$need" -print -quit)"
                if [ -n "$candidate" ]; then
                    cp -L "$candidate" "$runtime/lib/"
                    added=$((added + 1))
                fi
            done < <(readelf -d "$lib" 2>/dev/null | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p')
        done
        [ "$added" -eq 0 ] && break
    done
    echo "[qt-kit] runtime dependency closure complete ($copied initial libraries)"
else
    echo "[qt-kit] warning: readelf is not available; the runtime dependency closure was not checked." >&2
fi
if [ ! -e "$runtime/lib/libQt6Core.so.6" ]; then
    echo "[qt-kit] libQt6Core.so.6 was not staged - the kit layout is not usable." >&2
    exit 1
fi
# Qt's Linux builds link ICU; a runtime without it fails at dlopen with a message that points at
# the glue rather than at ICU, so make the staging step say it.
if ! ls "$runtime"/lib/libicui18n.so.* >/dev/null 2>&1; then
    echo "[qt-kit] libicui18n was not staged, but Qt6Core needs it (the ICU archive provides it)." >&2
    exit 1
fi

# Every plugin directory the kit ships (platform integration, image formats, styles, TLS,
# multimedia and the Qt3D renderer/loader/scene plugins). Debug builds are not shipped.
plugins_root="$kit/plugins"
[ -d "$plugins_root" ] || plugins_root="$search_root/plugins"
while IFS= read -r plugin_dir; do
    name="$(basename "$plugin_dir")"
    mkdir -p "$runtime/plugins/$name"
    find "$plugin_dir" -maxdepth 1 -name '*.so' ! -name '*d.so' -exec cp -L {} "$runtime/plugins/$name/" \;
done < <(find "$plugins_root" -mindepth 1 -maxdepth 1 -type d 2>/dev/null)
if [ ! -f "$runtime/plugins/platforms/libqxcb.so" ] && [ ! -f "$runtime/plugins/platforms/libqoffscreen.so" ]; then
    echo "[qt-kit] neither libqxcb.so nor libqoffscreen.so was staged - check the kit layout." >&2
    exit 1
fi

# Qt's own deployment tools generate this file: Prefix=. resolves against the application
# directory and Plugins=plugins makes QLibraryInfo look in the tree deployed next to the
# executable. The managed runtime also points QT_PLUGIN_PATH here as a fallback, but qt.conf is
# what works without touching the process environment.
cat > "$runtime/qt.conf" <<'EOF'
[Paths]
Prefix = .
Plugins = plugins
EOF

# ------------------------------------------------------------------ deployment rpaths
# Qt's plugins are dlopen'ed by absolute path; their own NEEDED entries are resolved through the
# plugin's RUNPATH, not through the application's. Pin both levels to the layout buildTransitive
# copies, so the runtime works from any application directory.
if command -v patchelf >/dev/null 2>&1; then
    for lib in "$runtime"/lib/*.so*; do
        patchelf --set-rpath '$ORIGIN' "$lib"
    done
    while IFS= read -r -d '' plugin; do
        patchelf --set-rpath '$ORIGIN/../../lib' "$plugin"
    done < <(find "$runtime/plugins" -name '*.so' -print0)
    echo "[qt-kit] rpaths pinned with patchelf"
elif [ -n "${GITHUB_ACTIONS:-}" ]; then
    echo "[qt-kit] patchelf is required in CI: without it the plugins cannot find the Qt libraries." >&2
    exit 1
else
    echo "[qt-kit] warning: patchelf is not installed; plugins keep the rpath the kit shipped." >&2
    echo "[qt-kit]          install patchelf for a self-contained runtime (see README.md)." >&2
fi
fi

lib_size="$(du -sh "$runtime/lib" | cut -f1)"
plugin_count="$(find "$runtime/plugins" -name '*.so' | wc -l)"
echo "[qt-kit] runtime: $runtime (lib $lib_size, $plugin_count plugins)"

if [ -n "${GITHUB_ENV:-}" ]; then
    echo "QT_DIR=$kit" >> "$GITHUB_ENV"
    echo "QT_RUNTIME_DIR=$runtime" >> "$GITHUB_ENV"
fi
