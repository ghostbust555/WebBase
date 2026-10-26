using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

[assembly: MetadataUpdateHandler(typeof(WebBase.Api.Infrastructure.TypeScriptContracts))]

namespace WebBase.Api.Infrastructure;

/// <summary>
/// Writes client/src/api-types.ts: a TypeScript interface for every object a
/// controller takes or returns, made from the C# types themselves. The
/// website's api.ts uses them, so a renamed or retyped field fails the
/// website's type check instead of failing in a visitor's browser.
///
/// - What's included: the [FromBody] (or complex) parameters and the return
///   types of every action, ActionResult&lt;T&gt; unwrapped, plus
///   [ProducesResponseType] types, and everything they contain.
/// - Names and shapes follow System.Text.Json as the app configures it:
///   camelCase, [JsonPropertyName], enums as their names, nullable → "| null".
/// - An action returning plain IActionResult must say what it returns with
///   [ProducesResponseType], or generation fails and says where.
///
/// Kept up to date three ways: in development the API rewrites the file every
/// time it starts or hot-reloads (Dev:TypeScriptOutput, set in compose.dev.yaml);
/// ./scripts/gen-types.sh writes it; and a test fails while it's out of date.
/// </summary>
public static class TypeScriptContracts
{
    /// <summary>Where the file goes, relative to the repository root.</summary>
    public const string RelativePath = "client/src/api-types.ts";

    /// <summary>Set at startup in development: rewrite here after a hot reload too.</summary>
    public static string? DevelopmentOutput { get; set; }

    public static string Generate(Assembly assembly) => Generate(
        assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }
                                       && typeof(ControllerBase).IsAssignableFrom(t)
                                       && t.GetCustomAttribute<ApiControllerAttribute>() != null));

    public static string Generate(IEnumerable<Type> controllers) => new Generator().Run(controllers);

    /// <summary>Write the file if its text would change (an unchanged file isn't touched, so nothing reloads).</summary>
    public static bool WriteIfChanged(string path, Assembly assembly)
    {
        var text = Generate(assembly);
        if (File.Exists(path) && File.ReadAllText(path) == text) return false;
        // Over the existing file rather than replacing it, so it keeps its owner.
        using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
        {
            file.SetLength(0);
            file.Write(Encoding.UTF8.GetBytes(text));
        }
        return true;
    }

    /// <summary>Called by `dotnet watch` after a hot reload: a changed record shows up in TypeScript straight away.</summary>
    public static void UpdateApplication(Type[]? _)
    {
        if (DevelopmentOutput == null) return;
        try { WriteIfChanged(DevelopmentOutput, typeof(TypeScriptContracts).Assembly); }
        catch (Exception e) { Console.Error.WriteLine($"Could not update {RelativePath}: {e.Message}"); }
    }

    private sealed class Generator
    {
        private readonly NullabilityInfoContext _nullability = new();
        private readonly SortedDictionary<string, Type> _types = new(StringComparer.Ordinal);
        private readonly HashSet<Type> _inputs = [];
        private readonly HashSet<Type> _outputs = [];
        private readonly Queue<(Type Type, bool Input)> _pending = new();

        public string Run(IEnumerable<Type> controllers)
        {
            Add(typeof(Contracts.ErrorResponse), input: false); // every error, from every endpoint

            foreach (var controller in controllers.OrderBy(c => c.FullName, StringComparer.Ordinal))
            {
                if (controller.GetCustomAttribute<ApiExplorerSettingsAttribute>()?.IgnoreApi == true) continue;
                foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (action.IsSpecialName || action.GetCustomAttribute<NonActionAttribute>() != null) continue;
                    if (!action.GetCustomAttributes<HttpMethodAttribute>().Any()) continue;
                    if (action.GetCustomAttribute<ApiExplorerSettingsAttribute>()?.IgnoreApi == true) continue;
                    var where = $"{controller.Name}.{action.Name}";

                    foreach (var type in Responses(action, where)) Add(type, input: false);
                    foreach (var parameter in action.GetParameters().Where(IsBody)) Add(parameter.ParameterType, input: true);
                }
            }

            while (_pending.TryDequeue(out var next)) Visit(next.Type, next.Input);

            var output = new StringBuilder();
            output.Append("// GENERATED from the C# API's controllers by Infrastructure/TypeScriptContracts.cs.\n");
            output.Append("// Don't edit: change the C# types instead. ./dev.sh keeps this file up to date,\n");
            output.Append("// or run ./scripts/gen-types.sh. A test fails while it's out of date.\n");
            foreach (var (name, type) in _types)
            {
                output.Append('\n');
                output.Append($"/** C# {type.FullName} */\n");
                if (type.IsEnum)
                {
                    output.Append($"export type {name} = ")
                          .AppendJoin(" | ", Enum.GetNames(type).Select(n => $"'{n}'")).Append('\n');
                    continue;
                }
                var inputOnly = _inputs.Contains(type) && !_outputs.Contains(type);
                output.Append($"export interface {name} {{\n");
                foreach (var property in Properties(type))
                {
                    var info = _nullability.Create(property);
                    var ts = TypeOf(property.PropertyType, info, $"{type.Name}.{property.Name}", inputOnly);
                    // A request may leave out what's nullable; a response always has every field.
                    var optional = inputOnly && ts.EndsWith(" | null", StringComparison.Ordinal) ? "?" : "";
                    output.Append($"  {JsonName(property)}{optional}: {ts}\n");
                }
                output.Append("}\n");
            }
            return output.ToString();
        }

        private static IEnumerable<Type> Responses(MethodInfo action, string where)
        {
            var declared = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(a => a.Type).Where(t => t != typeof(void)).ToList();

            var type = action.ReturnType;
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
                type = type.GetGenericArguments()[0];
            else if (type == typeof(Task) || type == typeof(ValueTask) || type == typeof(void))
                return declared;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>))
                return declared.Prepend(type.GetGenericArguments()[0]);

            if (typeof(IActionResult).IsAssignableFrom(type))
            {
                if (!action.GetCustomAttributes<ProducesResponseTypeAttribute>().Any())
                    throw new InvalidOperationException(
                        $"{where} returns {type.Name}, which doesn't say what's in it. Return ActionResult<T>, " +
                        "or add [ProducesResponseType<T>(200)] (or [ProducesResponseType(204)] for no body).");
                return declared;
            }
            if (type == typeof(object))
                throw new InvalidOperationException($"{where} returns object. Return a record from Contracts/ instead.");
            return declared.Prepend(type);
        }

        private static bool IsBody(ParameterInfo p)
        {
            if (p.GetCustomAttribute<FromBodyAttribute>() != null) return true;
            if (p.GetCustomAttributes().Any(a => a is FromQueryAttribute or FromRouteAttribute or FromHeaderAttribute
                                                     or FromServicesAttribute or FromFormAttribute)) return false;
            // [ApiController] binds complex types from the body.
            return !IsScalar(p.ParameterType) && p.ParameterType != typeof(CancellationToken);
        }

        private void Add(Type type, bool input)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(object) || type == typeof(JsonElement)) return;
            if (DictionaryValue(type) is { } value) { Add(value, input); return; }
            if (ElementType(type) is { } element) { Add(element, input); return; }
            if (IsScalar(type) && !type.IsEnum) return;
            if ((input ? _inputs : _outputs).Add(type)) _pending.Enqueue((type, input));
        }

        private void Visit(Type type, bool input)
        {
            var name = type.Name;
            if (type.IsGenericType || name.Contains('<'))
                throw new InvalidOperationException($"{type} is generic or anonymous; give it a plain record in Contracts/.");
            if (_types.TryGetValue(name, out var existing) && existing != type)
                throw new InvalidOperationException($"Two API types are both called {name} ({existing.FullName}, {type.FullName}). Rename one.");
            _types[name] = type;
            if (type.IsEnum) return;
            foreach (var property in Properties(type)) Add(property.PropertyType, input);
        }

        private static IEnumerable<PropertyInfo> Properties(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod != null && p.GetIndexParameters().Length == 0
                            && p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition is not JsonIgnoreCondition.Always)
                .OrderBy(p => p.MetadataToken);

        private static string JsonName(PropertyInfo p) =>
            p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);

        private string TypeOf(Type type, NullabilityInfo? info, string where, bool inputOnly)
        {
            if (Nullable.GetUnderlyingType(type) is { } inner) return TypeOf(inner, null, where, inputOnly) + " | null";
            var nullable = !type.IsValueType && info?.ReadState == NullabilityState.Nullable;
            var ts = Plain(type, info, where, inputOnly);
            return nullable ? ts + " | null" : ts;
        }

        private string Plain(Type type, NullabilityInfo? info, string where, bool inputOnly)
        {
            if (type == typeof(bool)) return "boolean";
            if (IsNumber(type)) return "number";
            if (type.IsEnum) return type.Name;
            if (IsScalar(type)) return "string";
            if (type == typeof(JsonElement)) return "unknown";

            if (DictionaryValue(type) is { } value)
                return $"Record<string, {TypeOf(value, info?.GenericTypeArguments.ElementAtOrDefault(1), where, inputOnly)}>";
            if (ElementType(type) is { } element)
            {
                var elementInfo = type.IsArray ? info?.ElementType : info?.GenericTypeArguments.FirstOrDefault();
                var ts = TypeOf(element, elementInfo, where, inputOnly);
                return ts.Contains(' ') ? $"({ts})[]" : $"{ts}[]";
            }
            if (type == typeof(object)) throw new InvalidOperationException($"{where} is object; give it a real type.");
            return type.Name;
        }

        private static bool IsNumber(Type t) =>
            t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) || t == typeof(int) ||
            t == typeof(uint) || t == typeof(long) || t == typeof(ulong) || t == typeof(float) || t == typeof(double) ||
            t == typeof(decimal);

        /// <summary>Things JSON carries as a single value (the dates and ids as strings).</summary>
        private static bool IsScalar(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid) ||
                   t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly) ||
                   t == typeof(TimeOnly) || t == typeof(TimeSpan) || t == typeof(Uri);
        }

        private static Type? DictionaryValue(Type t)
        {
            var dictionary = t.IsGenericType && t.GetGenericTypeDefinition() is var d &&
                             (d == typeof(Dictionary<,>) || d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>))
                ? t : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
            if (dictionary == null) return null;
            var args = dictionary.GetGenericArguments();
            if (args[0] != typeof(string)) throw new InvalidOperationException($"{t} has non-string keys, which JSON can't carry.");
            return args[1];
        }

        private static Type? ElementType(Type t)
        {
            if (t == typeof(string) || DictionaryValue(t) != null) return null;
            if (t.IsArray) return t.GetElementType();
            if (!typeof(IEnumerable).IsAssignableFrom(t)) return null;
            var enumerable = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? t : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0];
        }
    }
}
