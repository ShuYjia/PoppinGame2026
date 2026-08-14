#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TJGenerators.Utils
{
    /// <summary>
    /// 生成贴图落盘/导入辅助：将索引色 PNG 展开为真正的 RGBA32，并锁定导入格式避免 DXT 压缩破坏 alpha。
    /// </summary>
    public static class GeneratedTextureImportUtils
    {
        /// <summary>
        /// 若 <paramref name="imageData"/> 为 PNG，则解码后重编码为 RGBA32 PNG（消除调色板/索引色）；
        /// 非 PNG 原样返回。
        /// </summary>
        public static byte[] EnsureRgba32PngBytes(byte[] imageData)
        {
            if (imageData == null || imageData.Length < 8)
                return imageData;

            if (!IsPng(imageData))
                return imageData;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(imageData))
                    return imageData;

                byte[] rgbaPng = tex.EncodeToPNG();
                return rgbaPng != null && rgbaPng.Length > 0 ? rgbaPng : imageData;
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// 配置生成贴图导入器：启用 alpha 透明，并将默认/Standalone 平台锁定为未压缩 RGBA32。
        /// </summary>
        public static void ConfigureImportedTexture(
            string assetPath,
            TextureImporterType textureType,
            bool alphaIsTransparency = true)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = textureType;
            if (textureType == TextureImporterType.Sprite)
                importer.spriteImportMode = SpriteImportMode.Single;

            importer.alphaIsTransparency = alphaIsTransparency;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;

            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.format = TextureImporterFormat.RGBA32;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            defaults.crunchedCompression = false;
            importer.SetPlatformTextureSettings(defaults);

            // Editor / Standalone 常覆盖为 DXT5；显式锁定，避免索引色 PNG 经块压缩后 alpha 异常。
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.name = "Standalone";
            standalone.overridden = true;
            standalone.format = TextureImporterFormat.RGBA32;
            standalone.textureCompression = TextureImporterCompression.Uncompressed;
            standalone.crunchedCompression = false;
            if (standalone.maxTextureSize <= 0)
                standalone.maxTextureSize = importer.maxTextureSize > 0 ? importer.maxTextureSize : 2048;
            importer.SetPlatformTextureSettings(standalone);

            importer.SaveAndReimport();
        }

        private static bool IsPng(byte[] data)
        {
            return data[0] == 0x89
                && data[1] == 0x50
                && data[2] == 0x4E
                && data[3] == 0x47;
        }
    }
}
#endif
