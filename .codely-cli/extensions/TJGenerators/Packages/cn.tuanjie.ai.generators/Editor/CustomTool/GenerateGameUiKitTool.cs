using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Pipeline;
using TJGenerators.Utils;
using Unity.EditorCoroutines.Editor;
#endif

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// CustomTool for generating game UI asset kits via a two-step workflow.
    /// Step 1: text→UI screenshot (frontier-game-design, landscape_16_9)
    /// Step 2: screenshot→cutout sheet (frontier-game-design, square_hd, image-to-image)
    /// Reuses the frontier-game-design config and endpoint.
    /// Uses ImageTaskTracker + ImagePipelineHost for both steps.
    /// </summary>
    public static class GenerateGameUiKitTool
    {
        private const string GeneratorId = "frontier-game-design";
        private const string ScreenshotPromptSuffix = ", complete game UI screen design, full HUD layout, " +
            "health bars, mana bars, buttons, panels, inventory grid, skill icons, mini-map, " +
            "dialogue box, score display, clean professional game interface, " +
            "high quality 2D game UI design, detailed UI elements, consistent art style";
        private const string CutoutPrompt = "Using the reference screenshot, extract every UI element as individual isolated cutouts " +
            "and arrange them in a grid with clear spacing between each element. " +
            "Extract: buttons, panels, health/mana bars, icons, frames, borders, dividers, sliders, checkboxes, tab headers. " +
            "Keep any dynamic text labels (numbers, player names, scores) as separate editable elements, not baked into button art. " +
            "Preserve thin UI borders, decorative edges, and fine stroke details exactly. " +
            "Render everything on a perfectly flat pure magenta (#FF00FF) background. " +
            "No shadows, no gradients, no scenery, no reflections, no texture in background. " +
            "Each element must have clear margin around it for clean extraction.";

        [ExecuteCustomTool.CustomTool("generate_game_ui_kit",
            "Generate a game UI asset kit in two async steps: " +
            "Step 1 (no screenshot_path): generates a game UI screenshot from text. " +
            "Step 2 (with screenshot_path): uses the screenshot as reference to generate a UI cutout sheet on magenta background. " +
            "Parameters: prompt (required), " +
            "screenshot_path (Step 2 only: local path of Step 1's output image_path), " +
            "quality (optional 'low'|'medium'|'high', default 'medium'), " +
            "output_format (optional 'png'|'jpeg'|'webp', default 'png'), " +
            "output_path (optional save path). " +
            "IMPORTANT: No placeholder is returned. A <bg_task_done> notification will arrive upon completion.")]
        public static object GenerateGameUiKit(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateGameUiKitTool] Generating game UI kit with parameters: {parameters}");

                string prompt = parameters["prompt"]?.ToString();
                string screenshotPath = parameters["screenshot_path"]?.ToString();
                string outputPath = parameters["output_path"]?.ToString();
                string sessionId = parameters["session_id"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(prompt))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'prompt' parameter is required" }
                    };
                }

                int maxLen = TJGeneratorsPromptLimits.GetMaxLength(GeneratorId);
                string effectivePrompt = string.IsNullOrEmpty(screenshotPath) ? prompt + ScreenshotPromptSuffix : CutoutPrompt;
                if (effectivePrompt.Length > maxLen)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error_code", "INVALID_PARAMS" },
                        { "message", $"Prompt length ({effectivePrompt.Length}) exceeds the {maxLen} character limit." }
                    };
                }

                // Load frontier-game-design config
                var config = ConfigManager.GetGeneratorConfig(ConfigType.Image, GeneratorId);
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Cannot find generator config for '{GeneratorId}'." }
                    };
                }

                var generator = new DynamicGenerator(config);
                generator.SetTextPrompt(effectivePrompt);
                generator.SetHistoryDisplayPrompt(prompt);

                int step = string.IsNullOrEmpty(screenshotPath) ? 1 : 2;

                if (step == 1)
                {
                    // Step 1: text-to-image, landscape_16_9
                    generator.SetParameter("imageSize", "landscape_16_9");
                }
                else
                {
                    // Step 2: image-to-image, square_hd, screenshot as reference
                    generator.SetParameter("imageSize", "square_hd");
                    generator.SetImagePath(screenshotPath);
                }

                // Apply common parameters
                ApplyGameUiKitParameters(generator, parameters);

                // Submit task
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator, sessionId);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateGameUiKitTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateGameUiKitTool] Step {step} 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // Create placeholder texture
                string placeholderPath = CreatePlaceholderTexture(outputPath);

                // Register task
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = ImageTaskTracker.CreateTask(GeneratorId, prompt, screenshotPath, placeholderPath, capturedBackendTaskId);

                // Create pipeline host
                var host = new ImagePipelineHost(
                    placeholderPath,
                    sessionId,
                    (savedPath, previewUrl) =>
                    {
                        ImageTaskTracker.MarkTaskCompleted(taskId, savedPath, previewUrl);
                        var t = ImageTaskTracker.GetTask(taskId);
                        GenerationNotifier.NotifyCompleted("generate_game_ui_kit", taskId, capturedBackendTaskId,
                            new JObject
                            {
                                ["session_id"]       = sessionId,
                                ["generator_id"]     = GeneratorId,
                                ["prompt"]           = prompt ?? "",
                                ["image_path"]       = savedPath,
                                ["preview_url"]      = previewUrl ?? "",
                                ["step"]             = step,
                                ["progress"]         = 100,
                                ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                            });
                    },
                    errorMsg =>
                    {
                        ImageTaskTracker.MarkTaskFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_game_ui_kit", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["generator_id"] = GeneratorId, ["prompt"] = prompt ?? "", ["step"] = step });
                    }
                );

                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);
                var pipeline = new GenerationPipeline(host, ConfigType.Image, GenerationRequestOrigin.Agent, sessionId, "generate_game_ui_kit");
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateGameUiKitTool] Step {step} 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}");

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        step == 1
                            ? "Game UI kit Step 1 (screenshot) started. " +
                              "After <bg_task_done>, submit Step 2 with screenshot_path = the returned image_path. " +
                              "*** POLLING IS STRICTLY FORBIDDEN. ***"
                            : "Game UI kit Step 2 (cutout sheet) started. " +
                              "A <bg_task_done> notification will arrive with the final image_path. " +
                              "*** POLLING IS STRICTLY FORBIDDEN. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       GeneratorId },
                    { "step",               step },
                    { "prompt",             prompt },
                    { "placeholder_path",   placeholderPath },
                    { "estimated_wait_seconds", step == 1 ? 60 : 60 },
                    { "notification_mode",  "bg_task_done" },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateGameUiKitTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating game UI kit: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("query_game_ui_kit_status",
            "Query the status of a game UI kit generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, returns 'image_path' with the asset path. " +
            "Status values: 'generating', 'completed', 'failed'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryGameUiKitStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters["task_id"]?.ToString();
                if (string.IsNullOrEmpty(taskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'task_id' parameter is required" }
                    };
                }

                var task = ImageTaskTracker.GetTask(taskId);
                if (task == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Task '{taskId}' not found. It may have been completed and cleaned up." }
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "task_id", task.TaskId },
                    { "generator_id", task.GeneratorId },
                    { "status", task.Status },
                    { "progress", task.Progress },
                    { "prompt", task.Prompt },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.ResultPath))
                    result["image_path"] = task.ResultPath;

                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

                if (!string.IsNullOrEmpty(task.ErrorMessage))
                    result["error"] = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "generating")
                {
                    if (!string.IsNullOrEmpty(task.PlaceholderPath))
                        result["placeholder_path"] = task.PlaceholderPath;
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateGameUiKitTool] Query error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying task status: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("list_game_ui_kit_tasks", "List all active and recent game UI kit generation tasks")]
        public static object ListGameUiKitTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                // game_ui_kit tasks are tracked as frontier-game-design tasks in ImageTaskTracker.
                // We can't distinguish them from regular generate_image tasks in the tracker,
                // so we return all frontier-game-design tasks.
                // The toolName field in InterruptedTaskData would help, but the tracker doesn't store it.
                // For now, return all image tasks with generator_id == frontier-game-design.
                var allTasks = ImageTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in allTasks)
                {
                    if (task.GeneratorId != GeneratorId)
                        continue;

                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id", task.TaskId },
                        { "generator_id", task.GeneratorId },
                        { "status", task.Status },
                        { "progress", task.Progress },
                        { "prompt", task.Prompt },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.ResultPath))
                        taskData["image_path"] = task.ResultPath;

                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

                    if (!string.IsNullOrEmpty(task.ErrorMessage))
                        taskData["error"] = task.ErrorMessage;

                    if (task.EndTime.HasValue)
                        taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    taskList.Add(taskData);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count", taskList.Count },
                    { "tasks", taskList },
                    { "note", "game_ui_kit tasks share ImageTaskTracker with generate_image (both use frontier-game-design). Tasks are not separately tagged." }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateGameUiKitTool] List error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error listing tasks: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

#if UNITY_EDITOR
        private static void ApplyGameUiKitParameters(DynamicGenerator generator, JObject parameters)
        {
            // quality is a fixedField in frontier-game-design config (defaults to "low").
            // Use SetExtraRawJsonField to override it AFTER ApplyFixedFields runs,
            // because ExtraRawJsonFields are applied last in BuildRequestJson.
            if (parameters["quality"] != null)
            {
                string quality = parameters["quality"].ToString();
                generator.SetExtraRawJsonField("quality", $"\"{quality}\"");
            }

            if (parameters["output_format"] != null)
                generator.SetParameter("outputFormat", parameters["output_format"].ToString());
        }

        private static string CreatePlaceholderTexture(string outputPath)
        {
            string placeholderPath;
            if (!string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    EnsureAssetDatabaseFolder(dir);
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(outputPath, ".png"));
            }
            else
            {
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                    AssetDatabase.CreateFolder("Assets", "TJGenerators");
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                    AssetDatabase.CreateFolder("Assets/TJGenerators", "History");
                string uniqueName = "GameUIKit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/History/" + uniqueName);
            }

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
            tex.Apply();
            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string absolutePath = PathUtils.ToAbsoluteAssetPath(placeholderPath);
            File.WriteAllBytes(absolutePath, pngBytes);
            PathUtils.ImportAssetAfterDiskWrite(placeholderPath);

            return placeholderPath;
        }

        private static void EnsureAssetDatabaseFolder(string folderPath)
        {
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        internal static void ApplyGameUiKitParametersInternal(DynamicGenerator generator, JObject parameters)
            => ApplyGameUiKitParameters(generator, parameters);
#endif
    }
}
