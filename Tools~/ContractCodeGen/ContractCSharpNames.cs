using System.Text;
using NJsonSchema;
using NJsonSchema.CodeGeneration;

internal sealed class ContractPropertyNameGenerator : IPropertyNameGenerator
{
    public string Generate(JsonSchemaProperty property)
    {
        return ContractCSharpIdentifier.ToPascalCase(property.Name);
    }
}

internal sealed class ContractTypeNameGenerator : ITypeNameGenerator
{
    private readonly DefaultTypeNameGenerator _inner = new();

    public string Generate(
        JsonSchema schema,
        string? typeNameHint,
        IEnumerable<string> reservedTypeNames)
    {
        var normalizedHint = string.IsNullOrEmpty(typeNameHint)
            ? typeNameHint
            : ContractCSharpIdentifier.ToPascalCase(typeNameHint);
        return _inner.Generate(schema, normalizedHint, reservedTypeNames);
    }
}

internal sealed class ContractEnumNameGenerator : IEnumNameGenerator
{
    public string Generate(int index, string? name, object? value, JsonSchema schema)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException(
                $"Contract enum '{schema.Title}' contains an empty value at index {index}.");
        }

        return ContractCSharpIdentifier.ToPascalCase(name);
    }
}

internal static class ContractCSharpIdentifier
{
    public static string ToPascalCase(string value)
    {
        var result = new StringBuilder(value.Length);
        var capitalize = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                capitalize = true;
                continue;
            }

            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        if (result.Length == 0)
        {
            throw new InvalidOperationException(
                $"Contract name '{value}' cannot form a C# identifier.");
        }

        if (char.IsDigit(result[0]))
        {
            result.Insert(0, '_');
        }

        return result.ToString();
    }
}
