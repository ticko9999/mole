using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MoleSurvivors.EditorTools
{
    public static class MoleFactoryEditorTools
    {
        private const string ValidateMenu = "Tools/MoleFactory/Validate Config v5.2";
        private const string HotReloadMenu = "Tools/MoleFactory/Hot Reload Config (Play Mode)";
        private const string ImportArtMenu = "Tools/MoleFactory/Import Art Zips (Nano Banana)";

        [MenuItem(ValidateMenu)]
        public static void ValidateConfig()
        {
            ConfigValidationReport report = MoleFactoryConfigValidator.ValidateFactoryConfig();
            string reportPath = WriteReportToTemp(report);
            LogValidationSummary(report, reportPath);
            string title = report.ErrorCount > 0 ? "配置校验失败" : "配置校验完成";
            EditorUtility.DisplayDialog(
                title,
                $"{report.BuildSummary()}\n报告: {reportPath}",
                "知道了");
        }

        [MenuItem(HotReloadMenu)]
        public static void HotReloadConfigPlayMode()
        {
            ConfigValidationReport report = MoleFactoryConfigValidator.ValidateFactoryConfig();
            string reportPath = WriteReportToTemp(report);
            LogValidationSummary(report, reportPath);
            if (report.ErrorCount > 0)
            {
                EditorUtility.DisplayDialog(
                    "热重载已阻断",
                    $"配置存在错误，已阻断热重载。\n{report.BuildSummary()}\n报告: {reportPath}",
                    "知道了");
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "无法热重载",
                    "请先进入 Play Mode，再执行 Hot Reload。",
                    "知道了");
                return;
            }

            DemoGameController controller = UnityEngine.Object.FindObjectOfType<DemoGameController>();
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "无法热重载",
                    "当前场景没有找到 DemoGameController 运行实例。",
                    "知道了");
                return;
            }

            bool success = controller.EditorHotReloadFromConfig();
            string message = controller.LastEditorHotReloadMessage;
            if (!success)
            {
                EditorUtility.DisplayDialog(
                    "热重载失败",
                    $"{message}\n报告: {reportPath}",
                    "知道了");
                return;
            }

            Debug.Log($"[MoleSurvivors] Hot reload completed. {message}");
            EditorUtility.DisplayDialog(
                "热重载成功",
                $"{message}\n报告: {reportPath}",
                "好的");
        }

        [MenuItem(ImportArtMenu)]
        public static void ImportNanoBananaArtZips()
        {
            NanoBananaImportReport import = NanoBananaArtZipImporter.ImportDefaultZips(2048);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"目标目录: {import.TargetDirectory}");
            sb.AppendLine($"导入文件: {import.ImportedFiles}");
            sb.AppendLine($"缩放到2048: {import.ResizedFiles}");
            if (import.MissingZipPaths.Count > 0)
            {
                sb.AppendLine("缺失zip:");
                for (int i = 0; i < import.MissingZipPaths.Count; i++)
                {
                    sb.AppendLine("- " + import.MissingZipPaths[i]);
                }
            }

            Debug.Log("[MoleSurvivors] Art import done.\n" + sb);

            // Auto-apply new assets immediately when playing.
            if (EditorApplication.isPlaying)
            {
                HotReloadConfigPlayMode();
                return;
            }

            EditorUtility.DisplayDialog("美术导入完成", sb.ToString(), "知道了");
        }

        private static void LogValidationSummary(ConfigValidationReport report, string reportPath)
        {
            if (report.ErrorCount > 0)
            {
                Debug.LogError($"[MoleSurvivors] {report.BuildSummary()} report={reportPath}");
            }
            else if (report.WarningCount > 0)
            {
                Debug.LogWarning($"[MoleSurvivors] {report.BuildSummary()} report={reportPath}");
            }
            else
            {
                Debug.Log($"[MoleSurvivors] {report.BuildSummary()} report={reportPath}");
            }
        }

        private static string WriteReportToTemp(ConfigValidationReport report)
        {
            string safeTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(Path.GetTempPath(), $"mole_factory_validation_{safeTime}.txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Mole Factory Config Validation Report");
            sb.AppendLine("====================================");
            sb.AppendLine($"CreatedAt(UTC): {report.CreatedAtIsoUtc}");
            sb.AppendLine($"SourcePath: {report.SourcePath}");
            sb.AppendLine($"CsvRoot: {report.CsvRootPath}");
            sb.AppendLine(report.BuildSummary());
            sb.AppendLine();
            for (int i = 0; i < report.Issues.Count; i++)
            {
                ConfigValidationIssue issue = report.Issues[i];
                sb.Append('[').Append(issue.Severity).Append("] ")
                    .Append(issue.Table).Append(':').Append(issue.Row)
                    .Append(" field=").Append(issue.Field)
                    .Append(" -> ").Append(issue.Message).AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
