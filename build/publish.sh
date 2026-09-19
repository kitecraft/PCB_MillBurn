#!/usr/bin/env bash
# Publishes the app and the CLI for one runtime, as the release ships them.
#
#   build/publish.sh <runtime> <output-folder> [version]
#   build/publish.sh win-x64 out/windows
#   build/publish.sh linux-x64 out/linux 0.1.5
#
# Each program is one self-contained file — its code, every library it uses and the .NET runtime
# bundled together — so the folder someone unpacks holds two programs and the help, instead of
# three hundred files to hunt the right one out of. Renamed after publishing rather than in the
# projects, because the app's own resources are addressed by its assembly name; a single-file
# program does not care what its file is called.
#
# The release workflow runs this, so what a person builds locally is what a download contains.
set -euo pipefail

rid="${1:?runtime, e.g. win-x64 or linux-x64}"
out="${2:?output folder, e.g. out/windows}"
version="${3:-}"

cd "$(dirname "$0")/.."

options=(
  -c Release -r "$rid" --self-contained
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:EnableCompressionInSingleFile=true
  -p:DebugType=none
  -p:PublishDocumentationFile=false
  -p:PublishReferencesDocumentationFiles=false
)

if [[ -n "$version" ]]; then
  options+=(-p:Version="$version")
fi

# Start clean: a folder published the old way holds hundreds of loose libraries, and a stale one
# left beside a single-file program is only confusing. Emptied rather than removed, because a
# folder that an Explorer window or a shell is sitting in cannot be removed.
mkdir -p "$out"
find "$out" -mindepth 1 -delete

dotnet publish src/MillBurn.App "${options[@]}" -o "$out"
dotnet publish src/MillBurn.Cli "${options[@]}" -o "$out"

# Symbols for the native graphics libraries still come through; nobody running the app needs them.
rm -f "$out"/*.pdb

ext=""
if [[ "$rid" == win-* ]]; then
  ext=".exe"
fi

mv "$out/MillBurn.App$ext" "$out/MillBurn$ext"
mv "$out/MillBurn.Cli$ext" "$out/millburn-cli$ext"

cp LICENSE THIRD-PARTY-NOTICES.md "$out/"

# Harmless on Windows; on Linux it is what makes them runnable after a copy from NTFS.
chmod +x "$out/MillBurn$ext" "$out/millburn-cli$ext"

echo "Published $rid to $out:"
ls -1 "$out"
