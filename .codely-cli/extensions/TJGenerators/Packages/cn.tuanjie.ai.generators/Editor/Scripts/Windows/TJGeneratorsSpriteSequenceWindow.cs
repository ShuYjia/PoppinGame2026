#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TJGenerators.Config;
using TJGenerators.Generators;
using TJGenerators.Pipeline;
using TJGenerators.UI;
using TJGenerators.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Unity.UniAsset.Manager.Editor.InternalBridge;
using Unity.EditorCoroutines.Editor;

namespace TJGenerators
{
    /// <summary>
    /// 2D 序列帧（动作）生成窗口：输入动作描述（必填）+ 参考图（可选），输出多帧 Sprite + AnimationClip。
    /// </summary>
    public class TJGeneratorsSpriteSequenceWindow : GenerationWindowBase, IGenerationPipelineHost, IGenerationTriggerHost, IMediaAssetPipelineHost
    {
        // ========== 基类抽象属性实现 ==========
        protected override ConfigType WindowConfigType => ConfigType.SpriteSequence;
        protected override string LogTag => "[TJGeneratorsSpriteSequence]";

        // ========== 窗口特定字段 ==========
        private string actionDescription = "";
        private string referenceImagePath = "";
        private Texture2D referenceImageThumb;

        // ========== 目标资产 ==========
        [SerializeField]
        private TJGeneratorsAssetReference targetAnimationAsset;

        private static readonly Dictionary<string, TJGeneratorsSpriteSequenceWindow> s_openWindows = new Dictionary<string, TJGeneratorsSpriteSequenceWindow>();

        // ========== 公开方法 ==========

        public static void ShowWindow()
        {
            var rect = GetDefaultMainWindowRect();
            var window = GetWindowWithRect<TJGeneratorsSpriteSequenceWindow>(
                rect,
                utility: false,
                title: TJGeneratorsL10n.L("TJGenerators 2D动作序列帧"),
                focus: true
            );
            window.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 2D动作序列帧"));
            FinalizeMainWindowShow(window, rect);
        }

        /// <summary>
        /// 从指定 AnimationClip 资产路径打开窗口；生成时将写入该资产或与之关联历史。
        /// </summary>
        public static void OpenForAsset(string assetPath)
        {
            GenerationWindowBase.OpenForAsset(
                assetPath,
                s_openWindows,
                "[TJGeneratorsSpriteSequence]",
                TJGeneratorsL10n.L("TJGenerators 2D动作序列帧 - {0}"),
                () =>
                {
                    var window = CreateInstance<TJGeneratorsSpriteSequenceWindow>();
                    return window;
                },
                (w, r) => w.targetAnimationAsset = r,
                ShowWindow);
        }

        // ========== 生命周期 ==========

        protected override void OnBootstrapWindowContent()
        {
            if (targetAnimationAsset != null && !string.IsNullOrEmpty(targetAnimationAsset.guid))
                s_openWindows[targetAnimationAsset.guid] = this;

            InitializeGeneratorsFromConfig(ConfigType.SpriteSequence);
            OnRefreshWindowContent();
        }

        protected override void OnRefreshWindowContent()
        {
            generationHistory = TJGeneratorsHistoryManager.LoadHistoryForAsset(GetCurrentAssetGuid());
            if (generationHistory.Count > 0)
                selectedHistoryIndex = 0;
            CheckAndRecoverInterruptedTasks();
            Repaint();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            wantsMouseMove = true;

            EditorCoroutineUtility.StartCoroutineOwnerless(UserInfoHelper.GetUserInfoCoroutine(ConfigManager.GetUserInfoUrl(), OnUserInfoLoaded));
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            wantsMouseMove = false;
            if (targetAnimationAsset != null && !string.IsNullOrEmpty(targetAnimationAsset.guid))
                s_openWindows.Remove(targetAnimationAsset.guid);

            ClearPreviewCaches();

            if (referenceImageThumb != null)
            {
                DestroyImmediate(referenceImageThumb);
                referenceImageThumb = null;
            }
        }

        // ========== 任务恢复 ==========

        protected override string GetCurrentAssetGuid() => targetAnimationAsset?.guid ?? "";

        protected override void SetHistory(List<TJGeneratorsGenerationHistoryItem> history)
        {
            generationHistory = history;
            if (generationHistory.Count > 0) selectedHistoryIndex = 0;
        }

        protected override void OnGeneratorRestoredFromTask(ModelGeneratorBase generator)
        {
            base.OnGeneratorRestoredFromTask(generator);
            currentSelectedModel = BuildModelInfoFromGenerator(generator);
            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("恢复中...");
        }

        // ========== UI 绘制 ==========

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();
            var splitLayout = UIComponents.CalculateFixedSplitLayout(
                position.width,
                CommonStyles.MainWindowMinSize.y,
                CommonStyles.LeftPanelFixedWidth,
                CommonStyles.MinHistoryPanelWidth,
                CommonStyles.OuterMargin);
            minSize = new Vector2(splitLayout.WindowMinWidth, splitLayout.WindowMinHeight);
            isVerticalLayout = false;
            currentHistoryPanelWidth = splitLayout.RightPanelWidth;
            _effectiveLeftPanelWidth = CommonStyles.LeftComponentWidth;

            if (_generators == null || _generators.Count == 0)
            {
                EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), CommonStyles.WindowBackgroundColor);
                EditorGUILayout.HelpBox(TJGeneratorsL10n.L("未找到可用的 2D 序列帧生成器，请检查 GeneratorConfig.json 中的 spriteSequenceGenerators"), MessageType.Error);
                return;
            }

            UIComponents.DrawAdaptiveLayoutBackground(
                new Rect(0, 0, position.width, position.height),
                false,
                splitLayout.LeftPanelWidth,
                position.height);

            GUILayout.BeginHorizontal();
            DrawLeftPanelColumn(
                splitLayout.LeftPanelWidth,
                ref scrollPosition,
                () =>
                {
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                    GUILayout.BeginVertical(
                        GUILayout.Width(CommonStyles.LeftComponentWidth),
                        GUILayout.MinWidth(CommonStyles.LeftComponentWidth),
                        GUILayout.MaxWidth(CommonStyles.LeftComponentWidth));

                    UIComponents.DrawTargetHeaderComposite(
                        TJGeneratorsL10n.L("目标动画"),
                        DrawTargetHeaderContentRect,
                        SelectTargetAnimationAsset
                    );
                    GUILayout.Space(CommonStyles.Space2);
                    UIComponents.DrawModelSelector(
                        currentSelectedModel?.Name ?? _currentGenerator?.DisplayName ?? TJGeneratorsL10n.L("未选择"),
                        currentSelectedModel,
                        OnModelSelected,
                        ConfigType.SpriteSequence
                    );
                    GUILayout.Space(CommonStyles.Space3);
                    DrawInputSection();
                    GUILayout.Space(CommonStyles.Space3);
                    DrawAdvancedSection();
                    GUILayout.Space(CommonStyles.Space3);

                    GUILayout.EndVertical();
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                });

            GUILayout.Space(splitLayout.GapWidth);

            DrawHistoryPanel(currentHistoryPanelWidth);
            GUILayout.EndHorizontal();

            DrawLeftPanelBottomDock(splitLayout.LeftPanelWidth, DrawGenerationSection);
        }

        private void DrawTargetHeaderContentRect(Rect rect)
        {
            if (targetAnimationAsset != null && targetAnimationAsset.IsValid())
            {
                string assetPath = targetAnimationAsset.GetPath();
                string name = Path.GetFileNameWithoutExtension(assetPath);
                if (GUI.Button(rect, name, CommonStyles.TargetPrefabNameStyle))
                    SelectTargetAnimationAsset();
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
            else
            {
                GUI.Label(rect, TJGeneratorsL10n.L("未绑定（生成到历史）"), CommonStyles.ContentStyle);
            }
        }

        private void OnModelSelected(AIModelInfo model) => OnModelSelectedBase(model);

        protected override void ResetInputStateAfterModelChange()
        {
            var config = GetCurrentGeneratorConfig();
            ResetTextPromptIfHidden(config, ref actionDescription);
            ClearSingleReferenceImageWhenUploadHidden(config, ref referenceImagePath, ref referenceImageThumb);
        }

        private void SelectTargetAnimationAsset()
        {
            if (targetAnimationAsset == null || !targetAnimationAsset.IsValid())
                return;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetAnimationAsset.GetPath());
            if (clip != null)
            {
                EditorGUIUtility.PingObject(clip);
                Selection.activeObject = clip;
            }
        }

        private void DrawInputSection()
        {
            var genConfig = GetCurrentGeneratorConfig();
            actionDescription = DrawConfiguredTextPromptInput(
                actionDescription,
                "sprite_sequence_prompt_input",
                genConfig);

            if (ShouldShowImageUpload(genConfig))
            {
                if (ShouldShowTextInput(genConfig))
                    GUILayout.Space(CommonStyles.Space3);
                var uiLayout = genConfig?.uiLayout;
                UIComponents.DrawReferenceImageSectionTitle(
                    ResolveImageUploadLabel(uiLayout),
                    string.IsNullOrEmpty(referenceImagePath) ? 0 : 1,
                    1);
                GUILayout.Space(CommonStyles.Space2);
                UploadImageComponents.DrawLargeImageUpload(
                    ref referenceImagePath,
                    ref referenceImageThumb,
                    null,
                    Repaint,
                    onPickDone: (path, tex) =>
                    {
                        referenceImagePath = path;
                        referenceImageThumb = tex;
                    });
            }
        }

        private void DrawAdvancedSection()
        {
            var provider = _currentGenerator as IGeneratorParameterProvider;
            showAdvancedSettings = DrawConfiguredAdvancedSettingsFoldout(
                showAdvancedSettings,
                provider,
                GetCurrentGeneratorParameters());
            if (provider is DynamicGenerator dyn)
                dyn.SyncReferenceImagesForCostPreview(!string.IsNullOrEmpty(referenceImagePath));
            SyncGenerationCostWithCurrentGeneratorState();
        }

        private void DrawGenerationSection(LeftPanelBottomDock.Layout layout)
        {
            UIComponents.DrawGenerationSectionAt(
                layout,
                isGenerating,
                generationProgress,
                generationStatus,
                _currentGenerator != null && !string.IsNullOrEmpty(referenceImagePath),
                StartGeneration,
                null,
                Repaint,
                currentGenerationCost);
        }

        // ========== 历史记录 ==========

        private void DrawHistoryPanel(float panelWidth)
        {
            DrawStandardHistoryPanel(panelWidth, new StandardHistoryPanelOptions
            {
                GetLargePreviewTexture = GetPreviewTextureForHistoryItem,
                DrawTilePreview = DrawSpriteSequenceHistoryPreview,
                GetModelLabel = item => item.GetTimeString(),
                DrawHistoryActions = DrawHistoryActions,
            });
        }

        private void DrawSpriteSequenceHistoryPreview(Rect rect, TJGeneratorsGenerationHistoryItem item)
        {
            if (item.isGenerating)
            {
                UIComponents.DrawLoadingSpinner(rect, null, Repaint);
                return;
            }

            Texture2D preview = GetPreviewTextureForHistoryItem(item);
            if (preview != null)
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
        }

        private void DrawHistoryActions()
        {
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            bool hasSelection = selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count;
            bool isGenerating = hasSelection && generationHistory[selectedHistoryIndex].isGenerating;
            GUI.enabled = hasSelection && !isGenerating;

            if (GUILayout.Button(TJGeneratorsL10n.L("应用到当前动画"), GUILayout.Height(25)))
            {
                ApplyHistoryToAnimation(selectedHistoryIndex);
            }
            if (GUILayout.Button(TJGeneratorsL10n.L("在项目中显示"), GUILayout.Height(25)))
            {
                ShowHistoryInProject(selectedHistoryIndex);
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private Texture2D GetPreviewTextureForHistoryItem(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating) return null;

            // 优先使用 URL 预览图（内存缓存）
            if (!string.IsNullOrEmpty(item.previewImageUrl))
            {
                if (urlPreviewCache.TryGetValue(item.previewImageUrl, out var cachedTex) && cachedTex != null)
                    return cachedTex;

                // 检查本地文件缓存
                var localTex = LoadPreviewFromLocalCache(item.previewImageUrl);
                if (localTex != null)
                {
                    urlPreviewCache[item.previewImageUrl] = localTex;
                    return localTex;
                }

                // 触发下载（异步，下次 Repaint 时显示）
                if (!urlPreviewLoading.Contains(item.previewImageUrl) && !urlPreviewFailed.Contains(item.previewImageUrl))
                    EditorCoroutineUtility.StartCoroutineOwnerless(DownloadPreviewImage(item.previewImageUrl));
            }

            // Fallback：从本地 AnimationClip 取第一帧 Sprite
            if (!string.IsNullOrEmpty(item.modelPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(item.modelPath);
                if (clip != null)
                {
                    try
                    {
                        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                        foreach (var binding in bindings)
                        {
                            if (binding.propertyName != null && binding.propertyName.Contains("m_Sprite"))
                            {
                                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                                var sprite = keys != null && keys.Length > 0 ? keys[0].value as Sprite : null;
                                if (sprite != null)
                                    return AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite) as Texture2D;
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private Texture2D LoadPreviewFromLocalCache(string imageUrl)
        {
            string cacheDir = Path.Combine(Application.dataPath, "../Library/AI.TJGenerators/PreviewCache");
            string hash = imageUrl.GetHashCode().ToString("X8");
            string path = Path.Combine(cacheDir, hash + ".png");
            if (!File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(File.ReadAllBytes(path)))
                    return tex;
                DestroyImmediate(tex);
            }
            catch { }
            return null;
        }

        private IEnumerator DownloadPreviewImage(string imageUrl)
        {
            urlPreviewLoading.Add(imageUrl);
            using (var uwr = UnityWebRequestTexture.GetTexture(imageUrl))
            {
                yield return uwr.SendWebRequest();
                if (UnityWebRequestCompat.IsSuccess(uwr) && uwr.downloadHandler.data != null)
                {
                    var tex = DownloadHandlerTexture.GetContent(uwr);
                    if (tex != null)
                    {
                        urlPreviewCache[imageUrl] = tex;
                        string cacheDir = Path.Combine(Application.dataPath, "../Library/AI.TJGenerators/PreviewCache");
                        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                        string hash = imageUrl.GetHashCode().ToString("X8");
                        File.WriteAllBytes(Path.Combine(cacheDir, hash + ".png"), tex.EncodeToPNG());
                    }
                }
                else
                {
                    urlPreviewFailed.Add(imageUrl);
                }
            }
            urlPreviewLoading.Remove(imageUrl);
            Repaint();
        }

        // ========== 生成逻辑 ==========

        private void StartGeneration()
        {
            if (_currentGenerator == null)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("请先选择模型"), LogTag);
                return;
            }


            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("准备中...");
            generationProgress = 0f;

            if (_currentGenerator is DynamicGenerator dynamicGen)
            {
                dynamicGen.SetImagePath(!string.IsNullOrEmpty(referenceImagePath) ? referenceImagePath : null);
            }

            string assetGuid = targetAnimationAsset?.guid ?? "";
            EditorCoroutineUtility.StartCoroutineOwnerless(_pipeline.StartGeneration(_currentGenerator, assetGuid));
        }

        private void ApplyHistoryToAnimation(int index)
        {
            if (index < 0 || index >= generationHistory.Count) return;
            var item = generationHistory[index];
            if (item.isGenerating)
            {
                Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("请等待该条生成完成后再应用。")}");
                return;
            }
            if (string.IsNullOrEmpty(item.modelPath) || !File.Exists(item.modelPath))
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("动画文件不存在，可能已被删除。"), LogTag);
                TJGeneratorsHistoryManager.RemoveFromHistory(item.modelPath);
                generationHistory = TJGeneratorsHistoryManager.LoadHistoryForAsset(GetCurrentAssetGuid());
                Repaint();
                return;
            }
            if (targetAnimationAsset == null || !targetAnimationAsset.IsValid())
            {
                Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("当前未绑定目标动画资产，无法应用。")}");
                return;
            }

            string targetPath = targetAnimationAsset.GetPath();
            try
            {
                File.Copy(item.modelPath, targetPath, true);
                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
                if (clip != null)
                {
                    Selection.activeObject = clip;
                    EditorGUIUtility.PingObject(clip);
                }
                Debug.Log($"{LogTag} {TJGeneratorsL10n.L("已将历史记录应用到当前动画。")}");
            }
            catch (Exception ex)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("应用失败: {0}", ex.Message), LogTag);
            }
        }

        private void ShowHistoryInProject(int index)
        {
            if (index < 0 || index >= generationHistory.Count) return;
            var item = generationHistory[index];
            if (string.IsNullOrEmpty(item.modelPath)) return;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(item.modelPath);
            if (clip != null)
            {
                Selection.activeObject = clip;
                EditorGUIUtility.PingObject(clip);
            }
        }

        // ========== IGenerationPipelineHost ==========

        public TJGeneratorsAssetReference GetTargetAsset() => targetAnimationAsset;


        public void OnGenerationCompleted(string assetPath)
        {
            if (generationHistory != null && !string.IsNullOrEmpty(assetPath))
            {
                int index = generationHistory.FindIndex(x => x.modelPath == assetPath || x.imagePath == assetPath);
                if (index >= 0)
                {
                    selectedHistoryIndex = index;
                }
            }

            isGenerating = false;
            generationStatus = TJGeneratorsL10n.L("完成");
            generationProgress = 1f;

            if (!string.IsNullOrEmpty(assetPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip != null)
                {
                    Selection.activeObject = clip;
                    EditorGUIUtility.PingObject(clip);
                }
            }
        }

        public string GetAssetSavePath(PipelineMediaType _type, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return null;

            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                AssetDatabase.CreateFolder("Assets/TJGenerators", "History");
            string uniqueName = "SpriteSequence_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            return AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/History/" + uniqueName);
        }

        public void OnAssetSaved(PipelineMediaType _type, string savePath, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return;

            var importer = AssetImporter.GetAtPath(savePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));
        }

        public void StartGeneration(ModelGeneratorBase generator)
        {
            if (generator == _currentGenerator)
                StartGeneration();
        }

    }
}
#endif
