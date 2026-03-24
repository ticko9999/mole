using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace MoleSurvivors.EditorTools
{
    public sealed class NanoBananaImportReport
    {
        public string TargetDirectory;
        public int ImportedFiles;
        public int ResizedFiles;
        public readonly List<string> MissingZipPaths = new List<string>();
        public readonly List<string> ImportedOutputPaths = new List<string>();
    }

    public static class NanoBananaArtZipImporter
    {
        private static readonly string[] DefaultZipPaths =
        {
            "/Users/shiyuqian/Downloads/factory_master_art_pack.zip",
            "/Users/shiyuqian/Downloads/boss_bg_env.zip",
            "/Users/shiyuqian/Downloads/characters_part1.zip",
            "/Users/shiyuqian/Downloads/characters_part2.zip",
        };

        private static readonly string[] CanonicalMoleIds =
        {
            "mole_common",
            "mole_swift",
            "mole_tank",
            "mole_bomb",
            "mole_chest",
            "mole_chain",
            "mole_shield",
            "mole_elite",
        };

        public static NanoBananaImportReport ImportDefaultZips(int maxTextureSize = 2048)
        {
            string targetDirectory = Path.Combine(Application.dataPath, "Art", "Temp", "Round4_Nano");
            Directory.CreateDirectory(targetDirectory);

            NanoBananaImportReport report = new NanoBananaImportReport
            {
                TargetDirectory = targetDirectory,
            };

            for (int zipIndex = 0; zipIndex < DefaultZipPaths.Length; zipIndex++)
            {
                string zipPath = DefaultZipPaths[zipIndex];
                if (!File.Exists(zipPath))
                {
                    report.MissingZipPaths.Add(zipPath);
                    continue;
                }

                ImportSingleZip(zipPath, targetDirectory, maxTextureSize, report);
            }

            EnsureLegacyMoleFallbacks(targetDirectory);
            CopyDropFallbacks(targetDirectory);
            AssetDatabase.Refresh();
            return report;
        }

        private static void ImportSingleZip(
            string zipPath,
            string targetDirectory,
            int maxTextureSize,
            NanoBananaImportReport report)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string projectRootFullPath = Path.GetFullPath(projectRoot);
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            for (int i = 0; i < archive.Entries.Count; i++)
            {
                ZipArchiveEntry entry = archive.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                if (!entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string normalizedPath = (entry.FullName ?? string.Empty).Replace("\\", "/");
                string outputPath = ResolveOutputPath(normalizedPath, entry.Name, targetDirectory, projectRootFullPath);
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    continue;
                }

                string parentDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }

                using (Stream input = entry.Open())
                using (FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }

                report.ImportedFiles++;
                report.ImportedOutputPaths.Add(outputPath);
                if (ClampTexturePng(outputPath, maxTextureSize))
                {
                    report.ResizedFiles++;
                }
            }
        }

        private static string ResolveOutputPath(
            string fullName,
            string entryName,
            string targetDirectory,
            string projectRoot)
        {
            string normalizedPath = (fullName ?? string.Empty).Replace("\\", "/").TrimStart('/');
            if (normalizedPath.IndexOf("../", StringComparison.Ordinal) >= 0)
            {
                return string.Empty;
            }

            int assetsIndex = normalizedPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                string assetsRelativePath = normalizedPath.Substring(assetsIndex);
                string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetsRelativePath));
                if (!absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                return absolutePath;
            }

            string outputName = ResolveOutputFileName(fullName, entryName);
            if (string.IsNullOrWhiteSpace(outputName))
            {
                return string.Empty;
            }

            return Path.Combine(targetDirectory, outputName);
        }

        private static string ResolveOutputFileName(string fullName, string entryName)
        {
            string normalized = (fullName ?? string.Empty).ToLowerInvariant();
            string baseName = (entryName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return string.Empty;
            }

            if (normalized.Contains("/background/") && baseName.Equals("bg_factory_main.png", StringComparison.OrdinalIgnoreCase))
            {
                return "bg_factory_main.png";
            }

            if (normalized.Contains("/environment/") && baseName.Equals("hole_factory.png", StringComparison.OrdinalIgnoreCase))
            {
                return "hole_factory.png";
            }

            if (normalized.Contains("/bosses/"))
            {
                if (baseName.Equals("boss_foreman.png", StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("boss_rat_king.png", StringComparison.OrdinalIgnoreCase))
                {
                    return baseName;
                }
            }

            if (normalized.Contains("/characters/"))
            {
                return baseName;
            }

            return baseName;
        }

        private static void EnsureLegacyMoleFallbacks(string targetDirectory)
        {
            for (int i = 0; i < CanonicalMoleIds.Length; i++)
            {
                string id = CanonicalMoleIds[i];
                string idlePath = Path.Combine(targetDirectory, id + "_idle.png");
                string legacyPath = Path.Combine(targetDirectory, id + ".png");
                if (File.Exists(legacyPath) || !File.Exists(idlePath))
                {
                    continue;
                }

                File.Copy(idlePath, legacyPath, true);
            }
        }

        private static void CopyDropFallbacks(string targetDirectory)
        {
            string artRoot = Path.Combine(Application.dataPath, "Art");
            string artDropRoot = Path.Combine(Application.dataPath, "ArtDrop");

            CopyFirstAvailable(
                Path.Combine(targetDirectory, "drop_gold.png"),
                Path.Combine(artRoot, "Drops", "Gold", "drop_gold.png"),
                Path.Combine(artDropRoot, "drop_gold.png"));

            CopyFirstAvailable(
                Path.Combine(targetDirectory, "drop_exp.png"),
                Path.Combine(artRoot, "Drops", "Exp", "drop_exp.png"),
                Path.Combine(artDropRoot, "drop_exp.png"));

            CopyFirstAvailable(
                Path.Combine(targetDirectory, "drop_core.png"),
                Path.Combine(artRoot, "Drops", "Core", "drop_core.png"),
                Path.Combine(artDropRoot, "drop_core.png"));
        }

        private static void CopyFirstAvailable(string destinationPath, params string[] sourceCandidates)
        {
            if (File.Exists(destinationPath) || sourceCandidates == null)
            {
                return;
            }

            for (int i = 0; i < sourceCandidates.Length; i++)
            {
                string sourcePath = sourceCandidates[i];
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    continue;
                }

                File.Copy(sourcePath, destinationPath, true);
                return;
            }
        }

        private static bool ClampTexturePng(string filePath, int maxSize)
        {
            if (maxSize <= 0 || !File.Exists(filePath))
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(bytes, false))
                {
                    return false;
                }

                int maxDimension = Mathf.Max(source.width, source.height);
                if (maxDimension <= maxSize)
                {
                    return false;
                }

                float scale = maxSize / (float)maxDimension;
                int targetWidth = Mathf.Max(8, Mathf.RoundToInt(source.width * scale));
                int targetHeight = Mathf.Max(8, Mathf.RoundToInt(source.height * scale));

                RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
                    try
                    {
                        resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                        resized.Apply(false, false);
                        byte[] encoded = resized.EncodeToPNG();
                        File.WriteAllBytes(filePath, encoded);
                        return true;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(resized);
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
}
