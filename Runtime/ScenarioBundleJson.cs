#nullable enable

using System;
using EmbodiedLab.Contracts;
using Newtonsoft.Json;

namespace EmbodiedLab.Unity
{
    /// <summary>
    /// Reads and writes the versioned EmbodiedLab scenario contract.
    /// </summary>
    public static class ScenarioBundleJson
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        public static string Serialize(ScenarioBundle scenario, bool indented = false)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            return JsonConvert.SerializeObject(
                scenario,
                indented ? Formatting.Indented : Formatting.None,
                SerializerSettings);
        }

        public static ScenarioBundle Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Scenario JSON cannot be empty.", nameof(json));
            }

            return JsonConvert.DeserializeObject<ScenarioBundle>(json, SerializerSettings) ??
                throw new JsonSerializationException(
                    "Scenario JSON did not contain a scenario bundle.");
        }
    }
}
