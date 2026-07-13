#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/scripts/progpu-package-list.sh"

dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

configuration="${PROGPU_CONFIGURATION:-Release}"
avalonia_version="${PROGPU_AVALONIA_VERSION:-12.0.5}"
runtime_version="${PROGPU_RUNTIME_VERSION:-0.1.0-preview.12}"
integration_version="${PROGPU_INTEGRATION_VERSION:-12.0.5-preview.12}"
package_output="${PROGPU_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/${configuration}}"
restore_root="$(mktemp -d "${TMPDIR:-/tmp}/progpu-avalonia-pack.XXXXXX")"
restore_artifacts="${restore_root}/artifacts"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-${restore_root}/http-cache}"

cleanup() {
  local exit_code=$?
  rm -rf "${restore_root}"
  return "${exit_code}"
}
trap cleanup EXIT

restore_args=(--artifacts-path "${restore_artifacts}")
msbuild_args=(
  "-p:ProGpuDependencyMode=Package"
  "-p:ProGpuAvaloniaVersion=${avalonia_version}"
  "-p:ProGpuRuntimeVersion=${runtime_version}"
  "-p:ProGpuIntegrationVersion=${integration_version}"
)

if [[ -n "${PROGPU_PACKAGE_SOURCE:-}" ]]; then
  "${dotnet}" new nugetconfig --output "${restore_root}" --force >/dev/null
  "${dotnet}" nuget add source "${PROGPU_PACKAGE_SOURCE}" \
    --name progpu-local \
    --configfile "${restore_root}/nuget.config" >/dev/null
  restore_args+=(--configfile "${restore_root}/nuget.config")
  msbuild_args+=("-p:RestorePackagesPath=${restore_root}/packages")
elif [[ -n "${PROGPU_RESTORE_PACKAGES_PATH:-}" ]]; then
  msbuild_args+=("-p:RestorePackagesPath=${PROGPU_RESTORE_PACKAGES_PATH}")
fi

mkdir -p "${package_output}"

for package_id in "${progpu_avalonia_package_ids[@]}"; do
  rm -f \
    "${package_output}/${package_id}.${integration_version}.nupkg" \
    "${package_output}/${package_id}.${integration_version}.snupkg"
done

echo "Packing ProGPU Avalonia ${integration_version} against Avalonia ${avalonia_version} and ProGPU ${runtime_version}..."
for index in "${!progpu_avalonia_package_ids[@]}"; do
  package_id="${progpu_avalonia_package_ids[$index]}"
  project="${repo_root}/${progpu_avalonia_package_projects[$index]}"

  restore_command=("${dotnet}" restore "${project}")
  if [[ "${#restore_args[@]}" -gt 0 ]]; then
    restore_command+=("${restore_args[@]}")
  fi
  restore_command+=("${msbuild_args[@]}" --verbosity minimal)
  "${restore_command[@]}"

  "${dotnet}" pack "${project}" \
    --configuration "${configuration}" \
    --output "${package_output}" \
    --artifacts-path "${restore_artifacts}" \
    --no-restore \
    --verbosity minimal \
    "${msbuild_args[@]}" \
    -p:ContinuousIntegrationBuild=true \
    -p:IncludeSymbols=true \
    -p:SymbolPackageFormat=snupkg \
    -p:Version="${integration_version}" \
    -p:PackageVersion="${integration_version}"

  for extension in nupkg snupkg; do
    artifact="${package_output}/${package_id}.${integration_version}.${extension}"
    if [[ ! -f "${artifact}" ]]; then
      echo "Expected package was not produced: ${artifact}" >&2
      exit 1
    fi
  done
done

is_expected_artifact() {
  local file_name="$1"
  local package_id
  local extension

  for package_id in "${progpu_avalonia_package_ids[@]}"; do
    for extension in nupkg snupkg; do
      if [[ "${file_name}" == "${package_id}.${integration_version}.${extension}" ]]; then
        return 0
      fi
    done
  done

  return 1
}

unexpected_artifact_found=0
while IFS= read -r -d '' artifact; do
  if ! is_expected_artifact "$(basename "${artifact}")"; then
    echo "Unexpected integration package artifact: ${artifact}" >&2
    unexpected_artifact_found=1
  fi
done < <(find "${package_output}" -maxdepth 1 -type f \
  \( -name "*.${integration_version}.nupkg" -o -name "*.${integration_version}.snupkg" \) -print0)

if [[ "${unexpected_artifact_found}" -ne 0 ]]; then
  exit 1
fi

echo "ProGPU Avalonia package build succeeded: ${package_output}"
