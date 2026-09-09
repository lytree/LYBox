using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

[Generator]
public class MetadataGenerator : IIncrementalGenerator
{
    private const string GenerateMetadataAttributeName =
        "LYBox.Plugin.Shared.Attributes.GenerateMetadataAttribute";
    private const string ViewMapAttributeName =
        "LYBox.Plugin.Shared.Attributes.ViewMapAttribute";
    private const string NavigationItemAttributeName =
        "LYBox.Plugin.Shared.Attributes.NavigationItemAttribute";
    private const string MenuAttributeName =
        "LYBox.Plugin.Shared.Attributes.MenuAttribute";
    private const string RpcCommandAttributeName =
        "LYBox.Plugin.Shared.Attributes.RpcCommandAttribute";
    private const string CommandRegistrarInterfaceName =
        "LYBox.Plugin.Shared.CommandLine.IPluginCommandRegistrar";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targetClasses = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateMetadataAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateTarget(attributeContext));

        var viewDefinitions = context.SyntaxProvider.ForAttributeWithMetadataName(
            ViewMapAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateViewDefinition(attributeContext));

        var navigationItems = context.SyntaxProvider.ForAttributeWithMetadataName(
            NavigationItemAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateNavigationItem(attributeContext));

        var menuItems = context.SyntaxProvider.ForAttributeWithMetadataName(
            MenuAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateMenuItem(attributeContext));

        var rpcBindingTypes = context.SyntaxProvider.ForAttributeWithMetadataName(
            RpcCommandAttributeName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => ((IMethodSymbol)attributeContext.TargetSymbol)
                .ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        var cliRegistrarTypes = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntaxContext, _) => GetCliRegistrarType(syntaxContext))
            .Where(static typeName => typeName is not null)
            .Select(static (typeName, _) => typeName!);

        var metadata = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => new PluginMetadataInfo(
                GetMsBuildProperty(provider, "PluginName"),
                GetMsBuildProperty(provider, "PluginVersion"),
                GetMsBuildProperty(provider, "PluginAuthor"),
                GetMsBuildProperty(provider, "PluginDescription"),
                GetMsBuildProperty(provider, "PluginId"),
                GetMsBuildProperty(provider, "MinPluginSdkVersion"),
                GetMsBuildProperty(provider, "PluginKind"),
                GetMsBuildProperty(provider, "PluginWwwroot"),
                GetMsBuildProperty(provider, "PluginEntryPage"),
                GetMsBuildProperty(provider, "PluginIconResources")));

        var generationInputs = targetClasses
            .Combine(viewDefinitions.Collect())
            .Combine(navigationItems.Collect())
            .Combine(menuItems.Collect())
            .Combine(rpcBindingTypes.Collect())
            .Combine(cliRegistrarTypes.Collect())
            .Combine(metadata)
            .Select(static (data, _) => new GeneratorData(
                data.Left.Left.Left.Left.Left.Left,
                data.Left.Left.Left.Left.Left.Right,
                data.Left.Left.Left.Left.Right,
                data.Left.Left.Left.Right,
                data.Left.Left.Right,
                data.Left.Right,
                data.Right));

        context.RegisterSourceOutput(generationInputs, static (sourceContext, data) =>
        {
            var viewLines = GenerateViewLines(data.ViewDefinitions);
            var navLines = GenerateNavigationLines(data.NavigationItems);
            var menuAddLines = GenerateMenuAddStatements(data.MenuItems);
            var meta = data.Metadata;
            var target = data.Target;

            var metaLines = new StringBuilder();
            metaLines.AppendLine($"        public string Name => {Str(meta.Name ?? target.ClassName)};");
            metaLines.AppendLine($"        public string Version => {Str(meta.Version ?? "1.0.0")};");
            metaLines.AppendLine($"        public string Author => {Str(meta.Author ?? string.Empty)};");
            metaLines.AppendLine($"        public string Description => {Str(meta.Description ?? string.Empty)};");
            metaLines.AppendLine($"        public string PluginId => {Str(meta.PluginId ?? target.ClassName)};");
            metaLines.AppendLine($"        public string MinPluginSdkVersion => {Str(meta.MinSdkVersion ?? "0.0.0")};");

            var baseInterfaces = "IPlugin, IPluginMetadata";
            var webLines = new StringBuilder();
            var webUsing = string.Empty;
            if (string.Equals(meta.Kind, "Web", StringComparison.OrdinalIgnoreCase))
            {
                baseInterfaces += ", IWebPlugin";
                webUsing = "using LYBox.Plugin.Shared.Web;\r\n";
                webLines.AppendLine("        public IWebPluginDescriptor Web => new WebPluginDescriptor(");
                webLines.AppendLine($"            {Str(meta.Wwwroot ?? "wwwroot")}, {Str(meta.EntryPage ?? "index.html")});");
            }

            // 图标资源：若 csproj 声明了 <PluginIconResources>，emit 一个加载器方法；
            // 否则维持历史硬编码 null 行为，避免破坏现有插件。
            //
            // 资源加载走 avares:// 协议：
            //   1) 插件 csproj 需显式声明 <AvaloniaResource Include="PluginIcons.axaml" />，
            //      这样 XAML 会被嵌入 dll 并由 Avalonia 资源系统暴露为 avares://{AssemblyName}/PluginIcons.axaml；
            //   2) 生成器用 Avalonia.Platform.AssetLoader 打开 URI，再经 Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(Uri, Uri) 解析。
            var iconResourcesLines = new StringBuilder();
            var iconResourcesUsing = string.Empty;
            if (!string.IsNullOrWhiteSpace(meta.IconResources))
            {
                var fileName = meta.IconResources!;
                iconResourcesUsing = "using System.Diagnostics;\r\nusing Avalonia.Markup.Xaml;\r\n";
                iconResourcesLines.AppendLine("        // 由 <PluginIconResources> csproj 属性驱动。插件需在 csproj 显式声明：");
                iconResourcesLines.AppendLine($"        //   <AvaloniaResource Include=\"{fileName}\" />");
                iconResourcesLines.AppendLine("        // 失败（资源缺失/解析抛错）一律返回 null，不影响宿主启动。");
                iconResourcesLines.AppendLine("        public Avalonia.Controls.IResourceDictionary? GetIconResources() => LoadIconResources();");
                iconResourcesLines.AppendLine();
                iconResourcesLines.AppendLine("        private static Avalonia.Controls.IResourceDictionary? LoadIconResources()");
                iconResourcesLines.AppendLine("        {");
                iconResourcesLines.AppendLine("            try");
                iconResourcesLines.AppendLine("            {");
                iconResourcesLines.AppendLine($"                var uri = new global::System.Uri(\"avares://{target.ClassName}/{fileName}\");");
                iconResourcesLines.AppendLine("                if (AvaloniaXamlLoader.Load(uri, uri) is not Avalonia.Controls.IResourceDictionary dict)");
                iconResourcesLines.AppendLine("                    return null;");
                iconResourcesLines.AppendLine("                return dict;");
                iconResourcesLines.AppendLine("            }");
                iconResourcesLines.AppendLine("            catch (System.Exception ex)");
                iconResourcesLines.AppendLine("            {");
                iconResourcesLines.AppendLine($"                Debug.WriteLine($\"[LYBox.Plugin] Failed to load icon resources '{fileName}': {{ex.Message}}\");");
                iconResourcesLines.AppendLine("                return null;");
                iconResourcesLines.AppendLine("            }");
                iconResourcesLines.AppendLine("        }");
            }
            else
            {
                // 与 IPlugin 契约保持一致：走源生成器的插件默认不带图标资源，返回 null。
                // 若插件需要自带图标资源（StreamGeometry/Brush 等），可在 csproj 声明
                // <PluginIconResources>PluginIcons.axaml</PluginIconResources> 并添加对应
                // <AvaloniaResource Include="..." /> 即可让生成器自动 emit 加载逻辑。
                // 也可选择放弃 [GenerateMetadata]、手动实现 IPlugin.GetIconResources()。
                iconResourcesLines.AppendLine("        public Avalonia.Controls.IResourceDictionary? GetIconResources() => null;");
            }

            var source = $@"// <auto-generated />
#nullable enable
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using LYBox.Plugin.Shared;
using LYBox.Plugin.Shared.ViewModels;
{webUsing}{iconResourcesUsing}using Microsoft.Extensions.DependencyInjection;

namespace {target.Namespace}
{{
    public partial class {target.ClassName} : {baseInterfaces}
    {{
{metaLines}
{webLines}        public IEnumerable<KeyValuePair<Type, ViewFactory>> GetViewDefinitions()
        {{
{viewLines}
        }}

        public Dictionary<string, ViewModelFactory> GetNavigationItems() => new Dictionary<string, ViewModelFactory>
        {{
{navLines}
        }};

        public List<KeyValuePair<string?, MenuItemViewModel>> GetMenuItems()
        {{
            var allItems = new List<(string? Parent, MenuItemViewModel Item, int Order)>();
{menuAddLines}
            return MenuItemTreeBuilder.BuildTree(allItems);
        }}

{iconResourcesLines}    }}
}}";

            sourceContext.AddSource(
                $"{target.ClassName}.g.cs",
                SourceText.From(source, Encoding.UTF8));
            sourceContext.AddSource(
                $"{target.ClassName}.Module.g.cs",
                SourceText.From(GenerateModule(data), Encoding.UTF8));
        });
    }

    private static string GenerateModule(GeneratorData data)
    {
        var target = data.Target;
        var pluginType = $"global::{target.Namespace}.{target.ClassName}";
        var moduleName = $"__Generated{target.ClassName}Module";
        var rpcTypes = data.RpcBindingTypes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var cliTypes = data.CliRegistrarTypes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var isWeb = string.Equals(data.Metadata.Kind, "Web", StringComparison.OrdinalIgnoreCase);
        var interfaces = new List<string>
        {
            "global::LYBox.Plugin.Shared.Generated.IGeneratedPluginModule"
        };

        if (isWeb)
            interfaces.Add("global::LYBox.Plugin.Shared.Web.IGeneratedPluginWebModule");
        if (cliTypes.Length > 0)
            interfaces.Add("global::LYBox.Plugin.Shared.CommandLine.IGeneratedPluginCliModule");

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine($"[assembly: global::LYBox.Plugin.Shared.Generated.GeneratedPluginModuleAttribute(typeof(global::{target.Namespace}.{moduleName}))]");
        builder.AppendLine();
        builder.AppendLine($"namespace {target.Namespace}");
        builder.AppendLine("{");
        builder.AppendLine($"    internal sealed class {moduleName} : {string.Join(", ", interfaces)}");
        builder.AppendLine("    {");
        builder.AppendLine($"        private readonly {pluginType} _plugin = new {pluginType}();");
        builder.AppendLine($"        public global::System.Type PluginType => typeof({pluginType});");
        builder.AppendLine("        public global::LYBox.Plugin.Shared.IPlugin CreatePlugin() => _plugin;");
        builder.AppendLine("        public global::LYBox.Plugin.Shared.IPluginMetadata Metadata => _plugin;");
        builder.AppendLine("        public global::LYBox.Plugin.Shared.Generated.GeneratedPluginUiDescriptor Ui { get; } =");
        builder.AppendLine("            new global::LYBox.Plugin.Shared.Generated.GeneratedPluginUiDescriptor(");
        AppendViewDescriptors(builder, data.ViewDefinitions);
        AppendNavigationDescriptors(builder, data.NavigationItems);
        AppendMenuDescriptors(builder, data.MenuItems);
        builder.AppendLine("            );");

        if (isWeb)
        {
            builder.AppendLine();
            builder.AppendLine("        public void RegisterRpcBindings(global::LYBox.Plugin.Shared.Rpc.IRpcHost host, global::System.IServiceProvider services)");
            builder.AppendLine("        {");
            foreach (var rpcType in rpcTypes)
            {
                var identifier = SanitizeIdentifier(rpcType);
                builder.AppendLine($"            var binding_{identifier} = services.GetService(typeof({rpcType})) as global::LYBox.Plugin.Shared.Rpc.IRpcBindingSource");
                builder.AppendLine($"                ?? global::System.Activator.CreateInstance(typeof({rpcType})) as global::LYBox.Plugin.Shared.Rpc.IRpcBindingSource;");
                builder.AppendLine($"            binding_{identifier}?.RegisterBindings(host);");
            }
            builder.AppendLine("        }");
        }

        if (cliTypes.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("        public global::System.Collections.Generic.IReadOnlyList<global::LYBox.Plugin.Shared.CommandLine.PluginCliRegistrarDescriptor> CliRegistrars { get; } =");
            builder.AppendLine("            new global::LYBox.Plugin.Shared.CommandLine.PluginCliRegistrarDescriptor[]");
            builder.AppendLine("            {");
            foreach (var cliType in cliTypes)
            {
                builder.AppendLine("                new global::LYBox.Plugin.Shared.CommandLine.PluginCliRegistrarDescriptor(");
                builder.AppendLine($"                    typeof({cliType}), services => (global::LYBox.Plugin.Shared.CommandLine.IPluginCommandRegistrar)(services.GetService(typeof({cliType})) ?? global::System.Activator.CreateInstance(typeof({cliType}))!)),");
            }
            builder.AppendLine("            };");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendViewDescriptors(
        StringBuilder builder,
        ImmutableArray<ViewDefinitionData> definitions)
    {
        builder.AppendLine("                new global::LYBox.Plugin.Shared.Generated.GeneratedViewDescriptor[]");
        builder.AppendLine("                {");
        foreach (var definition in definitions)
        {
            builder.AppendLine("                    new global::LYBox.Plugin.Shared.Generated.GeneratedViewDescriptor(");
            builder.AppendLine($"                        typeof({definition.ViewModelType}), typeof({definition.ViewType}), services => (global::Avalonia.Controls.Control)(services.GetService(typeof({definition.ViewType})) ?? new {definition.ViewType}())),");
        }
        builder.AppendLine("                },");
    }

    private static void AppendNavigationDescriptors(
        StringBuilder builder,
        ImmutableArray<NavigationItemData> items)
    {
        builder.AppendLine("                new global::LYBox.Plugin.Shared.Generated.GeneratedNavigationDescriptor[]");
        builder.AppendLine("                {");
        foreach (var item in items)
        {
            builder.AppendLine("                    new global::LYBox.Plugin.Shared.Generated.GeneratedNavigationDescriptor(");
            builder.AppendLine($"                        {item.Key}, typeof({item.ViewModelType}), services => services.GetService(typeof({item.ViewModelType})) ?? new {item.ViewModelType}()),");
        }
        builder.AppendLine("                },");
    }

    private static void AppendMenuDescriptors(
        StringBuilder builder,
        ImmutableArray<MenuItemData> items)
    {
        builder.AppendLine("                new global::LYBox.Plugin.Shared.Generated.GeneratedMenuDescriptor[]");
        builder.AppendLine("                {");
        foreach (var item in items)
        {
            builder.AppendLine(
                $"                    new global::LYBox.Plugin.Shared.Generated.GeneratedMenuDescriptor({item.Header}, {item.Key}, {item.Parent}, {item.IconName ?? "null"}, {item.Status}, {item.Order}),");
        }
        builder.AppendLine("                }");
    }

    private static string? GetCliRegistrarType(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type || type.IsAbstract)
            return null;

        return type.AllInterfaces.Any(static interfaceType =>
                interfaceType.ToDisplayString() == CommandRegistrarInterfaceName)
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }

    private static TargetData CreateTarget(GeneratorAttributeSyntaxContext context)
    {
        var target = (INamedTypeSymbol)context.TargetSymbol;
        var targetNamespace = target.ContainingNamespace.IsGlobalNamespace
            ? "Global"
            : target.ContainingNamespace.ToDisplayString();
        return new TargetData(targetNamespace, target.Name);
    }

    private static ViewDefinitionData CreateViewDefinition(GeneratorAttributeSyntaxContext context)
    {
        var viewModelType = ((INamedTypeSymbol)context.TargetSymbol)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var viewType = context.Attributes[0].ConstructorArguments[0].Value as ITypeSymbol;
        return new ViewDefinitionData(
            viewModelType,
            viewType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object");
    }

    private static NavigationItemData CreateNavigationItem(GeneratorAttributeSyntaxContext context)
    {
        var viewModelType = ((INamedTypeSymbol)context.TargetSymbol)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new NavigationItemData(
            viewModelType,
            ToSourceExpression(context.Attributes[0].ConstructorArguments[0]));
    }

    private static MenuItemData CreateMenuItem(GeneratorAttributeSyntaxContext context)
    {
        var attribute = context.Attributes[0];
        var header = GetConstructorArgument(attribute, 0, "null");
        var key = GetConstructorArgument(attribute, 1, "null");
        var parent = GetConstructorArgument(attribute, 2, "null");
        string? iconName = null;
        var status = "null";
        var order = 100;

        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "Header": header = ToSourceExpression(argument.Value); break;
                case "Key": key = ToSourceExpression(argument.Value); break;
                case "ParentKey": parent = ToSourceExpression(argument.Value); break;
                case "IconName": iconName = ToSourceExpression(argument.Value); break;
                case "Status": status = ToSourceExpression(argument.Value); break;
                case "Order" when argument.Value.Value is int value: order = value; break;
            }
        }

        return new MenuItemData(header, key, parent, iconName, status, order);
    }

    private static string GenerateViewLines(ImmutableArray<ViewDefinitionData> definitions)
    {
        var builder = new StringBuilder();
        foreach (var definition in definitions)
        {
            builder.AppendLine(
                $"        yield return new KeyValuePair<Type, ViewFactory>(typeof({definition.ViewModelType}), () => new {definition.ViewType}());");
        }
        return builder.ToString();
    }

    private static string GenerateNavigationLines(ImmutableArray<NavigationItemData> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
            builder.AppendLine($"        {{ {item.Key}, () => new {item.ViewModelType}() }},");
        return builder.ToString();
    }

    private static string GenerateMenuAddStatements(ImmutableArray<MenuItemData> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            var iconNameProperty = item.IconName is null ? string.Empty : $", MenuIconName = {item.IconName}";
            builder.AppendLine(
                $"            allItems.Add(({item.Parent}, new MenuItemViewModel {{ MenuHeader = {item.Header}, Key = {item.Key}{iconNameProperty}, Status = {item.Status}, Order = {item.Order} }}, {item.Order}));");
        }
        return builder.ToString();
    }

    private static string GetConstructorArgument(AttributeData attribute, int index, string fallback) =>
        attribute.ConstructorArguments.Length > index
            ? ToSourceExpression(attribute.ConstructorArguments[index])
            : fallback;

    private static string ToSourceExpression(TypedConstant constant)
    {
        if (constant.IsNull) return "null";
        return constant.Value switch
        {
            string value => SymbolDisplay.FormatLiteral(value, quote: true),
            char value => SymbolDisplay.FormatLiteral(value, quote: true),
            bool value => value ? "true" : "false",
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => constant.Value?.ToString() ?? "null"
        };
    }

    private static string? GetMsBuildProperty(AnalyzerConfigOptionsProvider provider, string name) =>
        provider.GlobalOptions.TryGetValue($"build_property.{name}", out var value) ? value : null;

    private static string Str(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private readonly struct GeneratorData(
        TargetData target,
        ImmutableArray<ViewDefinitionData> viewDefinitions,
        ImmutableArray<NavigationItemData> navigationItems,
        ImmutableArray<MenuItemData> menuItems,
        ImmutableArray<string> rpcBindingTypes,
        ImmutableArray<string> cliRegistrarTypes,
        PluginMetadataInfo metadata)
    {
        public TargetData Target { get; } = target;
        public ImmutableArray<ViewDefinitionData> ViewDefinitions { get; } = viewDefinitions;
        public ImmutableArray<NavigationItemData> NavigationItems { get; } = navigationItems;
        public ImmutableArray<MenuItemData> MenuItems { get; } = menuItems;
        public ImmutableArray<string> RpcBindingTypes { get; } = rpcBindingTypes;
        public ImmutableArray<string> CliRegistrarTypes { get; } = cliRegistrarTypes;
        public PluginMetadataInfo Metadata { get; } = metadata;
    }

    private readonly struct TargetData(string targetNamespace, string className)
    {
        public string Namespace { get; } = targetNamespace;
        public string ClassName { get; } = className;
    }

    private readonly struct ViewDefinitionData(string viewModelType, string viewType)
    {
        public string ViewModelType { get; } = viewModelType;
        public string ViewType { get; } = viewType;
    }

    private readonly struct NavigationItemData(string viewModelType, string key)
    {
        public string ViewModelType { get; } = viewModelType;
        public string Key { get; } = key;
    }

    private readonly struct MenuItemData(
        string header,
        string key,
        string parent,
        string? iconName,
        string status,
        int order)
    {
        public string Header { get; } = header;
        public string Key { get; } = key;
        public string Parent { get; } = parent;
        public string? IconName { get; } = iconName;
        public string Status { get; } = status;
        public int Order { get; } = order;
    }

    private readonly struct PluginMetadataInfo(
        string? name,
        string? version,
        string? author,
        string? description,
        string? pluginId,
        string? minSdkVersion,
        string? kind,
        string? wwwroot,
        string? entryPage,
        string? iconResources)
    {
        public string? Name { get; } = name;
        public string? Version { get; } = version;
        public string? Author { get; } = author;
        public string? Description { get; } = description;
        public string? PluginId { get; } = pluginId;
        public string? MinSdkVersion { get; } = minSdkVersion;
        public string? Kind { get; } = kind;
        public string? Wwwroot { get; } = wwwroot;
        public string? EntryPage { get; } = entryPage;
        public string? IconResources { get; } = iconResources;
    }
}
