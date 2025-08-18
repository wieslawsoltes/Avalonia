# XAML Debug Metadata Scanner

A command-line tool for scanning .NET assemblies and their PDB files to extract and display XAML-related debug metadata that Avalonia emits during XAML compilation.

## What it does

This tool analyzes:
- **Documents**: XAML files (.xaml/.axaml) referenced in debug information
- **Sequence Points**: Debugging breakpoint locations mapped to XAML source lines
- **Methods**: XAML-compiled methods (e.g., `InitializeComponent`, XamlIL-generated methods)
- **Local Variables**: Variables created during XAML compilation process

## Features

- ✅ Supports both embedded and external Portable PDB files
- ✅ Recursive directory scanning
- ✅ Detailed metadata extraction and reporting
- ✅ XAML-specific filtering to focus on relevant debug information
- ✅ Summary statistics
- ✅ Verbose mode for comprehensive output

## Usage

```bash
# Scan a single directory
XamlDebugMetadataScanner --path /path/to/assemblies

# Scan with verbose output
XamlDebugMetadataScanner --path /path/to/assemblies --recursive --verbose

# Output as JSON for automation/integration
XamlDebugMetadataScanner --path /path/to/assemblies --json

# Short form
XamlDebugMetadataScanner -p /path/to/assemblies -r -v -j
```

## Options

- `-p, --path` - **Required**. Path to directory containing assemblies to scan
- `-r, --recursive` - Scan subdirectories recursively (default: false)
- `-v, --verbose` - Show verbose output including non-XAML files (default: false)
- `-j, --json` - Output results in JSON format for programmatic consumption (default: false)

## Example Output

```
🎯 Sandbox.dll (Embedded Portable PDB)

  📄 XAML Documents (2):
     • /Users/user/Avalonia/samples/Sandbox/App.axaml
       Language: C#
       Hash: SHA1 - A1B2C3D4E5F6...
     • /Users/user/Avalonia/samples/Sandbox/MainWindow.axaml
       Language: C#
       Hash: SHA1 - F6E5D4C3B2A1...

  🔧 XAML-related Methods (15):
     • Sandbox.App.InitializeComponent()
     • Sandbox.MainWindow.InitializeComponent()
     • Sandbox.App.!XamlIlPopulate()
     • Sandbox.MainWindow.!XamlIlPopulate()
     ... and 11 more

  📍 XAML Sequence Points (23):
     • /Users/user/Avalonia/samples/Sandbox/App.axaml:1:1
       Method: Sandbox.App.InitializeComponent
       Range: Line 1-1, Col 1-45
     ... and 22 more

  🔤 XAML-related Variables (8):
     • !XamlIlContext (Index: 0)
     • !parentStackProvider (Index: 1)
     ... and 6 more

────────────────────────────────────────────────────────────────────────────────

📊 Summary:
   Total assemblies scanned: 25
   Assemblies with XAML debug info: 3
```

## JSON Output

When using the `--json` flag, the tool outputs structured data perfect for automation:

```json
{
  "totalAssemblies": 30,
  "assembliesWithXamlDebugInfo": 7,
  "results": [
    {
      "assemblyName": "Sandbox.dll",
      "assemblyPath": "/path/to/Sandbox.dll",
      "pdbType": "External Portable PDB",
      "xamlDocuments": [
        {
          "name": "/path/to/App.axaml",
          "isXamlFile": true,
          "language": "00000000-0000-0000-0000-000000000000",
          "hashAlgorithm": "SHA1",
          "hash": "ZU1ZDh/Rf7E/bkJMwezlT..."
        }
      ],
      "xamlMethods": [
        {
          "name": "InitializeComponent",
          "fullName": "Sandbox.MainWindow.InitializeComponent",
          "isXamlRelated": true,
          "typeName": "MainWindow",
          "namespace": "Sandbox"
        }
      ],
      "xamlSequencePoints": [...],
      "xamlVariables": [...]
    }
  ]
}
```

## Building

```bash
cd src/tools/XamlDebugMetadataScanner
dotnet build
```

## Running on Avalonia Samples

```bash
# Build and scan the Sandbox sample
dotnet build samples/Sandbox/Sandbox.csproj
dotnet run --project src/tools/XamlDebugMetadataScanner -- --path samples/Sandbox/bin/Debug/net8.0

# Or scan all samples
dotnet run --project src/tools/XamlDebugMetadataScanner -- --path samples --recursive
```

## Technical Details

The tool uses the `System.Reflection.Metadata` library to:

1. **Read PE files** (.dll/.exe) and locate associated PDB files
2. **Parse Portable PDB** metadata (both embedded and external)
3. **Extract debug information** including:
   - Document references (source files)
   - Sequence points (line mappings)
   - Local scopes and variables
   - Method definitions
4. **Filter XAML-related data** based on file extensions, method names, and naming patterns
5. **Present organized output** with clear categorization

### XAML Detection Logic

The tool identifies XAML-related debug information by looking for:

- **Files**: `.xaml` and `.axaml` extensions
- **Methods**: Names containing `InitializeComponent`, `!XamlIl`, or in types with `XamlIl` suffix
- **Variables**: Names containing `xaml`, `XamlIl`, or starting with `!`
- **Types**: Class names ending with `_AvaloniaXaml` or containing `XamlIl`

### PDB Support

- ✅ **Portable PDB** (embedded and external) - Full support
- ❌ **Legacy/Native PDB** - Not supported (requires different APIs)

## Use Cases

- **Debugging XAML compilation issues** - See exactly what debug info is emitted
- **Verifying XAML builds** - Ensure debug symbols are properly generated  
- **Performance analysis** - Understand the scope of generated XAML code
- **Development tooling** - Build IDE features that leverage XAML debug metadata
- **Education** - Learn how Avalonia's XAML compiler works under the hood
