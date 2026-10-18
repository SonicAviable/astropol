#!/usr/bin/env bash
# Compile-only check of the C# scripts without Unity (used in Claude cloud sessions).
# Uses UnityEngine 2021.3 reference assemblies from NuGet: most of the Unity 6 API is
# covered; Input System and AI Navigation are stubbed in PackageStubs.cs.
# A green result means "no syntax/type errors", not "works in Unity".
set -euo pipefail
cd "$(dirname "$0")"

export UNITY_REFS_DIR="${UNITY_REFS_DIR:-$HOME/.cache/wargamerts-unity-refs}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Installing .NET SDK 8..."
  (apt-get install -y dotnet-sdk-8.0 || (apt-get update && apt-get install -y dotnet-sdk-8.0)) >/dev/null
fi

fetch() { # name url
  if [ ! -d "$UNITY_REFS_DIR/$1" ]; then
    mkdir -p "$UNITY_REFS_DIR/$1"
    curl -sSL -o "$UNITY_REFS_DIR/$1.nupkg" "$2"
    (cd "$UNITY_REFS_DIR/$1" && unzip -q -o "../$1.nupkg")
  fi
}
fetch engine https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
fetch editor https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg

dotnet build Editor.csproj -nologo -v q -clp:NoSummary
echo "Compile check passed."
