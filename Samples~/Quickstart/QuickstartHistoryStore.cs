#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartHistoryStore
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        private readonly string filePath;
        private readonly List<QuickstartHistoryRecord> records = new();

        internal QuickstartHistoryStore(string filePath)
        {
            this.filePath = string.IsNullOrWhiteSpace(filePath)
                ? throw new ArgumentException("History path cannot be empty.", nameof(filePath))
                : Path.GetFullPath(filePath);
        }

        internal IReadOnlyList<QuickstartHistoryRecord> Records => records;

        internal void Load()
        {
            records.Clear();
            RecoverInterruptedSave();
            if (!File.Exists(filePath))
            {
                return;
            }

            List<QuickstartHistoryRecord> loaded = DeserializeRecords(
                File.ReadAllText(filePath));
            records.AddRange(loaded);
            SortNewestFirst();
        }

        internal QuickstartHistoryRecord? Find(string submissionId)
        {
            return records.FirstOrDefault(
                record => string.Equals(
                    record.SubmissionId,
                    submissionId,
                    StringComparison.Ordinal));
        }

        internal void Upsert(QuickstartHistoryRecord record)
        {
            Validate(record);
            var updatedRecords = new List<QuickstartHistoryRecord>(records);
            int existingIndex = updatedRecords.FindIndex(
                candidate => string.Equals(
                    candidate.SubmissionId,
                    record.SubmissionId,
                    StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                updatedRecords[existingIndex] = record;
            }
            else
            {
                updatedRecords.Add(record);
            }

            SortNewestFirst(updatedRecords);
            Save(updatedRecords);
            ReplaceRecords(updatedRecords);
        }

        internal bool Remove(string submissionId)
        {
            var updatedRecords = new List<QuickstartHistoryRecord>(records);
            int removed = updatedRecords.RemoveAll(
                record => string.Equals(
                    record.SubmissionId,
                    submissionId,
                    StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            Save(updatedRecords);
            ReplaceRecords(updatedRecords);
            return true;
        }

        private static void Validate(QuickstartHistoryRecord record)
        {
            if (record == null)
            {
                throw new InvalidDataException("Quickstart history contains a null record.");
            }

            RequireValue(record.SubmissionId, "submission_id");
            RequireValue(record.ApiBaseUrl, "api_base_url");
            RequireValue(record.ResultWebSocketBaseUrl, "result_websocket_base_url");
            RequireValue(record.ScenarioJson, "scenario_json");
            _ = ParseSubmittedAt(record);
        }

        private static void RequireValue(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"Quickstart history field '{fieldName}' cannot be empty.");
            }
        }

        private static List<QuickstartHistoryRecord> DeserializeRecords(string json)
        {
            List<QuickstartHistoryRecord> loaded =
                JsonConvert.DeserializeObject<List<QuickstartHistoryRecord>>(
                    json,
                    SerializerSettings) ??
                throw new InvalidDataException("Quickstart history cannot be null.");
            foreach (QuickstartHistoryRecord record in loaded)
            {
                Validate(record);
            }

            return loaded;
        }

        private static DateTimeOffset ParseSubmittedAt(QuickstartHistoryRecord record)
        {
            if (!DateTimeOffset.TryParseExact(
                record.SubmittedAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset submittedAt))
            {
                throw new InvalidDataException(
                    "Quickstart history submitted_at_utc must use round-trip format.");
            }

            return submittedAt;
        }

        private void SortNewestFirst()
        {
            SortNewestFirst(records);
        }

        private static void SortNewestFirst(List<QuickstartHistoryRecord> targetRecords)
        {
            targetRecords.Sort(
                (left, right) => ParseSubmittedAt(right).CompareTo(ParseSubmittedAt(left)));
        }

        private void ReplaceRecords(IEnumerable<QuickstartHistoryRecord> updatedRecords)
        {
            records.Clear();
            records.AddRange(updatedRecords);
        }

        private void RecoverInterruptedSave()
        {
            string temporaryPath = filePath + ".tmp";
            if (!File.Exists(temporaryPath))
            {
                return;
            }

            try
            {
                _ = DeserializeRecords(File.ReadAllText(temporaryPath));
            }
            catch (JsonException)
            {
                File.Delete(temporaryPath);
                return;
            }
            catch (InvalidDataException)
            {
                File.Delete(temporaryPath);
                return;
            }

            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, null);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }

        private void Save(IReadOnlyCollection<QuickstartHistoryRecord> updatedRecords)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = filePath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            try
            {
                string json = JsonConvert.SerializeObject(
                    updatedRecords,
                    Formatting.Indented,
                    SerializerSettings);
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(filePath))
                {
                    File.Replace(temporaryPath, filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }
    }
}
