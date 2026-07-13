using System.Text;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ContractCodeGen <normalized-schema> <output>");
    return 2;
}

var schema = await JsonSchema.FromFileAsync(args[0]);
var settings = new CSharpGeneratorSettings
{
    Namespace = "EmbodiedLab.Contracts",
    ClassStyle = CSharpClassStyle.Poco,
    JsonLibrary = CSharpJsonLibrary.NewtonsoftJson,
    GenerateDataAnnotations = true,
    GenerateJsonMethods = false,
    ExcludedTypeNames = ["EmbodiedLabContracts"],
};

var generated = new CSharpGenerator(schema, settings)
    .GenerateFile()
    .Replace("\r\n", "\n", StringComparison.Ordinal)
    .TrimEnd() + "\n";
await File.WriteAllTextAsync(args[1], generated, new UTF8Encoding(false));
return 0;
