#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.EditorCoroutines.Editor;
using TJGenerators.Config;
using TJGenerators.Generators;
using TJGenerators.Pipeline;
using TJGenerators.UI;
using TJGenerators.Utils;

namespace TJGenerators
{
    /// <summary>
    /// TJGenerators 图片生成窗口（文生图 / 图生图）。
    /// </summary>
    public class TJGeneratorsImageWindow : GenerationWindowBase, IGenerationPipelineHost, IGenerationTriggerHost, IMediaAssetPipelineHost
    {
        // ========== 固定配置 ==========
        protected override ConfigType WindowConfigType => ConfigType.Image;
        protected override string LogTag => "[TJGeneratorsImage]";

        [SerializeField]
        private string textPrompt = "";

        [SerializeField]
        private TJGeneratorsAssetReference targetImageAsset;

        private readonly List<string> referenceImagePaths = new List<string>();
        private readonly List<Texture2D> referenceUploadedImages = new List<Texture2D>();

        private static readonly Dictionary<string, TJGeneratorsImageWindow> imageOpenWindows =
            new Dictionary<string, TJGeneratorsImageWindow>();

        private Texture2D imagePreviewTexture;

        /// <summary>outputType 为 image 且配置启用时可选；prompt 经 DynamicRequestJsonBuilder.BuildEnhancedPrompt 拼为前缀</summary>
        private MaterialTemplateOptionConfig selectedPromptTemplate;

        private const string UnityTerrainHeightmapTemplateId = "unity_terrain_heightmap";

        [SerializeField]
        private bool terrainHeightmapGaussianBlur = true;

        [SerializeField]
        private bool terrainHeightmapMedian3x3 = true;

        [SerializeField]
        [Range(0.5f, 3f)]
        private float terrainHeightmapBlurSigma = 1.2f;

        [SerializeField]
        private bool terrainHeightmapRemapFoldout = true;

        [SerializeField]
        private bool terrainHeightmapPercentileNormalize = true;

        [SerializeField]
        [Range(0f, 0.2f)]
        private float terrainHeightmapPercentileLow = 0.05f;

        [SerializeField]
        [Range(0.8f, 1f)]
        private float terrainHeightmapPercentileHigh = 0.95f;

        [SerializeField]
        [Range(0.35f, 2.5f)]
        private float terrainHeightmapHeightGamma = 1f;

        [SerializeField]
        [Range(0f, 1f)]
        private float terrainHeightmapRemapOutMin = 0.02f;

        [SerializeField]
        [Range(0f, 1f)]
        private float terrainHeightmapRemapOutMax = 0.98f;

        // ========== 静态入口 ==========
        public static void ShowWindow()
        {
            var rect = GetDefaultMainWindowRect();
            var window = GetWindowWithRect<TJGeneratorsImageWindow>(
                rect,
                utility: false,
                title: TJGeneratorsL10n.L("TJGenerators 图片生成"),
                focus: true
            );
            window.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 图片生成"));
            FinalizeMainWindowShow(window, rect);
        }

        public static void OpenForAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                ShowWindow();
                return;
            }

            if (!IsSupportedImageAssetPath(assetPath))
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("TJGenerators 图片生成"),
                    TJGeneratorsL10n.L("仅支持绑定 .jpg / .jpeg / .png 的图片资产。\r\n\r\n建议先创建「生成图片」新资产。"),
                    "[TJGeneratorsImage]"
                );
                return;
            }

            GenerationWindowBase.OpenForAsset(
                assetPath,
                imageOpenWindows,
                "[TJGeneratorsImage]",
                TJGeneratorsL10n.L("TJGenerators 图片 - {0}"),
                () =>
                {
                    var window = CreateInstance<TJGeneratorsImageWindow>();
                    return window;
                },
                (w, r) => w.targetImageAsset = r,
                ShowWindow);
        }

        private static bool IsSupportedImageAssetPath(string assetPath) =>
            assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        // ========== 生命周期 ==========
        protected override void OnBootstrapWindowContent()
        {
            if (targetImageAsset != null && !string.IsNullOrEmpty(targetImageAsset.guid))
                imageOpenWindows[targetImageAsset.guid] = this;

            InitializeGeneratorsFromConfig(ConfigType.Image);
            OnRefreshWindowContent();
        }

        protected override void OnRefreshWindowContent()
        {
            RefreshHistory();
            CheckAndRecoverInterruptedTasks();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            wantsMouseMove = true;

            EditorCoroutineUtility.StartCoroutineOwnerless(
                UserInfoHelper.GetUserInfoCoroutine(
                    ConfigManager.GetUserInfoUrl(),
                    OnUserInfoLoaded
                )
            );
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            wantsMouseMove = false;
            if (targetImageAsset != null && !string.IsNullOrEmpty(targetImageAsset.guid))
            {
                imageOpenWindows.Remove(targetImageAsset.guid);
            }

            imagePreviewTexture = null;
            ClearPreviewCaches();

            foreach (var tex in referenceUploadedImages)
            {
                if (tex != null)
                    DestroyImmediate(tex);
            }
            referenceUploadedImages.Clear();
            referenceImagePaths.Clear();
        }

        // ========== 任务恢复 ==========
        protected override string GetCurrentAssetGuid() => GetCurrentImageAssetGuid();

        protected override void SetHistory(List<TJGeneratorsGenerationHistoryItem> history) =>
            generationHistory = history;

        protected override void OnGeneratorRestoredFromTask(ModelGeneratorBase generator)
        {
            base.OnGeneratorRestoredFromTask(generator);
            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("恢复中...");
            Repaint();
        }

        // ========== UI ==========
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
            maxSize = new Vector2(10000f, 10000f);
            isVerticalLayout = false;
            currentHistoryPanelWidth = splitLayout.RightPanelWidth;
            _effectiveLeftPanelWidth = CommonStyles.LeftComponentWidth;

            if (_generators == null || _generators.Count == 0)
            {
                EditorGUI.DrawRect(
                    new Rect(0, 0, position.width, position.height),
                    CommonStyles.WindowBackgroundColor
                );
                EditorGUILayout.HelpBox(TJGeneratorsL10n.L("未找到可用的图片生成器，请检查配置"), MessageType.Error);
                return;
            }

            UIComponents.DrawAdaptiveLayoutBackground(
                new Rect(0, 0, position.width, position.height),
                false,
                splitLayout.LeftPanelWidth,
                position.height
            );

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
                        TJGeneratorsL10n.L("目标图片"),
                        DrawTargetHeaderContentRect,
                        SelectTargetImageAsset
                    );
                    GUILayout.Space(CommonStyles.Space2);

                    UIComponents.DrawModelSelector(
                        currentSelectedModel?.Name ?? _currentGenerator?.DisplayName ?? TJGeneratorsL10n.L("未选择"),
                        currentSelectedModel,
                        OnModelSelected,
                        ConfigType.Image
                    );

                    GUILayout.Space(CommonStyles.Space3);

                    DrawInputSection();

                    GUILayout.Space(CommonStyles.Space3);

                    DrawConfigurationSection();

                    GUILayout.Space(CommonStyles.Space3);

                    DrawTerrainHeightmapAfterGenerationSection();

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
            if (targetImageAsset != null && targetImageAsset.IsValid())
            {
                string imageName = Path.GetFileNameWithoutExtension(targetImageAsset.GetPath());
                if (GUI.Button(rect, imageName, CommonStyles.TargetPrefabNameStyle))
                    SelectTargetImageAsset();
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
            else
            {
                GUI.Label(rect, TJGeneratorsL10n.L("未绑定（生成时自动创建）"), CommonStyles.ContentStyle);
            }
        }

        private void SelectTargetImageAsset()
        {
            if (targetImageAsset == null || !targetImageAsset.IsValid())
                return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(targetImageAsset.GetPath());
            if (tex != null)
            {
                EditorGUIUtility.PingObject(tex);
                Selection.activeObject = tex;
            }
        }

        private GeneratorConfig GetActiveImageGeneratorConfig()
        {
            return _currentGenerator == null
                ? null
                : GetGeneratorConfigFromIndex(_currentGenerator.GeneratorId);
        }

        private void ShowPromptTemplateSelectorWindow()
        {
            var cfg = GetActiveImageGeneratorConfig();
            if (cfg?.promptTemplateSelector?.options == null || cfg.promptTemplateSelector.options.Count == 0)
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("提示词模板不可用"),
                    TJGeneratorsL10n.L("当前模型未配置提示词模板选项（options 为空）"),
                    LogTag
                );
                return;
            }

            TJGeneratorsMaterialTemplateSelectorWindow.ShowWindow(
                cfg.promptTemplateSelector.options,
                OnPromptTemplateSelected,
                string.IsNullOrEmpty(cfg.promptTemplateSelector.title)
                    ? TJGeneratorsL10n.L("选择提示词")
                    : TJGeneratorsL10n.L(cfg.promptTemplateSelector.title),
                showPreviewThumbnails: false
            );
        }

        private void OnPromptTemplateSelected(MaterialTemplateOptionConfig template)
        {
            selectedPromptTemplate = template;

            if (_currentGenerator is DynamicGenerator dg)
                dg.SetPromptTemplateSelection(selectedPromptTemplate);

            Repaint();
        }

        private void DrawPromptTemplateSelector()
        {
            var cfg = GetActiveImageGeneratorConfig();
            if (cfg?.promptTemplateSelector == null
                || !cfg.promptTemplateSelector.enabled
                || cfg.promptTemplateSelector.options == null
                || cfg.promptTemplateSelector.options.Count == 0)
            {
                return;
            }

            string title = string.IsNullOrEmpty(cfg.promptTemplateSelector.title)
                ? TJGeneratorsL10n.L("提示词模板")
                : TJGeneratorsL10n.L(cfg.promptTemplateSelector.title);

            UIComponents.DrawSelectionRow(
                title,
                TJGeneratorsL10n.L("选择提示词"),
                CommonStyles.DropBoxRightArrow4xTexture,
                ShowPromptTemplateSelectorWindow,
                TJGeneratorsL10n.L(selectedPromptTemplate?.name));

            GUILayout.Space(CommonStyles.Space3);
        }

        private void DrawInputSection()
        {
            DrawPromptTemplateSelector();

            var genConfig = GetCurrentGeneratorConfig();
            textPrompt = DrawConfiguredTextPromptInput(textPrompt, "image_prompt_input", genConfig);

            if (ShouldShowImageUpload(genConfig))
            {
                GUILayout.Space(CommonStyles.Space3);
                DrawReferenceImagesSection();
            }
        }

        private void DrawReferenceImagesSection()
        {
            DrawConfiguredReferenceImageUpload(
                referenceImagePaths,
                referenceUploadedImages,
                "image_reference_upload");
        }

        private void DrawConfigurationSection()
        {
            var provider = _currentGenerator as IGeneratorParameterProvider;

            var allParams = GetCurrentGeneratorParameters();
            List<ParameterConfig> filteredParams = null;
            if (allParams != null && allParams.Count > 0)
            {
                filteredParams = new List<ParameterConfig>(allParams.Count);
                for (int i = 0; i < allParams.Count; i++)
                {
                    var p = allParams[i];
                    if (p == null || string.IsNullOrEmpty(p.id))
                        continue;

                    if (p.id == "isSegmentation" || p.id == "qValue" || p.id == "resizeWidth")
                        continue;

                    filteredParams.Add(p);
                }
            }

            showAdvancedSettings = DrawConfiguredAdvancedSettingsFoldout(
                showAdvancedSettings,
                provider,
                filteredParams
            );

            if (provider is DynamicGenerator dyn)
            {
                bool hasRef = referenceImagePaths != null && referenceImagePaths.Count > 0;
                dyn.SyncReferenceImagesForCostPreview(hasRef);
            }
            SyncGenerationCostWithCurrentGeneratorState();
        }

        private void DrawGenerationSection(LeftPanelBottomDock.Layout layout)
        {
            bool canGenerate =
                _currentGenerator != null && !string.IsNullOrWhiteSpace(textPrompt);
            UIComponents.DrawGenerationSectionAt(
                layout,
                isGenerating,
                generationProgress,
                generationStatus,
                canGenerate,
                StartGeneration,
                null,
                Repaint,
                currentGenerationCost
            );
        }

        private bool IsUnityTerrainHeightmapTemplateSelected()
        {
            return string.Equals(
                selectedPromptTemplate?.id,
                UnityTerrainHeightmapTemplateId,
                StringComparison.OrdinalIgnoreCase
            );
        }

        /// <summary>地形模板：后处理选项与「一键生成地形」位于生成按钮下方，顺序为「生成 → 后处理设置 → 建地形」。</summary>
        private void DrawTerrainHeightmapAfterGenerationSection()
        {
            if (!IsUnityTerrainHeightmapTemplateSelected())
                return;

            GUILayout.Label(TJGeneratorsL10n.L("地形高度图（生成后）"), CommonStyles.HeaderStyle);
            GUILayout.Space(6);

            GUILayout.Label(
                TJGeneratorsL10n.L("在右侧历史记录中选中对应 PNG 后，应用后处理并创建场景地形。"),
                CommonStyles.SmallGreyLabelStyle
            );
            GUILayout.Space(8);

            terrainHeightmapMedian3x3 = EditorGUILayout.ToggleLeft(
                TJGeneratorsL10n.L("后处理：Median 3x3 去尖刺（散点离群点）"),
                terrainHeightmapMedian3x3
            );

            GUILayout.Space(4);
            terrainHeightmapGaussianBlur = EditorGUILayout.ToggleLeft(
                TJGeneratorsL10n.L("后处理：高斯模糊平滑"),
                terrainHeightmapGaussianBlur
            );
            if (terrainHeightmapGaussianBlur)
            {
                EditorGUI.indentLevel++;
                terrainHeightmapBlurSigma = EditorGUILayout.Slider(
                    TJGeneratorsL10n.L("模糊强度 (σ)"),
                    terrainHeightmapBlurSigma,
                    0.5f,
                    3f
                );
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(8);
            terrainHeightmapRemapFoldout = EditorGUILayout.Foldout(
                terrainHeightmapRemapFoldout,
                TJGeneratorsL10n.L("高度重映射（类似 Terrain Tools · Height Remap）"),
                true
            );
            if (terrainHeightmapRemapFoldout)
            {
                EditorGUI.indentLevel++;
                terrainHeightmapPercentileNormalize = EditorGUILayout.ToggleLeft(
                    TJGeneratorsL10n.L("百分位拉伸（去掉极暗/极亮离群点再起有效对比）"),
                    terrainHeightmapPercentileNormalize
                );
                EditorGUI.BeginDisabledGroup(!terrainHeightmapPercentileNormalize);
                terrainHeightmapPercentileLow = EditorGUILayout.Slider(
                    new GUIContent(
                        TJGeneratorsL10n.L("低端截断"),
                        TJGeneratorsL10n.L("低于该百分位的亮度视作海平面一端，类似压低海底噪声")
                    ),
                    terrainHeightmapPercentileLow,
                    0f,
                    0.2f
                );
                terrainHeightmapPercentileHigh = EditorGUILayout.Slider(
                    new GUIContent(
                        TJGeneratorsL10n.L("高端截断"),
                        TJGeneratorsL10n.L("高于该百分位的亮度视作山顶一端")
                    ),
                    terrainHeightmapPercentileHigh,
                    0.8f,
                    1f
                );
                EditorGUI.EndDisabledGroup();
                if (terrainHeightmapPercentileHigh <= terrainHeightmapPercentileLow)
                    terrainHeightmapPercentileHigh =
                        Mathf.Min(1f, terrainHeightmapPercentileLow + 0.02f);

                terrainHeightmapHeightGamma = EditorGUILayout.Slider(
                    new GUIContent(
                        TJGeneratorsL10n.L("高度曲线 Gamma"),
                        TJGeneratorsL10n.L("1 = 线性；小于 1 中间调抬高（更陡）；大于 1 更平（更多平原）")
                    ),
                    terrainHeightmapHeightGamma,
                    0.35f,
                    2.5f
                );

                EditorGUILayout.LabelField(
                    TJGeneratorsL10n.L("输出垂直范围（归一化高度映射到 [最低, 最高]）"),
                    CommonStyles.SmallGreyLabelStyle
                );
                terrainHeightmapRemapOutMin = EditorGUILayout.Slider(
                    new GUIContent(TJGeneratorsL10n.L("输出最低"), TJGeneratorsL10n.L("地形最凹处对应高度图灰度下限")),
                    terrainHeightmapRemapOutMin,
                    0f,
                    1f
                );
                terrainHeightmapRemapOutMax = EditorGUILayout.Slider(
                    new GUIContent(TJGeneratorsL10n.L("输出最高"), TJGeneratorsL10n.L("地形最高处对应高度图灰度上限")),
                    terrainHeightmapRemapOutMax,
                    0f,
                    1f
                );
                if (terrainHeightmapRemapOutMax <= terrainHeightmapRemapOutMin)
                    terrainHeightmapRemapOutMax =
                        Mathf.Min(1f, terrainHeightmapRemapOutMin + 0.02f);

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            var selectedHistoryItem =
                selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count
                    ? generationHistory[selectedHistoryIndex]
                    : null;
            bool canTerrain =
                CanGenerateTerrainFromHistoryItem(selectedHistoryItem);
            EditorGUI.BeginDisabledGroup(!canTerrain);
            if (GUILayout.Button(TJGeneratorsL10n.L("一键生成地形"), GUILayout.Height(28)))
                GenerateTerrainFromHeightmap(selectedHistoryIndex);
            EditorGUI.EndDisabledGroup();

            if (!canTerrain)
            {
                GUILayout.Space(4);
                GUILayout.Label(
                    TJGeneratorsL10n.L("请先在历史中选中由本模板生成的已完成 PNG。"),
                    CommonStyles.SmallGreyLabelStyle
                );
            }
        }

        private void DrawHistoryPanel(float panelWidth)
        {
            DrawStandardHistoryPanel(panelWidth, new StandardHistoryPanelOptions
            {
                DrawLargePreviewBlock = DrawImageHistoryLargePreview,
                ScrollTopSpacing = 12f,
                BottomMargin = 90f,
                HistoryContentWidth = CommonStyles.HistoryScrollViewLayoutWidth(panelWidth),
                DrawTilePreview = DrawImageHistoryPreview,
                GetPrimaryLabel = GetHistoryUserPromptLabel,
                GetModelLabel = item => GetModelDisplayLabelFromIndex(item.modelVersion),
                ShowContextMenu = ShowHistoryContextMenu,
                DrawHistoryActions = DrawHistoryActions,
            });
        }

        private float DrawImageHistoryLargePreview(float panelWidth, float historyPanelHeight)
        {
            Texture2D historyPreviewTex = null;
            bool showHistoryPreview = false;
            if (selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count)
            {
                var selectedItem = generationHistory[selectedHistoryIndex];
                if (!selectedItem.isGenerating)
                {
                    showHistoryPreview = true;
                    historyPreviewTex = GetPreviewTextureForHistoryItem(selectedItem);
                }
            }

            if (historyPreviewTex == null)
                historyPreviewTex = imagePreviewTexture;

            return UIComponents.DrawHistoryTexturePreview(
                historyPreviewTex,
                showHistoryPreview || historyPreviewTex != null,
                isVerticalLayout,
                panelWidth,
                historyPanelHeight);
        }

        private void DrawImageHistoryPreview(Rect rect, TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating)
            {
                UIComponents.DrawLoadingSpinner(rect, CommonStyles.SmallGreyLabelStyle, Repaint);
                return;
            }

            if (!string.IsNullOrEmpty(item.modelPath))
            {
                if (
                    historyPreviewCache.TryGetValue(item.modelPath, out var cached)
                    && cached != null
                )
                {
                    GUI.DrawTexture(rect, cached, ScaleMode.ScaleToFit);
                    return;
                }

                var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(item.modelPath);
                if (assetTex != null)
                {
                    historyPreviewCache[item.modelPath] = assetTex;
                    GUI.DrawTexture(rect, assetTex, ScaleMode.ScaleToFit);
                    return;
                }

                string absPath = PathUtils.ToAbsoluteAssetPath(item.modelPath);
                if (File.Exists(absPath))
                {
                    // 异步加载本地预览图到缓存，避免OnGUI卡顿
                    EnqueuePreviewLoad(item.modelPath, absPath, false);
                }
            }

            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
            var iconRect = new Rect(
                rect.x + rect.width / 4,
                rect.y + rect.height / 4,
                rect.width / 2,
                rect.height / 2
            );
            GUI.Label(iconRect, EditorGUIUtility.IconContent("d_Texture2D Icon"));
        }

        private Texture2D GetPreviewTextureForHistoryItem(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating)
                return null;

            if (!string.IsNullOrEmpty(item.modelPath))
            {
                if (
                    historyPreviewCache.TryGetValue(item.modelPath, out var cached)
                    && cached != null
                )
                    return cached;

                var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(item.modelPath);
                if (assetTex != null)
                {
                    historyPreviewCache[item.modelPath] = assetTex;
                    return assetTex;
                }

                string absPath = PathUtils.ToAbsoluteAssetPath(item.modelPath);
                if (File.Exists(absPath))
                {
                    EnqueuePreviewLoad(item.modelPath, absPath, false);
                }
            }

            // 可选：如果历史项已经有 URL 预览缓存，也可以复用
            if (
                item.isTextToModel
                && !string.IsNullOrEmpty(item.previewImageUrl)
                && urlPreviewCache.TryGetValue(item.previewImageUrl, out var urlTex)
                && urlTex != null
            )
            {
                return urlTex;
            }

            return null;
        }

        private void DrawHistoryActions()
        {
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();

            bool hasSelection = selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count;
            bool isGenerating = hasSelection && generationHistory[selectedHistoryIndex].isGenerating;
            GUI.enabled = hasSelection && !isGenerating;

            if (GUILayout.Button(TJGeneratorsL10n.L("应用到当前图片"), GUILayout.Height(25)))
                ApplyHistoryToImage(selectedHistoryIndex);

            if (GUILayout.Button(TJGeneratorsL10n.L("在项目中显示"), GUILayout.Height(25)))
                ShowHistoryInProject(selectedHistoryIndex);

            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private static string GetHistoryUserPromptLabel(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null)
                return "";
            return TJGenerators.Utils.TJGeneratorsPromptDisplay.FormatHistoryTileLabel(item.GetUserFacingPrompt());
        }

        private void ShowHistoryContextMenu(int index)
        {
            if (index < 0 || index >= generationHistory.Count)
                return;
            var item = generationHistory[index];
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(TJGeneratorsL10n.L("应用到当前图片")), false, () => ApplyHistoryToImage(index));
            menu.AddItem(new GUIContent(TJGeneratorsL10n.L("在项目中显示")), false, () => ShowHistoryInProject(index));

            if (CanGenerateTerrainFromHistoryItem(item))
                menu.AddItem(
                    new GUIContent(TJGeneratorsL10n.L("一键生成地形")),
                    false,
                    () => GenerateTerrainFromHeightmap(index)
                );

            menu.AddSeparator("");

            if (!string.IsNullOrEmpty(item.modelPath))
                menu.AddItem(
                    new GUIContent(TJGeneratorsL10n.L("在资源管理器中显示")),
                    false,
                    () => EditorUtility.RevealInFinder(item.modelPath)
                );

            menu.AddSeparator("");

            menu.AddItem(
                new GUIContent(TJGeneratorsL10n.L("从历史记录中移除")),
                false,
                () =>
                {
                    TJGeneratorsHistoryManager.RemoveFromHistory(item.modelPath);
                    RefreshHistory();
                    if (generationHistory.Count == 0)
                        selectedHistoryIndex = -1;
                    else if (selectedHistoryIndex >= generationHistory.Count)
                        selectedHistoryIndex = Mathf.Max(0, generationHistory.Count - 1);
                    Repaint();
                }
            );

            menu.ShowAsContext();
        }

        private void ApplyHistoryToImage(int index)
        {
            if (index < 0 || index >= generationHistory.Count)
                return;
            var item = generationHistory[index];

            if (item.isGenerating)
            {
                Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("请等待该条生成完成后再应用。")}");
                return;
            }

            if (
                string.IsNullOrEmpty(item.modelPath)
                || !File.Exists(PathUtils.ToAbsoluteAssetPath(item.modelPath))
            )
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("该历史记录的图片文件不存在。"), LogTag);
                if (!string.IsNullOrEmpty(item.modelPath))
                    TJGeneratorsHistoryManager.RemoveFromHistory(item.modelPath);
                RefreshHistory();
                Repaint();
                return;
            }

            if (targetImageAsset == null || !targetImageAsset.IsValid())
            {
                Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("请先绑定或创建目标图片资产。")}");
                return;
            }

            string srcExt = string.IsNullOrEmpty(item.modelPath) ? ".png" : Path.GetExtension(item.modelPath);
            if (string.IsNullOrEmpty(srcExt)) srcExt = ".png";
            string targetPathForDialog = Path.ChangeExtension(targetImageAsset.GetPath(), srcExt);
            if (
                !EditorUtility.DisplayDialog(
                    TJGeneratorsL10n.L("确认替换"),
                    string.Format(TJGeneratorsL10n.L("确定将选中的图片应用到当前目标「{0}」吗？"), Path.GetFileNameWithoutExtension(targetPathForDialog)),
                    TJGeneratorsL10n.L("确定"),
                    TJGeneratorsL10n.L("取消")
                )
            )
            {
                return;
            }

            if (!ReplaceTargetImageFromSource(item.modelPath, TJGeneratorsL10n.L("已将历史图片应用到"), out string err))
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), string.IsNullOrEmpty(err) ? TJGeneratorsL10n.L("应用失败（详见控制台）。") : string.Format(TJGeneratorsL10n.L("应用失败: {0}"), err), LogTag);
            else
                RefreshHistory();

            Repaint();
        }

        /// <summary>
        /// 覆盖目标纹理资产前释放本窗口持有的引用，避免 Windows 下文件仍被 Unity/预览占用导致 File.Copy 失败。
        /// 行为对齐 <see cref="TJGeneratorsSpriteWindow"/> 在应用历史前对 <c>spritePreviewTexture</c> 的处理。
        /// </summary>
        /// <summary>
        /// 将源图片复制到当前目标资产；若扩展名变化则删除旧占位文件并更新 GUID / 历史记录，
        /// 与生成完成回调 <see cref="OnAssetSaved"/> 保持一致，避免同基名下残留 .jpg 与 .png 两个文件。
        /// </summary>
        private bool ReplaceTargetImageFromSource(string sourceAssetPath, string okLogVerb, out string errorMessage)
        {
            return TargetImageReplaceHelper.ReplaceTargetImageFromSource(
                sourceAssetPath,
                okLogVerb,
                LogTag,
                ref targetImageAsset,
                ref imagePreviewTexture,
                historyPreviewCache,
                ext =>
                {
                    EnsureTargetImage(ext);
                    return targetImageAsset;
                },
                TargetImageReplaceHelper.ConfigureDefaultTexture,
                OnTargetImageExtensionChanged,
                releaseExtraHandles: null,
                out errorMessage);
        }

        private void OnTargetImageExtensionChanged(string oldTargetGuid, string newTargetPath)
        {
            if (!string.IsNullOrEmpty(oldTargetGuid))
                imageOpenWindows.Remove(oldTargetGuid);

            titleContent = new GUIContent(
                string.Format(TJGeneratorsL10n.L("TJGenerators 图片 - {0}"), Path.GetFileNameWithoutExtension(newTargetPath)));

            string newGuid = targetImageAsset.guid;
            if (!string.IsNullOrEmpty(newGuid))
            {
                imageOpenWindows[newGuid] = this;
                TJGeneratorsHistoryManager.RewriteAssetGuid(oldTargetGuid, newGuid);
            }
        }

        private void ShowHistoryInProject(int index)
        {
            if (index < 0 || index >= generationHistory.Count)
                return;

            var item = generationHistory[index];
            if (string.IsNullOrEmpty(item.modelPath))
                return;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.modelPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        // ========== 生成 ==========
        private void StartGeneration()
        {
            if (string.IsNullOrWhiteSpace(textPrompt))
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("请输入文本提示词。"), LogTag);
                return;
            }

            bool hasImage = referenceImagePaths.Count > 0;

            if (_currentGenerator == null)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("未选择可用的生成模型。"), LogTag);
                return;
            }

            EnsureTargetImage();

            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("准备中...");
            generationProgress = 0f;

            if (_currentGenerator is DynamicGenerator dynamicGen)
            {
                dynamicGen.SetParameter("isSegmentation", false);
                dynamicGen.ClearExtraRawJsonFields();

                string finalPrompt = textPrompt.Trim();
                dynamicGen.SetPromptTemplateSelection(selectedPromptTemplate);
                dynamicGen.SetTextPrompt(finalPrompt);
                dynamicGen.SetHistoryDisplayPrompt(textPrompt.Trim());
                dynamicGen.SetImagePaths(hasImage ? referenceImagePaths : null);
            }

            string assetGuid = targetImageAsset?.guid ?? "";
            EditorCoroutineUtility.StartCoroutineOwnerless(
                _pipeline.StartGeneration(_currentGenerator, assetGuid)
            );
        }

        // ========== IGenerationPipelineHost ==========
        public TJGeneratorsAssetReference GetTargetAsset() => targetImageAsset;

        public void StartGeneration(ModelGeneratorBase generator)
        {
            if (generator == _currentGenerator)
                StartGeneration();
        }


        public void OnGenerationCompleted(string assetPath)
        {
            if (generationHistory != null && !string.IsNullOrEmpty(assetPath))
            {
                int index = generationHistory.FindIndex(x => x.imagePath == assetPath || x.modelPath == assetPath);
                if (index >= 0)
                {
                    selectedHistoryIndex = index;
                }
            }
            // 图片窗口不需要 3D/Prefab 预览
        }

        public string GetAssetSavePath(PipelineMediaType _type, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return null;

            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                AssetDatabase.CreateFolder("Assets/TJGenerators", "History");

            // 先用 .jpg 占位符路径（主图 savePath 会保留这个扩展名）
            string uniqueName = "Image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg";
            return AssetDatabase.GenerateUniqueAssetPath(
                "Assets/TJGenerators/History/" + uniqueName
            );
        }

        public void OnAssetSaved(PipelineMediaType _type, string savePath, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return;

            // 地形高度图后处理改为「一键生成地形」时执行，此处仅保留后端原图

            // 设置 history 文件本身的导入器（RGBA32 + alpha，避免索引色 PNG 被压成 DXT5）
            GeneratedTextureImportUtils.ConfigureImportedTexture(
                savePath, TextureImporterType.Default, alphaIsTransparency: true);

            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));

            // 同步更新绑定资产：扩展名变化时自动删除旧占位并重写历史 GUID，避免残留同名异扩展名文件
            if (!ReplaceTargetImageFromSource(savePath, TJGeneratorsL10n.L("已生成图片并复制到"), out string replaceErr))
                TJLog.LogWarning($"{LogTag} 同步目标图片失败: {replaceErr}");

            // 更新生成状态（历史刷新由 GenerationPipeline 在 CompletePlaceholder 后统一处理）
            generationStatus = TJGeneratorsL10n.L("完成");
            generationProgress = 1f;
            isGenerating = false;
        }

        /// <summary>
        /// 允许一键生成：已完成、本地 PNG 存在；且（历史里保存了地形模板 id，或当前窗口正选中地形高度图模板）。
        /// 避免仅依赖 <see cref="TJGeneratorsGenerationHistoryItem.promptTemplateId"/>（旧历史或序列化前记录为空时按钮长期灰色）。
        /// </summary>
        private bool CanGenerateTerrainFromHistoryItem(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating || string.IsNullOrEmpty(item.modelPath))
                return false;
            if (!item.modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!File.Exists(PathUtils.ToAbsoluteAssetPath(item.modelPath)))
                return false;

            if (
                string.Equals(
                    item.promptTemplateId,
                    UnityTerrainHeightmapTemplateId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;

            return IsUnityTerrainHeightmapTemplateSelected();
        }

        /// <summary>
        /// 复制历史中的原始高度图 → 后处理写入单独 PNG → 按 PNG 宽高设置 Terrain 世界尺寸并创建场景地形。
        /// </summary>
        private void GenerateTerrainFromHeightmap(int historyIndex)
        {
            if (historyIndex < 0 || historyIndex >= generationHistory.Count)
                return;

            var item = generationHistory[historyIndex];
            if (!CanGenerateTerrainFromHistoryItem(item))
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("无法生成地形"),
                    TJGeneratorsL10n.L("请选择由「Unity 地形高度图」模板生成且已完成的 PNG 历史记录。"),
                    LogTag
                );
                return;
            }

            var hmOpts = new TerrainHeightmapPostProcessOptions
            {
                median3x3 = terrainHeightmapMedian3x3,
                gaussianBlur = terrainHeightmapGaussianBlur,
                gaussianSigma = terrainHeightmapBlurSigma,
                percentileNormalization = terrainHeightmapPercentileNormalize,
                percentileLow = terrainHeightmapPercentileLow,
                percentileHigh = terrainHeightmapPercentileHigh,
                heightGamma = terrainHeightmapHeightGamma,
                remapOutputMin = terrainHeightmapRemapOutMin,
                remapOutputMax = Mathf.Max(
                    terrainHeightmapRemapOutMax,
                    terrainHeightmapRemapOutMin + 0.01f
                ),
            };

            var (_, _, _, error) = TerrainCreationUtils.PostProcessAndCreateTerrain(
                item.modelPath, hmOpts);

            if (!string.IsNullOrEmpty(error))
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("地形生成失败"), error, LogTag);

            Repaint();
        }

        // ========== 辅助方法 ==========
        private string GetCurrentImageAssetGuid() => targetImageAsset?.guid ?? "";

        private void OnModelSelected(AIModelInfo model) => OnModelSelectedBase(model);

        protected override void ResetInputStateAfterModelChange()
        {
            var config = GetCurrentGeneratorConfig();
            ResetTextPromptIfHidden(config, ref textPrompt);
            ClearReferenceImagesWhenUploadHidden(config, referenceImagePaths, referenceUploadedImages);
        }

        protected override void OnModelSelectedBase(AIModelInfo model)
        {
            base.OnModelSelectedBase(model);
            selectedPromptTemplate = null;
            if (_currentGenerator is DynamicGenerator dg)
                dg.SetPromptTemplateSelection(null);
            UploadImageComponents.TrimReferenceImagesToMax(
                referenceImagePaths,
                referenceUploadedImages,
                GetMaxReferenceImages());
        }

        private void EnsureTargetImage()
        {
            // 初始化阶段：只在未绑定/无效时创建占位图，不强制改动用户已绑定的扩展名。
            if (targetImageAsset != null && targetImageAsset.IsValid())
                return;

            EnsureTargetImage(".jpg");
        }

        private void EnsureTargetImage(string desiredExt)
        {
            desiredExt = (desiredExt ?? ".jpg").Trim();
            if (!desiredExt.StartsWith("."))
                desiredExt = "." + desiredExt;
            desiredExt = desiredExt.ToLowerInvariant();

            // 目标已有效时直接使用（无论扩展名是否与 desiredExt 一致）；
            // 后续 ReplaceTargetImageFromSource 会在保存实际结果时处理扩展名变化。
            if (targetImageAsset != null && targetImageAsset.IsValid())
                return;

            string folder = PathUtils.GetProjectBrowserInsertionFolderAssetPath();
            // 跨所有常见图片扩展名检查同基名占用，避免与同名但不同后缀的文件冲突（如已有 New Image.png 时不创建 New Image.jpg）。
            string path = TJGeneratorsImageAssetPathUtility.GenerateUniqueImagePath(
                $"{folder}/New Image{desiredExt}"
            );
            path = CreateBlankImage(path);

            if (string.IsNullOrEmpty(path))
            {
                TJLog.LogError($"{LogTag} 无法创建图片资产");
                return;
            }

            targetImageAsset = TJGeneratorsAssetReference.FromPath(path);
            titleContent = new GUIContent(
                string.Format(TJGeneratorsL10n.L("TJGenerators 图片 - {0}"), Path.GetFileNameWithoutExtension(path))
            );

            if (!string.IsNullOrEmpty(targetImageAsset.guid))
                imageOpenWindows[targetImageAsset.guid] = this;

            Repaint();
        }

        /// <summary>
        /// 创建空白图片资产（根据扩展名创建 JPG/PNG）。
        /// </summary>
        public static string CreateBlankImage(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
            {
                path = Path.ChangeExtension(path, ".jpg");
                ext = ".jpg";
            }

            string absolutePath = PathUtils.ToAbsoluteAssetPath(path);

            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var blank =
                ext == ".png"
                    ? new Texture2D(4, 4, TextureFormat.RGBA32, false)
                    : new Texture2D(4, 4, TextureFormat.RGB24, false);
            var pixels = new Color[16];
            // 与「生成精灵」占位一致：PNG 全透明；JPG 无 alpha 时用与历史缩略图占位相近的深灰，避免一开始整片发白。
            Color fill = ext == ".png" ? Color.clear : new Color(0.2f, 0.2f, 0.2f);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = fill;
            blank.SetPixels(pixels);
            blank.Apply();

            if (ext == ".png")
            {
                File.WriteAllBytes(absolutePath, blank.EncodeToPNG());
            }
            else
            {
                File.WriteAllBytes(absolutePath, blank.EncodeToJPG(75));
            }
            DestroyImmediate(blank);

            // 导入并设置类型
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
            }

            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(path));
            return path;
        }
    }
}
#endif
