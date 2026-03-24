using System;
using System.Collections.Generic;

namespace MoleSurvivors.EditorTools
{
    public enum ConfigValidationSeverity
    {
        Info,
        Warning,
        Error,
    }

    [Serializable]
    public sealed class ConfigValidationIssue
    {
        public ConfigValidationSeverity Severity;
        public string Table;
        public int Row;
        public string Field;
        public string Message;
    }

    [Serializable]
    public sealed class ConfigValidationReport
    {
        public string SourcePath;
        public string CsvRootPath;
        public string CreatedAtIsoUtc;
        public int ErrorCount;
        public int WarningCount;
        public int InfoCount;
        public List<ConfigValidationIssue> Issues = new List<ConfigValidationIssue>();

        public void AddIssue(
            ConfigValidationSeverity severity,
            string table,
            int row,
            string field,
            string message)
        {
            ConfigValidationIssue issue = new ConfigValidationIssue
            {
                Severity = severity,
                Table = table ?? string.Empty,
                Row = row,
                Field = field ?? string.Empty,
                Message = message ?? string.Empty,
            };
            Issues.Add(issue);
            switch (severity)
            {
                case ConfigValidationSeverity.Error:
                    ErrorCount++;
                    break;
                case ConfigValidationSeverity.Warning:
                    WarningCount++;
                    break;
                default:
                    InfoCount++;
                    break;
            }
        }

        public string BuildSummary()
        {
            return $"Config validation done. errors={ErrorCount}, warnings={WarningCount}, info={InfoCount}";
        }
    }
}
