#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json;
using TJGenerators;
using TJGenerators.Config;
using TJGenerators.Utils;

namespace TJGenerators.Generators
{
    internal readonly struct DynamicTaskResponseContext
    {
        public DynamicTaskResponseContext(
            GeneratorConfig config,
            IReadOnlyDictionary<string, object> parameterValues,
            string generatorId,
            string currentInputMode = "text"
        )
        {
            Config = config;
            ParameterValues = parameterValues;
            GeneratorId = generatorId ?? "";
            CurrentInputMode = currentInputMode ?? "text";
        }

        public GeneratorConfig Config { get; }
        public IReadOnlyDictionary<string, object> ParameterValues { get; }
        public string GeneratorId { get; }
        public string CurrentInputMode { get; }
    }

    /// <summary>
    /// 动态生成器任务状态响应中的 URL / 文件名解析（原 DynamicGenerator 响应解析区域）。
    /// </summary>
    internal static class DynamicTaskResponseResolver
    {
        #region Path helpers

        /// <summary>从容器按顺序读取两个 URL 字段，返回第一个非空（典型：FBX 优先于 GLB）。</summary>
        private static string PreferUrlKeys(
            object container,
            string primaryKey,
            string fallbackKey
        )
        {
            if (container == null)
                return null;
            string primary = PathUtils.GetString(container, primaryKey);
            if (!string.IsNullOrEmpty(primary))
                return primary;
            string fallback = PathUtils.GetString(container, fallbackKey);
            return string.IsNullOrEmpty(fallback) ? null : fallback;
        }

        /// <summary>Meshy：从 rig_result / steps.rig 的 basic_animations 解析成对动画 URL。</summary>
        private static string GetMeshyBasicAnimationUrl(
            object resultRoot,
            string glbKey,
            string fbxKey
        )
        {
            if (resultRoot == null)
                return null;
            var rigResult = PathUtils.GetRaw(resultRoot, "rig_result");
            string url = PreferUrlKeys(
                PathUtils.GetRaw(rigResult, "basic_animations"),
                glbKey,
                fbxKey
            );
            if (!string.IsNullOrEmpty(url))
                return url;
            var stepsRig = PathUtils.GetRaw(resultRoot, "steps.rig");
            return PreferUrlKeys(PathUtils.GetRaw(stepsRig, "basic_animations"), glbKey, fbxKey);
        }

        #endregion

        #region Provider fallbacks

        private static string TryResolveRodinDownloadUrl(GeneratorConfig config, object result)
        {
            if (
                result == null
                || !DynamicRequestJsonBuilder.IsRodinGenerator(config)
            )
                return null;

            string url = PathUtils.GetString(result, "base_basic_shaded");
            if (!string.IsNullOrEmpty(url))
            {
                TJLog.Log(
                    "[DynamicGenerator] GetDownloadUrl: Rodin 主路径为空，使用 'base_basic_shaded'"
                );
                return url;
            }

            url = PathUtils.GetString(result, "base");
            if (!string.IsNullOrEmpty(url))
                TJLog.Log("[DynamicGenerator] GetDownloadUrl: Rodin 使用 'base' 作为兜底");
            return string.IsNullOrEmpty(url) ? null : url;
        }

        private static string TryResolveMeshyModelUrl(object result)
        {
            if (result == null)
                return null;

            string url = PreferUrlKeys(PathUtils.GetRaw(result, "refine_model_urls"), "fbx", "glb");
            if (!string.IsNullOrEmpty(url))
                return url;

            url = PreferUrlKeys(
                PathUtils.GetRaw(result, "rig_result"),
                "rigged_character_fbx_url",
                "rigged_character_glb_url"
            );
            if (!string.IsNullOrEmpty(url))
                return url;

            url = PreferUrlKeys(PathUtils.GetRaw(result, "preview_model_urls"), "fbx", "glb");
            if (!string.IsNullOrEmpty(url))
                return url;

            var stepsRefine = PathUtils.GetRaw(result, "steps.refine");
            return PreferUrlKeys(PathUtils.GetRaw(stepsRefine, "model_urls"), "fbx", "glb");
        }

        private static bool IsImageDownloadPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            return string.Equals(path, "imageUrls", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "image_urls", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveDownloadUrlPath(DynamicTaskResponseContext ctx)
        {
            var mapping = ctx.Config.responseMapping;
            if (
                string.Equals(ctx.CurrentInputMode, "multiview", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(mapping?.downloadUrlPathMultiview)
            )
                return mapping.downloadUrlPathMultiview;

            return mapping?.downloadUrlPath ?? "pbr_model";
        }

        #endregion

        public static string GetDownloadUrl(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data == null)
            {
                TJLog.Log($"[DynamicGenerator] GetDownloadUrl: output.data 为空");
                return null;
            }

            string defaultPath = ResolveDownloadUrlPath(ctx);
            TJLog.Log($"[DynamicGenerator] GetDownloadUrl: 使用路径 '{defaultPath}'");

            if (IsImageDownloadPath(defaultPath))
            {
                string[] imageUrls = TaskStatusOutputUrlHelper.TryGetImageDownloadUrls(
                    response,
                    defaultPath
                );
                if (imageUrls != null && imageUrls.Length > 0)
                    return imageUrls[0];
            }

            if (
                defaultPath == "audio_url"
                && !string.IsNullOrEmpty(response.output.data.audio_url)
            )
                return response.output.data.audio_url;
            if (
                defaultPath == "audioUrl"
                && !string.IsNullOrEmpty(response.output.data.audioUrl)
            )
                return response.output.data.audioUrl;

            if (defaultPath.StartsWith("resultFiles", StringComparison.Ordinal))
            {
                string urlFromData = PathUtils.GetString(response.output.data, defaultPath);
                if (!string.IsNullOrEmpty(urlFromData))
                    return urlFromData;
            }

            if (response.output.data.result == null)
            {
                string flatUrl = PathUtils.GetString(response.output.data, defaultPath);
                if (!string.IsNullOrEmpty(flatUrl))
                    return flatUrl;

                TJLog.Log(
                    $"[DynamicGenerator] GetDownloadUrl: output.data.result 为空, output.data 内容: {JsonConvert.SerializeObject(response.output.data, Formatting.Indented)}"
                );
                return null;
            }

            string url = PathUtils.GetString(response.output.data.result, defaultPath);

            object result = response.output.data.result;

            if (string.IsNullOrEmpty(url))
                url = TryResolveRodinDownloadUrl(ctx.Config, result);

            if (string.IsNullOrEmpty(url))
            {
                url = TryResolveMeshyModelUrl(result);
                if (!string.IsNullOrEmpty(url))
                    TJLog.Log($"[DynamicGenerator] GetDownloadUrl: 使用 Meshy 动画模型 URL: {url}");
            }

            if (string.IsNullOrEmpty(url))
            {
                TJLog.Log(
                    $"[DynamicGenerator] GetDownloadUrl: 路径 '{defaultPath}' 未找到URL, result 内容: {JsonConvert.SerializeObject(response.output.data.result, Formatting.Indented)}"
                );
            }

            return url;
        }

        public static string[] GetDownloadUrls(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data == null)
                return null;

            string defaultPath = ResolveDownloadUrlPath(ctx);

            if (IsImageDownloadPath(defaultPath))
            {
                string[] imageUrls = TaskStatusOutputUrlHelper.TryGetImageDownloadUrls(
                    response,
                    defaultPath
                );
                if (imageUrls != null && imageUrls.Length > 0)
                    return imageUrls;
            }

            object raw = null;
            if (response.output.data.result != null)
            {
                raw = PathUtils.GetRaw(response.output.data.result, defaultPath);
            }

            if (raw == null)
            {
                raw = PathUtils.GetRaw(response.output.data, defaultPath);
            }

            if (raw is Array arr && arr.Length > 0)
            {
                var urls = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    urls[i] = arr.GetValue(i)?.ToString();
                }
                return urls;
            }

            if (raw is string singleStr && !string.IsNullOrEmpty(singleStr))
            {
                return new[] { singleStr };
            }
            return null;
        }

        public static string GetPreviewImageUrl(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data == null)
                return null;

            string path = ctx.Config.responseMapping?.previewUrlPath ?? "generated_image";

            if (path != null && path.StartsWith("resultFiles", StringComparison.Ordinal))
            {
                string urlFromData = PathUtils.GetString(response.output.data, path);
                if (!string.IsNullOrEmpty(urlFromData))
                    return urlFromData;
            }

            if (path != null && path.StartsWith("assets.", StringComparison.Ordinal))
            {
                string urlFromAssets = PathUtils.GetString(response.output.data, path);
                if (!string.IsNullOrEmpty(urlFromAssets))
                    return urlFromAssets;
            }

            if (response.output.data.result == null)
                return null;

            return PathUtils.GetString(response.output.data.result, path);
        }

        public static string GetRenderedImageUrl(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data?.result == null)
                return null;

            string path = ctx.Config.responseMapping?.renderedImagePath;
            if (string.IsNullOrEmpty(path))
                return null;

            return PathUtils.GetString(response.output.data.result, path);
        }

        public static string GetAnimationUrl(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data?.result == null)
                return null;

            string path = ctx.Config.responseMapping?.animationUrlPath;
            if (!string.IsNullOrEmpty(path))
            {
                string configUrl = PathUtils.GetString(response.output.data.result, path);
                if (!string.IsNullOrEmpty(configUrl))
                    return configUrl;
            }

            object result = response.output.data.result;
            string url = PreferUrlKeys(
                PathUtils.GetRaw(result, "animation_result"),
                "animation_fbx_url",
                "animation_glb_url"
            );
            if (!string.IsNullOrEmpty(url))
                return url;

            return PreferUrlKeys(
                PathUtils.GetRaw(result, "steps.animation"),
                "animation_fbx_url",
                "animation_glb_url"
            );
        }

        public static string GetWalkingAnimationUrl(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data?.result == null)
                return null;

            string path = ctx.Config.responseMapping?.walkingAnimationUrlPath;
            if (!string.IsNullOrEmpty(path))
            {
                string configUrl = PathUtils.GetString(response.output.data.result, path);
                if (!string.IsNullOrEmpty(configUrl))
                    return configUrl;
            }

            return GetMeshyBasicAnimationUrl(
                response.output.data.result,
                "walking_fbx_url",
                "walking_glb_url"
            );
        }

        public static string GetRunningAnimationUrl(
            DynamicTaskResponseContext ctx,
            TJTaskStatusResponse response
        )
        {
            if (response?.output?.data?.result == null)
                return null;

            string path = ctx.Config.responseMapping?.runningAnimationUrlPath;
            if (!string.IsNullOrEmpty(path))
            {
                string configUrl = PathUtils.GetString(response.output.data.result, path);
                if (!string.IsNullOrEmpty(configUrl))
                    return configUrl;
            }

            return GetMeshyBasicAnimationUrl(
                response.output.data.result,
                "running_fbx_url",
                "running_glb_url"
            );
        }

        public static string GetModelFileName(DynamicTaskResponseContext ctx)
        {
            string ext = "fbx";
            if (
                ctx.ParameterValues != null
                && ctx.ParameterValues.TryGetValue("geometryFormat", out object geoFormat)
            )
            {
                ext = geoFormat?.ToString()?.ToLower() ?? "fbx";
            }
            return $"{ctx.Config.id}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        }
    }
}
#endif
