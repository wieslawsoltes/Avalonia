#!/bin/bash

# Script to build and run the XAML Debug Metadata Scanner

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
AVALONIA_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

echo "Building XAML Debug Metadata Scanner..."
cd "$PROJECT_DIR"
dotnet build

echo ""
echo "XAML Debug Metadata Scanner built successfully!"
echo ""
echo "Usage examples:"
echo "  # Scan Sandbox sample:"
echo "  dotnet run -- --path \"$AVALONIA_ROOT/samples/Sandbox/bin/Debug/net8.0\""
echo ""
echo "  # Scan all samples recursively:"
echo "  dotnet run -- --path \"$AVALONIA_ROOT/samples\" --recursive"
echo ""
echo "  # Scan with verbose output:"
echo "  dotnet run -- --path \"$AVALONIA_ROOT/samples/Sandbox/bin/Debug/net8.0\" --verbose"
echo ""
echo "  # Output as JSON for automation:"
echo "  dotnet run -- --path \"$AVALONIA_ROOT/samples/Sandbox/bin/Debug/net8.0\" --json"
echo ""
echo "  # Scan single project output:"
echo "  dotnet run -- --path \"path/to/your/project/bin/Debug/net8.0\""
echo ""
echo "For more options, run: dotnet run -- --help"
