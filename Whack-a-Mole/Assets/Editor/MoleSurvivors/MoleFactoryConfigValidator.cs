using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MoleSurvivors.EditorTools
{
    public static class MoleFactoryConfigValidator
    {
        private const string ConfigRootEnvVar = "MOLE_FACTORY_CONFIG_ROOT";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private sealed class CsvTableData
        {
            public string Name;
            public readonly List<string> Headers = new List<string>();
            public readonly List<Dictionary<string, string>> Rows = new List<Dictionary<string, string>>();
        }

        private sealed class ValidationRule
        {
            public string SheetName;
            public string FieldName;
            public string FieldType;
            public string EnumType;
            public string RefTable;
            public string RefField;
            public bool Required;
            public bool MultiValue;
        }

        public static ConfigValidationReport ValidateFactoryConfig(string configRootOverride = null)
        {
            ConfigValidationReport report = new ConfigValidationReport
            {
                CreatedAtIsoUtc = DateTime.UtcNow.ToString("O", Invariant),
            };

            string configRoot = ResolveConfigRoot(configRootOverride);
            report.SourcePath = configRoot ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Config",
                    0,
                    "Root",
                    "Config root not found. Set MOLE_FACTORY_CONFIG_ROOT or place data under Config/FactoryConfig_v5.2.");
                return report;
            }

            string csvRoot = ResolveCsvRoot(configRoot);
            report.CsvRootPath = csvRoot ?? string.Empty;
            if (string.IsNullOrWhiteSpace(csvRoot) || !Directory.Exists(csvRoot))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Config",
                    0,
                    "csv",
                    $"CSV folder missing under: {configRoot}");
                return report;
            }

            Dictionary<string, CsvTableData> tables = LoadCsvTables(csvRoot, report);
            if (tables.Count == 0)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Config",
                    0,
                    "csv",
                    $"No CSV files loaded from: {csvRoot}");
                return report;
            }

            if (!tables.TryGetValue("ValidationMap", out CsvTableData validationMap))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "ValidationMap",
                    0,
                    "Sheet",
                    "ValidationMap.csv is required for schema validation.");
                return report;
            }

            Dictionary<string, HashSet<string>> enumCodes = BuildEnumCodeLookup(tables, report);
            List<ValidationRule> rules = BuildValidationRules(validationMap, report);
            ValidateSchemaAndRows(tables, rules, enumCodes, report);
            ValidateRuntimeSemantics(tables, report);
            ValidateJsonMirror(configRoot, tables, report);
            return report;
        }

        private static string ResolveConfigRoot(string overrideRoot)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                candidates.Add(overrideRoot.Trim());
            }

            string env = Environment.GetEnvironmentVariable(ConfigRootEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                candidates.Add(env.Trim());
            }

            string projectRoot = null;
            try
            {
                projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            }
            catch
            {
                projectRoot = null;
            }

            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                candidates.Add(Path.Combine(projectRoot, "Config", "FactoryConfig_v5.2"));
                candidates.Add(Path.Combine(projectRoot, "Config", "地鼠工厂_配置导出版_v5.2_csv_json"));
                candidates.Add(Path.Combine(projectRoot, "Config"));
            }

            candidates.Add("/Users/shiyuqian/Downloads/地鼠工厂_配置导出版_v5.2_csv_json");

            for (int i = 0; i < candidates.Count; i++)
            {
                string root = candidates[i];
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string csvRoot = ResolveCsvRoot(root);
                if (!string.IsNullOrWhiteSpace(csvRoot) && Directory.Exists(csvRoot))
                {
                    return root;
                }
            }

            return string.Empty;
        }

        private static string ResolveCsvRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return string.Empty;
            }

            string rootName = Path.GetFileName(root);
            if (string.Equals(rootName, "csv", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            string nestedCsv = Path.Combine(root, "csv");
            if (Directory.Exists(nestedCsv))
            {
                return nestedCsv;
            }

            return File.Exists(Path.Combine(root, "Global.csv")) ? root : string.Empty;
        }

        private static Dictionary<string, CsvTableData> LoadCsvTables(string csvRoot, ConfigValidationReport report)
        {
            Dictionary<string, CsvTableData> tables = new Dictionary<string, CsvTableData>(StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(csvRoot, "*.csv");
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string filePath = files[fileIndex];
                string tableName = Path.GetFileNameWithoutExtension(filePath);
                try
                {
                    CsvTableData table = ReadTable(filePath, tableName);
                    tables[tableName] = table;
                }
                catch (Exception ex)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Error,
                        tableName,
                        0,
                        "Read",
                        $"Failed to read CSV: {ex.Message}");
                }
            }

            report.AddIssue(
                ConfigValidationSeverity.Info,
                "Config",
                0,
                "csv",
                $"Loaded {tables.Count} csv tables from {csvRoot}");
            return tables;
        }

        private static CsvTableData ReadTable(string filePath, string tableName)
        {
            CsvTableData table = new CsvTableData { Name = tableName };
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
            {
                return table;
            }

            List<string> headers = ParseCsvLine(lines[0]);
            if (headers.Count == 0)
            {
                return table;
            }

            headers[0] = TrimBom(headers[0]);
            for (int i = 0; i < headers.Count; i++)
            {
                table.Headers.Add((headers[i] ?? string.Empty).Trim());
            }

            for (int rowIndex = 1; rowIndex < lines.Length; rowIndex++)
            {
                string line = lines[rowIndex];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<string> values = ParseCsvLine(line);
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int col = 0; col < table.Headers.Count; col++)
                {
                    string header = table.Headers[col];
                    string value = col < values.Count ? values[col] : string.Empty;
                    row[header] = value?.Trim() ?? string.Empty;
                }

                row["__row"] = (rowIndex + 1).ToString(Invariant);
                table.Rows.Add(row);
            }

            return table;
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> values = new List<string>();
            if (line == null)
            {
                return values;
            }

            StringBuilder current = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        bool escaped = i + 1 < line.Length && line[i + 1] == '"';
                        if (escaped)
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (ch == ',')
                {
                    values.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }

                current.Append(ch);
            }

            values.Add(current.ToString());
            return values;
        }

        private static Dictionary<string, HashSet<string>> BuildEnumCodeLookup(
            Dictionary<string, CsvTableData> tables,
            ConfigValidationReport report)
        {
            Dictionary<string, HashSet<string>> lookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (!tables.TryGetValue("Enums", out CsvTableData enumsTable))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Warning,
                    "Enums",
                    0,
                    "Sheet",
                    "Enums.csv not found, enum checks will be skipped.");
                return lookup;
            }

            for (int i = 0; i < enumsTable.Rows.Count; i++)
            {
                Dictionary<string, string> row = enumsTable.Rows[i];
                string enumType = GetString(row, "EnumType");
                string code = GetString(row, "Code");
                if (string.IsNullOrWhiteSpace(enumType) || string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                if (!lookup.TryGetValue(enumType, out HashSet<string> set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    lookup[enumType] = set;
                }

                set.Add(code);
            }

            return lookup;
        }

        private static List<ValidationRule> BuildValidationRules(CsvTableData validationMap, ConfigValidationReport report)
        {
            List<ValidationRule> rules = new List<ValidationRule>();
            for (int i = 0; i < validationMap.Rows.Count; i++)
            {
                Dictionary<string, string> row = validationMap.Rows[i];
                string sheetName = GetString(row, "SheetName");
                string fieldName = GetString(row, "FieldName");
                if (string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                ValidationRule rule = new ValidationRule
                {
                    SheetName = sheetName,
                    FieldName = fieldName,
                    FieldType = GetString(row, "FieldType"),
                    EnumType = GetString(row, "EnumType"),
                    RefTable = GetString(row, "RefTable"),
                    RefField = GetString(row, "RefField"),
                    Required = ParseBool(GetString(row, "Required")),
                    MultiValue = ParseBool(GetString(row, "MultiValue")),
                };
                rules.Add(rule);
            }

            report.AddIssue(
                ConfigValidationSeverity.Info,
                "ValidationMap",
                0,
                "Rules",
                $"Loaded {rules.Count} validation rules.");
            return rules;
        }

        private static void ValidateSchemaAndRows(
            Dictionary<string, CsvTableData> tables,
            List<ValidationRule> rules,
            Dictionary<string, HashSet<string>> enumCodes,
            ConfigValidationReport report)
        {
            Dictionary<string, List<ValidationRule>> rulesBySheet = rules
                .GroupBy(rule => rule.SheetName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, List<ValidationRule>> sheetRules in rulesBySheet)
            {
                string sheetName = sheetRules.Key;
                List<ValidationRule> list = sheetRules.Value;
                if (!tables.TryGetValue(sheetName, out CsvTableData table))
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Error,
                        sheetName,
                        0,
                        "Sheet",
                        $"Sheet '{sheetName}' referenced by ValidationMap but csv file is missing.");
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    ValidationRule rule = list[i];
                    if (!table.Headers.Contains(rule.FieldName))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            sheetName,
                            1,
                            rule.FieldName,
                            $"Field '{rule.FieldName}' missing in table header.");
                    }
                }

                ValidatePrimaryKeys(table, list, report);
                ValidateRows(table, list, tables, enumCodes, report);
            }
        }

        private static void ValidatePrimaryKeys(CsvTableData table, List<ValidationRule> rules, ConfigValidationReport report)
        {
            List<ValidationRule> keyRules = rules
                .Where(rule => string.Equals(rule.FieldType, "pk", StringComparison.OrdinalIgnoreCase))
                .ToList();
            for (int i = 0; i < keyRules.Count; i++)
            {
                ValidationRule rule = keyRules[i];
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    Dictionary<string, string> row = table.Rows[rowIndex];
                    int rowNumber = GetRowNumber(row, rowIndex);
                    string value = GetString(row, rule.FieldName);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            table.Name,
                            rowNumber,
                            rule.FieldName,
                            "Primary key value is empty.");
                        continue;
                    }

                    if (seen.Contains(value))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            table.Name,
                            rowNumber,
                            rule.FieldName,
                            $"Duplicate primary key '{value}'.");
                    }
                    else
                    {
                        seen.Add(value);
                    }
                }
            }
        }

        private static void ValidateRows(
            CsvTableData table,
            List<ValidationRule> rules,
            Dictionary<string, CsvTableData> tables,
            Dictionary<string, HashSet<string>> enumCodes,
            ConfigValidationReport report)
        {
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                Dictionary<string, string> row = table.Rows[rowIndex];
                int rowNumber = GetRowNumber(row, rowIndex);
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    ValidationRule rule = rules[ruleIndex];
                    if (!row.TryGetValue(rule.FieldName, out string rawValue))
                    {
                        continue;
                    }

                    string value = rawValue?.Trim() ?? string.Empty;
                    if (rule.Required && string.IsNullOrWhiteSpace(value))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            table.Name,
                            rowNumber,
                            rule.FieldName,
                            "Required field is empty.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    List<string> tokens = rule.MultiValue ? SplitMulti(value) : new List<string> { value };
                    for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
                    {
                        string token = tokens[tokenIndex];
                        ValidateFieldType(table.Name, rowNumber, rule, token, enumCodes, report);
                        ValidateForeignKey(table.Name, rowNumber, rule, token, tables, report);
                    }

                    if (rule.MultiValue && value.Contains(",") && !value.Contains("|"))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Warning,
                            table.Name,
                            rowNumber,
                            rule.FieldName,
                            "Field is marked as multi-value but uses ',' instead of '|'.");
                    }
                }
            }
        }

        private static void ValidateFieldType(
            string tableName,
            int rowNumber,
            ValidationRule rule,
            string value,
            Dictionary<string, HashSet<string>> enumCodes,
            ConfigValidationReport report)
        {
            string fieldType = (rule.FieldType ?? string.Empty).Trim().ToLowerInvariant();
            switch (fieldType)
            {
                case "int":
                    if (!int.TryParse(value, NumberStyles.Integer, Invariant, out _) &&
                        !float.TryParse(value, NumberStyles.Float, Invariant, out _))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            tableName,
                            rowNumber,
                            rule.FieldName,
                            $"Invalid int value '{value}'.");
                    }

                    break;
                case "float":
                    if (!float.TryParse(value, NumberStyles.Float, Invariant, out _))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            tableName,
                            rowNumber,
                            rule.FieldName,
                            $"Invalid float value '{value}'.");
                    }

                    break;
                case "bool":
                    if (!ParseBoolStrict(value, out _))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            tableName,
                            rowNumber,
                            rule.FieldName,
                            $"Invalid bool value '{value}'. Expected 0/1/Y/N/TRUE/FALSE.");
                    }

                    break;
                case "enum":
                    if (string.IsNullOrWhiteSpace(rule.EnumType))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Warning,
                            tableName,
                            rowNumber,
                            rule.FieldName,
                            "Enum field has no EnumType in ValidationMap.");
                        break;
                    }

                    if (!enumCodes.TryGetValue(rule.EnumType, out HashSet<string> allowed) || allowed.Count == 0)
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Warning,
                            tableName,
                            rowNumber,
                            rule.FieldName,
                            $"EnumType '{rule.EnumType}' not found in Enums table.");
                        break;
                    }

                    if (!allowed.Contains(value))
                    {
                        report.AddIssue(
                            ConfigValidationSeverity.Error,
                            tableName,
                            rowNumber,
                            rule.FieldName,
                            $"Enum value '{value}' is not in EnumType '{rule.EnumType}'.");
                    }

                    break;
            }
        }

        private static void ValidateForeignKey(
            string tableName,
            int rowNumber,
            ValidationRule rule,
            string value,
            Dictionary<string, CsvTableData> tables,
            ConfigValidationReport report)
        {
            if (!string.Equals(rule.FieldType, "fk", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(rule.RefTable))
            {
                return;
            }

            if (!tables.TryGetValue(rule.RefTable, out CsvTableData refTable))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    tableName,
                    rowNumber,
                    rule.FieldName,
                    $"RefTable '{rule.RefTable}' not found.");
                return;
            }

            string refField = !string.IsNullOrWhiteSpace(rule.RefField)
                ? rule.RefField
                : (refTable.Headers.Count > 0 ? refTable.Headers[0] : string.Empty);
            if (string.IsNullOrWhiteSpace(refField) || !refTable.Headers.Contains(refField))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    tableName,
                    rowNumber,
                    rule.FieldName,
                    $"RefField '{refField}' not found in RefTable '{rule.RefTable}'.");
                return;
            }

            bool matched = refTable.Rows.Any(row => string.Equals(GetString(row, refField), value, StringComparison.OrdinalIgnoreCase));
            if (!matched)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    tableName,
                    rowNumber,
                    rule.FieldName,
                    $"FK value '{value}' not found in {rule.RefTable}.{refField}.");
            }
        }

        private static void ValidateRuntimeSemantics(Dictionary<string, CsvTableData> tables, ConfigValidationReport report)
        {
            List<string> diagnostics = new List<string>();
            bool loaded = ConfigDrivenContentLoader.TryLoad(out GameContent content, out string summary, diagnostics);
            if (!loaded || content == null)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "TryLoad",
                    $"ConfigDrivenContentLoader failed: {summary}");
                return;
            }

            report.AddIssue(
                ConfigValidationSeverity.Info,
                "Runtime",
                0,
                "TryLoad",
                $"Content loaded: {summary}");

            if (content.Weapons.Count == 0 || content.Characters.Count == 0 || content.Moles.Count == 0)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "CoreLists",
                    "Weapons/Characters/Moles must all be non-empty.");
            }

            if (tables.TryGetValue("Upgrades", out CsvTableData upgradesTable))
            {
                List<Dictionary<string, string>> runtimeUpgradeRows = upgradesTable.Rows
                    .Where(row => IsRuntimeUpgradePoolForValidation(GetString(row, "Pool")))
                    .ToList();

                int placeholderCount = runtimeUpgradeRows.Count(IsPlaceholderUpgradeRow);
                if (placeholderCount > 0)
                {
                    string sampleIds = string.Join(", ",
                        runtimeUpgradeRows
                            .Where(IsPlaceholderUpgradeRow)
                            .Select(row => GetString(row, "UpgradeID"))
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .Take(5));
                    report.AddIssue(
                        ConfigValidationSeverity.Error,
                        "Upgrades",
                        0,
                        "Pool",
                        $"Playable pools contain {placeholderCount} placeholder upgrades (e.g. {sampleIds}). Move them out of runtime pools.");
                }

                int skillTextCount = runtimeUpgradeRows.Count(row =>
                    (GetString(row, "Description") ?? string.Empty)
                    .IndexOf("给与技能", StringComparison.Ordinal) >= 0);
                if (skillTextCount > 0)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Warning,
                        "Upgrades",
                        0,
                        "Description",
                        $"Playable pools contain {skillTextCount} rows using '给与技能' placeholder wording. Recommend replacing with player-facing effect text.");
                }

                if (runtimeUpgradeRows.Count < 3)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Error,
                        "Upgrades",
                        0,
                        "Pool",
                        "Playable pools must contain at least 3 upgrades.");
                }
            }

            if (string.IsNullOrWhiteSpace(content.DefaultWeaponId) ||
                !content.Weapons.Any(weapon => weapon != null && weapon.Id == content.DefaultWeaponId))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "DefaultWeaponId",
                    "DefaultWeaponId is empty or does not exist in Weapons.");
            }

            if (string.IsNullOrWhiteSpace(content.DefaultCharacterId) ||
                !content.Characters.Any(character => character != null && character.Id == content.DefaultCharacterId))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "DefaultCharacterId",
                    "DefaultCharacterId is empty or does not exist in Characters.");
            }

            BossEncounterService bossEncounterService = new BossEncounterService();
            List<BossEncounterRuntime> timeline = bossEncounterService.CreateTimeline(content);
            bool hasFinalBoss = timeline.Any(encounter => encounter != null && encounter.Def != null && encounter.Def.IsFinalBoss);
            if (!hasFinalBoss)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "BossTimeline",
                    "Boss timeline must contain at least one final boss encounter.");
            }

            if (content.Upgrades.Count < 3)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "Upgrades",
                    "At least 3 upgrades are required for upgrade offer generation.");
            }
            else if (content.Upgrades.Count < 40 || content.Upgrades.Count > 220)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Warning,
                    "Runtime",
                    0,
                    "Upgrades",
                    $"Runtime upgrade count is {content.Upgrades.Count}; recommended range is 40-220 for current config set.");
            }

            UpgradeOfferService upgradeService = new UpgradeOfferService();
            RunState probeRun = new RunState
            {
                WeaponId = content.DefaultWeaponId,
                CharacterId = content.DefaultCharacterId,
            };
            List<UpgradeDef> probeOffer = upgradeService.BuildOffer(content, probeRun, new System.Random(7));
            if (probeOffer == null || probeOffer.Count < 3)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Runtime",
                    0,
                    "UpgradeOffer",
                    "Failed to produce a valid 3-choice upgrade offer in probe run.");
            }
            else
            {
                ValidateProgressionWindows(content, upgradeService, report);
            }

            if (!tables.TryGetValue("Global", out CsvTableData globalTable) || globalTable.Rows.Count == 0)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Error,
                    "Global",
                    0,
                    "Table",
                    "Global table is missing or empty.");
            }
        }

        private static void ValidateProgressionWindows(
            GameContent content,
            UpgradeOfferService upgradeService,
            ConfigValidationReport report)
        {
            (float second, string label)[] checkpoints =
            {
                (30f, "30s"),
                (100f, "100s"),
                (240f, "240s"),
                (420f, "420s"),
            };

            for (int i = 0; i < checkpoints.Length; i++)
            {
                (float second, string label) checkpoint = checkpoints[i];
                RunState snapshot = new RunState
                {
                    WeaponId = content.DefaultWeaponId,
                    CharacterId = content.DefaultCharacterId,
                    ElapsedSeconds = checkpoint.second,
                };

                List<UpgradeDef> offer = upgradeService.BuildOffer(content, snapshot, new System.Random(19 + i * 17));
                if (offer == null || offer.Count < 3)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Error,
                        "Upgrades",
                        0,
                        $"Progression@{checkpoint.label}",
                        $"Offer generation failed at checkpoint {checkpoint.label}.");
                    continue;
                }

                int epicOrLegendary = offer.Count(def => def != null && def.Rarity >= Rarity.Epic);
                int automationCount = offer.Count(def => def != null && def.IsAutomation);
                int droneCount = offer.Count(def => def != null && def.EffectType == UpgradeEffectType.AddDroneCount);
                if (checkpoint.second <= 60f && epicOrLegendary > 0)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Warning,
                        "Upgrades",
                        0,
                        $"Progression@{checkpoint.label}",
                        $"Opening offer contains {epicOrLegendary} epic+ options; consider lowering early spike.");
                }

                if (checkpoint.second <= 120f && droneCount > 0)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Warning,
                        "Upgrades",
                        0,
                        $"Progression@{checkpoint.label}",
                        "Drone growth appears too early; consider delaying drone-related upgrades.");
                }

                if (checkpoint.second >= 90f && checkpoint.second <= 120f && automationCount <= 0)
                {
                    report.AddIssue(
                        ConfigValidationSeverity.Warning,
                        "Upgrades",
                        0,
                        $"Progression@{checkpoint.label}",
                        "No automation relief option appears in 90-120s checkpoint.");
                }
            }
        }

        private static bool IsRuntimeUpgradePoolForValidation(string pool)
        {
            if (string.IsNullOrWhiteSpace(pool))
            {
                return true;
            }

            string normalized = pool.Trim();
            return string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Runtime", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Release", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Live", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Prod", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlaceholderUpgradeRow(Dictionary<string, string> row)
        {
            string id = GetString(row, "UpgradeID");
            if (!string.IsNullOrWhiteSpace(id) &&
                id.StartsWith("UPG_MISC_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string description = GetString(row, "Description");
            return !string.IsNullOrWhiteSpace(description) &&
                   description.IndexOf("向泛用条目", StringComparison.Ordinal) >= 0;
        }

        private static void ValidateJsonMirror(
            string configRoot,
            Dictionary<string, CsvTableData> tables,
            ConfigValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(configRoot))
            {
                return;
            }

            string jsonRoot = Path.Combine(configRoot, "json");
            if (!Directory.Exists(jsonRoot))
            {
                report.AddIssue(
                    ConfigValidationSeverity.Warning,
                    "json",
                    0,
                    "Folder",
                    "JSON mirror folder is missing. CSV can still be used.");
                return;
            }

            int missing = 0;
            foreach (string tableName in tables.Keys)
            {
                string path = Path.Combine(jsonRoot, tableName + ".json");
                if (!File.Exists(path))
                {
                    missing++;
                    report.AddIssue(
                        ConfigValidationSeverity.Warning,
                        "json",
                        0,
                        tableName,
                        $"Missing mirrored json file: {tableName}.json");
                }
            }

            if (missing == 0)
            {
                report.AddIssue(
                    ConfigValidationSeverity.Info,
                    "json",
                    0,
                    "Mirror",
                    "JSON mirror exists for all loaded CSV tables.");
            }
        }

        private static int GetRowNumber(Dictionary<string, string> row, int fallbackIndex)
        {
            if (row != null &&
                row.TryGetValue("__row", out string raw) &&
                int.TryParse(raw, NumberStyles.Integer, Invariant, out int parsed))
            {
                return parsed;
            }

            return fallbackIndex + 2;
        }

        private static bool ParseBool(string raw)
        {
            return ParseBoolStrict(raw, out bool parsed) && parsed;
        }

        private static bool ParseBoolStrict(string raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string normalized = raw.Trim();
            if (normalized == "1" || normalized.Equals("Y", StringComparison.OrdinalIgnoreCase) || normalized.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (normalized == "0" ||
                normalized.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
                normalized == "否")
            {
                value = false;
                return true;
            }

            if (normalized == "是")
            {
                value = true;
                return true;
            }

            return false;
        }

        private static List<string> SplitMulti(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static string GetString(Dictionary<string, string> row, string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return row.TryGetValue(key, out string value) ? (value ?? string.Empty) : string.Empty;
        }

        private static string TrimBom(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text[0] == '\uFEFF' ? text.Substring(1) : text;
        }
    }
}
