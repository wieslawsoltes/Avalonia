using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Avalonia.Build.Tasks.UnitTest;

public class CompiledResourceFactoryTests
{
    [Fact]
    public void Factory_Bodies_Call_Compiled_Xaml_And_Codebehind_Directly()
    {
        using var fixture = new CompilationFixture();
        Assert.True(fixture.Compile(), fixture.Errors);
        using var assembly = AssemblyDefinition.ReadAssembly(fixture.Output);
        var factories = assembly.MainModule.GetType("PInvoke.ResourceFactories");
        var dictionary = factories.Methods.Single(m => m.Name == "CreateDictionary");
        Assert.Contains(dictionary.Body.Instructions, i => i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference method && method.Name == "Build:/FactoryResources.axaml");
        var window = factories.Methods.Single(m => m.Name == "CreateWindow");
        Assert.Contains(window.Body.Instructions, i => i.OpCode == OpCodes.Newobj &&
            i.Operand is MethodReference method && method.DeclaringType.FullName == "PInvoke.MainWindow");
        var internalDictionary = factories.Methods.Single(m => m.Name == "CreateInternalDictionary");
        Assert.Contains(internalDictionary.Body.Instructions, i => i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference method && method.Name == "Build:/InternalFactoryResources.axaml");
        var dispatcher = assembly.MainModule.GetType("CompiledAvaloniaXaml.!XamlLoader");
        Assert.DoesNotContain(dispatcher.Methods.SelectMany(m => m.Body.Instructions), i =>
            i.OpCode == OpCodes.Ldstr && (string)i.Operand == "avares://PInvoke/InternalFactoryResources.axaml");
        Assert.DoesNotContain(factories.Methods.SelectMany(m => m.Body.Instructions), i =>
            i.OpCode == OpCodes.Throw || i.Operand is MethodReference method &&
            method.DeclaringType.Namespace.StartsWith("System.Reflection", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("avares://PInvoke/Missing.axaml")]
    [InlineData("avares://Other/FactoryResources.axaml")]
    [InlineData("FactoryResources.axaml")]
    public void Invalid_Resource_Is_A_Build_Error(string uri)
    {
        using var fixture = new CompilationFixture();
        fixture.ChangeFactory(m => m.CustomAttributes[0].ConstructorArguments[0] =
            new CustomAttributeArgument(m.Module.TypeSystem.String, uri));
        Assert.False(fixture.Compile());
    }

    [Fact]
    public void Instance_Factory_Is_A_Build_Error()
    {
        using var fixture = new CompilationFixture();
        fixture.ChangeFactory(m => m.IsStatic = false);
        Assert.False(fixture.Compile());
    }

    private sealed class CompilationFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "avalonia-resource-factory-" + Guid.NewGuid().ToString("N"));
        private readonly string _source;
        private readonly string _referenceList;

        public CompilationFixture()
        {
            Directory.CreateDirectory(_directory);
            var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
            _source = Path.Combine(_directory, "PInvoke.dll");
            _referenceList = Path.Combine(assets, "PInvoke.dll.refs");
            File.Copy(Path.Combine(assets, "PInvoke.dll"), _source);
            Output = Path.Combine(_directory, "Compiled.dll");
        }

        public string Output { get; }
        public string Errors { get; private set; } = string.Empty;

        public void ChangeFactory(Action<MethodDefinition> change)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(_source);
            change(assembly.MainModule.GetType("PInvoke.ResourceFactories").Methods.Single(m => m.Name == "CreateDictionary"));
            using var buffer = new MemoryStream();
            assembly.Write(buffer);
            File.WriteAllBytes(_source, buffer.ToArray());
        }

        public bool Compile()
        {
            using var engine = UnitTestBuildEngine.Start(continueOnError: true);
            var assembly = new TaskItem(_source);
            assembly.SetMetadata(CompileAvaloniaXamlTask.AvaloniaCompileOutputMetadataName, Output);
            var success = new CompileAvaloniaXamlTask
            {
                AssemblyFile = assembly,
                References = File.ReadAllLines(_referenceList).Select(p => new TaskItem(p)).ToArray(),
                BuildEngine = engine,
                ProjectDirectory = Directory.GetCurrentDirectory(),
                VerifyIl = true
            }.Execute();
            Errors = string.Join(Environment.NewLine, engine.Errors.Select(e => e.Message));
            return success;
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }
}
