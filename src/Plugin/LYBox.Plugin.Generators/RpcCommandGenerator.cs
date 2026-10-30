using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LYBox.Plugin.Generators;

[Generator]
public sealed class RpcCommandGenerator : IIncrementalGenerator
{
    private const string AttributeName = "LYBox.Plugin.Shared.Attributes.RpcCommandAttribute";
    private const string CancellationTokenName = "System.Threading.CancellationToken";

    private static readonly DiagnosticDescriptor LegacyParameters = new(
        "WPG0902",
        "Legacy positional RPC parameters",
        "RPC method '{0}' uses multiple positional parameters; use one request DTO for generated clients",
        "LYBox.Plugin.Rpc",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidRpcMethod = new(
        "WPG0201",
        "Invalid RPC command method",
        "RPC method '{0}' cannot be exposed: {1}; the binding was skipped",
        "LYBox.Plugin.Rpc",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateRpcCommand = new(
        "WPG0202",
        "Duplicate RPC command name",
        "RPC command name '{0}' is declared more than once on '{1}'; the duplicate binding was skipped",
        "LYBox.Plugin.Rpc",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax,
            static (attributeContext, _) => CreateCommand((IMethodSymbol)attributeContext.TargetSymbol, attributeContext.Attributes[0]));

        context.RegisterSourceOutput(commands.Collect(), static (sourceContext, commandList) =>
        {
            foreach (var group in commandList.GroupBy(static command => command.ContainingTypeName, StringComparer.Ordinal))
                EmitClass(sourceContext, group.OrderBy(static command => command.MethodName, StringComparer.Ordinal).ToList());
        });
    }

    private static CommandSpec CreateCommand(IMethodSymbol method, AttributeData attribute)
    {
        var name = attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string configuredName
                ? configuredName
                : method.Name;
        var parameters = method.Parameters.Select(static parameter => new ParameterSpec(
            parameter.Name,
            parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            parameter.Type.ToDisplayString() == CancellationTokenName,
            ToTypeScriptType(parameter.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))).ToArray();

        return new CommandSpec(
            ValidateMethod(method, name),
            method.ContainingType.ToDisplayString(),
            method.ContainingType.ContainingNamespace.IsGlobalNamespace
                ? null
                : method.ContainingType.ContainingNamespace.ToDisplayString(),
            method.ContainingType.Name,
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.Name,
            name,
            method.IsStatic,
            GetReturnKind(method.ReturnType),
            GetReturnTypeScriptType(method.ReturnType),
            parameters,
            method.Locations.FirstOrDefault());
    }

    private static string? ValidateMethod(IMethodSymbol method, string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return "the command name is empty";
        if (method.DeclaredAccessibility != Accessibility.Public)
            return "the method must be public";
        if (method.IsGenericMethod)
            return "generic methods are not supported";
        if (method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None))
            return "ref, out and in parameters are not supported";

        var cancellationCount = method.Parameters.Count(static parameter =>
            parameter.Type.ToDisplayString() == CancellationTokenName);
        if (cancellationCount > 1)
            return "only one CancellationToken parameter is allowed";

        return null;
    }

    private static void EmitClass(SourceProductionContext context, List<CommandSpec> commands)
    {
        if (commands.Count == 0) return;
        var first = commands[0];
        var validCommands = new List<CommandSpec>(commands.Count);
        var commandNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            if (command.ValidationError is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidRpcMethod,
                    command.Location ?? Location.None,
                    command.MethodName,
                    command.ValidationError));
                continue;
            }

            if (!commandNames.Add(command.CommandName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateRpcCommand,
                    command.Location ?? Location.None,
                    command.CommandName,
                    first.ContainingTypeName));
                continue;
            }

            validCommands.Add(command);
        }

        if (validCommands.Count == 0) return;
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine("using System.Text.Json;");
        source.AppendLine("using LYBox.Plugin.Shared.Rpc;");
        source.AppendLine();
        if (first.Namespace is not null)
            source.AppendLine($"namespace {first.Namespace};");
        source.AppendLine();
        source.AppendLine($"public partial class {first.ClassName} : global::LYBox.Plugin.Shared.Rpc.IRpcBindingSource");
        source.AppendLine("{");

        if (validCommands.Any(static command => !command.IsStatic))
        {
            source.AppendLine($"    private static readonly {first.FullyQualifiedTypeName} __instance = new();");
            source.AppendLine();
        }

        source.AppendLine("    public void RegisterBindings(global::LYBox.Plugin.Shared.Rpc.IRpcHost host)");
        source.AppendLine("    {");
        foreach (var command in validCommands)
        {
            var businessParameters = command.Parameters.Where(static parameter => !parameter.IsCancellationToken).ToArray();
            if (businessParameters.Length <= 1)
                EmitCanonicalBinding(source, command, businessParameters);
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(LegacyParameters, command.Location, command.MethodName));
                EmitLegacyBinding(source, command, businessParameters);
            }
        }

        var javaScript = GenerateJavaScriptClient(first.ClassName, validCommands);
        var typeScript = GenerateTypeScriptClient(first.ClassName, validCommands);
        source.AppendLine("        host.RegisterGeneratedClientArtifact(new global::LYBox.Plugin.Shared.Rpc.RpcClientArtifact(");
        source.AppendLine($"            {Literal(first.ClassName + ".client.js")},");
        source.AppendLine("            global::LYBox.Plugin.Shared.Rpc.RpcClientArtifact.JavaScriptContentType,");
        source.AppendLine($"            {Literal(javaScript)}));");
        source.AppendLine("        host.RegisterGeneratedClientArtifact(new global::LYBox.Plugin.Shared.Rpc.RpcClientArtifact(");
        source.AppendLine($"            {Literal(first.ClassName + ".client.d.ts")},");
        source.AppendLine("            global::LYBox.Plugin.Shared.Rpc.RpcClientArtifact.TypeScriptContentType,");
        source.AppendLine($"            {Literal(typeScript)}));");
        source.AppendLine("    }");
        source.AppendLine("}");

        var hintName = first.Namespace is null
            ? $"{first.ClassName}.Rpc.g.cs"
            : $"{first.Namespace}.{first.ClassName}.Rpc.g.cs";
        context.AddSource(hintName, source.ToString());
    }

    private static void EmitCanonicalBinding(StringBuilder source, CommandSpec command, ParameterSpec[] businessParameters)
    {
        source.AppendLine($"        host.RegisterCanonicalCommand({Literal(command.CommandName)}, async (payload, ct) =>");
        source.AppendLine("        {");
        if (businessParameters.Length == 1)
        {
            var parameter = businessParameters[0];
            source.AppendLine($"            var p0 = global::System.Text.Json.JsonSerializer.Deserialize<{parameter.CSharpType}>(payload.GetRawText(), global::LYBox.Plugin.Shared.Rpc.RpcEnvelope.JsonOptions)!;");
        }
        EmitInvocation(source, command, businessParameters, canonical: true);
        source.AppendLine("        });");
    }

    private static void EmitLegacyBinding(StringBuilder source, CommandSpec command, ParameterSpec[] businessParameters)
    {
        source.AppendLine($"        host.RegisterCommand({Literal(command.CommandName)}, async (args, ct) =>");
        source.AppendLine("        {");
        for (var index = 0; index < businessParameters.Length; index++)
        {
            source.AppendLine($"            var p{index} = {index} < args.Length");
            source.AppendLine($"                ? global::System.Text.Json.JsonSerializer.Deserialize<{businessParameters[index].CSharpType}>(args[{index}].GetRawText(), global::LYBox.Plugin.Shared.Rpc.RpcEnvelope.JsonOptions)!");
            source.AppendLine($"                : default({businessParameters[index].CSharpType})!;");
        }
        EmitInvocation(source, command, businessParameters, canonical: false);
        source.AppendLine("        });");
    }

    private static void EmitInvocation(
        StringBuilder source,
        CommandSpec command,
        ParameterSpec[] businessParameters,
        bool canonical)
    {
        var target = command.IsStatic ? command.FullyQualifiedTypeName : "__instance";
        var businessIndex = 0;
        var arguments = command.Parameters.Select(parameter =>
            parameter.IsCancellationToken ? "ct" : $"p{businessIndex++}");
        var call = $"{target}.{command.MethodName}({string.Join(", ", arguments)})";

        switch (command.ReturnKind)
        {
            case ReturnKind.Void:
                source.AppendLine($"            {call};");
                source.AppendLine("            return null;");
                break;
            case ReturnKind.AwaitableVoid:
                source.AppendLine($"            await {call}.ConfigureAwait(false);");
                source.AppendLine("            return null;");
                break;
            case ReturnKind.AwaitableValue:
                source.AppendLine($"            var result = await {call}.ConfigureAwait(false);");
                source.AppendLine("            return (object?)result;");
                break;
            default:
                source.AppendLine($"            var result = {call};");
                source.AppendLine("            return (object?)result;");
                break;
        }
    }

    private static string GenerateJavaScriptClient(string className, IReadOnlyList<CommandSpec> commands)
    {
        var source = new StringBuilder();
        source.AppendLine("// Generated by LYBox.Plugin.Generators.");
        source.AppendLine($"export function create{className}Client(transport) {{");
        source.AppendLine("  if (!transport || typeof transport.invoke !== \"function\") throw new TypeError(\"A canonical LYBox RPC transport is required.\");");
        source.AppendLine("  return Object.freeze({");
        foreach (var command in commands)
        {
            var parameters = command.Parameters.Where(static parameter => !parameter.IsCancellationToken).ToArray();
            var memberName = ToClientMemberName(command.MethodName);
            if (parameters.Length == 0)
                source.AppendLine($"    {memberName}: (options) => transport.invoke({Json(command.CommandName)}, null, options),");
            else if (parameters.Length == 1)
                source.AppendLine($"    {memberName}: (payload, options) => transport.invoke({Json(command.CommandName)}, payload, options),");
            else
            {
                var names = string.Join(", ", parameters.Select(static parameter => parameter.Name));
                source.AppendLine($"    {memberName}: ({names}) => (transport.invokeLegacy || transport.rpc).call(transport, {Json(command.CommandName)}, {names}),");
            }
        }
        source.AppendLine("  });");
        source.AppendLine("}");
        return source.ToString();
    }

    private static string GenerateTypeScriptClient(string className, IReadOnlyList<CommandSpec> commands)
    {
        var source = new StringBuilder();
        source.AppendLine("// Generated by LYBox.Plugin.Generators.");
        source.AppendLine("export interface RpcInvokeOptions { signal?: AbortSignal; timeout?: number; }");
        source.AppendLine("export interface PluginRpcTransport {");
        source.AppendLine("  invoke<TRequest, TResponse>(method: string, payload: TRequest, options?: RpcInvokeOptions): Promise<TResponse>;");
        source.AppendLine("  invokeLegacy?(method: string, ...args: unknown[]): Promise<unknown>;");
        source.AppendLine("  rpc?(method: string, ...args: unknown[]): Promise<unknown>;");
        source.AppendLine("}");
        source.AppendLine($"export interface {className}Client {{");
        foreach (var command in commands)
        {
            var parameters = command.Parameters.Where(static parameter => !parameter.IsCancellationToken).ToArray();
            var memberName = ToClientMemberName(command.MethodName);
            if (parameters.Length == 0)
                source.AppendLine($"  {memberName}(options?: RpcInvokeOptions): Promise<{command.TypeScriptReturnType}>;");
            else if (parameters.Length == 1)
                source.AppendLine($"  {memberName}(payload: {parameters[0].TypeScriptType}, options?: RpcInvokeOptions): Promise<{command.TypeScriptReturnType}>;");
            else
                source.AppendLine($"  {memberName}({string.Join(", ", parameters.Select(static parameter => parameter.Name + ": " + parameter.TypeScriptType))}): Promise<{command.TypeScriptReturnType}>;");
        }
        source.AppendLine("}");
        source.AppendLine($"export declare function create{className}Client(transport: PluginRpcTransport): {className}Client;");
        return source.ToString();
    }

    private static ReturnKind GetReturnKind(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void) return ReturnKind.Void;
        if (type is INamedTypeSymbol named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && (named.Name == "Task" || named.Name == "ValueTask"))
        {
            return named.TypeArguments.Length == 0 ? ReturnKind.AwaitableVoid : ReturnKind.AwaitableValue;
        }
        return ReturnKind.Value;
    }

    private static string GetReturnTypeScriptType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void) return "void";
        if (type is INamedTypeSymbol named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && (named.Name == "Task" || named.Name == "ValueTask"))
        {
            return named.TypeArguments.Length == 0
                ? "void"
                : ToTypeScriptType(named.TypeArguments[0], new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        }
        return ToTypeScriptType(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    }

    private static string ToTypeScriptType(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return ToTypeScriptType(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), visited) + " | null";

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return "boolean";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: return "number";
            case SpecialType.System_String:
            case SpecialType.System_Char: return "string";
            case SpecialType.System_Object: return "unknown";
        }

        if (type is IArrayTypeSymbol array)
            return $"ReadonlyArray<{ToTypeScriptType(array.ElementType, visited)}>";

        if (type is INamedTypeSymbol named)
        {
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return ToTypeScriptType(named.TypeArguments[0], visited) + " | null";
            if (named.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.Uri")
                return "string";
            if (named.TypeKind == TypeKind.Enum)
                return "string";
            if (named.TypeArguments.Length == 1
                && named.Name is "List" or "IList" or "IReadOnlyList" or "IEnumerable" or "ICollection")
                return $"ReadonlyArray<{ToTypeScriptType(named.TypeArguments[0], visited)}>";
            if (!visited.Add(named)) return "unknown";

            var properties = named.GetMembers().OfType<IPropertySymbol>()
                .Where(static property => !property.IsStatic && property.DeclaredAccessibility == Accessibility.Public && property.GetMethod is not null)
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToArray();
            if (properties.Length == 0) return "unknown";
            var fields = properties.Select(property =>
                $"{ToCamelCase(property.Name)}: {ToTypeScriptType(property.Type, visited)}");
            visited.Remove(named);
            return "{ " + string.Join("; ", fields) + " }";
        }

        return "unknown";
    }

    private static string ToClientMemberName(string methodName)
    {
        var name = methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;
        return ToCamelCase(name);
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private enum ReturnKind
    {
        Void,
        Value,
        AwaitableVoid,
        AwaitableValue,
    }

    private sealed class ParameterSpec
    {
        public ParameterSpec(string name, string cSharpType, bool isCancellationToken, string typeScriptType)
        {
            Name = name;
            CSharpType = cSharpType;
            IsCancellationToken = isCancellationToken;
            TypeScriptType = typeScriptType;
        }

        public string Name { get; }
        public string CSharpType { get; }
        public bool IsCancellationToken { get; }
        public string TypeScriptType { get; }
    }

    private sealed class CommandSpec
    {
        public CommandSpec(
            string? validationError,
            string containingTypeName,
            string? @namespace,
            string className,
            string fullyQualifiedTypeName,
            string methodName,
            string commandName,
            bool isStatic,
            ReturnKind returnKind,
            string typeScriptReturnType,
            ParameterSpec[] parameters,
            Location? location)
        {
            ValidationError = validationError;
            ContainingTypeName = containingTypeName;
            Namespace = @namespace;
            ClassName = className;
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            MethodName = methodName;
            CommandName = commandName;
            IsStatic = isStatic;
            ReturnKind = returnKind;
            TypeScriptReturnType = typeScriptReturnType;
            Parameters = parameters;
            Location = location;
        }

        public string? ValidationError { get; }
        public string ContainingTypeName { get; }
        public string? Namespace { get; }
        public string ClassName { get; }
        public string FullyQualifiedTypeName { get; }
        public string MethodName { get; }
        public string CommandName { get; }
        public bool IsStatic { get; }
        public ReturnKind ReturnKind { get; }
        public string TypeScriptReturnType { get; }
        public ParameterSpec[] Parameters { get; }
        public Location? Location { get; }
    }
}
