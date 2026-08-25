#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_dir="$repo_root/artifacts/deployment"
publish_dir="$repo_root/artifacts/publish"
revision="${KOI_BUILD_REVISION:-$(git -C "$repo_root" rev-parse HEAD)}"
phase="${1:-all}"

if [[ ! "$phase" =~ ^(all|restore|test|package)$ ]]; then
  echo "Usage: $0 [all|restore|test|package]" >&2
  exit 1
fi

required_commands=(dotnet)
if [[ "$phase" == "all" || "$phase" == "package" ]]; then
  required_commands+=(az zip)
fi
for command_name in "${required_commands[@]}"; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Missing required command: $command_name" >&2
    exit 1
  fi
done

if [[ "$phase" != "restore" && ! "$revision" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "KOI_BUILD_REVISION must be a full 40-character Git commit SHA." >&2
  exit 1
fi

restore_solution() {
  dotnet restore "$repo_root/Koi.slnx" --locked-mode --nologo
}

test_solution() {
  dotnet test "$repo_root/Koi.slnx" \
    --configuration Release \
    --no-restore \
    --nologo \
    --logger "console;verbosity=normal" \
    -p:SourceRevisionId="$revision"
}

package_deployment() {
  rm -rf "$artifacts_dir" "$publish_dir"
  mkdir -p "$artifacts_dir" "$publish_dir"

  dotnet publish "$repo_root/src/Koi.Functions/Koi.Functions.csproj" \
    --configuration Release \
    --no-restore \
    --nologo \
    --output "$publish_dir" \
    -p:SourceRevisionId="$revision"

  (
    cd "$publish_dir"
    zip -q -r "$artifacts_dir/koi-functions.zip" .
  )

  az bicep build \
    --file "$repo_root/infra/bootstrap.bicep" \
    --outfile "$artifacts_dir/bootstrap.json"
  az bicep build \
    --file "$repo_root/infra/main.bicep" \
    --outfile "$artifacts_dir/main.json"

  echo "Built KOI revision $revision"
  echo "Function package: $artifacts_dir/koi-functions.zip"
  echo "Infrastructure template: $artifacts_dir/main.json"
}

case "$phase" in
  restore)
    restore_solution
    ;;
  test)
    test_solution
    ;;
  package)
    package_deployment
    ;;
  all)
    restore_solution
    test_solution
    package_deployment
    ;;
esac
