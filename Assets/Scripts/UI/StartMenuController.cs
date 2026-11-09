using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class StartMenuController : MonoBehaviour
{
    private enum DestructiveAction
    {
        Overwrite,
        Delete
    }

    private Button newGameButton;
    private Button continueButton;
    private Button exitButton;
    private Canvas canvas;
    private GameObject slotPanel;
    private GameObject decisionPanel;
    private GameObject confirmationPanel;
    private Transform slotRoot;
    private TMP_Text slotTitle;
    private TMP_Text decisionText;
    private TMP_Text confirmationText;
    private int pendingSlot = -1;
    private DestructiveAction pendingAction;
    private bool choosingNewGame;

    private void Start()
    {
        canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("StartMenuController: no Canvas found.", this);
            return;
        }

        canvas.transform.localScale = Vector3.one;
        FindMainButtons();
        BuildRuntimeUi();
        RefreshMainMenu();
    }

    private void FindMainButtons()
    {
        newGameButton = FindButton("NewGame");
        continueButton = FindButton("Continue");
        exitButton = FindButton("Exit");

        if (newGameButton != null)
            newGameButton.onClick.AddListener(OpenNewGameSlots);
        if (continueButton != null)
            continueButton.onClick.AddListener(OpenContinueSlots);
        if (exitButton != null)
            exitButton.onClick.AddListener(ExitGame);
    }

    private Button FindButton(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        return target != null ? target.GetComponent<Button>() : null;
    }

    private void RefreshMainMenu()
    {
        if (continueButton != null)
            continueButton.gameObject.SetActive(SaveManager.HasAnySave());
    }

    private void OpenNewGameSlots()
    {
        choosingNewGame = true;
        pendingSlot = -1;
        if (decisionPanel != null)
            decisionPanel.SetActive(false);
        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);
        if (slotPanel != null)
            slotPanel.SetActive(true);
        RefreshSlots();
    }

    private void OpenContinueSlots()
    {
        if (!SaveManager.HasAnySave())
        {
            RefreshMainMenu();
            return;
        }

        choosingNewGame = false;
        pendingSlot = -1;
        if (decisionPanel != null)
            decisionPanel.SetActive(false);
        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);
        if (slotPanel != null)
            slotPanel.SetActive(true);
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        if (slotRoot == null)
            return;

        for (int i = slotRoot.childCount - 1; i >= 0; i--)
            Destroy(slotRoot.GetChild(i).gameObject);

        SaveSlotSummary[] summaries = SaveManager.GetSlotSummaries();
        slotTitle.text = choosingNewGame ? "选择新游戏存档位置" : "选择要继续的存档";

        for (int i = 0; i < SaveManager.SlotCount; i++)
        {
            SaveSlotSummary summary = summaries[i];
            bool canSelect = choosingNewGame || summary.occupied;
            string label = string.Format("存档 {0}    {1}", i + 1, summary.DisplayText);
            Button button = CreateButton(slotRoot, label, 54f);
            button.interactable = canSelect;

            int slot = i;
            button.onClick.AddListener(delegate { SelectSlot(slot); });
        }
    }

    private void SelectSlot(int slot)
    {
        SaveSlotSummary[] summaries = SaveManager.GetSlotSummaries();
        if (slot < 0 || slot >= summaries.Length)
            return;

        if (!choosingNewGame)
        {
            if (summaries[slot].occupied)
                StartLevelFromSlot(slot);
            return;
        }

        if (!summaries[slot].occupied)
        {
            SaveManager.CreateNew(slot);
            StartLevelFromSlot(slot);
            return;
        }

        pendingSlot = slot;
        decisionText.text = string.Format("存档 {0} 已有内容。\n请选择覆盖原存档、删除存档，或取消。", slot + 1);
        slotPanel.SetActive(false);
        decisionPanel.SetActive(true);
    }

    private void ShowConfirmation(DestructiveAction action)
    {
        pendingAction = action;
        decisionPanel.SetActive(false);
        confirmationText.text = action == DestructiveAction.Delete
            ? string.Format("确认删除存档 {0}？此操作无法撤销。", pendingSlot + 1)
            : string.Format("确认覆盖存档 {0}？原有进度将被删除。", pendingSlot + 1);
        confirmationPanel.SetActive(true);
    }

    private void ConfirmDestructiveAction()
    {
        if (pendingSlot < 0)
            return;

        if (pendingAction == DestructiveAction.Delete)
        {
            SaveManager.Delete(pendingSlot);
            pendingSlot = -1;
            confirmationPanel.SetActive(false);
            slotPanel.SetActive(true);
            RefreshSlots();
            RefreshMainMenu();
            return;
        }

        SaveManager.Overwrite(pendingSlot);
        StartLevelFromSlot(pendingSlot);
    }

    private void StartLevelFromSlot(int slot)
    {
        SaveManager.SetActiveSlot(slot);
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene("Level1");
    }

    private void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void BuildRuntimeUi()
    {
        RectTransform slotContent;
        slotPanel = CreateOverlay("SaveSlotPanel", out slotContent).gameObject;
        slotTitle = CreateText(slotContent, "选择存档", 30, TextAlignmentOptions.Center);
        SetRect(slotTitle.rectTransform, new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.96f), Vector2.zero);

        GameObject listObject = new GameObject("SlotList", typeof(RectTransform), typeof(VerticalLayoutGroup));
        listObject.transform.SetParent(slotContent, false);
        RectTransform listRect = listObject.GetComponent<RectTransform>();
        SetRect(listRect, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.80f), Vector2.zero);
        VerticalLayoutGroup layout = listObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        slotRoot = listObject.transform;

        Button backButton = CreateButton(slotContent, "返回", 48f);
        SetRect(backButton.GetComponent<RectTransform>(), new Vector2(0.36f, 0.04f), new Vector2(0.64f, 0.14f), Vector2.zero);
        backButton.onClick.AddListener(CloseOverlays);

        RectTransform decisionContent;
        decisionPanel = CreateOverlay("SaveDecisionPanel", out decisionContent).gameObject;
        TMP_Text decisionTitle = CreateText(decisionContent, "存档操作", 30, TextAlignmentOptions.Center);
        SetRect(decisionTitle.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.95f), Vector2.zero);
        decisionText = CreateText(decisionContent, string.Empty, 22, TextAlignmentOptions.Center);
        SetRect(decisionText.rectTransform, new Vector2(0.10f, 0.48f), new Vector2(0.90f, 0.75f), Vector2.zero);

        Button overwriteButton = CreateButton(decisionContent, "覆盖", 48f);
        SetRect(overwriteButton.GetComponent<RectTransform>(), new Vector2(0.10f, 0.28f), new Vector2(0.42f, 0.40f), Vector2.zero);
        overwriteButton.onClick.AddListener(delegate { ShowConfirmation(DestructiveAction.Overwrite); });

        Button deleteButton = CreateButton(decisionContent, "删除", 48f);
        SetRect(deleteButton.GetComponent<RectTransform>(), new Vector2(0.58f, 0.28f), new Vector2(0.90f, 0.40f), Vector2.zero);
        deleteButton.onClick.AddListener(delegate { ShowConfirmation(DestructiveAction.Delete); });

        Button decisionCancel = CreateButton(decisionContent, "取消", 44f);
        SetRect(decisionCancel.GetComponent<RectTransform>(), new Vector2(0.36f, 0.08f), new Vector2(0.64f, 0.20f), Vector2.zero);
        decisionCancel.onClick.AddListener(CloseOverlays);

        RectTransform confirmationContent;
        confirmationPanel = CreateOverlay("SaveConfirmationPanel", out confirmationContent).gameObject;
        TMP_Text confirmationTitle = CreateText(confirmationContent, "请确认", 30, TextAlignmentOptions.Center);
        SetRect(confirmationTitle.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.95f), Vector2.zero);
        confirmationText = CreateText(confirmationContent, string.Empty, 22, TextAlignmentOptions.Center);
        SetRect(confirmationText.rectTransform, new Vector2(0.10f, 0.50f), new Vector2(0.90f, 0.75f), Vector2.zero);

        Button confirmButton = CreateButton(confirmationContent, "确认", 48f);
        SetRect(confirmButton.GetComponent<RectTransform>(), new Vector2(0.10f, 0.27f), new Vector2(0.42f, 0.39f), Vector2.zero);
        confirmButton.onClick.AddListener(ConfirmDestructiveAction);

        Button confirmCancel = CreateButton(confirmationContent, "取消", 48f);
        SetRect(confirmCancel.GetComponent<RectTransform>(), new Vector2(0.58f, 0.27f), new Vector2(0.90f, 0.39f), Vector2.zero);
        confirmCancel.onClick.AddListener(delegate
        {
            confirmationPanel.SetActive(false);
            decisionPanel.SetActive(true);
        });

        slotPanel.SetActive(false);
        decisionPanel.SetActive(false);
        confirmationPanel.SetActive(false);
    }

    private void CloseOverlays()
    {
        if (slotPanel != null)
            slotPanel.SetActive(false);
        if (decisionPanel != null)
            decisionPanel.SetActive(false);
        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);
        pendingSlot = -1;
        RefreshMainMenu();
    }

    private RectTransform CreateOverlay(string objectName, out RectTransform content)
    {
        GameObject overlay = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        SetRect(overlayRect, Vector2.zero, Vector2.one, Vector2.zero);
        Image overlayImage = overlay.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.84f);

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(Image));
        contentObject.transform.SetParent(overlay.transform, false);
        content = contentObject.GetComponent<RectTransform>();
        SetRect(content, new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero);
        contentObject.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.14f, 0.98f);
        return overlayRect.gameObject.GetComponent<RectTransform>();
    }

    private TMP_Text CreateText(Transform parent, string value, float size, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        return text;
    }

    private Button CreateButton(Transform parent, string label, float height)
    {
        GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, height);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.24f, 0.20f, 0.36f, 1f);

        TMP_Text text = CreateText(buttonObject.transform, label, 22f, TextAlignmentOptions.Center);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.40f, 0.34f, 0.58f, 1f);
        colors.pressedColor = new Color(0.16f, 0.14f, 0.24f, 1f);
        button.colors = colors;
        return button;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 sizeDelta)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = sizeDelta;
        rect.offsetMax = sizeDelta;
        rect.anchoredPosition = Vector2.zero;
    }
}
