#!/bin/bash
# Writes a .meta (fresh random guid) next to every file / folder under the Metroid code folder that has none.
# Usage: metas.sh [root]. Prints what it wrote. Known kinds: folder, .cs, .shader, .mat, .asmdef, .md/.txt.
ROOT="${1:-D:/Dreamport/Metroid Prime/METROIDPRIME/Assets/#User/SomniumMetroid}"
guid() { od -An -N16 -tx1 /dev/urandom | tr -d ' \n'; }
emit() { # $1 path
  local p="$1" m="$1.meta" g; [ -e "$m" ] && return; g=$(guid)
  if [ -d "$p" ]; then printf 'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$g" > "$m"
  else case "$p" in
    *.cs) printf 'fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$g" > "$m";;
    *.shader) printf 'fileFormatVersion: 2\nguid: %s\nShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$g" > "$m";;
    *.mat) printf 'fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$g" > "$m";;
    *.asmdef) printf 'fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$g" > "$m";;
    *.hlsl) printf 'fileFormatVersion: 2
guid: %s
ShaderIncludeImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
' "$g" > "$m";;
    *.md|*.txt) printf 'fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$g" > "$m";;
    *) return;;
  esac; fi
  echo "meta $g  ${p#$ROOT/}"
}
emit "$ROOT"
find "$ROOT" -mindepth 1 \( -name "*.meta" -prune \) -o -print | while IFS= read -r p; do emit "$p"; done
