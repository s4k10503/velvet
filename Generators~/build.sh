#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

CONFIGURATION="${CONFIGURATION:-Release}"
PROJECT="src/Velvet.SourceGenerators/Velvet.SourceGenerators.csproj"
OUTPUT_DLL="src/Velvet.SourceGenerators/bin/${CONFIGURATION}/netstandard2.0/Velvet.SourceGenerators.dll"
DEPLOY_DIR="../Runtime/Plugins/Generators"

CODEFIX_PROJECT="src/Velvet.SourceGenerators.CodeFixes/Velvet.SourceGenerators.CodeFixes.csproj"
CODEFIX_DLL="src/Velvet.SourceGenerators.CodeFixes/bin/${CONFIGURATION}/netstandard2.0/Velvet.SourceGenerators.CodeFixes.dll"
CODEFIX_DEPLOY_DIR="../Runtime/Plugins/Analyzers"

echo "[Velvet.SourceGenerators] dotnet build -c ${CONFIGURATION}"
dotnet build "${PROJECT}" -c "${CONFIGURATION}" --nologo

mkdir -p "${DEPLOY_DIR}"
cp -f "${OUTPUT_DLL}" "${DEPLOY_DIR}/Velvet.SourceGenerators.dll"
echo "[Velvet.SourceGenerators] Deployed to ${DEPLOY_DIR}/Velvet.SourceGenerators.dll"

echo "[Velvet.SourceGenerators.CodeFixes] dotnet build -c ${CONFIGURATION}"
dotnet build "${CODEFIX_PROJECT}" -c "${CONFIGURATION}" --nologo

mkdir -p "${CODEFIX_DEPLOY_DIR}"
cp -f "${CODEFIX_DLL}" "${CODEFIX_DEPLOY_DIR}/Velvet.SourceGenerators.CodeFixes.dll"
echo "[Velvet.SourceGenerators.CodeFixes] Deployed to ${CODEFIX_DEPLOY_DIR}/Velvet.SourceGenerators.CodeFixes.dll"
