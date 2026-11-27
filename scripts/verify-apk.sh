#!/usr/bin/env bash
set -euo pipefail
apk="${1:?apk path}"; want_ver="${2:?expected version, e.g. 1.0.0}"; want_pkg="${3:-dev.kzstudio.kzcompanion}"; want_fp="${4:-}"

sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/AppData/Local/Android/Sdk}}"
bt="$(ls -d "$sdk"/build-tools/*/ 2>/dev/null | sort -V | tail -1 || true)"
pick() { command -v "$1" 2>/dev/null || command -v "$1.bat" 2>/dev/null || { [ -n "$bt" ] && { ls "$bt$1" "$bt$1.bat" "$bt$1.exe" 2>/dev/null | head -1; }; } || true; }
apksigner="$(pick apksigner)"; aapt2="$(pick aapt2)"
[ -n "$apksigner" ] && [ -n "$aapt2" ] || { echo "apksigner/aapt2 not found"; exit 2; }

certs="$("$apksigner" verify --verbose --print-certs "$apk")" || { echo "FAIL: signature does not verify"; exit 1; }
echo "$certs" | grep -E "Verified using|Signer #1 certificate (DN|SHA-256)" || true
if echo "$certs" | grep -qiE "CN=Android Debug|O=Android"; then echo "FAIL: APK is signed with the Android debug key"; exit 1; fi
echo "$certs" | grep -q "Verified using v2 scheme (APK Signature Scheme v2): true" || { echo "FAIL: no v2 signature"; exit 1; }

if [ -n "$want_fp" ]; then
  got_fp="$(echo "$certs" | sed -n 's/^\(Signer #1\|V[0-9.]* Signer:\) certificate SHA-256 digest: //p' | head -1 | tr -d ':' | tr 'a-f' 'A-F')"
  exp_fp="$(echo "$want_fp" | tr -d ':' | tr 'a-f' 'A-F')"
  [ -n "$got_fp" ] || { echo "FAIL: could not read the signing certificate fingerprint"; exit 1; }
  [ "$got_fp" = "$exp_fp" ] || { echo "FAIL: signing certificate SHA-256 is $got_fp, expected $exp_fp"; exit 1; }
  echo "Certificate fingerprint matches the expected release certificate"
fi

badging="$("$aapt2" dump badging "$apk")"
pkg="$(echo "$badging" | sed -n "s/^package: name='\([^']*\)'.*/\1/p")"
ver="$(echo "$badging" | sed -n "s/^package:.*versionName='\([^']*\)'.*/\1/p")"
[ "$pkg" = "$want_pkg" ] || { echo "FAIL: package '$pkg', expected '$want_pkg'"; exit 1; }
[ "$ver" = "$want_ver" ] || { echo "FAIL: versionName '$ver', expected '$want_ver'"; exit 1; }
abis="$(echo "$badging" | sed -n "s/^native-code: //p")"
case "$abis" in "'arm64-v8a'") ;; *) echo "FAIL: native code is $abis, expected only 'arm64-v8a'"; exit 1;; esac
echo "OK: $pkg $ver, $abis, release-signed"
