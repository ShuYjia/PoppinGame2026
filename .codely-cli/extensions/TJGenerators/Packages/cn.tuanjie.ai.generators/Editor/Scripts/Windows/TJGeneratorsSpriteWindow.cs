#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using TJGenerators.Generators;
using TJGenerators.Pipeline;
using TJGenerators.Config;
using TJGenerators.UI;
using TJGenerators.Utils;
using Unity.UniAsset.Manager.Editor.InternalBridge;
using Unity.EditorCoroutines.Editor;

namespace TJGenerators
{
    /// <summary>
    /// 窗口模式枚举
    /// </summary>
    public enum WindowMode
    {
        Sprite,
        Material
    }

    /// <summary>
    /// TJGenerators Sprite 生成窗口 - 使用 huoshan_seedream 等生成器生成 Sprite 贴图
    /// 同时支持 Material 模式，通过图生图生成 Unity Material 素材
    /// </summary>
    public class TJGeneratorsSpriteWindow : GenerationWindowBase, IGenerationPipelineHost, IGenerationTriggerHost, IMediaAssetPipelineHost
    {
        // ========== 窗口模式 ==========
        [SerializeField]
        private WindowMode _currentMode = WindowMode.Sprite;

        // ========== 基类抽象属性实现 ==========
        protected override ConfigType WindowConfigType => _currentMode == WindowMode.Material ? ConfigType.Material : ConfigType.Sprite;
        protected override string LogTag => _currentMode == WindowMode.Material ? "[TJGeneratorsMaterial]" : "[TJGeneratorsSprite]";

        // ========== 窗口特定字段 ==========
        private string textPrompt = "";
        private readonly List<string> referenceImagePaths = new List<string>();
        private readonly List<Texture2D> referenceUploadedImages = new List<Texture2D>();
        /// <summary>材质模式参考图（与精灵模式的 referenceImagePaths 分离，由 UploadImageComponents + 选模板 维护）。</summary>
        private string materialReferenceImagePath = "";
        private Texture2D materialReferenceImageThumb = null;
        /// <summary>材质模式：已选模型但缺少参考图时，生成区旁的提示。</summary>
        private static string MaterialMissingReferenceMessage => TJGeneratorsL10n.L("请输入文本提示词，或选择纹理走势 / 上传材质模板图片");
        /// <summary>材质模式：开始生成前校验失败（未选模型或缺少输入）时的对话框文案。</summary>
        private static string MaterialStartBlockedMessage => TJGeneratorsL10n.L("请先选择模型，并输入文本提示词或选择纹理走势 / 上传材质模板图片。");

        [SerializeField]
        private TJGeneratorsAssetReference targetSpriteAsset;
        private static Dictionary<string, TJGeneratorsSpriteWindow> spriteOpenWindows = new Dictionary<string, TJGeneratorsSpriteWindow>();
        private static Dictionary<string, TJGeneratorsSpriteWindow> materialOpenWindows = new Dictionary<string, TJGeneratorsSpriteWindow>();

        // 类型和风格选择状态
        private SelectorOptionConfig selectedType = null;
        private SelectorOptionConfig selectedStyle = null;
        private MaterialTemplateOptionConfig selectedMaterialPreset = null;
        private MaterialTemplateOptionConfig selectedTexturePattern = null;
        private MaterialTemplateOptionConfig selectedMaterialStyle = null;
        private Texture2D spritePreviewTexture;

        private ConfigType CurrentModeConfigType =>
            _currentMode == WindowMode.Material ? ConfigType.Material : ConfigType.Sprite;

        private GeneratorConfig GetActiveGeneratorConfig()
        {
            return _currentGenerator == null
                ? null
                : GetGeneratorConfigFromIndex(_currentGenerator.GeneratorId);
        }

        private void ClearMaterialReferenceImage()
        {
            materialReferenceImagePath = "";
            if (materialReferenceImageThumb != null)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(materialReferenceImageThumb)))
                    DestroyImmediate(materialReferenceImageThumb);
                materialReferenceImageThumb = null;
            }
        }

        public static void ShowWindow()
        {
            var rect = GetDefaultMainWindowRect();
            var window = GetWindowWithRect<TJGeneratorsSpriteWindow>(
                rect,
                utility: false,
                title: TJGeneratorsL10n.L("TJGenerators 精灵生成"),
                focus: true
            );
            window._currentMode = WindowMode.Sprite;
            window.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 精灵生成"));
            FinalizeMainWindowShow(window, rect);
        }

        public static void ShowMaterialWindow()
        {
            var rect = GetDefaultMainWindowRect();
            var window = GetWindowWithRect<TJGeneratorsSpriteWindow>(
                rect,
                utility: false,
                title: TJGeneratorsL10n.L("TJGenerators 材质生成"),
                focus: true
            );
            window._currentMode = WindowMode.Material;
            window.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 材质生成"));
            FinalizeMainWindowShow(window, rect);
        }

        public static void OpenForAsset(string assetPath)
        {
            GenerationWindowBase.OpenForAsset(
                assetPath,
                spriteOpenWindows,
                "[TJGeneratorsSprite]",
                TJGeneratorsL10n.L("TJGenerators 精灵 - {0}"),
                () =>
                {
                    var window = CreateInstance<TJGeneratorsSpriteWindow>();
                    window._currentMode = WindowMode.Sprite;
                    return window;
                },
                (w, r) => w.targetSpriteAsset = r,
                ShowWindow);
        }

        /// <summary>
        /// 为指定的 Material 资产打开窗口
        /// </summary>
        public static void OpenForMaterialAsset(string assetPath)
        {
            GenerationWindowBase.OpenForAsset(
                assetPath,
                materialOpenWindows,
                "[TJGeneratorsMaterial]",
                TJGeneratorsL10n.L("TJGenerators 材质 - {0}"),
                () =>
                {
                    var window = CreateInstance<TJGeneratorsSpriteWindow>();
                    window._currentMode = WindowMode.Material;
                    return window;
                },
                (w, r) => w.targetSpriteAsset = r,
                ShowMaterialWindow);
        }

        /// <summary>
        /// 为材质生成产出的贴图 PNG 打开窗口（历史绑定在贴图本身；若有同名 .mat 则一并绑定）。
        /// </summary>
        public static void OpenForMaterialTextureAsset(string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath))
                return;

            string matPath = Path.ChangeExtension(texturePath, ".mat");
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
            {
                OpenForMaterialAsset(matPath);
                return;
            }

            GenerationWindowBase.OpenForAsset(
                texturePath,
                materialOpenWindows,
                "[TJGeneratorsMaterial]",
                TJGeneratorsL10n.L("TJGenerators 材质 - {0}"),
                () =>
                {
                    var window = CreateInstance<TJGeneratorsSpriteWindow>();
                    window._currentMode = WindowMode.Material;
                    return window;
                },
                (w, r) => w.targetSpriteAsset = r,
                ShowMaterialWindow);
        }

        protected override void OnBootstrapWindowContent()
        {
            if (targetSpriteAsset != null && !string.IsNullOrEmpty(targetSpriteAsset.guid))
            {
                if (_currentMode == WindowMode.Material)
                    materialOpenWindows[targetSpriteAsset.guid] = this;
                else
                    spriteOpenWindows[targetSpriteAsset.guid] = this;
            }

            InitializeGeneratorsFromConfig(CurrentModeConfigType);
            OnRefreshWindowContent();
        }

        protected override void OnRefreshWindowContent()
        {
            generationHistory = LoadGenerationHistory();
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
            if (targetSpriteAsset != null && !string.IsNullOrEmpty(targetSpriteAsset.guid))
            {
                if (_currentMode == WindowMode.Material)
                    materialOpenWindows.Remove(targetSpriteAsset.guid);
                else
                    spriteOpenWindows.Remove(targetSpriteAsset.guid);
            }

            // 清理基类预览纹理缓存（只销毁非 AssetDatabase 管理的运行时 Texture2D）
            ClearPreviewCaches();

            // 清理当前窗口的 sprite 预览纹理（避免销毁真正项目资产）
            if (spritePreviewTexture != null)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(spritePreviewTexture)))
                    DestroyImmediate(spritePreviewTexture);
                spritePreviewTexture = null;
            }

            foreach (var tex in referenceUploadedImages)
            {
                if (tex != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                    DestroyImmediate(tex);
            }
            referenceUploadedImages.Clear();
            referenceImagePaths.Clear();

            if (materialReferenceImageThumb != null)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(materialReferenceImageThumb)))
                    DestroyImmediate(materialReferenceImageThumb);
                materialReferenceImageThumb = null;
            }
            materialReferenceImagePath = "";
        }

        // ========== 任务恢复 ==========

        protected override string GetCurrentAssetGuid() => GetCurrentSpriteAssetGuid();

        public override void RefreshHistory()
        {
            if (_currentMode != WindowMode.Material)
            {
                base.RefreshHistory();
                return;
            }

            string oldSelectedPath = null;
            string oldSelectedTaskId = null;
            if (generationHistory != null && selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count)
            {
                var item = generationHistory[selectedHistoryIndex];
                oldSelectedPath = item.modelPath ?? item.imagePath;
                oldSelectedTaskId = item.taskId;
            }

            generationHistory = LoadGenerationHistory();

            if (generationHistory.Count > 0)
            {
                int newIndex = -1;
                if (!string.IsNullOrEmpty(oldSelectedPath) || !string.IsNullOrEmpty(oldSelectedTaskId))
                {
                    newIndex = generationHistory.FindIndex(x =>
                        (!string.IsNullOrEmpty(oldSelectedPath) && (x.modelPath == oldSelectedPath || x.imagePath == oldSelectedPath)) ||
                        (!string.IsNullOrEmpty(oldSelectedTaskId) && x.taskId == oldSelectedTaskId));
                }

                selectedHistoryIndex = newIndex >= 0 ? newIndex : 0;
            }
            else
            {
                selectedHistoryIndex = -1;
            }

            Repaint();
        }

        private List<TJGeneratorsGenerationHistoryItem> LoadGenerationHistory()
        {
            if (_currentMode == WindowMode.Material
                && targetSpriteAsset != null
                && targetSpriteAsset.IsValid())
            {
                string path = targetSpriteAsset.GetPath();
                if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    return TJGeneratorsHistoryManager.LoadHistoryForMaterialAsset(path);
                return TJGeneratorsHistoryManager.LoadHistoryForAsset(targetSpriteAsset.guid);
            }

            return TJGeneratorsHistoryManager.LoadHistoryForAsset(GetCurrentSpriteAssetGuid());
        }

        protected override void SetHistory(List<TJGeneratorsGenerationHistoryItem> history) => generationHistory = history;

        protected override void OnGeneratorRestoredFromTask(ModelGeneratorBase generator)
        {
            base.OnGeneratorRestoredFromTask(generator);
            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("恢复中...");
        }

        protected override void OnAfterGeneratorInitialize(ConfigType configType)
        {
            // Material 模式特定的调试日志
            if (_currentMode == WindowMode.Material && _currentGenerator != null)
            {
                var genConfig = GetActiveGeneratorConfig();
                TJLog.Log($"[TJGeneratorsMaterial] GeneratorId: {_currentGenerator.GeneratorId}");
                TJLog.Log($"[TJGeneratorsMaterial] genConfig != null: {genConfig != null}");
                if (genConfig != null)
                {
                    TJLog.Log($"[TJGeneratorsMaterial] materialPresetSelector != null: {genConfig.materialPresetSelector != null}");
                    TJLog.Log($"[TJGeneratorsMaterial] texturePatternSelector != null: {genConfig.texturePatternSelector != null}");
                    TJLog.Log($"[TJGeneratorsMaterial] materialStyleSelector != null: {genConfig.materialStyleSelector != null}");
                }
            }
        }

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
                string errorMsg = _currentMode == WindowMode.Material
                    ? TJGeneratorsL10n.L("未找到可用的 Material 生成器，请检查 GeneratorConfig.json 中的 materialGenerators")
                    : TJGeneratorsL10n.L("未找到可用的 Sprite 生成器，请检查 GeneratorConfig.json 中的 spriteGenerators");
                EditorGUILayout.HelpBox(errorMsg, MessageType.Error);
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
                        _currentMode == WindowMode.Material ? TJGeneratorsL10n.L("目标材质") : TJGeneratorsL10n.L("目标精灵"),
                        DrawTargetHeaderContentRect,
                        SelectTargetSpriteAsset
                    );
                    GUILayout.Space(CommonStyles.Space2);
                    UIComponents.DrawModelSelector(
                        currentSelectedModel?.Name ?? _currentGenerator?.DisplayName ?? TJGeneratorsL10n.L("未选择"),
                        currentSelectedModel,
                        OnModelSelected,
                        _currentMode == WindowMode.Material ? ConfigType.Material : ConfigType.Sprite
                    );
                    GUILayout.Space(CommonStyles.Space3);

                    DrawInputSection();
                    DrawTypeAndStyleSelectors();
                    if (_currentMode != WindowMode.Material)
                    {
                        var genConfig = GetActiveGeneratorConfig();
                        if (ShouldShowImageUpload(genConfig))
                        {
                            GUILayout.Space(CommonStyles.Space3);
                            DrawImageUploadArea();
                            GUILayout.Space(CommonStyles.Space2);
                        }
                    }
                    if (_currentMode == WindowMode.Material)
                    {
                        GUILayout.Space(CommonStyles.Space3);
                        UIComponents.DrawGapLine();
                        GUILayout.Space(CommonStyles.Space3);
                    }
                    DrawConfigurationSection();
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
            if (targetSpriteAsset != null && targetSpriteAsset.IsValid())
            {
                string name = Path.GetFileNameWithoutExtension(targetSpriteAsset.GetPath());
                if (GUI.Button(rect, name, CommonStyles.TargetPrefabNameStyle))
                    SelectTargetSpriteAsset();
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
            else
            {
                string unboundLabel = _currentMode == WindowMode.Material
                    ? TJGeneratorsL10n.L("未绑定（生成后自动创建材质）")
                    : TJGeneratorsL10n.L("未绑定（生成时自动创建）");
                GUI.Label(rect, unboundLabel, CommonStyles.ContentStyle);
            }
        }

        private void SelectTargetSpriteAsset()
        {
            if (targetSpriteAsset == null || !targetSpriteAsset.IsValid())
                return;
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetSpriteAsset.GetPath());
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        private void DrawTypeAndStyleSelectors()
        {
            if (_currentGenerator == null) return;
            var genConfig = GetActiveGeneratorConfig();
            if (genConfig == null) return;

            if (_currentMode == WindowMode.Material)
            {
                GUILayout.Space(CommonStyles.Space2);
                // DrawMaterialPresetSelector();  // 材质预设暂时隐藏
                DrawTexturePatternSelector();
                // DrawMaterialStyleSelector();  // 风格状态暂时隐藏
                return;
            }

            // Sprite 模式：显示类型和风格选择器
            // 优先使用生成器自有配置（需有有效 options），回退到共享配置
            var typeSelectorConfig = (genConfig.typeSelector?.options != null ? genConfig.typeSelector : null)
                                     ?? ConfigManager.GetSpriteTypeSelector();
            var styleSelectorConfig = (genConfig.styleSelector?.options != null ? genConfig.styleSelector : null)
                                      ?? ConfigManager.GetSpriteStyleSelector();
            bool hasTypeSelector = typeSelectorConfig != null && typeSelectorConfig.enabled;
            bool hasStyleSelector = styleSelectorConfig != null && styleSelectorConfig.enabled;

            if (!hasTypeSelector && !hasStyleSelector)
                return;

            // 类型选择
            if (hasTypeSelector)
            {
                UIComponents.DrawSelectionRow(
                    TJGeneratorsL10n.L("内容类型"),
                    TJGeneratorsL10n.L("选择类型"),
                    CommonStyles.DropBoxRightArrow4xTexture,
                    ShowTypeSelector,
                    selectedType != null ? TJGeneratorsL10n.L(selectedType.name) : null);
                GUILayout.Space(CommonStyles.Space2);
                if (hasStyleSelector)
                {
                    UIComponents.DrawGapLine();
                    GUILayout.Space(CommonStyles.Space2);
                }
            }

            // 风格选择
            if (hasStyleSelector)
            {
                UIComponents.DrawSelectionRow(
                    TJGeneratorsL10n.L("艺术风格"),
                    TJGeneratorsL10n.L("选择风格"),
                    CommonStyles.DropBoxRightArrow4xTexture,
                    ShowStyleSelector,
                    selectedStyle != null ? TJGeneratorsL10n.L(selectedStyle.name) : null);
                GUILayout.Space(CommonStyles.Space2);
                UIComponents.DrawGapLine();
                GUILayout.Space(CommonStyles.Space2);
            }
        }

        /// <summary>
        /// 绘制材质预设选择器（Material模式）
        /// </summary>
        private void DrawMaterialPresetSelector()
        {
            if (_currentGenerator == null)
            {
                return;
            }

            var genConfig = GetActiveGeneratorConfig();
            if (genConfig == null || genConfig.materialPresetSelector == null || !genConfig.materialPresetSelector.enabled)
            {
                return;
            }

            GUILayout.Space(CommonStyles.Space2);
            UIComponents.DrawSelectorRow(
                TJGeneratorsL10n.L("材质预设："),
                selectedMaterialPreset != null ? TJGeneratorsL10n.L(selectedMaterialPreset.name) : null,
                TJGeneratorsL10n.L("选择预设"),
                () =>
                {
                    selectedMaterialPreset = null;
                    UpdateMaterialPrompt();
                    Repaint();
                },
                ShowMaterialPresetSelector
            );
        }

        /// <summary>
        /// 绘制纹理走势选择器（Material模式）
        /// </summary>
        private void DrawTexturePatternSelector()
        {
            if (_currentGenerator == null)
                return;

            var genConfig = GetActiveGeneratorConfig();
            if (genConfig == null || genConfig.texturePatternSelector == null || !genConfig.texturePatternSelector.enabled)
            {
                return;
            }

            var patterns = genConfig.texturePatternSelector.options;
            if (patterns == null || patterns.Count == 0)
            {
                return;
            }

            if (selectedTexturePattern != null && string.IsNullOrEmpty(materialReferenceImagePath))
                selectedTexturePattern = null;
            UIComponents.DrawSectionTitle(TJGeneratorsL10n.L("纹理走势"), uppercase: false);
            GUILayout.Space(CommonStyles.Space2);
            DrawMaterialReferenceImageGrid();
            GUILayout.Space(5);
        }

        private void DrawMaterialReferenceImageGrid()
        {
            UploadImageComponents.DrawLargeImageUpload(
                ref materialReferenceImagePath,
                ref materialReferenceImageThumb,
                ShowTexturePatternSelector,
                Repaint,
                () => { selectedTexturePattern = null; },
                TJGeneratorsL10n.L("选择模板"),
                onPickDone: (path, tex) =>
                {
                    materialReferenceImagePath = path;
                    materialReferenceImageThumb = tex;
                });
        }

        /// <summary>
        /// 绘制风格状态选择器（Material模式）
        /// </summary>
        private void DrawMaterialStyleSelector()
        {
            if (_currentGenerator == null)
            {
                return;
            }

            var genConfig = GetActiveGeneratorConfig();
            if (genConfig == null || genConfig.materialStyleSelector == null || !genConfig.materialStyleSelector.enabled)
            {
                return;
            }

            var styleOptions = genConfig.materialStyleSelector.options;
            if (styleOptions == null || styleOptions.Count == 0)
                return;

            UIComponents.DrawSelectorRow(
                TJGeneratorsL10n.L("风格状态："),
                selectedMaterialStyle != null ? TJGeneratorsL10n.L(selectedMaterialStyle.name) : null,
                TJGeneratorsL10n.L("选择风格"),
                () =>
                {
                    selectedMaterialStyle = null;
                    UpdateMaterialPrompt();
                    Repaint();
                },
                ShowMaterialStyleSelector
            );
            GUILayout.Space(5);
        }

        /// <summary>
        /// 更新材质生成的提示词
        /// </summary>
        private void UpdateMaterialPrompt()
        {
            // 组合预设和风格的提示词
            string combinedPrompt = "";
            
            if (selectedMaterialPreset != null && !string.IsNullOrEmpty(selectedMaterialPreset.prompt))
            {
                combinedPrompt += selectedMaterialPreset.prompt;
            }
            
            if (selectedMaterialStyle != null && !string.IsNullOrEmpty(selectedMaterialStyle.prompt))
            {
                if (!string.IsNullOrEmpty(combinedPrompt))
                    combinedPrompt += ", ";
                combinedPrompt += selectedMaterialStyle.prompt;
            }
            
            // 更新文本输入
            textPrompt = combinedPrompt;
        }

        /// <summary>
        /// 显示材质预设选择器窗口
        /// </summary>
        private void ShowMaterialPresetSelector()
        {
            if (_currentGenerator == null) return;
            var genConfig = GetActiveGeneratorConfig();
            if (genConfig?.materialPresetSelector?.options == null)
            {
                TJLog.LogError("[TJGeneratorsMaterial] 材质预设选择器配置为空");
                return;
            }

            TJGeneratorsMaterialTemplateSelectorWindow.ShowWindow(
                genConfig.materialPresetSelector.options,
                OnMaterialPresetSelected,
                TJGeneratorsL10n.L("选择材质预设")
            );
        }

        /// <summary>
        /// 材质预设选择回调
        /// </summary>
        private void OnMaterialPresetSelected(MaterialTemplateOptionConfig preset)
        {
            if (preset == null)
            {
                selectedMaterialPreset = null;
                TJLog.Log("[TJGeneratorsMaterial] 用户取消选择材质预设");
            }
            else
            {
                selectedMaterialPreset = preset;
                TJLog.Log($"[TJGeneratorsMaterial] 选择材质预设: {preset.name}");
            }

            UpdateMaterialPrompt();
            Repaint();
        }

        /// <summary>
        /// 显示纹理走势选择器窗口
        /// </summary>
        private void ShowTexturePatternSelector()
        {
            if (_currentGenerator == null) return;
            var genConfig = GetActiveGeneratorConfig();
            if (genConfig?.texturePatternSelector?.options == null)
            {
                TJLog.LogError("[TJGeneratorsMaterial] 纹理走势选择器配置为空");
                return;
            }

            TJGeneratorsTexturePatternSelectorWindow.ShowWindow(
                genConfig.texturePatternSelector.options,
                OnTexturePatternSelected,
                TJGeneratorsL10n.L("选择纹理走势"),
                selectedTexturePattern
            );
        }

        /// <summary>
        /// 纹理走势选择回调
        /// </summary>
        private void OnTexturePatternSelected(MaterialTemplateOptionConfig pattern)
        {
            if (pattern == null)
            {
                selectedTexturePattern = null;
                TJLog.Log("[TJGeneratorsMaterial] 用户取消选择纹理走势");
                ClearMaterialReferenceImage();
            }
            else
            {
                TJLog.Log($"[TJGeneratorsMaterial] 选择纹理走势: {pattern.name}");

                string absolutePath = TJGeneratorsMaterialTemplateGenerator.GetAbsoluteTemplatePath(pattern.id);

                if (File.Exists(absolutePath))
                {
                    selectedTexturePattern = pattern;
                    if (materialReferenceImageThumb != null)
                    {
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(materialReferenceImageThumb)))
                            DestroyImmediate(materialReferenceImageThumb);
                        materialReferenceImageThumb = null;
                    }

                    materialReferenceImagePath = absolutePath;
                    var tex2 = new Texture2D(2, 2);
                    if (tex2.LoadImage(File.ReadAllBytes(absolutePath)))
                        materialReferenceImageThumb = tex2;
                    else
                        DestroyImmediate(tex2);

                    TJLog.Log($"[TJGeneratorsMaterial] 已加载纹理图片: {pattern.id}");
                }
                else
                {
                    ErrorDialogUtils.ShowErrorDialog(
                        TJGeneratorsL10n.L("纹理图片不存在"),
                        TJGeneratorsL10n.L("纹理走势 '{0}' 的图片尚未生成。\r\n\r\n请通过菜单 'AI/开发/生成纹理走势模板图' 生成纹理图片。", pattern.name),
                        LogTag
                    );
                }
            }

            Repaint();
        }

        /// <summary>
        /// 显示风格状态选择器窗口
        /// </summary>
        private void ShowMaterialStyleSelector()
        {
            if (_currentGenerator == null) return;
            var genConfig = GetActiveGeneratorConfig();
            if (genConfig?.materialStyleSelector?.options == null)
            {
                TJLog.LogError("[TJGeneratorsMaterial] 风格状态选择器配置为空");
                return;
            }

            TJGeneratorsMaterialTemplateSelectorWindow.ShowWindow(
                genConfig.materialStyleSelector.options,
                OnMaterialStyleSelected,
                TJGeneratorsL10n.L("选择风格状态")
            );
        }

        /// <summary>
        /// 风格状态选择回调
        /// </summary>
        private void OnMaterialStyleSelected(MaterialTemplateOptionConfig style)
        {
            if (style == null)
            {
                selectedMaterialStyle = null;
                TJLog.Log("[TJGeneratorsMaterial] 用户取消选择风格");
            }
            else
            {
                selectedMaterialStyle = style;
                TJLog.Log($"[TJGeneratorsMaterial] 选择风格状态: {style.name}");
            }

            UpdateMaterialPrompt();
            Repaint();
        }

        private void ShowTypeSelector()
        {
            var genConfig = GetActiveGeneratorConfig();
            // 优先使用生成器自有配置（需有有效 options），回退到共享配置
            var typeSelector = (genConfig?.typeSelector?.options != null ? genConfig.typeSelector : null)
                               ?? ConfigManager.GetSpriteTypeSelector();
            if (typeSelector?.options == null)
            {
                TJLog.LogError($"{LogTag} 类型选择器配置为空");
                return;
            }

            // 打开类型选择器窗口
            TJGeneratorsModelSelectorWindow.ShowTypeSelector(
                typeSelector.options,
                OnTypeSelected,
                selectedType
            );
        }

        private void ShowStyleSelector()
        {
            var genConfig = GetActiveGeneratorConfig();
            // 优先使用生成器自有配置（需有有效 options），回退到共享配置
            var styleSelector = (genConfig?.styleSelector?.options != null ? genConfig.styleSelector : null)
                                ?? ConfigManager.GetSpriteStyleSelector();
            if (styleSelector?.options == null)
            {
                TJLog.LogError($"{LogTag} 风格选择器配置为空");
                return;
            }

            // 打开风格选择器窗口
            TJGeneratorsModelSelectorWindow.ShowStyleSelector(
                styleSelector.options,
                OnStyleSelected,
                selectedStyle
            );
        }

        private void OnTypeSelected(SelectorOptionConfig type)
        {
            if (type?.id == "none")
            {
                selectedType = null;
                TJLog.Log($"{LogTag} 用户选择不使用特定类型");
            }
            else
            {
                selectedType = type;
                TJLog.Log($"{LogTag} 选择类型: {type?.name}");
            }

            // 更新生成器的类型选择
            if (_currentGenerator is Generators.DynamicGenerator dynamicGen)
            {
                dynamicGen.SetTypeSelection(selectedType);
            }

            Repaint();
        }

        private void OnStyleSelected(SelectorOptionConfig style)
        {
            if (style?.id == "none")
            {
                selectedStyle = null;
                TJLog.Log($"{LogTag} 用户选择不使用特定风格");
            }
            else
            {
                selectedStyle = style;
                TJLog.Log($"{LogTag} 选择风格: {style?.name}");
            }

            // 更新生成器的风格选择
            if (_currentGenerator is Generators.DynamicGenerator dynamicGen)
            {
                dynamicGen.SetStyleSelection(selectedStyle);
            }

            Repaint();
        }

        protected override void ResetInputStateAfterModelChange()
        {
            var config = GetActiveGeneratorConfig();
            ResetTextPromptIfHidden(config, ref textPrompt);
            ClearReferenceImagesWhenUploadHidden(config, referenceImagePaths, referenceUploadedImages);
        }

        private void OnModelSelected(AIModelInfo model)
        {
            OnModelSelectedBase(model);
            UploadImageComponents.TrimReferenceImagesToMax(
                referenceImagePaths,
                referenceUploadedImages,
                GetMaxReferenceImages());
        }

        private void DrawInputSection()
        {
            var genConfig = GetActiveGeneratorConfig();
            textPrompt = DrawConfiguredTextPromptInput(textPrompt, "sprite_prompt_input", genConfig);
            if (ShouldShowTextInput(genConfig))
                GUILayout.Space(CommonStyles.Space3);
        }

        private void DrawImageUploadArea()
        {
            DrawConfiguredReferenceImageUpload(
                referenceImagePaths,
                referenceUploadedImages,
                "sprite_reference_upload");
        }

        private void DrawConfigurationSection()
        {
            var provider = _currentGenerator as IGeneratorParameterProvider;
            showAdvancedSettings = DrawConfiguredAdvancedSettingsFoldout(
                showAdvancedSettings,
                provider,
                GetCurrentGeneratorParameters());

            if (provider is DynamicGenerator dyn)
            {
                bool hasRef = _currentMode == WindowMode.Material
                    ? !string.IsNullOrEmpty(materialReferenceImagePath)
                    : referenceImagePaths != null && referenceImagePaths.Count > 0;
                dyn.SyncReferenceImagesForCostPreview(hasRef);
            }
            SyncGenerationCostWithCurrentGeneratorState();
        }

        private void DrawGenerationSection(LeftPanelBottomDock.Layout layout)
        {
            bool canGenerate = _currentMode == WindowMode.Material
                ? HasMaterialGenerationInput()
                : !string.IsNullOrWhiteSpace(textPrompt);
            UIComponents.DrawGenerationSectionAt(
                layout,
                isGenerating,
                generationProgress,
                generationStatus,
                canGenerate,
                StartGeneration,
                null,
                Repaint,
                currentGenerationCost);
        }

        private void DrawSpritePreview()
        {
            GUILayout.BeginHorizontal();
            string previewLabel = _currentMode == WindowMode.Material ? TJGeneratorsL10n.L("材质预览") : TJGeneratorsL10n.L("精灵预览");
            GUILayout.Label(previewLabel, CommonStyles.HeaderStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            Texture2D previewToShow = null;
            if (selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count)
            {
                var item = generationHistory[selectedHistoryIndex];
                if (!item.isGenerating) previewToShow = GetPreviewTextureForHistoryItem(item);
            }
            if (previewToShow == null) previewToShow = spritePreviewTexture;
            if (previewToShow != null)
            {
                GUILayout.BeginHorizontal();
                float availableWidth = Mathf.Max(300, Mathf.Min(position.width - 60, _effectiveLeftPanelWidth - 60));
                float previewHeight = Mathf.Min(availableWidth * 0.5f, 200f);
                Rect previewRect = GUILayoutUtility.GetRect(availableWidth, previewHeight);
                EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f, 1f));
                GUI.DrawTexture(previewRect, previewToShow, ScaleMode.ScaleToFit);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                Rect emptyRect = GUILayoutUtility.GetRect(0, 80, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(emptyRect, CommonStyles.EmptyAreaBackgroundColor);
                GUI.Label(emptyRect, TJGeneratorsL10n.L("尚未生成\r\n生成后预览将在此处显示"), CommonStyles.CenteredGreyLabelStyle);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawHistoryPanel(float panelWidth)
        {
            DrawStandardHistoryPanel(panelWidth, new StandardHistoryPanelOptions
            {
                AddPanelTopSpacing = true,
                GetLargePreviewTexture = GetPreviewTextureForHistoryItem,
                DrawTilePreview = DrawSpriteHistoryPreview,
                GetModelLabel = item => GetSpriteModelDisplayLabel(item.modelVersion),
                ShowContextMenu = ShowHistoryContextMenu,
                DrawHistoryActions = DrawHistoryActions,
            });
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
                    // 使用基类的异步加载队列，避免在OnGUI中直接读取文件
                    EnqueuePreviewLoad(item.modelPath, absPath, false);
                }
            }
            if (
                !string.IsNullOrEmpty(item.previewImageUrl)
                && urlPreviewCache.TryGetValue(item.previewImageUrl, out var urlTex)
                && urlTex != null
            )
                return urlTex;
            if (
                !item.isTextToModel
                && !string.IsNullOrEmpty(item.imagePath)
                && historyPreviewCache.TryGetValue(item.imagePath, out var up)
                && up != null
            )
                return up;
            return null;
        }

        private void DrawSpriteHistoryPreview(Rect rect, TJGeneratorsGenerationHistoryItem item)
        {
            if (item.isGenerating)
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
                    // 使用基类的异步加载队列，避免在OnGUI中直接读取文件
                    EnqueuePreviewLoad(item.modelPath, absPath, false);
                }
            }
            if (
                !item.isTextToModel
                && !string.IsNullOrEmpty(item.imagePath)
                && historyPreviewCache.TryGetValue(item.imagePath, out var up)
                && up != null
            )
            {
                GUI.DrawTexture(rect, up, ScaleMode.ScaleToFit);
                return;
            }
            if (
                item.isTextToModel
                && !string.IsNullOrEmpty(item.previewImageUrl)
                && urlPreviewCache.TryGetValue(item.previewImageUrl, out var urlTex)
                && urlTex != null
            )
            {
                GUI.DrawTexture(rect, urlTex, ScaleMode.ScaleToFit);
                return;
            }
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
            GUI.Label(
                new Rect(
                    rect.x + rect.width / 4,
                    rect.y + rect.height / 4,
                    rect.width / 2,
                    rect.height / 2
                ),
                EditorGUIUtility.IconContent("d_Texture2D Icon")
            );
        }

        private string GetSpriteModelDisplayLabel(string modelVersion)
        {
            return GetModelDisplayLabelFromIndex(modelVersion);
        }

        private void DrawHistoryActions()
        {
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            bool hasSelection = selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count;
            bool isGenerating = hasSelection && generationHistory[selectedHistoryIndex].isGenerating;
            GUI.enabled = hasSelection && !isGenerating;
            string applyLabel = _currentMode == WindowMode.Material ? TJGeneratorsL10n.L("应用到当前材质") : TJGeneratorsL10n.L("应用到当前精灵");
            if (GUILayout.Button(applyLabel, GUILayout.Height(25)))
                ApplyHistoryToAsset(selectedHistoryIndex);
            if (GUILayout.Button(TJGeneratorsL10n.L("在项目中显示"), GUILayout.Height(25)))
                ShowHistoryInProject(selectedHistoryIndex);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            currentHistoryTileSize = GUILayout.HorizontalSlider(currentHistoryTileSize, MinHistoryTileSize, MaxHistoryTileSize, GUILayout.Width(60f));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void ShowHistoryContextMenu(int index)
        {
            var item = generationHistory[index];
            var menu = new GenericMenu();
            string applyLabel = _currentMode == WindowMode.Material ? TJGeneratorsL10n.L("应用到当前材质") : TJGeneratorsL10n.L("应用到当前精灵");
            menu.AddItem(new GUIContent(applyLabel), false, () => ApplyHistoryToAsset(index));
            menu.AddItem(new GUIContent(TJGeneratorsL10n.L("在项目中显示")), false, () => ShowHistoryInProject(index));
            menu.AddSeparator("");
            if (!string.IsNullOrEmpty(item.modelPath))
                menu.AddItem(new GUIContent(TJGeneratorsL10n.L("在资源管理器中显示")), false, () => EditorUtility.RevealInFinder(item.modelPath));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(TJGeneratorsL10n.L("从历史记录中移除")), false, () =>
            {
                TJGeneratorsHistoryManager.RemoveFromHistory(item.modelPath);
                generationHistory = LoadGenerationHistory();
                if (selectedHistoryIndex >= generationHistory.Count) selectedHistoryIndex = Mathf.Max(0, generationHistory.Count - 1);
                Repaint();
            });
            menu.ShowAsContext();
        }

        private void ApplyHistoryToAsset(int index)
        {
            if (index < 0 || index >= generationHistory.Count) return;
            var item = generationHistory[index];
            if (string.IsNullOrEmpty(item.modelPath) || !File.Exists(PathUtils.ToAbsoluteAssetPath(item.modelPath)))
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("该历史记录的纹理文件不存在。"), LogTag);
                return;
            }

            if (_currentMode == WindowMode.Material)
            {
                // Material 模式：将历史纹理应用到绑定的 Material
                if (targetSpriteAsset != null && targetSpriteAsset.IsValid())
                {
                    string materialPath = targetSpriteAsset.GetPath();
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material != null)
                    {
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(item.modelPath);
                        if (texture != null)
                        {
                            material.mainTexture = texture;
                            EditorUtility.SetDirty(material);
                            AssetDatabase.SaveAssets();
                            Selection.activeObject = material;
                            EditorGUIUtility.PingObject(material);
                            TJLog.Log($"[TJGeneratorsMaterial] 已将历史纹理应用到 {materialPath}");
                        }
                    }
                    else
                    {
                        TJLog.LogWarning($"[TJGeneratorsMaterial] 绑定的资产不是 Material: {materialPath}");
                    }
                }
                else
                {
                    // 未绑定 Material，创建新的
                    CreateMaterialAsset(item.modelPath, item.modelPath);
                }
            }
            else
            {
                // Sprite 模式
                if (targetSpriteAsset == null || !targetSpriteAsset.IsValid())
                {
                    Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("请先绑定或创建目标精灵资产。")}");
                    return;
                }
                string targetPath = targetSpriteAsset.GetPath();
                if (!targetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) targetPath = Path.ChangeExtension(targetPath, ".png");
                if (!EditorUtility.DisplayDialog(TJGeneratorsL10n.L("确认替换"), string.Format(TJGeneratorsL10n.L("确定要将选中的历史应用到 {0} 吗？"), Path.GetFileName(targetPath)), TJGeneratorsL10n.L("确定"), TJGeneratorsL10n.L("取消"))) return;
                try
                {
                    File.Copy(PathUtils.ToAbsoluteAssetPath(item.modelPath), PathUtils.ToAbsoluteAssetPath(targetPath), true);
                    AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                    var importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
                    if (importer != null) { importer.textureType = TextureImporterType.Sprite; importer.SaveAndReimport(); }
                    if (spritePreviewTexture != null)
                    {
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(spritePreviewTexture)))
                            DestroyImmediate(spritePreviewTexture);
                        spritePreviewTexture = null;
                    }

                    // 从AssetDatabase加载纹理，避免直接读取文件
                    var loadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                    if (loadedTexture != null)
                    {
                        spritePreviewTexture = loadedTexture;
                    }
                    else
                    {
                        // 如果AssetDatabase加载失败，使用异步队列加载
                        string absolutePath = PathUtils.ToAbsoluteAssetPath(targetPath);
                        if (File.Exists(absolutePath))
                        {
                            EnqueuePreviewLoad(targetPath, absolutePath, false);
                        }
                    }
                    TJLog.Log($"[TJGeneratorsSprite] 已将历史应用到 {targetPath}");
                }
                catch (Exception e) { ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("应用失败: ") + e.Message, LogTag); }
            }
            Repaint();
        }

        private void ShowHistoryInProject(int index)
        {
            if (index < 0 || index >= generationHistory.Count) return;

            var item = generationHistory[index];
            string assetPath = !string.IsNullOrEmpty(item.modelPath) ? item.modelPath : item.imagePath;
            var asset = !string.IsNullOrEmpty(assetPath)
                ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)
                : null;
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        private void EnsureTargetMaterial()
        {
            if (targetSpriteAsset != null && targetSpriteAsset.IsValid())
            {
                var existing = AssetDatabase.LoadAssetAtPath<Material>(targetSpriteAsset.GetPath());
                if (existing != null)
                    return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");

            string materialPath = AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/New Material.mat");
            Shader shader = TJMaterialShaderUtility.ResolveSurfaceLitShader()
                            ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();

            targetSpriteAsset = TJGeneratorsAssetReference.FromPath(materialPath);
            titleContent = new GUIContent(string.Format(TJGeneratorsL10n.L("TJGenerators 材质 - {0}"), Path.GetFileNameWithoutExtension(materialPath)));
            if (!string.IsNullOrEmpty(targetSpriteAsset.guid))
                materialOpenWindows[targetSpriteAsset.guid] = this;
            TJGeneratorsGenerationLabel.EnableLabel(targetSpriteAsset);
            Repaint();
        }

        private void EnsureTargetSprite()
        {
            if (targetSpriteAsset != null && targetSpriteAsset.IsValid()) return;
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");
            string spritePath = AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/New Sprite.png");
            spritePath = CreateBlankSprite(spritePath);
            if (string.IsNullOrEmpty(spritePath)) { TJLog.LogError("[TJGeneratorsSprite] 无法创建精灵"); return; }
            targetSpriteAsset = TJGeneratorsAssetReference.FromPath(spritePath);
            titleContent = new GUIContent(string.Format(TJGeneratorsL10n.L("TJGenerators 精灵 - {0}"), Path.GetFileNameWithoutExtension(spritePath)));
            if (!string.IsNullOrEmpty(targetSpriteAsset.guid)) spriteOpenWindows[targetSpriteAsset.guid] = this;
            Repaint();
        }


        /// <summary>
        /// 在指定路径创建空白 PNG 并导入为 Sprite。
        /// </summary>
        public static string CreateBlankSprite(string path)
        {
            path = Path.ChangeExtension(path, ".png");
            string absolutePath = PathUtils.ToAbsoluteAssetPath(path);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            int size = 4;
            var blank = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0, 0, 0, 0);
            blank.SetPixels(pixels);
            blank.Apply();
            File.WriteAllBytes(absolutePath, blank.EncodeToPNG());
            DestroyImmediate(blank);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null) { importer.textureType = TextureImporterType.Sprite; importer.SaveAndReimport(); }
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(path));
            return path;
        }

        private bool HasMaterialGenerationInput()
        {
            return !string.IsNullOrWhiteSpace(textPrompt)
                || !string.IsNullOrEmpty(materialReferenceImagePath);
        }

        private void StartGeneration()
        {
            // Material 模式验证
            if (_currentMode == WindowMode.Material)
            {
                if (_currentGenerator == null || !HasMaterialGenerationInput())
                {
                    ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), MaterialStartBlockedMessage, LogTag);
                    return;
                }
            }
            else
            {
                // Sprite 模式验证
                if (_currentGenerator == null || string.IsNullOrEmpty(textPrompt))
                {
                    ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("请先选择模型并输入文本提示词"), LogTag);
                    return;
                }
            }

            if (_currentMode == WindowMode.Material)
            {
                EnsureTargetMaterial();
            }
            else if (_currentMode == WindowMode.Sprite)
            {
                EnsureTargetSprite();
            }
            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("准备中...");
            generationProgress = 0f;
            if (_currentGenerator is DynamicGenerator dynamicGen)
            {
                dynamicGen.SetTextPrompt(textPrompt);
                if (_currentMode == WindowMode.Material)
                {
                    dynamicGen.SetImagePaths(
                        string.IsNullOrEmpty(materialReferenceImagePath)
                            ? null
                            : new List<string> { materialReferenceImagePath });
                }
                else
                {
                    dynamicGen.SetImagePaths(
                        referenceImagePaths != null && referenceImagePaths.Count > 0
                            ? referenceImagePaths
                            : null);
                }
            }
            string assetGuid = targetSpriteAsset?.guid ?? "";
            EditorCoroutineUtility.StartCoroutineOwnerless(_pipeline.StartGeneration(_currentGenerator, assetGuid));
        }

        public TJGeneratorsAssetReference GetTargetAsset() => targetSpriteAsset;

        public void StartGeneration(ModelGeneratorBase generator)
        {
            if (generator == _currentGenerator)
                StartGeneration();
        }


        private string GetCurrentSpriteAssetGuid() => targetSpriteAsset?.guid ?? "";

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
        }


        public string GetAssetSavePath(PipelineMediaType _type, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return null;

            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                AssetDatabase.CreateFolder("Assets/TJGenerators", "History");
            string prefix = _currentMode == WindowMode.Material ? "Material_" : "Sprite_";
            string uniqueName = prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            return AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/History/" + uniqueName);
        }

        public void OnAssetSaved(PipelineMediaType _type, string savePath, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return;

            TJLog.Log($"{LogTag} OnAssetSaved: {savePath}");
            var importType = _currentMode == WindowMode.Material
                ? TextureImporterType.Default
                : TextureImporterType.Sprite;
            GeneratedTextureImportUtils.ConfigureImportedTexture(
                savePath, importType, alphaIsTransparency: true);
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));

            string pathToShow = savePath;
            if (_currentMode == WindowMode.Sprite)
            {
                EnsureTargetSprite();
                if (targetSpriteAsset != null && targetSpriteAsset.IsValid())
                {
                    string targetPath = targetSpriteAsset.GetPath();
                    if (!targetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) targetPath = Path.ChangeExtension(targetPath, ".png");
                    try
                    {
                        File.Copy(PathUtils.ToAbsoluteAssetPath(savePath), PathUtils.ToAbsoluteAssetPath(targetPath), true);
                        AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                        GeneratedTextureImportUtils.ConfigureImportedTexture(
                            targetPath, TextureImporterType.Sprite, alphaIsTransparency: true);
                        pathToShow = targetPath;
                    }
                    catch (Exception e) { TJLog.LogWarning($"[TJGeneratorsSprite] 复制到目标失败: {e.Message}"); }
                }
            }
            else if (_currentMode == WindowMode.Material)
            {
                // Material 模式：将生成的纹理应用到绑定的 Material 或创建新 Material
                ApplyTextureToMaterial(savePath);
            }

            if (spritePreviewTexture != null)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(spritePreviewTexture)))
                    DestroyImmediate(spritePreviewTexture);
                spritePreviewTexture = null;
            }

            // 从AssetDatabase加载纹理，避免直接读取文件
            var loadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(pathToShow);
            if (loadedTexture != null)
            {
                spritePreviewTexture = loadedTexture;
            }
            else
            {
                // 如果AssetDatabase加载失败，使用异步队列加载
                string absoluteShowPath = PathUtils.ToAbsoluteAssetPath(pathToShow);
                if (File.Exists(absoluteShowPath))
                {
                    EnqueuePreviewLoad(pathToShow, absoluteShowPath, false);
                }
            }

            if (_currentMode == WindowMode.Sprite)
            {
                var textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(pathToShow);
                if (textureAsset != null) { Selection.activeObject = textureAsset; EditorGUIUtility.PingObject(textureAsset); }
                TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(pathToShow));
            }

            generationStatus = TJGeneratorsL10n.L("完成");
            generationProgress = 1f;
            isGenerating = false;
        }

        /// <summary>
        /// 将生成的纹理应用到绑定的 Material，或创建新 Material
        /// </summary>
        private void ApplyTextureToMaterial(string texturePath)
        {
            try
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null)
                {
                    TJLog.LogWarning($"[TJGeneratorsMaterial] 无法加载纹理: {texturePath}");
                    return;
                }

                Material material = null;
                string materialPath = null;

                // 如果绑定了 Material 资产，直接更新它
                if (targetSpriteAsset != null && targetSpriteAsset.IsValid())
                {
                    materialPath = targetSpriteAsset.GetPath();
                    material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                }

                if (material != null)
                {
                    TJMaterialShaderUtility.EnsureCompatibleSurfaceShader(material);
                    TJMaterialShaderUtility.AssignBaseColorTexture(material, texture);
                    if (selectedMaterialPreset != null)
                    {
                        TJMaterialShaderUtility.ApplySurfaceMaterialPreset(material, selectedMaterialPreset.id);
                    }
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssets();
                    TJLog.Log($"[TJGeneratorsMaterial] 已更新 Material 纹理: {materialPath}");

                    Selection.activeObject = material;
                    EditorGUIUtility.PingObject(material);
                }
                else
                {
                    // 没有绑定 Material，创建新的
                    CreateMaterialAsset(texturePath, texturePath);
                }
            }
            catch (Exception e)
            {
                TJLog.LogError($"[TJGeneratorsMaterial] 应用纹理到材质失败: {e.Message}");
            }
        }

        /// <summary>
        /// 创建 Material 资产（Material 模式专用，仅在未绑定 Material 时调用）
        /// </summary>
        private void CreateMaterialAsset(string texturePath, string textureToShowPath)
        {
            try
            {
                // 创建 Material 资产路径
                string materialPath = Path.ChangeExtension(texturePath, ".mat");
                materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);

                Shader shader = TJMaterialShaderUtility.ResolveSurfaceLitShader();
                if (shader == null)
                {
                    TJLog.LogError("[TJGeneratorsMaterial] 无法解析表面材质 Lit Shader，请检查渲染管线与 Shader 是否可用。");
                    return;
                }

                // 创建 Material
                Material material = new Material(shader);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(textureToShowPath);
                TJMaterialShaderUtility.AssignBaseColorTexture(material, tex);

                // 设置材质属性
                if (selectedMaterialPreset != null)
                {
                    TJMaterialShaderUtility.ApplySurfaceMaterialPreset(material, selectedMaterialPreset.id);
                }

                // 保存 Material
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();

                TJLog.Log($"[TJGeneratorsMaterial] Material 创建成功: {materialPath}");

                // 绑定窗口到新创建的 Material
                targetSpriteAsset = TJGeneratorsAssetReference.FromPath(materialPath);
                if (!string.IsNullOrEmpty(targetSpriteAsset.guid))
                    materialOpenWindows[targetSpriteAsset.guid] = this;
                titleContent = new GUIContent(string.Format(TJGeneratorsL10n.L("TJGenerators 材质 - {0}"), Path.GetFileNameWithoutExtension(materialPath)));

                // 选中新创建的 Material
                var materialAsset = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (materialAsset != null)
                {
                    Selection.activeObject = materialAsset;
                    EditorGUIUtility.PingObject(materialAsset);
                }
            }
            catch (Exception e)
            {
                TJLog.LogError($"[TJGeneratorsMaterial] 创建 Material 失败: {e.Message}");
            }
        }

    }
}
#endif
