using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Owns the title/pause/save UI and coordinates local save-slot operations.
/// The current project starts in Level1, so the title page is shown as a full-screen
/// overlay until a dedicated title scene is added.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class SaveCoordinator : MonoBehaviour
{
    private enum View
    {
        Hidden,
        Title,
        Pause,
        ManageSlots,
        SaveSlots,
        Confirm
    }

    private enum ConfirmAction
    {
        None,
        Overwrite,
        Delete
    }

    private static SaveCoordinator instance;
    private static LevelSnapshot pendingSnapshot;

    private SaveSlotStore store;
    private List<InputActionReference> uiActionReferences;

    private View view;
    private View returnView;
    private View confirmReturnView;
    private ConfirmAction confirmAction;
    private int confirmSlotId;
    private int selectedSlotId;
    private bool hasLoadedFirstScene;
    private bool startingNewGame;
    private bool isTitleMode;
    private string gameplayScenePath;
    private string statusMessage = string.Empty;

    private PlayerController player;
    private LevelManager level;
    private InputAction saveAction;
    private InputAction pauseAction;
    private InputAction cancelAction;

    private GameObject canvasObject;
    private GameObject overlayRoot;
    private GameObject panelObject;
    private TMP_Text promptText;
    private TMP_Text pageStatusText;
    private EventSystem eventSystem;
    private InputSystemUIInputModule uiInputModule;

    public static bool IsOverlayOpen => instance != null && instance.view != View.Hidden;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        pendingSnapshot = null;
        Time.timeScale = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject coordinator = new GameObject(nameof(SaveCoordinator));
        coordinator.AddComponent<SaveCoordinator>();
    }

    public static bool TryConsumePendingSnapshot(out LevelSnapshot snapshot)
    {
        snapshot = pendingSnapshot;
        if (snapshot == null)
            return false;

        pendingSnapshot = null;
        return true;
    }

    private void Awake()
    {
        store = new SaveSlotStore();
        uiActionReferences = new List<InputActionReference>();

        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindGameplayActions();
        UnbindUiActions();
        instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResolveSceneReferences();
        EnsureMenuUi();
        BindGameplayActions();
        BindUiActions();

        if (!hasLoadedFirstScene)
        {
            hasLoadedFirstScene = true;
            if (level != null)
                gameplayScenePath = scene.path;
            else if (string.IsNullOrEmpty(gameplayScenePath))
                gameplayScenePath = FindFirstOtherBuildScene(scene.path);

            ShowTitle();
            return;
        }

        if (startingNewGame)
        {
            startingNewGame = false;
            isTitleMode = false;
            HideOverlayAndResume();
            return;
        }

        if (pendingSnapshot != null)
        {
            isTitleMode = false;
            HideOverlayAndResume();
            return;
        }

        if (level != null)
            gameplayScenePath = scene.path;

        if (isTitleMode)
            ShowTitle();
        else
            HideOverlayAndResume();
    }

    private void Update()
    {
        bool showPrompt = view == View.Hidden
                          && !isTitleMode
                          && level != null
                          && !level.IsCompleted
                          && !level.IsRespawning
                          && player != null
                          && !player.IsDead
                          && level.NearbyCheckpoint != null;

        if (promptText != null)
        {
            promptText.gameObject.SetActive(showPrompt);
            if (showPrompt)
                promptText.text = "CHECKPOINT  |  Press P to save";
        }
    }

    private void ResolveSceneReferences()
    {
        player = FindObjectOfType<PlayerController>();
        level = LevelManager.Instance;
    }

    private void BindGameplayActions()
    {
        UnbindGameplayActions();
        if (player == null || player.inputControl == null)
            return;

        saveAction = player.inputControl.GamePlay.Save;
        pauseAction = player.inputControl.GamePlay.Pause;
        saveAction.started += OnSavePressed;
        pauseAction.started += OnPausePressed;
    }

    private void UnbindGameplayActions()
    {
        if (saveAction != null)
            saveAction.started -= OnSavePressed;
        if (pauseAction != null)
            pauseAction.started -= OnPausePressed;
        saveAction = null;
        pauseAction = null;
    }

    private void BindUiActions()
    {
        UnbindUiActions();
        if (uiInputModule == null)
            return;

        uiInputModule.enabled = false;
        if (player == null || player.inputControl == null)
        {
            uiInputModule.AssignDefaultActions();
            uiInputModule.enabled = true;
            return;
        }

        InputActionAsset asset = player.inputControl.asset;
        uiInputModule.actionsAsset = asset;
        uiInputModule.move = CreateUiReference(asset, "UI/Navigate");
        uiInputModule.submit = CreateUiReference(asset, "UI/Submit");
        uiInputModule.cancel = CreateUiReference(asset, "UI/Cancel");
        uiInputModule.point = CreateUiReference(asset, "UI/Point");
        uiInputModule.leftClick = CreateUiReference(asset, "UI/Click");
        cancelAction = asset.FindAction("UI/Cancel", false);
        if (cancelAction != null)
            cancelAction.started += OnCancelPressed;
        uiInputModule.enabled = true;

        if (view != View.Hidden)
            player.inputControl.UI.Enable();
    }

    private InputActionReference CreateUiReference(InputActionAsset asset, string actionName)
    {
        InputAction action = asset.FindAction(actionName, false);
        if (action == null)
            return null;

        InputActionReference reference = InputActionReference.Create(action);
        uiActionReferences.Add(reference);
        return reference;
    }

    private void UnbindUiActions()
    {
        if (cancelAction != null)
            cancelAction.started -= OnCancelPressed;
        cancelAction = null;

        if (uiActionReferences == null)
            return;

        for (int i = 0; i < uiActionReferences.Count; i++)
        {
            if (uiActionReferences[i] != null)
                Destroy(uiActionReferences[i]);
        }
        uiActionReferences.Clear();
    }

    private void OnSavePressed(InputAction.CallbackContext context)
    {
        if (view != View.Hidden || level == null || player == null || player.IsDead
            || level.IsCompleted || level.IsRespawning)
            return;

        Checkpoint checkpoint = level.NearbyCheckpoint;
        if (checkpoint == null)
            return;

        returnView = View.Hidden;
        selectedSlotId = 0;
        statusMessage = string.Empty;
        ShowView(View.SaveSlots);
    }

    private void OnPausePressed(InputAction.CallbackContext context)
    {
        if (view != View.Hidden || level == null || player == null || player.IsDead
            || level.IsCompleted || level.IsRespawning)
            return;

        returnView = View.Hidden;
        ShowView(View.Pause);
    }

    private void OnCancelPressed(InputAction.CallbackContext context)
    {
        if (view == View.Hidden)
            return;

        if (view == View.Confirm)
        {
            confirmAction = ConfirmAction.None;
            ShowView(confirmReturnView);
            return;
        }

        if (view == View.SaveSlots && returnView == View.Hidden)
        {
            HideOverlayAndResume();
            return;
        }

        if (view == View.ManageSlots || view == View.SaveSlots)
        {
            ShowView(returnView);
            return;
        }

        if (view == View.Pause)
            HideOverlayAndResume();
    }

    private void EnsureMenuUi()
    {
        if (canvasObject == null)
        {
            canvasObject = new GameObject("SaveMenuCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            overlayRoot = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(canvasObject.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRoot.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.04f, 0.86f);

            panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelObject.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 820f);
            panelObject.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.17f, 0.98f);
            VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(44, 44, 34, 34);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            promptText = CreateText("CheckpointPrompt", canvasObject.transform, 22f, Color.white,
                TextAlignmentOptions.Center);
            RectTransform promptRect = promptText.rectTransform;
            promptRect.anchorMin = new Vector2(0.15f, 0.04f);
            promptRect.anchorMax = new Vector2(0.85f, 0.12f);
            promptRect.offsetMin = Vector2.zero;
            promptRect.offsetMax = Vector2.zero;

            EnsureEventSystem();
        }

        if (eventSystem == null)
            EnsureEventSystem();
    }

    private void EnsureEventSystem()
    {
        eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("SaveMenuEventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(eventSystemObject);
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
            uiInputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            uiInputModule.enabled = false;
            return;
        }

        StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
        if (legacyModule != null)
            legacyModule.enabled = false;

        uiInputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (uiInputModule == null)
            uiInputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        uiInputModule.enabled = false;
    }

    private void ShowTitle()
    {
        isTitleMode = true;
        Time.timeScale = 0f;
        LockPlayer(true);
        EnableUiInput(true);
        returnView = View.Title;
        ShowView(View.Title);
    }

    private void ShowView(View nextView)
    {
        view = nextView;
        bool visible = nextView != View.Hidden;
        if (overlayRoot != null)
            overlayRoot.SetActive(visible);
        if (promptText != null && visible)
            promptText.gameObject.SetActive(false);
        if (!visible)
            return;

        LockPlayer(true);
        EnableUiInput(true);
        RebuildPanel();
    }

    private void HideOverlayAndResume()
    {
        view = View.Hidden;
        confirmAction = ConfirmAction.None;
        statusMessage = string.Empty;
        isTitleMode = false;
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
        Time.timeScale = 1f;
        EnableUiInput(false);
        LockPlayer(false);
    }

    private void LockPlayer(bool locked)
    {
        if (player != null)
            player.SetInputLocked(locked);
    }

    private void EnableUiInput(bool enabled)
    {
        if (player == null || player.inputControl == null)
            return;

        if (enabled)
            player.inputControl.UI.Enable();
        else
            player.inputControl.UI.Disable();
    }

    private void RebuildPanel()
    {
        if (panelObject == null)
            return;

        for (int i = panelObject.transform.childCount - 1; i >= 0; i--)
            Destroy(panelObject.transform.GetChild(i).gameObject);

        pageStatusText = null;
        switch (view)
        {
            case View.Title:
                BuildTitlePage();
                break;
            case View.Pause:
                BuildPausePage();
                break;
            case View.ManageSlots:
                BuildManageSlotsPage();
                break;
            case View.SaveSlots:
                BuildSaveSlotsPage();
                break;
            case View.Confirm:
                BuildConfirmPage();
                break;
        }

        if (eventSystem != null && panelObject.transform.childCount > 0)
        {
            Button firstButton = panelObject.GetComponentInChildren<Button>();
            if (firstButton != null)
                eventSystem.SetSelectedGameObject(firstButton.gameObject);
        }
    }

    private void BuildTitlePage()
    {
        CreateText("Title", panelObject.transform, 52f, new Color(0.92f, 0.96f, 1f),
            TextAlignmentOptions.Center, 74f).text = "POPPIN";
        CreateText("TitleSubtitle", panelObject.transform, 22f, new Color(0.68f, 0.76f, 0.84f),
            TextAlignmentOptions.Center, 40f).text = "Adventure";

        CreateButton("New Game", 58f, StartNewGame);
        SaveSlotInfo latest = GetLatestLoadableSlot();
        Button continueButton = CreateButton("Continue", 58f, ContinueLatest);
        continueButton.interactable = latest != null;
        CreateButton("Manage Saves", 58f, () =>
        {
            returnView = View.Title;
            selectedSlotId = 0;
            ShowView(View.ManageSlots);
        });
        CreateButton("Exit", 58f, () => Application.Quit());
        CreateStatus();
    }

    private void BuildPausePage()
    {
        CreateText("PauseTitle", panelObject.transform, 42f, Color.white,
            TextAlignmentOptions.Center, 64f).text = "PAUSED";
        CreateButton("Resume", 58f, HideOverlayAndResume);
        CreateButton("Manage Saves", 58f, () =>
        {
            returnView = View.Pause;
            selectedSlotId = 0;
            ShowView(View.ManageSlots);
        });
        CreateButton("Return to Title", 58f, ShowTitle);
        CreateStatus();
    }

    private void BuildManageSlotsPage()
    {
        CreateText("ManageTitle", panelObject.transform, 36f, Color.white,
            TextAlignmentOptions.Center, 56f).text = "SAVE FILES";
        CreateText("ManageHint", panelObject.transform, 18f, new Color(0.68f, 0.76f, 0.84f),
            TextAlignmentOptions.Center, 32f).text = "Select a slot to load or delete.";

        SaveSlotInfo[] slots = store.ListSlots();
        for (int i = 0; i < slots.Length; i++)
        {
            SaveSlotInfo info = slots[i];
            string label = FormatSlotLabel(info);
            int slotId = info.SlotId;
            Button row = CreateButton(label, 48f, () =>
            {
                selectedSlotId = slotId;
                statusMessage = info.IsOccupied && !info.IsValid ? info.Error : string.Empty;
                RebuildPanel();
            });
            row.interactable = true;
            if (slotId == selectedSlotId)
                row.GetComponent<Image>().color = new Color(0.18f, 0.32f, 0.34f);
        }

        SaveSlotInfo selected = FindSlot(slots, selectedSlotId);
        bool canLoad = selected != null && selected.IsValid && IsSceneLoadable(selected.ScenePath);
        bool canDelete = selected != null && selected.IsOccupied;
        Button load = CreateButton("Load Selected", 48f, () => LoadSelectedSlot());
        load.interactable = canLoad;
        Button delete = CreateButton("Delete Selected", 48f, () => AskForConfirmation(ConfirmAction.Delete, selectedSlotId));
        delete.interactable = canDelete;
        CreateButton("Back", 48f, () => ShowView(returnView));
        CreateStatus();
    }

    private void BuildSaveSlotsPage()
    {
        CreateText("SaveTitle", panelObject.transform, 36f, Color.white,
            TextAlignmentOptions.Center, 56f).text = "SAVE CHECKPOINT";
        CreateText("SaveHint", panelObject.transform, 18f, new Color(0.68f, 0.76f, 0.84f),
            TextAlignmentOptions.Center, 32f).text = "Choose a slot. Existing saves require confirmation.";

        SaveSlotInfo[] slots = store.ListSlots();
        for (int i = 0; i < slots.Length; i++)
        {
            SaveSlotInfo info = slots[i];
            int slotId = info.SlotId;
            CreateButton(FormatSlotLabel(info), 56f, () =>
            {
                if (info.IsOccupied)
                    AskForConfirmation(ConfirmAction.Overwrite, slotId);
                else
                    CommitSave(slotId);
            });
        }

        CreateButton("Cancel", 54f, OnCancelPressedFromUi);
        CreateStatus();
    }

    private void BuildConfirmPage()
    {
        string message = confirmAction == ConfirmAction.Overwrite
            ? "Overwrite Slot " + confirmSlotId + "? This cannot be undone."
            : "Delete Slot " + confirmSlotId + "? This cannot be undone.";
        CreateText("ConfirmTitle", panelObject.transform, 36f, Color.white,
            TextAlignmentOptions.Center, 60f).text = confirmAction == ConfirmAction.Overwrite ? "OVERWRITE SAVE" : "DELETE SAVE";
        CreateText("ConfirmMessage", panelObject.transform, 20f, new Color(0.78f, 0.84f, 0.9f),
            TextAlignmentOptions.Center, 70f).text = message;
        CreateButton("Confirm", 58f, ConfirmPendingAction);
        CreateButton("Cancel", 58f, OnCancelPressedFromUi);
        CreateStatus();
    }

    private void CreateStatus()
    {
        pageStatusText = CreateText("Status", panelObject.transform, 17f, new Color(0.82f, 0.89f, 0.72f),
            TextAlignmentOptions.Center, 36f);
        pageStatusText.text = statusMessage;
    }

    private TMP_Text CreateText(string objectName, Transform parent, float fontSize, Color color,
        TextAlignmentOptions alignment, float height = 48f)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        LayoutElement layout = textObject.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        return text;
    }

    private Button CreateButton(string label, float height, Action action)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(panelObject.transform, false);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.17f, 0.23f, 0.29f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.17f, 0.23f, 0.29f);
        colors.highlightedColor = new Color(0.25f, 0.39f, 0.42f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.12f, 0.35f, 0.36f);
        colors.disabledColor = new Color(0.12f, 0.14f, 0.16f, 0.65f);
        button.colors = colors;
        button.onClick.AddListener(() => action?.Invoke());

        TMP_Text text = CreateText("Label", buttonObject.transform, 21f, Color.white,
            TextAlignmentOptions.Center, height);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(12f, 0f);
        text.rectTransform.offsetMax = new Vector2(-12f, 0f);
        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        return button;
    }

    private void StartNewGame()
    {
        if (!IsSceneLoadable(gameplayScenePath))
        {
            statusMessage = "No playable scene is available in Build Settings.";
            RebuildPanel();
            return;
        }

        pendingSnapshot = null;
        startingNewGame = true;
        isTitleMode = false;
        view = View.Hidden;
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
        EnableUiInput(false);
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplayScenePath);
    }

    private void ContinueLatest()
    {
        SaveSlotInfo latest = GetLatestLoadableSlot();
        if (latest == null)
        {
            statusMessage = "No valid save is available.";
            RebuildPanel();
            return;
        }

        LoadSlot(latest.SlotId);
    }

    private SaveSlotInfo GetLatestLoadableSlot()
    {
        SaveSlotInfo[] slots = store.ListSlots();
        return slots
            .Where(slot => slot.IsValid && IsSceneLoadable(slot.ScenePath))
            .OrderByDescending(slot => ParseTimestamp(slot.SavedAtUtc))
            .FirstOrDefault();
    }

    private void LoadSelectedSlot()
    {
        if (selectedSlotId <= 0)
            return;
        LoadSlot(selectedSlotId);
    }

    private void LoadSlot(int slotId)
    {
        SaveOperationResult result = store.LoadSlot(slotId, out SaveSlotData data);
        if (!result.Succeeded)
        {
            statusMessage = result.Error;
            RebuildPanel();
            return;
        }
        if (!IsSceneLoadable(data.level.scenePath))
        {
            statusMessage = "The scene for this save is not included in Build Settings.";
            RebuildPanel();
            return;
        }

        pendingSnapshot = data.level;
        startingNewGame = false;
        isTitleMode = false;
        view = View.Hidden;
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
        EnableUiInput(false);
        Time.timeScale = 1f;
        SceneManager.LoadScene(data.level.scenePath);
    }

    private void CommitSave(int slotId)
    {
        if (level == null)
        {
            statusMessage = "The game state is not ready.";
            RebuildPanel();
            return;
        }

        Checkpoint checkpoint = level.NearbyCheckpoint;
        if (!level.TryCaptureSnapshot(checkpoint, out LevelSnapshot snapshot, out string captureError))
        {
            statusMessage = captureError;
            RebuildPanel();
            return;
        }

        SaveOperationResult result = store.SaveSlot(slotId, snapshot);
        statusMessage = result.Succeeded ? "Saved to Slot " + slotId + "." : result.Error;
        RebuildPanel();
    }

    private void AskForConfirmation(ConfirmAction action, int slotId)
    {
        confirmReturnView = view;
        confirmAction = action;
        confirmSlotId = slotId;
        statusMessage = string.Empty;
        ShowView(View.Confirm);
    }

    private void ConfirmPendingAction()
    {
        if (confirmAction == ConfirmAction.Overwrite)
        {
            int slotId = confirmSlotId;
            confirmAction = ConfirmAction.None;
            ShowView(View.SaveSlots);
            CommitSave(slotId);
            return;
        }

        if (confirmAction == ConfirmAction.Delete)
        {
            SaveOperationResult result = store.DeleteSlot(confirmSlotId);
            confirmAction = ConfirmAction.None;
            statusMessage = result.Succeeded ? "Save deleted." : result.Error;
            ShowView(confirmReturnView);
        }
    }

    private void OnCancelPressedFromUi()
    {
        if (view == View.Confirm)
        {
            confirmAction = ConfirmAction.None;
            ShowView(confirmReturnView);
        }
        else
        {
            OnCancelPressed(default);
        }
    }

    private void ShowStatus(string message)
    {
        statusMessage = message;
        if (pageStatusText != null)
            pageStatusText.text = message;
    }

    private string FormatSlotLabel(SaveSlotInfo info)
    {
        if (!info.IsOccupied)
            return "Slot " + info.SlotId + "    |    Empty";
        if (!info.IsValid)
            return "Slot " + info.SlotId + "    |    Invalid save";

        string sceneName = System.IO.Path.GetFileNameWithoutExtension(info.ScenePath);
        string timestamp = DateTime.TryParse(info.SavedAtUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out DateTime savedAt)
            ? savedAt.ToLocalTime().ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture)
            : "Unknown time";
        int score = info.Snapshot.killPoints
                    + info.Snapshot.stars * (level != null ? level.starScore : 100)
                    + info.Snapshot.signsRead * (level != null ? level.signScore : 10);
        return "Slot " + info.SlotId + "    |    " + sceneName + "    |    "
               + info.Snapshot.stars + " stars    |    " + score + " pts    |    " + timestamp;
    }

    private static SaveSlotInfo FindSlot(SaveSlotInfo[] slots, int slotId)
    {
        if (slotId <= 0 || slots == null)
            return null;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].SlotId == slotId)
                return slots[i];
        }
        return null;
    }

    private static DateTime ParseTimestamp(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
            out DateTime timestamp)
            ? timestamp
            : DateTime.MinValue;
    }

    private static bool IsSceneLoadable(string scenePath)
    {
        return !string.IsNullOrWhiteSpace(scenePath)
               && Application.CanStreamedLevelBeLoaded(scenePath);
    }

    private static string FindFirstOtherBuildScene(string currentScenePath)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (!string.IsNullOrEmpty(path) && path != currentScenePath && IsSceneLoadable(path))
                return path;
        }
        return string.Empty;
    }
}
