#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using TJGenerators.Config;
using Unity.UniAsset.Manager.Editor.InternalBridge;

namespace TJGenerators.Utils
{
    /// <summary>
    /// 生成积分查询工具：根据模型 id（兼容旧参数 taskType）获取当前生效积分消耗。
    /// </summary>
    public static class GenerationCreditHelper
    {
        private const string CostEndpoint = "credit/task-configs/value";
        private const string CostConfigsEndpoint = "credit/task-configs";

        /// <summary>单条积分查询项（主生成或后处理子任务）。</summary>
        public readonly struct CostComponent
        {
            public readonly string ModelId;
            public readonly string ApiEndpoint;

            public CostComponent(string modelId, string apiEndpoint)
            {
                ModelId = modelId;
                ApiEndpoint = apiEndpoint;
            }
        }

        /// <summary>
        /// 查询多项任务积分并求和，用于按钮展示预计总消耗（主任务 + 可选后处理）。
        /// </summary>
        public static IEnumerator GetTotalGenerationCostCoroutine(
            IReadOnlyList<CostComponent> components,
            Action<int?> onComplete
        )
        {
            if (components == null || components.Count == 0)
            {
                onComplete?.Invoke(null);
                yield break;
            }

            int total = 0;
            bool anyResolved = false;

            for (int i = 0; i < components.Count; i++)
            {
                var c = components[i];
                int? part = null;
                yield return GetGenerationCostByIdCoroutine(c.ModelId, c.ApiEndpoint, v => part = v);
                if (!part.HasValue)
                    continue;
                total += part.Value;
                anyResolved = true;
            }

            onComplete?.Invoke(anyResolved ? total : (int?)null);
        }

        /// <summary>多项预计消耗的缓存键（各子项键用 + 连接）。</summary>
        public static string BuildTotalCostCacheKey(IReadOnlyList<CostComponent> components)
        {
            if (components == null || components.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < components.Count; i++)
            {
                if (i > 0)
                    sb.Append('+');
                sb.Append(BuildCostCacheKey(components[i].ModelId, components[i].ApiEndpoint));
            }
            return sb.ToString();
        }

        public static IEnumerator GetGenerationCostByIdCoroutine(string modelId, string apiEndpoint, Action<int?> onComplete)
        {
            var taskTypeCandidates = BuildTaskTypeCandidates(modelId, apiEndpoint);
            if (taskTypeCandidates.Count == 0)
            {
                onComplete?.Invoke(null);
                yield break;
            }

            foreach (var taskType in taskTypeCandidates)
            {
                string escaped = UnityWebRequest.EscapeURL(taskType);
                string requestUrl = $"{ConfigManager.GetApiBaseUrl()}{CostEndpoint}?id={escaped}&taskType={escaped}";
                using (var uwr = UnityWebRequest.Get(requestUrl))
                {
                    SetupHeaders(uwr);
                    yield return SendWithTimeout(uwr);

                    if (UnityWebRequestCompat.IsNotSuccess(uwr) || uwr.downloadHandler == null)
                        continue;

                    string body = uwr.downloadHandler.text ?? string.Empty;
                    int? credits = TryParseCredits(body);
                    if (credits.HasValue)
                    {
                        onComplete?.Invoke(Mathf.Max(0, credits.Value));
                        yield break;
                    }
                }
            }

            string configsUrl = $"{ConfigManager.GetApiBaseUrl()}{CostConfigsEndpoint}";
            using (var uwr = UnityWebRequest.Get(configsUrl))
            {
                SetupHeaders(uwr);
                yield return SendWithTimeout(uwr);

                if (UnityWebRequestCompat.IsSuccess(uwr) && uwr.downloadHandler != null)
                {
                    string body = uwr.downloadHandler.text ?? string.Empty;
                    int? matched = TryParseCreditsFromTaskConfigs(body, taskTypeCandidates);
                    if (matched.HasValue)
                    {
                        onComplete?.Invoke(Mathf.Max(0, matched.Value));
                        yield break;
                    }

                    int? defaultCredits = TryParseDefaultCredits(body);
                    if (defaultCredits.HasValue)
                    {
                        onComplete?.Invoke(Mathf.Max(0, defaultCredits.Value));
                        yield break;
                    }
                }
            }

            onComplete?.Invoke(null);
        }

        private static int? TryParseCredits(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var match = Regex.Match(
                json,
                "\"(?:credits|cost|requiredCredits|consumeCredits|points)\"\\s*:\\s*(-?\\d+)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            if (!int.TryParse(match.Groups[1].Value, out int value))
                return null;

            return Mathf.Max(0, value);
        }

        /// <summary>
        /// 根据模型 id 与当前生效的 API 端点解析主 taskType（用于积分缓存键与查询）。
        /// </summary>
        public static string GetPrimaryTaskTypeForCost(string modelId, string apiEndpoint)
        {
            var candidates = BuildTaskTypeCandidates(modelId, apiEndpoint);
            if (candidates.Count > 0)
                return candidates[0];
            return NormalizeTaskType(modelId) ?? string.Empty;
        }

        /// <summary>
        /// 积分缓存键：同一模型在不同 task 端点（文生/图生/多视图等）可能对应不同消耗。
        /// </summary>
        public static string BuildCostCacheKey(string modelId, string apiEndpoint)
        {
            if (string.IsNullOrEmpty(modelId))
                return string.Empty;
            string taskType = GetPrimaryTaskTypeForCost(modelId, apiEndpoint);
            return string.IsNullOrEmpty(taskType) ? modelId : modelId + "|" + taskType;
        }

        private static List<string> BuildTaskTypeCandidates(string modelId, string apiEndpoint)
        {
            var result = new List<string>();
            AddIfNotEmpty(result, NormalizeTaskType(modelId));

            if (!string.IsNullOrEmpty(apiEndpoint))
            {
                var m = Regex.Match(apiEndpoint, "task/([a-zA-Z0-9\\-_]+)");
                if (m.Success && m.Groups.Count > 1)
                    AddIfNotEmpty(result, NormalizeTaskType(m.Groups[1].Value));
            }

            return result;
        }

        private static string NormalizeTaskType(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            return raw.Trim().ToLowerInvariant().Replace("-", "_");
        }

        private static void AddIfNotEmpty(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            if (!list.Contains(value))
                list.Add(value);
        }

        private static void SetupHeaders(UnityWebRequest uwr)
        {
            uwr.downloadHandler = new DownloadHandlerBuffer();
            string token = UnityConnectSession.instance.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
                uwr.SetRequestHeader("Authorization", $"Bearer {token}");
            uwr.SetRequestHeader("orgId", UnityConnectSession.instance.GetOrgId());
            uwr.SetRequestHeader("source", ConfigManager.GetRequestSource());
        }

        private static IEnumerator SendWithTimeout(UnityWebRequest uwr)
        {
            yield return uwr.SendWebRequest();

            float timeout = Mathf.Max(5f, ConfigManager.GetRequestTimeout());
            float elapsed = 0f;
            while (UnityWebRequestCompat.IsInProgress(uwr) && elapsed < timeout)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        private static int? TryParseCreditsFromTaskConfigs(string json, List<string> candidates)
        {
            if (string.IsNullOrEmpty(json) || candidates == null || candidates.Count == 0)
                return null;

            foreach (var taskType in candidates)
            {
                string pattern =
                    "\"taskType\"\\s*:\\s*\"" + Regex.Escape(taskType) +
                    "\"[\\s\\S]*?\"credits\"\\s*:\\s*(-?\\d+)";
                var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                if (int.TryParse(match.Groups[1].Value, out int value))
                    return Mathf.Max(0, value);
            }
            return null;
        }

        private static int? TryParseDefaultCredits(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var match = Regex.Match(json, "\"defaultCredits\"\\s*:\\s*(-?\\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;
            return int.TryParse(match.Groups[1].Value, out int value) ? Mathf.Max(0, value) : (int?)null;
        }
    }
}
#endif
