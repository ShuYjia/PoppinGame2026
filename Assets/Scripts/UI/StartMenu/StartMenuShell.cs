using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// StartMenu 主控制器（新实现，替代旧的 StartMenuController）。
/// 只负责菜单页面、槽位选择与设置入口；真实周目存档由 IMenuSaveProvider 提供。
/// </summary>
public sealed class StartMenuShell : MonoBehaviour
{
    private enum MenuPage
    {
        Main,
        SaveSlots,
        Settings,
        Confirm,
        Notice
    }

    private static readonly Color DeepPurple = new Color32(0x25, 0x1B, 0x36, 0xFF);
    private static readonly Color Pink = new Color32(0xFF, 0x9F, 0xC6, 0xFF);
    private static readonly Color Coral = new Color32(0xF1, 0x6E, 0x91, 0xFF);
    private static readonly Color StarYellow = new Color32(0xFF, 0xD7, 0x5A, 0xFF);
    private static readonly Color LightCyan = new Color32(0xBF, 0xE9, 0xE6, 0xFF);
    private static readonly Color MutedLabel = new Color32(0x8C, 0x84, 0x9E, 0xFF);

    [Header("场景引用")]
    public Canvas canvas;
    public Button newGameButton;
    public Button continueButton;
    public Button settingButton;
    public Button exitButton;
    public TMP_Text titleLabel;
    public TMP_Text subtitleLabel;
    public TMP_FontAsset menuFont;
    public Sprite starSprite;
    public PlaceholderSaveProvider saveProvider;
    public MenuSettingsController settingsController;

    [Tooltip("仅在 Unity Editor 中预览占用槽位的 Continue 页面，正式运行逻辑不使用。")]
    [SerializeField] private bool previewContinueInEditor = false;

    private MenuPage page = MenuPage.Main;
    private bool newGameMode = true;
    private int pendingSlot = -1;

    private RectTransform runtimeRoot;
    private GameObject saveSlotsPage;
    private GameObject settingsPage;
    private GameObject confirmPage;
    private GameObject noticePage;

    private TMP_Text slotPageTitle;
    private TMP_Text noticeMessage;
    private TMP_Text confirmMessage;
    private Button noticeStartButton;
    private Button noticeBackButton;
    private Button confirmOverwriteButton;
    private Button confirmDeleteButton;
    private Button confirmCancelButton;

    private Slider volumeSlider;
    private TMP_Dropdown resolutionDropdown;
    private Toggle fullscreenToggle;

    private readonly List<MenuVisual> mainVisuals = new List<MenuVisual>();
    private readonly List<MenuVisual> slotVisuals = new List<MenuVisual>();
    private readonly List<Button> slotButtons = new List<Button>();

    private GameObject lastSelected;
    private bool settingsConfigured;
    private GameObject titleDecor;

    private sealed class MenuVisual
    {
        public Button button;
        public TMP_Text label;
        public Image marker;
    }

    private void Start()
    {
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("[StartMenuShell] 未找到 Canvas，无法构建菜单。", this);
            return;
        }

        canvas.transform.localScale = Vector3.one;
        titleDecor = GameObject.Find("TitleDecor");
        EnsureRuntimeRoot();
        BuildMainMenuStyle();
        BuildRuntimePages();
        ShowMain();
    }

    private void Update()
    {
        if (HandleBackInput())
            return;

        RefreshSelectionVisual();
    }

    private bool HandleBackInput()
    {
        bool pressed = false;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            pressed = true;

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
            pressed = true;

        if (!pressed)
            return false;

        Back();
        return true;
    }

    private void EnsureRuntimeRoot()
    {
        runtimeRoot = transform as RectTransform;
        if (runtimeRoot == null)
        {
            GameObject rootObject = new GameObject("RuntimePages", typeof(RectTransform));
            rootObject.transform.SetParent(canvas.transform, false);
            runtimeRoot = rootObject.GetComponent<RectTransform>();
        }

        runtimeRoot.SetParent(canvas.transform, false);
        runtimeRoot.anchorMin = Vector2.zero;
        runtimeRoot.anchorMax = Vector2.one;
        runtimeRoot.offsetMin = Vector2.zero;
        runtimeRoot.offsetMax = Vector2.zero;
        runtimeRoot.SetAsLastSibling();
    }

    private TMP_FontAsset FontAsset
    {
        get { return menuFont != null ? menuFont : TMP_Settings.defaultFontAsset; }
    }

    private void BuildMainMenuStyle()
    {
        mainVisuals.Clear();
        PrepareMainButton(newGameButton, "新游戏", new Vector2(28f, -92f));
        PrepareMainButton(continueButton, "继续游戏", new Vector2(28f, -124f));
        PrepareMainButton(settingButton, "设置", new Vector2(28f, -156f));
        PrepareMainButton(exitButton, "退出游戏", new Vector2(28f, -188f));

        if (newGameButton != null)
            newGameButton.onClick.AddListener(OnNewGameClicked);
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueClicked);
        if (settingButton != null)
            settingButton.onClick.AddListener(OnSettingsClicked);
        if (exitButton != null)
            exitButton.onClick.AddListener(ExitGame);
    }

    private void PrepareMainButton(Button button, string labelText, Vector2 anchoredPosition)
    {
        if (button == null)
            return;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(140f, 26f);

        StyleButton(button);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.name = "Label";
            ApplyLabelStyle(label, labelText, 14, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(14f, 0f, 6f, 0f);
        }

        Image marker = AddSelectionMarker(button.transform);
        MenuVisual visual = new MenuVisual();
        visual.button = button;
        visual.label = label;
        visual.marker = marker;
        mainVisuals.Add(visual);
    }

    private void StyleButton(Button button)
    {
        Image image = button.targetGraphic as Image;
        if (image == null)
            image = button.GetComponent<Image>();

        if (image != null)
        {
            image.color = Color.white;
            button.targetGraphic = image;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.92f);
        colors.highlightedColor = new Color(0.24f, 0.18f, 0.33f, 0.96f);
        colors.pressedColor = Coral;
        colors.selectedColor = new Color(0.24f, 0.18f, 0.33f, 0.96f);
        colors.disabledColor = new Color(0.35f, 0.33f, 0.4f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Navigation navigation = new Navigation();
        navigation.mode = Navigation.Mode.Automatic;
        button.navigation = navigation;
    }

    private Image AddSelectionMarker(Transform parent)
    {
        GameObject markerObject = new GameObject("SelectionMarker", typeof(RectTransform), typeof(Image));
        markerObject.transform.SetParent(parent, false);

        RectTransform rect = markerObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-6f, 0f);
        rect.sizeDelta = new Vector2(10f, 10f);

        Image image = markerObject.GetComponent<Image>();
        image.sprite = starSprite;
        image.color = StarYellow;
        image.raycastTarget = false;
        markerObject.SetActive(false);
        return image;
    }

    private void ApplyLabelStyle(TMP_Text label, string content, int fontSize, TextAlignmentOptions alignment)
    {
        label.font = FontAsset;
        label.fontSize = fontSize;
        label.color = LightCyan;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.text = content;
    }

    private void BuildRuntimePages()
    {
        saveSlotsPage = CreatePage("SaveSlotsPage");
        BuildSaveSlotsPage(saveSlotsPage);

        settingsPage = CreatePage("SettingsPage");
        BuildSettingsPage(settingsPage);

        confirmPage = CreatePage("ConfirmPage");
        BuildConfirmPage(confirmPage);

        noticePage = CreatePage("NoticePage");
        BuildNoticePage(noticePage);
    }

    private GameObject CreatePage(string name)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
        root.transform.SetParent(runtimeRoot, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = root.GetComponent<Image>();
        image.color = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.82f);
        image.raycastTarget = true;

        root.SetActive(false);
        return root;
    }

    private TMP_Text CreateText(Transform parent, string name, string content, int fontSize, Color color,
        Vector2 anchoredPosition, Vector2 sizeDelta, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = FontAsset;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    private Button CreateButton(Transform parent, string name, string labelText, Vector2 anchoredPosition,
        Vector2 sizeDelta, int fontSize)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image image = buttonObject.GetComponent<Image>();
        image.color = Color.white;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        StyleButton(button);

        TMP_Text label = CreateButtonLabel(buttonObject.transform, labelText, fontSize);
        Image marker = AddSelectionMarker(buttonObject.transform);

        MenuVisual visual = new MenuVisual();
        visual.button = button;
        visual.label = label;
        visual.marker = marker;
        slotVisuals.Add(visual);

        return button;
    }

    private TMP_Text CreateButtonLabel(Transform parent, string content, int fontSize)
    {
        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 2f);
        rect.offsetMax = new Vector2(-6f, -2f);

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = FontAsset;
        text.fontSize = fontSize;
        text.color = LightCyan;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    private void BuildSaveSlotsPage(GameObject pageObject)
    {
        slotPageTitle = CreateText(pageObject.transform, "SlotPageTitle", "选择新游戏存档位置", 16, Pink,
            new Vector2(0f, -14f), new Vector2(480f, 24f), TextAlignmentOptions.Center);

        slotButtons.Clear();
        slotVisuals.Clear();

        Vector2 size = new Vector2(190f, 46f);
        for (int i = 0; i < 6; i++)
        {
            int column = i % 2;
            int row = i / 2;
            Vector2 position = new Vector2(30f + column * 230f, -76f - row * 54f);
            int slotIndex = i;
            Button button = CreateButton(pageObject.transform, "SlotButton" + (i + 1), "存档 " + (i + 1) + "    空槽", position, size, 11);
            button.onClick.AddListener(delegate { OnSlotClicked(slotIndex); });
            slotButtons.Add(button);
        }

        Button backButton = CreateButton(pageObject.transform, "SlotsBackButton", "返回",
            new Vector2(185f, -236f), new Vector2(110f, 22f), 12);
        backButton.onClick.AddListener(ShowMain);
    }

    private void BuildNoticePage(GameObject pageObject)
    {
        noticeMessage = CreateText(pageObject.transform, "NoticeMessage", "存档功能待接入，当前进入试玩模式。", 13, LightCyan,
            new Vector2(40f, -96f), new Vector2(400f, 56f), TextAlignmentOptions.Center);
        noticeMessage.enableWordWrapping = true;

        noticeStartButton = CreateButton(pageObject.transform, "TrialStartButton", "开始试玩",
            new Vector2(63f, -214f), new Vector2(110f, 26f), 12);
        noticeStartButton.onClick.AddListener(StartTrial);

        noticeBackButton = CreateButton(pageObject.transform, "TrialBackButton", "返回",
            new Vector2(307f, -214f), new Vector2(110f, 26f), 12);
        noticeBackButton.onClick.AddListener(ShowSaveSlots);
    }

    private void BuildConfirmPage(GameObject pageObject)
    {
        confirmMessage = CreateText(pageObject.transform, "ConfirmMessage", "该槽位已有存档，请选择操作。", 13, LightCyan,
            new Vector2(40f, -96f), new Vector2(400f, 56f), TextAlignmentOptions.Center);
        confirmMessage.enableWordWrapping = true;

        confirmOverwriteButton = CreateButton(pageObject.transform, "OverwriteButton", "覆盖",
            new Vector2(48f, -214f), new Vector2(110f, 26f), 12);
        confirmOverwriteButton.onClick.AddListener(ConfirmOverwrite);

        confirmDeleteButton = CreateButton(pageObject.transform, "DeleteButton", "删除",
            new Vector2(185f, -214f), new Vector2(110f, 26f), 12);
        confirmDeleteButton.onClick.AddListener(ConfirmDelete);

        confirmCancelButton = CreateButton(pageObject.transform, "ConfirmCancelButton", "取消",
            new Vector2(322f, -214f), new Vector2(110f, 26f), 12);
        confirmCancelButton.onClick.AddListener(ShowSaveSlots);
    }

    private void BuildSettingsPage(GameObject pageObject)
    {
        CreateText(pageObject.transform, "SettingsTitle", "设置", 16, Pink,
            new Vector2(0f, -14f), new Vector2(480f, 24f), TextAlignmentOptions.Center);

        CreateText(pageObject.transform, "VolumeLabel", "音量", 12, LightCyan,
            new Vector2(30f, -66f), new Vector2(110f, 20f), TextAlignmentOptions.MidlineLeft);
        CreateText(pageObject.transform, "ResolutionLabel", "分辨率", 12, LightCyan,
            new Vector2(30f, -100f), new Vector2(110f, 20f), TextAlignmentOptions.MidlineLeft);
        CreateText(pageObject.transform, "FullscreenLabel", "全屏", 12, LightCyan,
            new Vector2(30f, -134f), new Vector2(110f, 20f), TextAlignmentOptions.MidlineLeft);

        volumeSlider = CreateVolumeSlider(pageObject.transform);
        resolutionDropdown = CreateResolutionDropdown(pageObject.transform);
        fullscreenToggle = CreateFullscreenToggle(pageObject.transform);

        Button applyButton = CreateButton(pageObject.transform, "ApplyButton", "应用",
            new Vector2(38f, -186f), new Vector2(110f, 26f), 12);
        applyButton.onClick.AddListener(OnApplySettings);

        Button resetButton = CreateButton(pageObject.transform, "ResetButton", "恢复默认",
            new Vector2(172f, -186f), new Vector2(110f, 26f), 12);
        resetButton.onClick.AddListener(OnRestoreDefaults);

        Button backButton = CreateButton(pageObject.transform, "SettingsBackButton", "返回",
            new Vector2(306f, -186f), new Vector2(110f, 26f), 12);
        backButton.onClick.AddListener(ShowMain);

        if (settingsController != null)
        {
            settingsController.Configure(volumeSlider, resolutionDropdown, fullscreenToggle);
            settingsConfigured = true;
        }
        else
        {
            Debug.LogError("[StartMenuShell] settingsController 未赋值，设置页无法工作。", this);
        }
    }

    private Slider CreateVolumeSlider(Transform parent)
    {
        GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderObject.name = "VolumeSlider";
        sliderObject.transform.SetParent(parent, false);

        RectTransform rect = sliderObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(150f, -61f);
        rect.sizeDelta = new Vector2(280f, 18f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.direction = Slider.Direction.LeftToRight;

        Image[] images = sliderObject.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].name == "Fill")
            {
                images[i].color = Pink;
            }
            else if (images[i].name == "Handle")
            {
                images[i].color = StarYellow;
                images[i].sprite = starSprite;
                images[i].preserveAspect = true;
            }
            else if (images[i].name == "Background")
            {
                images[i].color = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.85f);
            }
        }

        return slider;
    }

    private TMP_Dropdown CreateResolutionDropdown(Transform parent)
    {
        GameObject dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownObject.name = "ResolutionDropdown";
        dropdownObject.transform.SetParent(parent, false);

        RectTransform rect = dropdownObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(150f, -94f);
        rect.sizeDelta = new Vector2(280f, 28f);

        TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        TMP_Text[] texts = dropdownObject.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i].font = FontAsset;
            texts[i].fontSize = 11;
            texts[i].color = LightCyan;
            texts[i].raycastTarget = false;
        }

        Image rootImage = dropdownObject.GetComponent<Image>();
        if (rootImage != null)
            rootImage.color = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.95f);

        Image[] dropdownImages = dropdownObject.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < dropdownImages.Length; i++)
        {
            string imageName = dropdownImages[i].name;
            if (imageName == "Arrow")
            {
                dropdownImages[i].sprite = starSprite;
                dropdownImages[i].color = StarYellow;
                dropdownImages[i].preserveAspect = true;
            }
            else if (imageName == "Item Checkmark")
            {
                dropdownImages[i].color = Pink;
            }
            else if (imageName == "Template" || imageName == "Item Background" || imageName == "Viewport")
            {
                dropdownImages[i].color = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.95f);
            }
            else if (imageName == "Scrollbar")
            {
                dropdownImages[i].color = new Color(0.18f, 0.13f, 0.26f, 0.95f);
            }
            else if (imageName == "Handle")
            {
                dropdownImages[i].color = Pink;
            }
        }

        if (dropdown.captionText != null)
            dropdown.captionText.fontSize = 11;
        if (dropdown.itemText != null)
            dropdown.itemText.fontSize = 11;

        return dropdown;
    }

    private Toggle CreateFullscreenToggle(Transform parent)
    {
        GameObject toggleObject = DefaultControls.CreateToggle(new DefaultControls.Resources());
        toggleObject.name = "FullscreenToggle";
        toggleObject.transform.SetParent(parent, false);

        RectTransform rect = toggleObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(150f, -133f);
        rect.sizeDelta = new Vector2(24f, 24f);

        Transform legacyLabel = toggleObject.transform.Find("Label");
        if (legacyLabel != null)
        {
            legacyLabel.gameObject.SetActive(false);
            Destroy(legacyLabel.gameObject);
        }

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        Image background = toggleObject.GetComponent<Image>();
        if (background != null)
            background.color = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.95f);

        Transform toggleBackground = toggleObject.transform.Find("Background");
        if (toggleBackground != null)
        {
            Image backgroundImage = toggleBackground.GetComponent<Image>();
            if (backgroundImage != null)
                backgroundImage.color = new Color(DeepPurple.r, DeepPurple.g, DeepPurple.b, 0.95f);
        }

        if (toggle.graphic != null)
        {
            Image checkmark = toggle.graphic as Image;
            if (checkmark != null)
            {
                checkmark.color = StarYellow;
                checkmark.sprite = starSprite;
                checkmark.preserveAspect = true;
            }
        }

        return toggle;
    }

    private void OnApplySettings()
    {
        if (settingsConfigured && settingsController != null)
            settingsController.Apply();
    }

    private void OnRestoreDefaults()
    {
        if (settingsConfigured && settingsController != null)
            settingsController.RestoreDefaults();
    }

    private bool IsPreviewMode()
    {
#if UNITY_EDITOR
        return previewContinueInEditor;
#else
        return false;
#endif
    }

    private MenuSaveSlotSummary[] GetDisplaySummaries()
    {
        int count = 6;
        if (saveProvider != null)
            count = Mathf.Max(1, saveProvider.SlotCount);

        MenuSaveSlotSummary[] result = new MenuSaveSlotSummary[count];
        MenuSaveSlotSummary[] provided = saveProvider != null ? saveProvider.GetSlotSummaries() : null;

        for (int i = 0; i < count; i++)
        {
            if (provided != null && i < provided.Length)
            {
                result[i] = provided[i];
            }
            else
            {
                result[i].occupied = false;
                result[i].title = "存档 " + (i + 1);
                result[i].detail = "空槽";
            }
        }

        if (IsPreviewMode())
        {
            for (int i = 0; i < result.Length && i < 2; i++)
            {
                if (result[i].occupied)
                    continue;

                result[i].occupied = true;
                result[i].title = "预览存档 " + (i + 1);
                result[i].detail = "预览数据（未接入真实存档）";
                result[i].runCount = i + 1;
                result[i].clearCount = i;
            }
        }

        return result;
    }

    private void OnNewGameClicked()
    {
        newGameMode = true;
        pendingSlot = -1;
        UpdateSlotButtons();
        ShowPage(MenuPage.SaveSlots, GetFirstInteractableSlot());
    }

    private void OnContinueClicked()
    {
        bool hasSave = (saveProvider != null && saveProvider.HasAnySave) || IsPreviewMode();
        if (!hasSave)
        {
            if (continueButton != null)
                continueButton.gameObject.SetActive(false);
            return;
        }

        newGameMode = false;
        pendingSlot = -1;
        UpdateSlotButtons();
        ShowPage(MenuPage.SaveSlots, GetFirstInteractableSlot());
    }

    private void OnSettingsClicked()
    {
        ShowPage(MenuPage.Settings, volumeSlider);
    }

    private Selectable GetFirstInteractableSlot()
    {
        for (int i = 0; i < slotButtons.Count; i++)
        {
            if (slotButtons[i] != null && slotButtons[i].interactable)
                return slotButtons[i];
        }

        return null;
    }

    private void UpdateSlotButtons()
    {
        MenuSaveSlotSummary[] summaries = GetDisplaySummaries();
        if (slotPageTitle != null)
            slotPageTitle.text = newGameMode ? "选择新游戏存档位置" : "选择要继续的存档";

        for (int i = 0; i < slotButtons.Count; i++)
        {
            Button button = slotButtons[i];
            if (button == null)
                continue;

            MenuSaveSlotSummary summary = i < summaries.Length ? summaries[i] : default(MenuSaveSlotSummary);
            bool selectable = newGameMode || summary.occupied;
            button.interactable = selectable;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                string detail = string.IsNullOrEmpty(summary.detail) ? (summary.occupied ? "已有存档" : "空槽") : summary.detail;
                label.text = "存档 " + (i + 1) + "    " + detail;
                label.color = selectable ? LightCyan : MutedLabel;
            }
        }
    }

    private void OnSlotClicked(int slot)
    {
        MenuSaveSlotSummary[] summaries = GetDisplaySummaries();
        if (slot < 0 || slot >= summaries.Length)
            return;

        pendingSlot = slot;
        bool occupied = summaries[slot].occupied;

        if (newGameMode)
        {
            if (occupied)
                ShowConfirm();
            else
                ShowNotice();
            return;
        }

        if (!occupied)
            return;

        if (IsPreviewMode())
        {
            ShowConfirm();
            return;
        }

        if (saveProvider != null)
            saveProvider.Load(slot);
        LoadLevel1();
    }

    private void ShowNotice()
    {
        if (noticeMessage != null)
            noticeMessage.text = "存档功能待接入，当前进入试玩模式。";
        ShowPage(MenuPage.Notice, noticeStartButton);
    }

    private void ShowConfirm()
    {
        if (confirmMessage != null)
            confirmMessage.text = "存档 " + (pendingSlot + 1) + " 已有周目档案，请选择操作。";
        ShowPage(MenuPage.Confirm, confirmOverwriteButton);
    }

    private void ConfirmOverwrite()
    {
        if (saveProvider != null)
            saveProvider.Overwrite(pendingSlot);
        // TODO: 接入真实大存档系统后，覆盖完成后刷新槽位摘要。
        ShowSaveSlots();
    }

    private void ConfirmDelete()
    {
        if (saveProvider != null)
            saveProvider.Delete(pendingSlot);
        // TODO: 接入真实大存档系统后，删除完成后刷新槽位摘要。
        ShowSaveSlots();
    }

    private void StartTrial()
    {
        if (saveProvider != null)
            saveProvider.CreateNew(pendingSlot);
        // 占位 provider 不写入真实存档，Level1 以无存档槽状态运行。
        LoadLevel1();
    }

    private void LoadLevel1()
    {
        SceneManager.LoadScene("Level1");
    }

    private void Back()
    {
        if (page == MenuPage.Main)
            return;

        if (page == MenuPage.Confirm || page == MenuPage.Notice)
        {
            ShowSaveSlots();
            return;
        }

        ShowMain();
    }

    private void ShowSaveSlots()
    {
        UpdateSlotButtons();
        ShowPage(MenuPage.SaveSlots, GetFirstInteractableSlot());
    }

    private void ShowMain()
    {
        page = MenuPage.Main;
        pendingSlot = -1;
        SetPageActive(MenuPage.Main);
        RefreshMainVisibility();
        Select(newGameButton != null ? newGameButton : settingButton);
    }

    private void ShowPage(MenuPage target, Selectable firstSelect)
    {
        page = target;
        SetPageActive(target);
        SetMainMenuVisible(false);

        if (target == MenuPage.Main)
            RefreshMainVisibility();

        Select(firstSelect);
        lastSelected = null;
    }

    private void SetPageActive(MenuPage target)
    {
        if (saveSlotsPage != null)
            saveSlotsPage.SetActive(target == MenuPage.SaveSlots);
        if (settingsPage != null)
            settingsPage.SetActive(target == MenuPage.Settings);
        if (confirmPage != null)
            confirmPage.SetActive(target == MenuPage.Confirm);
        if (noticePage != null)
            noticePage.SetActive(target == MenuPage.Notice);
    }

    private void SetMainMenuVisible(bool visible)
    {
        if (!visible)
        {
            if (newGameButton != null) newGameButton.gameObject.SetActive(false);
            if (continueButton != null) continueButton.gameObject.SetActive(false);
            if (settingButton != null) settingButton.gameObject.SetActive(false);
            if (exitButton != null) exitButton.gameObject.SetActive(false);
            if (titleLabel != null) titleLabel.gameObject.SetActive(false);
            if (subtitleLabel != null) subtitleLabel.gameObject.SetActive(false);
            if (titleDecor != null) titleDecor.SetActive(false);
        }
    }

    private void RefreshMainVisibility()
    {
        if (newGameButton != null) newGameButton.gameObject.SetActive(true);
        if (settingButton != null) settingButton.gameObject.SetActive(true);
        if (exitButton != null) exitButton.gameObject.SetActive(true);
        if (titleLabel != null) titleLabel.gameObject.SetActive(true);
        if (subtitleLabel != null) subtitleLabel.gameObject.SetActive(true);
        if (titleDecor != null) titleDecor.SetActive(true);

        bool hasSave = (saveProvider != null && saveProvider.HasAnySave) || IsPreviewMode();
        if (continueButton != null)
            continueButton.gameObject.SetActive(hasSave);

        RectTransform newGameRect = newGameButton != null ? newGameButton.GetComponent<RectTransform>() : null;
        RectTransform continueRect = continueButton != null ? continueButton.GetComponent<RectTransform>() : null;
        RectTransform settingRect = settingButton != null ? settingButton.GetComponent<RectTransform>() : null;
        RectTransform exitRect = exitButton != null ? exitButton.GetComponent<RectTransform>() : null;

        if (newGameRect != null)
            newGameRect.anchoredPosition = new Vector2(28f, -92f);
        if (hasSave && continueRect != null)
            continueRect.anchoredPosition = new Vector2(28f, -124f);
        if (settingRect != null)
            settingRect.anchoredPosition = new Vector2(28f, hasSave ? -156f : -124f);
        if (exitRect != null)
            exitRect.anchoredPosition = new Vector2(28f, hasSave ? -188f : -156f);
    }

    private void Select(Selectable selectable)
    {
        if (EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(selectable != null ? selectable.gameObject : null);
        lastSelected = null;
    }

    private void RefreshSelectionVisual()
    {
        if (EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == lastSelected)
            return;

        lastSelected = selected;
        ApplyVisualState(mainVisuals, selected);
        ApplyVisualState(slotVisuals, selected);
    }

    private void ApplyVisualState(List<MenuVisual> visuals, GameObject selected)
    {
        for (int i = 0; i < visuals.Count; i++)
        {
            MenuVisual visual = visuals[i];
            if (visual == null || visual.button == null)
                continue;

            bool isSelected = visual.button.gameObject == selected;
            if (visual.label != null)
            {
                if (!visual.button.interactable)
                    visual.label.color = MutedLabel;
                else
                    visual.label.color = isSelected ? Pink : LightCyan;
            }

            if (visual.marker != null)
                visual.marker.gameObject.SetActive(isSelected);
        }
    }

    /// <summary>退出程序；Editor 中停止 Play Mode。</summary>
    public void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
