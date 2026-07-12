#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="${repo_root}/integration/ProGpuPackageApp/ProGpuPackageApp.csproj"
mode="${1:-nuget}"
configuration="${PROGPU_CONFIGURATION:-Release}"
integration_version="${PROGPU_INTEGRATION_PACKAGE_VERSION:-12.0.5-preview.9}"
working_directory="$(mktemp -d "${TMPDIR:-/tmp}/progpu-package-app.XXXXXX")"
consumer_artifacts="${working_directory}/artifacts"

if [[ "$#" -gt 0 ]]; then
  shift
fi

cleanup() {
  local exit_code=$?
  rm -rf "${working_directory}"
  return "${exit_code}"
}
trap cleanup EXIT

dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

"${dotnet}" new nugetconfig --output "${working_directory}" --force >/dev/null

case "${mode}" in
  local)
    runtime_package_source="${PROGPU_PACKAGE_SOURCE:-${repo_root}/../ProGPU/artifacts/packages/${configuration}}"
    if [[ ! -d "${runtime_package_source}" ]]; then
      echo "Local ProGPU package source was not found: ${runtime_package_source}" >&2
      echo "Pack ProGPU first or set PROGPU_PACKAGE_SOURCE to its package output directory." >&2
      exit 1
    fi

    NUGET_HTTP_CACHE_PATH="${working_directory}/pack-http-cache" \
    PROGPU_RESTORE_PACKAGES_PATH="${working_directory}/pack-packages" \
    PROGPU_PACKAGE_SOURCE="${runtime_package_source}" \
    PROGPU_INTEGRATION_VERSION="${integration_version}" \
      "${repo_root}/scripts/progpu-pack.sh"
    "${dotnet}" nuget remove source nuget \
      --configfile "${working_directory}/nuget.config" >/dev/null
    "${dotnet}" nuget add source "${repo_root}/artifacts/packages/${configuration}" \
      --name progpu-avalonia-local \
      --configfile "${working_directory}/nuget.config" >/dev/null
    "${dotnet}" nuget add source "${runtime_package_source}" \
      --name progpu-runtime-local \
      --configfile "${working_directory}/nuget.config" >/dev/null
    "${dotnet}" nuget add source https://api.nuget.org/v3/index.json \
      --name nuget \
      --configfile "${working_directory}/nuget.config" >/dev/null
    ;;
  nuget)
    ;;
  *)
    echo "Usage: $0 [local|nuget] [application arguments...]" >&2
    exit 2
    ;;
esac

export NUGET_HTTP_CACHE_PATH="${working_directory}/http-cache"
packages_path="${working_directory}/packages"

"${dotnet}" restore "${project}" \
  --packages "${packages_path}" \
  --artifacts-path "${consumer_artifacts}" \
  --configfile "${working_directory}/nuget.config" \
  --force \
  --no-cache \
  --verbosity minimal \
  "-p:ProGpuIntegrationPackageVersion=${integration_version}"

if [[ "${PROGPU_INTEGRATION_BUILD_ONLY:-0}" == 1 ]]; then
  "${dotnet}" build "${project}" \
    --configuration "${configuration}" \
    --artifacts-path "${consumer_artifacts}" \
    --no-restore \
    --verbosity minimal \
    "-p:RestorePackagesPath=${packages_path}" \
    "-p:ProGpuIntegrationPackageVersion=${integration_version}"
else
  "${dotnet}" run \
    --project "${project}" \
    --configuration "${configuration}" \
    --artifacts-path "${consumer_artifacts}" \
    --no-restore \
    "-p:RestorePackagesPath=${packages_path}" \
    "-p:ProGpuIntegrationPackageVersion=${integration_version}" \
    -- "$@"
fi
