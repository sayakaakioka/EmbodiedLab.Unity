#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EmbodiedLab.Unity.Tests
{
    internal static class ContractJsonAssertions
    {
        public static void AssertPreserved(
            JToken expected,
            JToken actual,
            JObject schema,
            string typeName)
        {
            AssertPreserved(expected, actual, schema, schema, "$", typeName);
        }

        private static void AssertPreserved(
            JToken expected,
            JToken actual,
            JObject schema,
            JObject schemaRoot,
            string path,
            string typeName)
        {
            var resolvedSchema = ResolveSchema(schema, schemaRoot, actual);
            if (expected is JObject expectedObject && actual is JObject actualObject)
            {
                AssertObjectPreserved(
                    expectedObject,
                    actualObject,
                    resolvedSchema,
                    schemaRoot,
                    path,
                    typeName);
                return;
            }

            if (expected is JArray expectedArray && actual is JArray actualArray)
            {
                AssertArrayPreserved(
                    expectedArray,
                    actualArray,
                    resolvedSchema,
                    schemaRoot,
                    path,
                    typeName);
                return;
            }

            if (!JToken.DeepEquals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"{typeName} changed canonical JSON value at '{path}'. " +
                    $"Expected {expected}, but received {actual}.");
            }
        }

        private static void AssertObjectPreserved(
            JObject expected,
            JObject actual,
            JObject schema,
            JObject schemaRoot,
            string path,
            string typeName)
        {
            foreach (var property in expected.Properties())
            {
                var propertySchema = GetPropertySchema(schema, property.Name);
                var actualProperty = actual.Property(property.Name);
                if (
                    actualProperty == null &&
                    property.Value.Type == JTokenType.Null &&
                    AllowsNull(propertySchema, schemaRoot))
                {
                    continue;
                }

                if (actualProperty == null)
                {
                    throw new InvalidOperationException(
                        $"{typeName} dropped canonical JSON property " +
                        $"'{path}.{property.Name}'.");
                }

                AssertPreserved(
                    property.Value,
                    actualProperty.Value,
                    propertySchema,
                    schemaRoot,
                    $"{path}.{property.Name}",
                    typeName);
            }

            foreach (var property in actual.Properties())
            {
                if (expected.Property(property.Name) != null)
                {
                    continue;
                }

                var propertySchema = GetPropertySchema(schema, property.Name);
                var defaultValue = GetDefault(propertySchema, schemaRoot);
                if (
                    defaultValue == null ||
                    !JToken.DeepEquals(defaultValue, property.Value))
                {
                    throw new InvalidOperationException(
                        $"{typeName} added non-default JSON property " +
                        $"'{path}.{property.Name}' with value {property.Value}.");
                }
            }
        }

        private static void AssertArrayPreserved(
            JArray expected,
            JArray actual,
            JObject schema,
            JObject schemaRoot,
            string path,
            string typeName)
        {
            if (expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    $"{typeName} changed canonical JSON array length at '{path}'.");
            }

            var itemSchema = schema["items"] as JObject ?? new JObject();
            for (var index = 0; index < expected.Count; index++)
            {
                AssertPreserved(
                    expected[index],
                    actual[index],
                    itemSchema,
                    schemaRoot,
                    $"{path}[{index}]",
                    typeName);
            }
        }

        private static JObject GetPropertySchema(JObject schema, string propertyName)
        {
            if (schema["properties"] is JObject properties)
            {
                if (properties[propertyName] is JObject propertySchema)
                {
                    return propertySchema;
                }
            }

            if (schema["additionalProperties"] is JObject additionalProperties)
            {
                return additionalProperties;
            }

            if (schema["additionalProperties"]?.Value<bool>() == false)
            {
                throw new InvalidOperationException(
                    $"JSON property '{propertyName}' is not declared by the canonical schema.");
            }

            return new JObject();
        }

        private static JToken? GetDefault(JObject schema, JObject schemaRoot)
        {
            if (schema.TryGetValue("default", out var defaultValue))
            {
                return defaultValue;
            }

            if (schema["$ref"]?.Value<string>() is string reference)
            {
                return GetDefault(ResolveReference(reference, schemaRoot), schemaRoot);
            }

            return null;
        }

        private static bool AllowsNull(JObject schema, JObject schemaRoot)
        {
            if (schema["type"]?.Value<string>() == "null")
            {
                return true;
            }

            if (schema["$ref"]?.Value<string>() is string reference)
            {
                return AllowsNull(ResolveReference(reference, schemaRoot), schemaRoot);
            }

            return schema["anyOf"] is JArray alternatives &&
                alternatives
                    .OfType<JObject>()
                    .Any(alternative => AllowsNull(alternative, schemaRoot));
        }

        private static JObject ResolveSchema(
            JObject schema,
            JObject schemaRoot,
            JToken actual)
        {
            while (true)
            {
                if (schema["$ref"]?.Value<string>() is string reference)
                {
                    schema = ResolveReference(reference, schemaRoot);
                    continue;
                }

                if (schema["anyOf"] is JArray alternatives)
                {
                    schema = SelectNullableAlternative(
                        alternatives,
                        schemaRoot,
                        actual);
                    continue;
                }

                if (schema["oneOf"] is JArray variants)
                {
                    if (schema["discriminator"] is null)
                    {
                        ValidateSemanticOneOfShape(schema, variants);
                        return schema;
                    }

                    schema = SelectDiscriminatedVariant(
                        schema,
                        variants,
                        schemaRoot,
                        actual);
                    continue;
                }

                return schema;
            }
        }

        private static void ValidateSemanticOneOfShape(JObject schema, JArray variants)
        {
            var title = schema["title"]?.Value<string>();
            JArray expected = title switch
            {
                "ReplayLogStep" => CreateExpectedReplayStepBranches(),
                "ResultBundle" => CreateExpectedResultBundleBranches(),
                "ResultDocument" => CreateExpectedResultDocumentBranches(),
                _ => throw new InvalidOperationException(
                    $"Unsupported non-discriminated oneOf schema: {title ?? "<untitled>"}"),
            };

            if (!JToken.DeepEquals(variants, expected))
            {
                throw new InvalidOperationException(
                    $"Canonical semantic oneOf content changed for {title}.");
            }
        }

        private static JArray CreateExpectedReplayStepBranches()
        {
            return new JArray(
                CreatePropertiesBranch(
                    ("phase", new JObject { ["const"] = "train" }),
                    ("policy_mode", new JObject { ["const"] = "stochastic" })),
                CreatePropertiesBranch(
                    ("phase", new JObject { ["const"] = "eval" }),
                    ("policy_mode", new JObject { ["const"] = "deterministic" })));
        }

        private static JArray CreateExpectedResultBundleBranches()
        {
            return new JArray(
                CreatePropertiesBranch(
                    ("artifacts", new JObject
                    {
                        ["properties"] = new JObject
                        {
                            ["onnx_model"] = new JObject
                            {
                                ["not"] = new JObject { ["type"] = "null" },
                            },
                            ["replay_bundle"] = new JObject
                            {
                                ["not"] = new JObject { ["type"] = "null" },
                            },
                        },
                    }),
                    ("error", new JObject { ["type"] = "null" }),
                    ("status", new JObject { ["const"] = "completed" }),
                    ("summary", new JObject
                    {
                        ["not"] = new JObject { ["type"] = "null" },
                    })),
                CreatePropertiesBranch(
                    ("artifacts", new JObject
                    {
                        ["properties"] = new JObject
                        {
                            ["onnx_model"] = new JObject { ["type"] = "null" },
                            ["replay_bundle"] = new JObject { ["type"] = "null" },
                        },
                    }),
                    ("error", new JObject
                    {
                        ["not"] = new JObject { ["type"] = "null" },
                    }),
                    ("status", new JObject { ["const"] = "failed" }),
                    ("summary", new JObject { ["type"] = "null" })));
        }

        private static JArray CreateExpectedResultDocumentBranches()
        {
            var branches = new JArray();
            foreach (string status in new[]
            {
                "queued",
                "starting",
                "running",
                "cancelling",
                "cancelled",
            })
            {
                branches.Add(CreateResultDocumentBranch(
                    status,
                    new JObject { ["type"] = "null" },
                    new JObject { ["type"] = "null" }));
            }

            branches.Add(CreateResultDocumentBranch(
                "completed",
                new JObject { ["type"] = "null" },
                new JObject
                {
                    ["properties"] = new JObject
                    {
                        ["status"] = new JObject { ["const"] = "completed" },
                    },
                    ["required"] = new JArray("status"),
                    ["type"] = "object",
                }));
            branches.Add(CreateResultDocumentBranch(
                "failed",
                new JObject { ["minLength"] = 1, ["type"] = "string" },
                new JObject
                {
                    ["anyOf"] = new JArray(
                        new JObject { ["type"] = "null" },
                        new JObject
                        {
                            ["properties"] = new JObject
                            {
                                ["status"] = new JObject { ["const"] = "failed" },
                            },
                            ["required"] = new JArray("status"),
                            ["type"] = "object",
                        }),
                }));
            return branches;
        }

        private static JObject CreateResultDocumentBranch(
            string status,
            JObject error,
            JObject resultBundle)
        {
            return CreatePropertiesBranch(
                ("error", error),
                ("progress", new JObject
                {
                    ["properties"] = new JObject
                    {
                        ["phase"] = new JObject { ["const"] = status },
                    },
                    ["required"] = new JArray("phase"),
                    ["type"] = "object",
                }),
                ("result_bundle", resultBundle),
                ("status", new JObject { ["const"] = status }));
        }

        private static JObject CreatePropertiesBranch(
            params (string Name, JObject Schema)[] properties)
        {
            var value = new JObject();
            foreach ((string name, JObject propertySchema) in properties)
            {
                value[name] = propertySchema;
            }

            return new JObject { ["properties"] = value };
        }

        private static JObject SelectNullableAlternative(
            JArray alternatives,
            JObject schemaRoot,
            JToken actual)
        {
            var actualIsNull = actual.Type == JTokenType.Null;
            var matching = alternatives
                .OfType<JObject>()
                .Select(alternative => ResolveSchema(alternative, schemaRoot, actual))
                .Where(alternative =>
                    (alternative["type"]?.Value<string>() == "null") == actualIsNull)
                .ToArray();
            if (matching.Length != 1)
            {
                throw new InvalidOperationException(
                    "Could not select the canonical nullable schema alternative.");
            }

            return matching[0];
        }

        private static JObject SelectDiscriminatedVariant(
            JObject schema,
            JArray variants,
            JObject schemaRoot,
            JToken actual)
        {
            var discriminatorProperty = schema["discriminator"]?["propertyName"]?
                .Value<string>() ?? throw new InvalidOperationException(
                    "Canonical discriminated union has no propertyName.");
            var discriminator = actual[discriminatorProperty];
            var matching = variants
                .OfType<JObject>()
                .Select(variant => ResolveSchema(variant, schemaRoot, actual))
                .Where(variant => JToken.DeepEquals(
                    variant["properties"]?[discriminatorProperty]?["const"],
                    discriminator))
                .ToArray();
            if (matching.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Could not select the canonical schema for discriminator " +
                    $"{discriminatorProperty}={discriminator}.");
            }

            return matching[0];
        }

        private static JObject ResolveReference(
            string reference,
            JObject schemaRoot)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Only local schema references are supported: {reference}");
            }

            JToken current = schemaRoot;
            foreach (var rawSegment in reference.Substring(2).Split('/'))
            {
                var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
                current = current[segment] ?? throw new InvalidOperationException(
                    $"Could not resolve canonical schema reference: {reference}");
            }

            return current as JObject ?? throw new InvalidOperationException(
                $"Canonical schema reference is not an object: {reference}");
        }
    }
}
