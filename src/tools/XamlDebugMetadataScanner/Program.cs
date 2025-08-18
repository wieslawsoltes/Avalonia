using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace XamlDebugMetadataScanner;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Scans assemblies for XAML debug metadata embedded in PDB files");

        var pathOption = new Option<DirectoryInfo>(
            name: "--path",
            description: "Path to directory containing assemblies to scan")
        {
            IsRequired = true
        };
        pathOption.AddAlias("-p");

        var recursiveOption = new Option<bool>(
            name: "--recursive",
            description: "Scan subdirectories recursively",
            getDefaultValue: () => false);
        recursiveOption.AddAlias("-r");

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Show verbose output including non-XAML files",
            getDefaultValue: () => false);
        verboseOption.AddAlias("-v");

        var jsonOption = new Option<bool>(
            name: "--json",
            description: "Output results in JSON format",
            getDefaultValue: () => false);
        jsonOption.AddAlias("-j");

        var xamlFileOption = new Option<string?>(
            name: "--xaml-file",
            description: "Specific XAML file to analyze for control-to-position mapping (e.g., MainWindow.axaml)")
        {
            IsRequired = false
        };
        xamlFileOption.AddAlias("-x");

        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(recursiveOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(jsonOption);
        rootCommand.AddOption(xamlFileOption);

        rootCommand.SetHandler((DirectoryInfo path, bool recursive, bool verbose, bool json, string? xamlFile) =>
        {
            if (!string.IsNullOrEmpty(xamlFile))
            {
                AnalyzeXamlFileMapping(path, xamlFile, json);
            }
            else
            {
                ScanDirectory(path, recursive, verbose, json);
            }
        }, pathOption, recursiveOption, verboseOption, jsonOption, xamlFileOption);

        return await rootCommand.InvokeAsync(args);
    }

    static void ScanDirectory(DirectoryInfo directory, bool recursive, bool verbose, bool json)
    {
        if (!directory.Exists)
        {
            Console.WriteLine($"Error: Directory '{directory.FullName}' does not exist.");
            return;
        }

        if (!json)
        {
            Console.WriteLine($"Scanning directory: {directory.FullName}");
            Console.WriteLine($"Recursive: {recursive}");
            Console.WriteLine();
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var assemblyFiles = directory.GetFiles("*.dll", searchOption)
            .Concat(directory.GetFiles("*.exe", searchOption))
            .Where(f => !f.Name.Contains(".resources."))
            .OrderBy(f => f.FullName);

        int totalAssemblies = 0;
        int assembliesWithXamlDebugInfo = 0;
        var results = new List<AssemblyXamlInfo>();

        foreach (var assemblyFile in assemblyFiles)
        {
            totalAssemblies++;
            
            try
            {
                var pdbFile = new FileInfo(Path.ChangeExtension(assemblyFile.FullName, ".pdb"));
                
                if (!pdbFile.Exists)
                {
                    if (verbose && !json)
                        Console.WriteLine($"⚠️  No PDB file found for {assemblyFile.Name}");
                    continue;
                }

                var xamlInfo = ScanAssemblyForXamlDebugInfo(assemblyFile, pdbFile, verbose);
                
                if (xamlInfo.HasXamlDebugInfo)
                {
                    assembliesWithXamlDebugInfo++;
                    var assemblyInfo = new AssemblyXamlInfo
                    {
                        AssemblyName = assemblyFile.Name,
                        AssemblyPath = assemblyFile.FullName,
                        PdbType = xamlInfo.PdbType,
                        XamlDocuments = xamlInfo.Documents.Where(d => d.IsXamlFile).ToList(),
                        XamlMethods = xamlInfo.Methods.Where(m => m.IsXamlRelated).ToList(),
                        XamlSequencePoints = xamlInfo.SequencePoints.Where(s => s.IsXamlRelated).ToList(),
                        XamlVariables = xamlInfo.LocalScopes.SelectMany(s => s.Variables)
                            .Where(v => v.Name.Contains("xaml") || v.Name.Contains("XamlIl") || v.Name.StartsWith("!"))
                            .ToList()
                    };
                    results.Add(assemblyInfo);
                    
                    if (!json)
                        PrintXamlDebugInfo(assemblyFile.Name, xamlInfo);
                }
                else if (verbose && !json)
                {
                    Console.WriteLine($"ℹ️  No XAML debug info found in {assemblyFile.Name}");
                }
            }
            catch (Exception ex)
            {
                if (!json)
                {
                    Console.WriteLine($"❌ Error scanning {assemblyFile.Name}: {ex.Message}");
                    if (verbose)
                        Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                }
            }
        }

        var summary = new ScanSummary
        {
            TotalAssemblies = totalAssemblies,
            AssembliesWithXamlDebugInfo = assembliesWithXamlDebugInfo,
            Results = results
        };

        if (json)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            Console.WriteLine(JsonSerializer.Serialize(summary, options));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"📊 Summary:");
            Console.WriteLine($"   Total assemblies scanned: {totalAssemblies}");
            Console.WriteLine($"   Assemblies with XAML debug info: {assembliesWithXamlDebugInfo}");
        }
    }

    static void AnalyzeXamlFileMapping(DirectoryInfo directory, string xamlFileName, bool json)
    {
        if (!directory.Exists)
        {
            Console.WriteLine($"Error: Directory '{directory.FullName}' does not exist.");
            return;
        }

        var assemblyFiles = directory.GetFiles("*.dll", SearchOption.TopDirectoryOnly)
            .Concat(directory.GetFiles("*.exe", SearchOption.TopDirectoryOnly))
            .Where(f => !f.Name.Contains(".resources."))
            .OrderBy(f => f.FullName);

        var mappingResults = new List<XamlControlMapping>();
        string? targetXamlPath = null;

        if (!json)
        {
            Console.WriteLine($"🔍 Analyzing XAML file mapping for: {xamlFileName}");
            Console.WriteLine($"Directory: {directory.FullName}");
            Console.WriteLine();
        }

        foreach (var assemblyFile in assemblyFiles)
        {
            try
            {
                var pdbFile = new FileInfo(Path.ChangeExtension(assemblyFile.FullName, ".pdb"));
                
                if (!pdbFile.Exists)
                    continue;

                var xamlInfo = ScanAssemblyForXamlDebugInfo(assemblyFile, pdbFile, false);
                
                // Find the target XAML file
                var targetDocument = xamlInfo.Documents.FirstOrDefault(d => 
                    d.IsXamlFile && Path.GetFileName(d.Name).Equals(xamlFileName, StringComparison.OrdinalIgnoreCase));

                if (targetDocument == null)
                    continue;

                targetXamlPath = targetDocument.Name;

                // Get sequence points for this XAML file
                var xamlSequencePoints = xamlInfo.SequencePoints
                    .Where(sp => sp.DocumentName.Equals(targetDocument.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(sp => sp.StartLine)
                    .ThenBy(sp => sp.StartColumn)
                    .ToList();

                // Group sequence points by method to identify control creation patterns
                var methodGroups = xamlSequencePoints
                    .GroupBy(sp => sp.MethodName)
                    .Where(g => g.Key.Contains("!XamlIl") || g.Key.Contains("InitializeComponent"))
                    .ToList();

                foreach (var methodGroup in methodGroups)
                {
                    var controlMappings = AnalyzeControlCreationPattern(methodGroup.Key, methodGroup.ToList(), targetDocument.Name);
                    mappingResults.AddRange(controlMappings);
                }

                if (!json && xamlSequencePoints.Any())
                {
                    PrintXamlFileMapping(assemblyFile.Name, targetDocument, xamlSequencePoints, mappingResults);
                }
            }
            catch (Exception ex)
            {
                if (!json)
                    Console.WriteLine($"❌ Error analyzing {assemblyFile.Name}: {ex.Message}");
            }
        }

        if (json)
        {
            var result = new XamlFileMappingResult
            {
                XamlFileName = xamlFileName,
                XamlFilePath = targetXamlPath,
                ControlMappings = mappingResults
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            Console.WriteLine(JsonSerializer.Serialize(result, options));
        }
        else if (mappingResults.Count == 0)
        {
            Console.WriteLine($"❌ No XAML file '{xamlFileName}' found or no debug information available.");
            Console.WriteLine("Make sure the assembly was built with debug information and contains the specified XAML file.");
        }
    }

    static List<XamlControlMapping> AnalyzeControlCreationPattern(string methodName, List<SequencePointInfo> sequencePoints, string xamlFile)
    {
        var mappings = new List<XamlControlMapping>();
        
        // Group sequence points that are close together (likely the same control)
        var controlGroups = new List<List<SequencePointInfo>>();
        List<SequencePointInfo>? currentGroup = null;
        
        foreach (var sp in sequencePoints)
        {
            if (currentGroup == null || 
                sp.StartLine - currentGroup.Last().StartLine > 2 || 
                (sp.StartLine == currentGroup.Last().StartLine && sp.StartColumn - currentGroup.Last().StartColumn > 50))
            {
                currentGroup = new List<SequencePointInfo>();
                controlGroups.Add(currentGroup);
            }
            currentGroup.Add(sp);
        }

        // Analyze each group to determine control type and properties
        foreach (var group in controlGroups)
        {
            var primarySequencePoint = group.First();
            var controlType = InferControlTypeFromSequencePoints(group, methodName);
            var properties = ExtractPropertiesFromSequencePoints(group);

            var mapping = new XamlControlMapping
            {
                ControlType = controlType,
                XamlFile = xamlFile,
                StartLine = primarySequencePoint.StartLine,
                StartColumn = primarySequencePoint.StartColumn,
                EndLine = group.Last().EndLine,
                EndColumn = group.Last().EndColumn,
                Method = methodName,
                Properties = properties,
                SequencePointCount = group.Count
            };

            mappings.Add(mapping);
        }

        return mappings;
    }

    static string InferControlTypeFromSequencePoints(List<SequencePointInfo> sequencePoints, string methodName)
    {
        // Try to infer control type from method name patterns or sequence point positions
        if (methodName.Contains("Button"))
            return "Button";
        if (methodName.Contains("TextBox"))
            return "TextBox";
        if (methodName.Contains("Grid"))
            return "Grid";
        if (methodName.Contains("StackPanel"))
            return "StackPanel";
        if (methodName.Contains("Border"))
            return "Border";

        // Look at the sequence point pattern to infer control type
        var lineSpan = sequencePoints.Last().EndLine - sequencePoints.First().StartLine;
        var sequenceCount = sequencePoints.Count;

        if (sequenceCount == 1)
            return "SimpleControl";
        else if (sequenceCount < 5)
            return "BasicControl";
        else if (sequenceCount < 15)
            return "ContainerControl";
        else
            return "ComplexControl";
    }

    static List<string> ExtractPropertiesFromSequencePoints(List<SequencePointInfo> sequencePoints)
    {
        var properties = new List<string>();
        
        // Analyze the sequence point distribution to infer likely properties
        var columnPositions = sequencePoints.Select(sp => sp.StartColumn).Distinct().OrderBy(c => c).ToList();
        var linePositions = sequencePoints.Select(sp => sp.StartLine).Distinct().OrderBy(l => l).ToList();

        // Add inferred properties based on patterns
        if (columnPositions.Count > 3)
            properties.Add("Multiple attributes detected");
        
        if (linePositions.Count > 1)
            properties.Add("Multi-line definition");
            
        if (sequencePoints.Any(sp => sp.StartColumn < 10))
            properties.Add("Root-level element");
        else if (sequencePoints.All(sp => sp.StartColumn > 20))
            properties.Add("Nested element");

        return properties;
    }

    static void PrintXamlFileMapping(string assemblyName, DocumentInfo xamlDocument, List<SequencePointInfo> sequencePoints, List<XamlControlMapping> controlMappings)
    {
        Console.WriteLine($"🎯 {assemblyName}");
        Console.WriteLine($"📄 XAML File: {Path.GetFileName(xamlDocument.Name)}");
        Console.WriteLine($"   Full Path: {xamlDocument.Name}");
        Console.WriteLine($"   Hash: {xamlDocument.HashAlgorithm} - {Convert.ToHexString(xamlDocument.Hash)}");
        Console.WriteLine();

        Console.WriteLine($"🎨 Control Mappings ({controlMappings.Count} controls detected):");
        foreach (var mapping in controlMappings.OrderBy(m => m.StartLine).ThenBy(m => m.StartColumn))
        {
            Console.WriteLine($"   📍 Line {mapping.StartLine}:{mapping.StartColumn}-{mapping.EndLine}:{mapping.EndColumn}");
            Console.WriteLine($"      Type: {mapping.ControlType}");
            Console.WriteLine($"      Method: {mapping.Method}");
            Console.WriteLine($"      Sequence Points: {mapping.SequencePointCount}");
            if (mapping.Properties.Any())
            {
                Console.WriteLine($"      Properties: {string.Join(", ", mapping.Properties)}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"📊 Detailed Sequence Points ({sequencePoints.Count} total):");
        var methodGroups = sequencePoints.GroupBy(sp => sp.MethodName).Take(3); // Limit for readability
        
        foreach (var methodGroup in methodGroups)
        {
            Console.WriteLine($"   🔧 {methodGroup.Key}:");
            foreach (var sp in methodGroup.Take(10)) // Limit sequence points per method
            {
                Console.WriteLine($"      • Line {sp.StartLine}:{sp.StartColumn}-{sp.EndLine}:{sp.EndColumn} (Offset: {sp.Offset})");
            }
            if (methodGroup.Count() > 10)
                Console.WriteLine($"      ... and {methodGroup.Count() - 10} more sequence points");
            Console.WriteLine();
        }

        Console.WriteLine(new string('─', 80));
        Console.WriteLine();
    }

    static XamlDebugInfo ScanAssemblyForXamlDebugInfo(FileInfo assemblyFile, FileInfo pdbFile, bool verbose)
    {
        var debugInfo = new XamlDebugInfo();

        using var assemblyStream = File.OpenRead(assemblyFile.FullName);
        using var pdbStream = File.OpenRead(pdbFile.FullName);
        
        using var peReader = new PEReader(assemblyStream);
        var metadataReader = peReader.GetMetadataReader();

        // Check if embedded PDB exists
        var debugDirectoryEntries = peReader.ReadDebugDirectory();
        var embeddedPdbEntry = debugDirectoryEntries.FirstOrDefault(d => d.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        
        MetadataReader? pdbReader = null;
        
        if (embeddedPdbEntry.DataSize > 0)
        {
            // Use embedded PDB
            var embeddedPdbProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdbEntry);
            pdbReader = embeddedPdbProvider.GetMetadataReader();
            debugInfo.PdbType = "Embedded Portable PDB";
        }
        else
        {
            // Try to read external PDB
            try
            {
                var pdbReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                pdbReader = pdbReaderProvider.GetMetadataReader();
                debugInfo.PdbType = "External Portable PDB";
            }
            catch
            {
                debugInfo.PdbType = "Legacy/Native PDB (not supported)";
                return debugInfo;
            }
        }

        if (pdbReader == null)
            return debugInfo;

        // Scan for XAML-related debug information
        debugInfo.Documents = ScanDocuments(pdbReader);
        debugInfo.SequencePoints = ScanSequencePoints(pdbReader, metadataReader);
        debugInfo.LocalScopes = ScanLocalScopes(pdbReader);
        debugInfo.Methods = ScanMethods(metadataReader, pdbReader);

        // Determine if this assembly has XAML debug info
        debugInfo.HasXamlDebugInfo = debugInfo.Documents.Any(d => d.IsXamlFile) ||
                                   debugInfo.SequencePoints.Any(s => s.IsXamlRelated) ||
                                   debugInfo.Methods.Any(m => m.IsXamlRelated);

        return debugInfo;
    }

    static List<DocumentInfo> ScanDocuments(MetadataReader pdbReader)
    {
        var documents = new List<DocumentInfo>();

        foreach (var docHandle in pdbReader.Documents)
        {
            var document = pdbReader.GetDocument(docHandle);
            var name = pdbReader.GetString(document.Name);
            
            var docInfo = new DocumentInfo
            {
                Name = name,
                IsXamlFile = name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                           name.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase),
                Language = GetLanguageFromGuid(pdbReader.GetGuid(document.Language)),
                HashAlgorithm = GetHashAlgorithmName(pdbReader.GetGuid(document.HashAlgorithm)),
                Hash = pdbReader.GetBlobBytes(document.Hash)
            };

            documents.Add(docInfo);
        }

        return documents;
    }

    static List<SequencePointInfo> ScanSequencePoints(MetadataReader pdbReader, MetadataReader metadataReader)
    {
        var sequencePoints = new List<SequencePointInfo>();

        foreach (var methodDebugInfoHandle in pdbReader.MethodDebugInformation)
        {
            var methodDebugInfo = pdbReader.GetMethodDebugInformation(methodDebugInfoHandle);
            var sequencePointCollection = methodDebugInfo.GetSequencePoints();

            foreach (var sequencePoint in sequencePointCollection)
            {
                if (sequencePoint.Document.IsNil)
                    continue;

                var document = pdbReader.GetDocument(sequencePoint.Document);
                var documentName = pdbReader.GetString(document.Name);

                var method = metadataReader.GetMethodDefinition(methodDebugInfoHandle.ToDefinitionHandle());
                var methodName = metadataReader.GetString(method.Name);
                var typeDef = metadataReader.GetTypeDefinition(method.GetDeclaringType());
                var typeName = metadataReader.GetString(typeDef.Name);

                var spInfo = new SequencePointInfo
                {
                    DocumentName = documentName,
                    IsXamlRelated = documentName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                  documentName.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                                  methodName.Contains("!XamlIl") ||
                                  methodName.Contains("InitializeComponent") ||
                                  typeName.Contains("XamlIl"),
                    MethodName = $"{typeName}.{methodName}",
                    StartLine = sequencePoint.StartLine,
                    StartColumn = sequencePoint.StartColumn,
                    EndLine = sequencePoint.EndLine,
                    EndColumn = sequencePoint.EndColumn,
                    Offset = sequencePoint.Offset
                };

                sequencePoints.Add(spInfo);
            }
        }

        return sequencePoints;
    }

    static List<LocalScopeInfo> ScanLocalScopes(MetadataReader pdbReader)
    {
        var localScopes = new List<LocalScopeInfo>();

        foreach (var scopeHandle in pdbReader.LocalScopes)
        {
            var scope = pdbReader.GetLocalScope(scopeHandle);
            
            var scopeInfo = new LocalScopeInfo
            {
                StartOffset = scope.StartOffset,
                Length = scope.Length,
                Variables = new List<LocalVariableInfo>()
            };

            foreach (var variableHandle in scope.GetLocalVariables())
            {
                var variable = pdbReader.GetLocalVariable(variableHandle);
                var variableName = pdbReader.GetString(variable.Name);
                
                scopeInfo.Variables.Add(new LocalVariableInfo
                {
                    Name = variableName,
                    Index = variable.Index,
                    Attributes = variable.Attributes
                });
            }

            localScopes.Add(scopeInfo);
        }

        return localScopes;
    }

    static List<MethodInfo> ScanMethods(MetadataReader metadataReader, MetadataReader pdbReader)
    {
        var methods = new List<MethodInfo>();

        foreach (var methodHandle in metadataReader.MethodDefinitions)
        {
            var method = metadataReader.GetMethodDefinition(methodHandle);
            var methodName = metadataReader.GetString(method.Name);
            var typeDef = metadataReader.GetTypeDefinition(method.GetDeclaringType());
            var typeName = metadataReader.GetString(typeDef.Name);
            var namespaceName = typeDef.Namespace.IsNil ? "" : metadataReader.GetString(typeDef.Namespace);

            var methodInfo = new MethodInfo
            {
                Name = methodName,
                FullName = $"{namespaceName}.{typeName}.{methodName}",
                IsXamlRelated = methodName.Contains("!XamlIl") ||
                              methodName.Contains("InitializeComponent") ||
                              methodName.StartsWith("!") ||
                              typeName.Contains("XamlIl") ||
                              typeName.EndsWith("_AvaloniaXaml"),
                TypeName = typeName,
                Namespace = namespaceName
            };

            methods.Add(methodInfo);
        }

        return methods;
    }

    static void PrintXamlDebugInfo(string assemblyName, XamlDebugInfo debugInfo)
    {
        Console.WriteLine($"🎯 {assemblyName} ({debugInfo.PdbType})");
        Console.WriteLine();

        // XAML Documents
        var xamlDocs = debugInfo.Documents.Where(d => d.IsXamlFile).ToList();
        if (xamlDocs.Any())
        {
            Console.WriteLine($"  📄 XAML Documents ({xamlDocs.Count}):");
            foreach (var doc in xamlDocs)
            {
                Console.WriteLine($"     • {doc.Name}");
                Console.WriteLine($"       Language: {doc.Language}");
                Console.WriteLine($"       Hash: {doc.HashAlgorithm} - {Convert.ToHexString(doc.Hash)}");
            }
            Console.WriteLine();
        }

        // XAML-related Methods
        var xamlMethods = debugInfo.Methods.Where(m => m.IsXamlRelated).ToList();
        if (xamlMethods.Any())
        {
            Console.WriteLine($"  🔧 XAML-related Methods ({xamlMethods.Count}):");
            foreach (var method in xamlMethods.Take(10)) // Limit to first 10 to avoid spam
            {
                Console.WriteLine($"     • {method.FullName}");
            }
            if (xamlMethods.Count > 10)
                Console.WriteLine($"     ... and {xamlMethods.Count - 10} more");
            Console.WriteLine();
        }

        // XAML Sequence Points
        var xamlSequencePoints = debugInfo.SequencePoints.Where(s => s.IsXamlRelated).ToList();
        if (xamlSequencePoints.Any())
        {
            Console.WriteLine($"  📍 XAML Sequence Points ({xamlSequencePoints.Count}):");
            foreach (var sp in xamlSequencePoints.Take(5)) // Limit to first 5
            {
                Console.WriteLine($"     • {sp.DocumentName}:{sp.StartLine}:{sp.StartColumn}");
                Console.WriteLine($"       Method: {sp.MethodName}");
                Console.WriteLine($"       Range: Line {sp.StartLine}-{sp.EndLine}, Col {sp.StartColumn}-{sp.EndColumn}");
            }
            if (xamlSequencePoints.Count > 5)
                Console.WriteLine($"     ... and {xamlSequencePoints.Count - 5} more");
            Console.WriteLine();
        }

        // Local Variables in XAML scopes
        var xamlVariables = debugInfo.LocalScopes
            .SelectMany(s => s.Variables)
            .Where(v => v.Name.Contains("xaml") || v.Name.Contains("XamlIl") || v.Name.StartsWith("!"))
            .ToList();
        
        if (xamlVariables.Any())
        {
            Console.WriteLine($"  🔤 XAML-related Variables ({xamlVariables.Count}):");
            foreach (var variable in xamlVariables.Take(10))
            {
                Console.WriteLine($"     • {variable.Name} (Index: {variable.Index})");
            }
            if (xamlVariables.Count > 10)
                Console.WriteLine($"     ... and {xamlVariables.Count - 10} more");
            Console.WriteLine();
        }

        Console.WriteLine(new string('─', 80));
        Console.WriteLine();
    }

    static string GetLanguageFromGuid(Guid languageGuid)
    {
        // Known language GUIDs
        if (languageGuid == new Guid("3f5162f8-07c6-11d3-9053-00c04fa302a1"))
            return "C#";
        if (languageGuid == new Guid("3a12d0b8-c26c-11d0-b442-00a0244a1dd2"))
            return "C/C++";
        if (languageGuid == new Guid("3a12d0b7-c26c-11d0-b442-00a0244a1dd2"))
            return "VB.NET";
        if (languageGuid == new Guid("af046cd1-d0e1-11d2-977c-00a0c9b4d50c"))
            return "JavaScript";
        
        return languageGuid.ToString();
    }

    static string GetHashAlgorithmName(Guid hashAlgorithm)
    {
        if (hashAlgorithm == new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460"))
            return "SHA1";
        if (hashAlgorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16"))
            return "SHA256";
        
        return hashAlgorithm.ToString();
    }
}

// Data structures for holding debug information
public class XamlDebugInfo
{
    public bool HasXamlDebugInfo { get; set; }
    public string PdbType { get; set; } = "";
    public List<DocumentInfo> Documents { get; set; } = new();
    public List<SequencePointInfo> SequencePoints { get; set; } = new();
    public List<LocalScopeInfo> LocalScopes { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
}

public class DocumentInfo
{
    public string Name { get; set; } = "";
    public bool IsXamlFile { get; set; }
    public string Language { get; set; } = "";
    public string HashAlgorithm { get; set; } = "";
    public byte[] Hash { get; set; } = Array.Empty<byte>();
}

public class SequencePointInfo
{
    public string DocumentName { get; set; } = "";
    public bool IsXamlRelated { get; set; }
    public string MethodName { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public int Offset { get; set; }
}

public class LocalScopeInfo
{
    public int StartOffset { get; set; }
    public int Length { get; set; }
    public List<LocalVariableInfo> Variables { get; set; } = new();
}

public class LocalVariableInfo
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public LocalVariableAttributes Attributes { get; set; }
}

public class MethodInfo
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsXamlRelated { get; set; }
    public string TypeName { get; set; } = "";
    public string Namespace { get; set; } = "";
}

public class AssemblyXamlInfo
{
    public string AssemblyName { get; set; } = "";
    public string AssemblyPath { get; set; } = "";
    public string PdbType { get; set; } = "";
    public List<DocumentInfo> XamlDocuments { get; set; } = new();
    public List<MethodInfo> XamlMethods { get; set; } = new();
    public List<SequencePointInfo> XamlSequencePoints { get; set; } = new();
    public List<LocalVariableInfo> XamlVariables { get; set; } = new();
}

public class ScanSummary
{
    public int TotalAssemblies { get; set; }
    public int AssembliesWithXamlDebugInfo { get; set; }
    public List<AssemblyXamlInfo> Results { get; set; } = new();
}

public class XamlControlMapping
{
    public string ControlType { get; set; } = "";
    public string XamlFile { get; set; } = "";
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Method { get; set; } = "";
    public List<string> Properties { get; set; } = new();
    public int SequencePointCount { get; set; }
}

public class XamlFileMappingResult
{
    public string XamlFileName { get; set; } = "";
    public string? XamlFilePath { get; set; }
    public List<XamlControlMapping> ControlMappings { get; set; } = new();
}
