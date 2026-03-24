using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoleSurvivors
{
    public sealed class ExternalArtPackReport
    {
        public bool Applied;
        public string PackDirectory;
        public int LoadedSprites;
        public readonly List<string> LoadedFiles = new List<string>();
    }

    public static class ExternalArtPackLoader
    {
        private const int RuntimeMaxTextureSize = 2048;

        private sealed class MoleSpriteFileSet
        {
            public string Legacy;
            public string Idle;
            public string Hit;
            public string HitAlt;
            public string Recover;
            public string[] Aliases;
        }

        private static readonly string[] FallbackRelativeFolders =
        {
            "Art/Temp/Round4_Nano",
            "Art/Temp/NanoBanana",
            "Art/Temp",
            "ArtDrop/Round4_Nano",
            "ArtDrop/NanoBanana",
            "ArtDrop",
        };

        private static readonly Dictionary<string, MoleSpriteFileSet> MoleFilesById = new Dictionary<string, MoleSpriteFileSet>
        {
            {
                "mole_common",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_common.png",
                    Idle = "mole_common_idle.png",
                    Hit = "mole_common_hit_01.png",
                    HitAlt = "mole_common_hit_02.png",
                    Recover = "mole_common_recover.png",
                    Aliases = new[] { "MOLE_COMMON_01" },
                }
            },
            {
                "mole_swift",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_swift.png",
                    Idle = "mole_swift_idle.png",
                    Hit = "mole_swift_hit_01.png",
                    HitAlt = "mole_swift_hit_02.png",
                    Recover = "mole_swift_recover.png",
                    Aliases = new[] { "MOLE_SWIFT_01" },
                }
            },
            {
                "mole_tank",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_tank.png",
                    Idle = "mole_tank_idle.png",
                    Hit = "mole_tank_hit_01.png",
                    HitAlt = "mole_tank_hit_02.png",
                    Recover = "mole_tank_recover.png",
                    Aliases = new[] { "MOLE_TANK_01" },
                }
            },
            {
                "mole_bomb",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_bomb.png",
                    Idle = "mole_bomb_idle.png",
                    Hit = "mole_bomb_hit_01.png",
                    HitAlt = "mole_bomb_hit_02.png",
                    Recover = "mole_bomb_recover.png",
                    Aliases = new[] { "MOLE_BOMB_01" },
                }
            },
            {
                "mole_chest",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_chest.png",
                    Idle = "mole_chest_idle.png",
                    Hit = "mole_chest_hit_01.png",
                    HitAlt = "mole_chest_hit_02.png",
                    Recover = "mole_chest_recover.png",
                    Aliases = new[] { "MOLE_CHEST_01" },
                }
            },
            {
                "mole_chain",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_chain.png",
                    Idle = "mole_chain_idle.png",
                    Hit = "mole_chain_hit_01.png",
                    HitAlt = "mole_chain_hit_02.png",
                    Recover = "mole_chain_recover.png",
                    Aliases = new[] { "MOLE_CHAIN_01" },
                }
            },
            {
                "mole_shield",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_shield.png",
                    Idle = "mole_shield_idle.png",
                    Hit = "mole_shield_hit_01.png",
                    HitAlt = "mole_shield_hit_02.png",
                    Recover = "mole_shield_recover.png",
                    Aliases = new[] { "MOLE_SHIELD_01" },
                }
            },
            {
                "mole_elite",
                new MoleSpriteFileSet
                {
                    Legacy = "mole_elite.png",
                    Idle = "mole_elite_idle.png",
                    Hit = "mole_elite_hit_01.png",
                    HitAlt = "mole_elite_hit_02.png",
                    Recover = "mole_elite_recover.png",
                    Aliases = new[] { "MOLE_ELITE_01" },
                }
            },
        };

        private static readonly Dictionary<DropType, string> DropFilesByType = new Dictionary<DropType, string>
        {
            { DropType.Gold, "drop_gold.png" },
            { DropType.Experience, "drop_exp.png" },
            { DropType.Core, "drop_core.png" },
        };

        public static ExternalArtPackReport TryApply(PresentationSkin skin, string preferredRelativeFolder)
        {
            ExternalArtPackReport report = new ExternalArtPackReport();
            if (skin == null)
            {
                return report;
            }

            EnsureSkinCollections(skin);
            string packDirectory = ResolvePackDirectory(preferredRelativeFolder);
            if (string.IsNullOrWhiteSpace(packDirectory))
            {
                return report;
            }

            report.PackDirectory = packDirectory;

            TryApplyBaseSprite(packDirectory, "bg_factory_main.png", 1f, false, sprite => skin.BackgroundSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "hole_factory.png", 0.38f, true, sprite => skin.HoleSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "boss_foreman.png", 1.28f, true, sprite => skin.MidBossSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "boss_rat_king.png", 1.38f, true, sprite => skin.BossSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "event_merchant.png", 0.32f, true, sprite => skin.EventMerchantSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "event_treasure.png", 0.32f, true, sprite => skin.EventTreasureSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "event_curse.png", 0.32f, true, sprite => skin.EventCurseSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "event_repair.png", 0.32f, true, sprite => skin.EventRepairSprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "event_bounty.png", 0.32f, true, sprite => skin.EventBountySprite = sprite, report);
            TryApplyBaseSprite(packDirectory, "event_rogue.png", 0.32f, true, sprite => skin.EventRogueSprite = sprite, report);
            if (skin.EventRogueSprite == null)
            {
                TryApplyBaseSprite(packDirectory, "event_rogue_zone.png", 0.32f, true, sprite => skin.EventRogueSprite = sprite, report);
            }

            foreach (KeyValuePair<string, MoleSpriteFileSet> pair in MoleFilesById)
            {
                string moleId = pair.Key;
                MoleSpriteFileSet fileSet = pair.Value;
                Sprite idle = TryLoadMoleSprite(packDirectory, fileSet.Idle, fileSet.Legacy, 0.94f, report);
                Sprite hit = TryLoadMoleSprite(packDirectory, fileSet.Hit, null, 0.94f, report);
                Sprite hitAlt = TryLoadMoleSprite(packDirectory, fileSet.HitAlt, null, 0.94f, report);
                Sprite recover = TryLoadMoleSprite(packDirectory, fileSet.Recover, null, 0.94f, report);
                Sprite primary = idle ?? hit ?? recover;
                if (primary == null)
                {
                    continue;
                }

                UpsertMoleVisual(skin, moleId, primary, hit, hitAlt, recover);
                if (fileSet.Aliases == null)
                {
                    continue;
                }

                for (int aliasIndex = 0; aliasIndex < fileSet.Aliases.Length; aliasIndex++)
                {
                    string alias = fileSet.Aliases[aliasIndex];
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    UpsertMoleVisual(skin, alias, primary, hit, hitAlt, recover);
                }
            }

            foreach (KeyValuePair<DropType, string> pair in DropFilesByType)
            {
                DropType type = pair.Key;
                string fileName = pair.Value;
                Sprite sprite = TryLoadSprite(packDirectory, fileName, 0.2f, true, report);
                if (sprite == null)
                {
                    continue;
                }

                UpsertDropVisual(skin, type, sprite);
            }

            if (skin.MoleDefaultSprite == null && TryGetMoleSprite(skin, "mole_common", out Sprite commonMoleSprite))
            {
                skin.MoleDefaultSprite = commonMoleSprite;
            }

            if (skin.DropDefaultSprite == null && TryGetDropSprite(skin, DropType.Gold, out Sprite goldDropSprite))
            {
                skin.DropDefaultSprite = goldDropSprite;
            }

            report.Applied = report.LoadedSprites > 0;
            return report;
        }

        private static void EnsureSkinCollections(PresentationSkin skin)
        {
            if (skin.MoleVisuals == null)
            {
                skin.MoleVisuals = new List<MoleVisualEntry>();
            }

            if (skin.DropVisuals == null)
            {
                skin.DropVisuals = new List<DropVisualEntry>();
            }
        }

        private static string ResolvePackDirectory(string preferredRelativeFolder)
        {
            string preferred = string.IsNullOrWhiteSpace(preferredRelativeFolder)
                ? null
                : preferredRelativeFolder.Trim().Replace("\\", "/");

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                string preferredPath = Path.Combine(Application.dataPath, preferred);
                if (Directory.Exists(preferredPath))
                {
                    return preferredPath;
                }
            }

            for (int i = 0; i < FallbackRelativeFolders.Length; i++)
            {
                string path = Path.Combine(Application.dataPath, FallbackRelativeFolders[i]);
                if (Directory.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static void TryApplyBaseSprite(
            string packDirectory,
            string fileName,
            float targetWorldSize,
            bool trimTransparent,
            Action<Sprite> applyAction,
            ExternalArtPackReport report)
        {
            Sprite sprite = TryLoadSprite(packDirectory, fileName, targetWorldSize, trimTransparent, report);
            if (sprite == null)
            {
                return;
            }

            applyAction?.Invoke(sprite);
        }

        private static Sprite TryLoadSprite(
            string packDirectory,
            string fileName,
            float targetWorldSize,
            bool trimTransparent,
            ExternalArtPackReport report)
        {
            string filePath = Path.Combine(packDirectory, fileName);
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = $"MS_External_{Path.GetFileNameWithoutExtension(fileName)}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                if (!texture.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Texture2D workingTexture = texture;
                if (Mathf.Max(workingTexture.width, workingTexture.height) > RuntimeMaxTextureSize)
                {
                    Texture2D resized = ResizeTexture(workingTexture, RuntimeMaxTextureSize);
                    if (resized != null)
                    {
                        UnityEngine.Object.Destroy(workingTexture);
                        workingTexture = resized;
                    }
                }

                if (trimTransparent)
                {
                    TryAutoKeyEdgeBackground(workingTexture);
                }

                Rect spriteRect = new Rect(0f, 0f, workingTexture.width, workingTexture.height);
                if (trimTransparent && TryGetOpaqueBounds(workingTexture, out RectInt opaqueRect))
                {
                    spriteRect = new Rect(opaqueRect.x, opaqueRect.y, opaqueRect.width, opaqueRect.height);
                }

                float maxDimension = Mathf.Max(spriteRect.width, spriteRect.height);
                float clampedTarget = Mathf.Clamp(targetWorldSize, 0.08f, 4f);
                float pixelsPerUnit = Mathf.Max(16f, maxDimension / clampedTarget);
                Sprite sprite = Sprite.Create(
                    workingTexture,
                    spriteRect,
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.Tight);
                sprite.name = $"MS_External_{Path.GetFileNameWithoutExtension(fileName)}";

                if (report != null)
                {
                    report.LoadedSprites++;
                    report.LoadedFiles.Add(fileName);
                }

                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MoleSurvivors] Failed to load external sprite '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static void TryAutoKeyEdgeBackground(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Color32[] pixels;
            try
            {
                pixels = texture.GetPixels32();
            }
            catch
            {
                return;
            }

            int width = texture.width;
            int height = texture.height;
            int total = pixels.Length;
            if (width < 4 || height < 4 || total == 0)
            {
                return;
            }

            int nonOpaque = 0;
            for (int i = 0; i < total; i++)
            {
                if (pixels[i].a < 250)
                {
                    nonOpaque++;
                }
            }

            // If alpha channel already contains meaningful transparency, trust it.
            if (nonOpaque > total * 0.01f)
            {
                return;
            }

            Dictionary<int, int> edgeBuckets = new Dictionary<int, int>();
            Action<int> sampleEdge = idx =>
            {
                Color32 c = pixels[idx];
                int bucket = QuantizeRgb(c);
                edgeBuckets[bucket] = edgeBuckets.TryGetValue(bucket, out int count) ? count + 1 : 1;
            };

            for (int x = 0; x < width; x++)
            {
                sampleEdge(x);
                sampleEdge((height - 1) * width + x);
            }

            for (int y = 0; y < height; y++)
            {
                sampleEdge(y * width);
                sampleEdge(y * width + (width - 1));
            }

            if (edgeBuckets.Count == 0)
            {
                return;
            }

            List<int> topBuckets = edgeBuckets
                .OrderByDescending(pair => pair.Value)
                .Take(3)
                .Select(pair => pair.Key)
                .ToList();
            if (topBuckets.Count == 0)
            {
                return;
            }

            bool[] visited = new bool[total];
            Queue<int> queue = new Queue<int>(Mathf.Max(128, (width + height) * 2));
            void EnqueueEdge(int idx)
            {
                if (visited[idx])
                {
                    return;
                }

                if (!IsLikelyBackgroundPixel(pixels[idx], topBuckets))
                {
                    return;
                }

                visited[idx] = true;
                queue.Enqueue(idx);
            }

            for (int x = 0; x < width; x++)
            {
                EnqueueEdge(x);
                EnqueueEdge((height - 1) * width + x);
            }

            for (int y = 0; y < height; y++)
            {
                EnqueueEdge(y * width);
                EnqueueEdge(y * width + (width - 1));
            }

            int removed = 0;
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                Color32 c = pixels[idx];
                if (c.a != 0)
                {
                    c.a = 0;
                    pixels[idx] = c;
                    removed++;
                }

                int x = idx % width;
                int y = idx / width;
                if (x > 0)
                {
                    TryFlood(x - 1, y);
                }

                if (x + 1 < width)
                {
                    TryFlood(x + 1, y);
                }

                if (y > 0)
                {
                    TryFlood(x, y - 1);
                }

                if (y + 1 < height)
                {
                    TryFlood(x, y + 1);
                }
            }

            if (removed <= total * 0.05f)
            {
                removed += TryAutoKeyCheckerboardBackground(pixels, topBuckets);
            }

            float removedRatio = removed / (float)total;
            if (removedRatio <= 0.05f || removedRatio >= 0.985f)
            {
                return;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return;

            void TryFlood(int x, int y)
            {
                int nidx = y * width + x;
                if (visited[nidx])
                {
                    return;
                }

                if (!IsLikelyBackgroundPixel(pixels[nidx], topBuckets))
                {
                    return;
                }

                visited[nidx] = true;
                queue.Enqueue(nidx);
            }
        }

        private static int TryAutoKeyCheckerboardBackground(Color32[] pixels, List<int> topBuckets)
        {
            if (pixels == null || pixels.Length == 0 || topBuckets == null || topBuckets.Count == 0)
            {
                return 0;
            }

            List<int> neutralBuckets = new List<int>(topBuckets.Count);
            for (int i = 0; i < topBuckets.Count; i++)
            {
                int bucket = topBuckets[i];
                DecodeQuantizedRgb(bucket, out int r, out int g, out int b);
                if (Mathf.Abs(r - g) <= 16 && Mathf.Abs(g - b) <= 16)
                {
                    neutralBuckets.Add(bucket);
                }
            }

            if (neutralBuckets.Count == 0)
            {
                return 0;
            }

            int removed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a == 0)
                {
                    continue;
                }

                if (!IsNearGray(pixel, 26))
                {
                    continue;
                }

                for (int j = 0; j < neutralBuckets.Count; j++)
                {
                    if (ColorDistanceRgb(pixel, neutralBuckets[j]) > 30f)
                    {
                        continue;
                    }

                    pixel.a = 0;
                    pixels[i] = pixel;
                    removed++;
                    break;
                }
            }

            return removed;
        }

        private static bool IsNearGray(Color32 pixel, int delta)
        {
            int max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
            int min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
            return max - min <= Mathf.Max(0, delta);
        }

        private static bool IsLikelyBackgroundPixel(Color32 pixel, List<int> buckets)
        {
            for (int i = 0; i < buckets.Count; i++)
            {
                if (ColorDistanceRgb(pixel, buckets[i]) <= 54f)
                {
                    return true;
                }
            }

            return false;
        }

        private static int QuantizeRgb(Color32 c)
        {
            int r = c.r >> 4;
            int g = c.g >> 4;
            int b = c.b >> 4;
            return (r << 8) | (g << 4) | b;
        }

        private static float ColorDistanceRgb(Color32 c, int quantizedRgb)
        {
            DecodeQuantizedRgb(quantizedRgb, out int r, out int g, out int b);
            float dr = c.r - r;
            float dg = c.g - g;
            float db = c.b - b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static void DecodeQuantizedRgb(int quantizedRgb, out int r, out int g, out int b)
        {
            r = ((quantizedRgb >> 8) & 0xF) * 17;
            g = ((quantizedRgb >> 4) & 0xF) * 17;
            b = (quantizedRgb & 0xF) * 17;
        }

        private static Texture2D ResizeTexture(Texture2D source, int maxDimension)
        {
            if (source == null)
            {
                return null;
            }

            int currentMax = Mathf.Max(source.width, source.height);
            if (currentMax <= maxDimension)
            {
                return source;
            }

            float scale = maxDimension / (float)currentMax;
            int targetWidth = Mathf.Max(8, Mathf.RoundToInt(source.width * scale));
            int targetHeight = Mathf.Max(8, Mathf.RoundToInt(source.height * scale));

            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false)
                {
                    name = source.name + "_resized",
                    filterMode = source.filterMode,
                    wrapMode = source.wrapMode,
                };
                resized.ReadPixels(new Rect(0f, 0f, targetWidth, targetHeight), 0, 0);
                resized.Apply(false, false);
                return resized;
            }
            catch
            {
                return source;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static bool TryGetOpaqueBounds(Texture2D texture, out RectInt rect)
        {
            rect = default;
            if (texture == null)
            {
                return false;
            }

            Color32[] pixels;
            try
            {
                pixels = texture.GetPixels32();
            }
            catch
            {
                return false;
            }

            int width = texture.width;
            int height = texture.height;
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            const byte alphaThreshold = 8;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[rowOffset + x].a <= alphaThreshold)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return false;
            }

            int widthRect = maxX - minX + 1;
            int heightRect = maxY - minY + 1;
            if (widthRect < 2 || heightRect < 2)
            {
                return false;
            }

            rect = new RectInt(minX, minY, widthRect, heightRect);
            return true;
        }

        private static Sprite TryLoadMoleSprite(
            string packDirectory,
            string primaryFileName,
            string fallbackFileName,
            float targetWorldSize,
            ExternalArtPackReport report)
        {
            if (!string.IsNullOrWhiteSpace(primaryFileName))
            {
                Sprite primary = TryLoadSprite(packDirectory, primaryFileName, targetWorldSize, true, report);
                if (primary != null)
                {
                    return primary;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackFileName))
            {
                return TryLoadSprite(packDirectory, fallbackFileName, targetWorldSize, true, report);
            }

            return null;
        }

        private static void UpsertMoleVisual(
            PresentationSkin skin,
            string moleId,
            Sprite idle,
            Sprite hit,
            Sprite hitAlt,
            Sprite recover)
        {
            for (int i = 0; i < skin.MoleVisuals.Count; i++)
            {
                MoleVisualEntry entry = skin.MoleVisuals[i];
                if (entry == null || entry.MoleId != moleId)
                {
                    continue;
                }

                entry.Sprite = idle;
                entry.WarningSprite = recover != null ? recover : idle;
                entry.ActiveSprite = idle;
                entry.IdleSprite = idle;
                entry.HitSprite = hit;
                entry.HitSpriteAlt = hitAlt;
                entry.RetreatSprite = recover != null ? recover : idle;
                entry.RecoverSprite = recover;
                entry.Tint = Color.white;
                return;
            }

            skin.MoleVisuals.Add(new MoleVisualEntry
            {
                MoleId = moleId,
                Sprite = idle,
                WarningSprite = recover != null ? recover : idle,
                ActiveSprite = idle,
                IdleSprite = idle,
                HitSprite = hit,
                HitSpriteAlt = hitAlt,
                RetreatSprite = recover != null ? recover : idle,
                RecoverSprite = recover,
                Tint = Color.white,
            });
        }

        private static void UpsertDropVisual(PresentationSkin skin, DropType dropType, Sprite sprite)
        {
            Color tint = ResolveDropTint(dropType);
            for (int i = 0; i < skin.DropVisuals.Count; i++)
            {
                DropVisualEntry entry = skin.DropVisuals[i];
                if (entry == null || entry.DropType != dropType)
                {
                    continue;
                }

                entry.Sprite = sprite;
                entry.Tint = tint;
                return;
            }

            skin.DropVisuals.Add(new DropVisualEntry
            {
                DropType = dropType,
                Sprite = sprite,
                Tint = tint,
            });
        }

        private static Color ResolveDropTint(DropType dropType)
        {
            return dropType switch
            {
                DropType.Gold => new Color(0.95f, 0.82f, 0.26f),
                DropType.Experience => new Color(0.45f, 0.95f, 0.95f),
                DropType.Core => new Color(0.95f, 0.35f, 0.95f),
                _ => Color.white,
            };
        }

        private static bool TryGetMoleSprite(PresentationSkin skin, string moleId, out Sprite sprite)
        {
            for (int i = 0; i < skin.MoleVisuals.Count; i++)
            {
                MoleVisualEntry entry = skin.MoleVisuals[i];
                if (entry == null || entry.MoleId != moleId)
                {
                    continue;
                }

                Sprite resolved = entry.ActiveSprite != null
                    ? entry.ActiveSprite
                    : (entry.IdleSprite != null ? entry.IdleSprite : entry.Sprite);
                if (resolved != null)
                {
                    sprite = resolved;
                    return true;
                }
            }

            sprite = null;
            return false;
        }

        private static bool TryGetDropSprite(PresentationSkin skin, DropType dropType, out Sprite sprite)
        {
            for (int i = 0; i < skin.DropVisuals.Count; i++)
            {
                DropVisualEntry entry = skin.DropVisuals[i];
                if (entry == null || entry.DropType != dropType || entry.Sprite == null)
                {
                    continue;
                }

                sprite = entry.Sprite;
                return true;
            }

            sprite = null;
            return false;
        }
    }
}
