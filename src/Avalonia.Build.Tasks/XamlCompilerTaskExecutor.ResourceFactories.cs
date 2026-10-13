using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Avalonia.Build.Tasks;

public static partial class XamlCompilerTaskExecutor
{
    private static Dictionary<string, List<MethodDefinition>> FindResourceFactories(ModuleDefinition module)
    {
        var result = new Dictionary<string, List<MethodDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods)
        {
            var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName ==
                "Avalonia.Markup.Xaml.CompiledResourceFactoryAttribute");
            if (attribute is null)
                continue;
            if (!method.IsStatic || method.HasParameters || method.HasGenericParameters ||
                method.ReturnType.FullName != "System.Object" || !method.HasBody ||
                HasGenericDeclaringType(type))
                throw new InvalidProgramException($"Compiled resource factory '{method.FullName}' must be a non-generic static parameterless method returning object with a body.");
            var uri = attribute.ConstructorArguments.Count == 1 ? attribute.ConstructorArguments[0].Value as string : null;
            if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
                parsed.Scheme != "avares" || !string.Equals(parsed.Host, module.Assembly.Name.Name, StringComparison.OrdinalIgnoreCase) ||
                parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
                throw new InvalidProgramException($"Compiled resource factory '{method.FullName}' must identify an absolute avares URI in its own assembly.");
            if (!result.TryGetValue(uri, out var methods))
                result.Add(uri, methods = new List<MethodDefinition>());
            methods.Add(method);
        }
        return result;
    }

    private static bool HasGenericDeclaringType(TypeDefinition type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            if (current.HasGenericParameters)
                return true;
        return false;
    }

    private static void EmitResourceFactories(Dictionary<string, List<MethodDefinition>> factories,
        string uri, MethodReference? buildMethod, MethodReference? constructor,
        MethodReference? constructorWithServiceProvider, MethodReference createServiceProvider)
    {
        if (!factories.TryGetValue(uri, out var methods))
            return;
        foreach (var method in methods)
        {
            method.Body = new MethodBody(method);
            method.DebugInformation.SequencePoints.Clear();
            method.DebugInformation.Scope = null;
            var il = method.Body.Instructions;
            if (constructor is not null)
                il.Add(Instruction.Create(OpCodes.Newobj, constructor));
            else
            {
                il.Add(Instruction.Create(OpCodes.Ldnull));
                il.Add(Instruction.Create(OpCodes.Call, createServiceProvider));
                il.Add(constructorWithServiceProvider is not null
                    ? Instruction.Create(OpCodes.Newobj, constructorWithServiceProvider)
                    : Instruction.Create(OpCodes.Call, buildMethod!));
            }
            il.Add(Instruction.Create(OpCodes.Ret));
        }
        factories.Remove(uri);
    }
}
