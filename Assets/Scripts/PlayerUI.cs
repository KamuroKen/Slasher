using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// This controller only operates on UI objects authored and saved in the scene.
[DefaultExecutionOrder(-100)]
public sealed class PlayerUI : MonoBehaviour
{
    public enum Screen { Gameplay, Inventory, Pause, Settings, MainMenu, Defeat }
    public static PlayerUI Instance { get; private set; }
    static int blockedThroughFrame;
    public static bool GameplayBlocked => Instance != null && (Instance.CurrentScreen != Screen.Gameplay || Time.frameCount <= blockedThroughFrame);
    public Screen CurrentScreen { get; private set; }
    public PlayerInventory Inventory { get; private set; }
    [SerializeField] bool startAtMainMenu;
    GameObject inventoryWindow, menuWindow, healthRoot, manaRoot;
    RectTransform detail;
    TMP_Text detailText;
    Image hpFill, heart;
    Image[] slots, quick;
    Button[] menuButtons;
    TMP_Text menuTitle;
    Damageable health;
    Screen settingsReturn;
    int hovered = -1, selected = -1, pressed = -1;
    Vector2 pressPosition;
    bool dragging, reloading;
    float previousTimeScale = 1, detailTop, flash, detailScroll, detailContentHeight;
    ItemDefinition shownItem;
    readonly List<RaycastResult> hits = new();
    static readonly Color Cream = new(1, .91f, .79f);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; blockedThroughFrame = -1; }
    Transform Find(string n) => GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
    void Awake()
    {
        Instance = this;
        var player = FindFirstObjectByType<PlayerHealth>();
        if (player != null) { health = player.GetComponent<Damageable>(); Inventory = player.GetComponent<PlayerInventory>(); }
        inventoryWindow = Find("inventory UI")?.gameObject; menuWindow = Find("main menu")?.gameObject;
        detail = Find("main panel 9-slice") as RectTransform;
        detailText = detail != null ? detail.GetComponentInChildren<TMP_Text>(true) : null;
        healthRoot = Find("health UI")?.gameObject; manaRoot = Find("stamina frame")?.gameObject;
        hpFill = Find("health")?.GetComponent<Image>(); heart = Find("heart")?.GetComponent<Image>();
        var group = Find("inventory slots");
        slots = group == null ? new Image[0] : group.GetComponentsInChildren<Image>(true).OrderByDescending(i => Mathf.Round(i.rectTransform.localPosition.y)).ThenBy(i => i.rectTransform.localPosition.x).ToArray();
        quick = transform.Cast<Transform>().Where(t => t.name.StartsWith("inventory slot bordered")).Select(t => t.GetComponent<Image>()).Where(i => i != null).OrderBy(i => i.rectTransform.anchoredPosition.x).ToArray();
        if (health == null || Inventory == null || inventoryWindow == null || menuWindow == null || detailText == null || hpFill == null || slots.Length != 20 || quick.Length != 4)
        { Debug.LogError("PlayerUI needs PlayerInventory, HP, 20 inventory slots, 4 quick slots, description and menu in the saved scene.", this); Instance = null; enabled = false; return; }
        detailTop = detail.anchoredPosition.y + detail.rect.height * .5f;
        menuButtons = new[] { "play", "options", "quit" }.Select(n => menuWindow.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == n)).ToArray();
        if (menuButtons.Any(b => b == null)) { Debug.LogError("Menu requires play, options and quit buttons.", this); Instance = null; enabled = false; return; }
        menuTitle = menuWindow.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.transform.parent.name == "paused banner");
        for (int i = 0; i < 3; i++) { int action = i; menuButtons[i].onClick.AddListener(() => MenuAction(action)); }
        foreach (var button in inventoryWindow.GetComponentsInChildren<Button>(true))
        {
            if (button.name == "button 1") { Label(button, "Use"); button.onClick.AddListener(() => Use(shownItem)); }
            else { if (button.name == "button 2") Label(button, "Close"); button.onClick.AddListener(() => SetScreen(Screen.Gameplay)); }
        }
        foreach (var button in GetComponentsInChildren<Button>(true)) button.navigation = new Navigation { mode = Navigation.Mode.None };
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat("UI.MasterVolume", 1));
    }
    void Start()
    {
        if (!enabled) return;
        health.HealthChanged += RefreshHealth; health.Damaged += Damaged; health.Died += Died; Inventory.Changed += RefreshInventory;
        RefreshHealth(); SetScreen(startAtMainMenu ? Screen.MainMenu : Screen.Gameplay);
    }
    void Update()
    {
        UpdatePointer(); var k = Keyboard.current;
        if (k != null)
        {
            if (k.escapeKey.wasPressedThisFrame) Back();
            else if (k.tabKey.wasPressedThisFrame) { if (CurrentScreen == Screen.Gameplay) SetScreen(Screen.Inventory); else if (CurrentScreen == Screen.Inventory) SetScreen(Screen.Gameplay); }
            for (int i = 0; i < 4; i++) if (k[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
            { if (CurrentScreen == Screen.Inventory) { var item = ItemAt(hovered >= 0 ? hovered : selected); if (item != null) Inventory.Assign(i, item); } else if (!GameplayBlocked) Use(Inventory.Quick(i)); }
        }
        flash = Mathf.Max(0, flash - Time.unscaledDeltaTime * 3);
        if (heart != null) heart.color = Color.Lerp(Color.white, new Color(1, .45f, .5f), flash);
    }
    ItemDefinition ItemAt(int i) => i < 0 ? null : i < 20 ? Inventory.Get(i)?.item : Inventory.Quick(i - 20);
    void UpdatePointer()
    {
        var mouse = Mouse.current; if (mouse == null || EventSystem.current == null) return;
        int target = -1; hits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() }, hits);
        if (hits.Count > 0)
        {
            var hit = hits[0].gameObject.transform;
            for (int i = 0; i < 24; i++) { var image = i < 20 ? slots[i] : quick[i - 20]; if (hit == image.transform || hit.IsChildOf(image.transform)) { target = i; break; } }
        }
        if (hovered != target) { hovered = target; if (CurrentScreen == Screen.Inventory && ItemAt(target) != null) ShowItem(ItemAt(target)); RefreshInventory(); }
        if (CurrentScreen != Screen.Gameplay && CurrentScreen != Screen.Inventory) return;
        if (mouse.leftButton.wasPressedThisFrame) { pressed = target; pressPosition = mouse.position.ReadValue(); dragging = false; }
        if (pressed >= 0 && mouse.leftButton.isPressed && Vector2.Distance(pressPosition, mouse.position.ReadValue()) > 8) dragging = true;
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (CurrentScreen == Screen.Inventory)
            {
                if (dragging && pressed >= 0 && target >= 0 && ItemAt(pressed) != null) { if (target >= 20) Inventory.Assign(target - 20, ItemAt(pressed)); else if (pressed < 20) Inventory.Move(pressed, target); }
                else if (target >= 0 && target == pressed) { selected = target; ShowItem(ItemAt(target)); }
            }
            else if (!GameplayBlocked && !dragging && target >= 20 && target == pressed) Use(ItemAt(target));
            pressed = -1; dragging = false; RefreshInventory();
        }
        if (CurrentScreen == Screen.Inventory && target >= 20 && mouse.rightButton.wasPressedThisFrame) Inventory.Assign(target - 20, null);
        if (CurrentScreen == Screen.Inventory && mouse.scroll.ReadValue().y != 0 &&
            RectTransformUtility.RectangleContainsScreenPoint(detail, mouse.position.ReadValue()))
            ScrollDetails(-Mathf.Sign(mouse.scroll.ReadValue().y) * 48);
    }
    public void SetScreen(Screen screen)
    {
        if (health.IsDead && screen == Screen.Gameplay) screen = Screen.Defeat;
        if (CurrentScreen == Screen.Gameplay && screen != Screen.Gameplay) previousTimeScale = Time.timeScale;
        Time.timeScale = screen == Screen.Gameplay ? previousTimeScale : 0;
        CurrentScreen = screen; blockedThroughFrame = Time.frameCount + 1; hovered = pressed = selected = -1; dragging = false;
        inventoryWindow.SetActive(screen == Screen.Inventory); detail.gameObject.SetActive(false);
        menuWindow.SetActive(screen != Screen.Gameplay && screen != Screen.Inventory);
        bool hud = screen == Screen.Gameplay || screen == Screen.Inventory;
        if (healthRoot != null) healthRoot.SetActive(hud); if (manaRoot != null) manaRoot.SetActive(hud);
        foreach (var image in quick) image.gameObject.SetActive(hud);
        if (screen == Screen.Inventory) ShowItem(null);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        RefreshMenu(); RefreshInventory();
    }
    void Back()
    {
        switch (CurrentScreen) { case Screen.Gameplay: SetScreen(Screen.Pause); break; case Screen.Inventory: case Screen.Pause: SetScreen(Screen.Gameplay); break; case Screen.Settings: SetScreen(settingsReturn); break; }
    }
    static void Label(Button button, string value)
    {
        var tmp = button.GetComponentInChildren<TMP_Text>(true); if (tmp != null) tmp.text = value;
        var legacy = button.GetComponentInChildren<Text>(true); if (legacy != null) legacy.text = value;
    }
    void RefreshMenu()
    {
        string title; string[] labels;
        switch (CurrentScreen)
        {
            case Screen.Settings: title = "Settings"; labels = new[] { "Volume: " + Mathf.RoundToInt(AudioListener.volume * 100) + "%", UnityEngine.Screen.fullScreen ? "Windowed" : "Fullscreen", "Back" }; break;
            case Screen.Pause: title = "Paused"; labels = new[] { "Resume", "Settings", "Main Menu" }; break;
            case Screen.Defeat: title = "Defeated"; labels = new[] { "Retry", "Main Menu", "Quit" }; break;
            default: title = "Main Menu"; labels = new[] { "Play", "Settings", "Quit" }; break;
        }
        if (menuTitle != null) menuTitle.text = title;
        for (int i = 0; i < 3; i++) Label(menuButtons[i], labels[i]);
    }
    void MenuAction(int action)
    {
        if (CurrentScreen == Screen.Settings)
        {
            if (action == 0) { AudioListener.volume = AudioListener.volume >= .99f ? 0 : Mathf.Min(1, AudioListener.volume + .1f); PlayerPrefs.SetFloat("UI.MasterVolume", AudioListener.volume); PlayerPrefs.Save(); }
            else if (action == 1) UnityEngine.Screen.fullScreen = !UnityEngine.Screen.fullScreen;
            else SetScreen(settingsReturn);
            RefreshMenu(); return;
        }
        if (action == 0) { if (health.IsDead) Restart(); else SetScreen(Screen.Gameplay); }
        else if (action == 1) { if (CurrentScreen == Screen.Defeat) SetScreen(Screen.MainMenu); else { settingsReturn = CurrentScreen; SetScreen(Screen.Settings); } }
        else if (CurrentScreen == Screen.Pause) SetScreen(Screen.MainMenu); else Quit();
    }
    void RefreshHealth() => hpFill.fillAmount = Mathf.Clamp01((float)health.CurrentHealth / health.MaxHealth);
    void Damaged(Vector2 source) => flash = 1;
    void Died() { RefreshHealth(); SetScreen(Screen.Defeat); }
    public void RefreshInventory()
    {
        for (int i = 0; i < 24; i++)
        {
            var image = i < 20 ? slots[i] : quick[i - 20]; var item = ItemAt(i);
            int count = i < 20 ? Inventory.Get(i)?.count ?? 0 : Inventory.Count(item);
            bool depleted = item != null && count == 0;
            image.color = depleted ? new Color(.55f, .55f, .55f) : i == selected || i == hovered ? Cream : item != null ? new Color(1, .85f, .66f) : Color.white;
            var labels = image.GetComponentsInChildren<TMP_Text>(true);
            var quantity = labels.FirstOrDefault(t => t.name == "Count");
            if (quantity != null) { quantity.text = count.ToString(); quantity.enabled = item != null; }
            var key = labels.FirstOrDefault(t => t.name == "Key");
            if (key != null) key.text = (i - 19).ToString();
            // Older saved canvases can still display their combined label.
            if (quantity == null && key == null && labels.Length > 0)
                labels[0].text = (i >= 20 ? (i - 19).ToString() : "") + (item != null ? "\n<size=18>" + count + "</size>" : "");
            var icon = image.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null) { icon.sprite = item != null ? item.icon : null; icon.enabled = icon.sprite != null; icon.color = depleted ? new Color(1, 1, 1, .35f) : Color.white; }
        }
    }
    void ShowItem(ItemDefinition item)
    {
        shownItem = item; detail.gameObject.SetActive(true);
        detailText.text = item == null ? "<size=32>Item Details</size>\n\nHover over an item to inspect it.\n\nPress 1–4 while hovering to assign a quick slot."
            : "<size=32>" + item.displayName + "</size>\n\n" + item.description + (string.IsNullOrWhiteSpace(item.statistics) ? "" : "\n\n<color=#FFE3AD>Stats</color>\n" + item.statistics) + "\n\n<color=#FFE3AD>1–4 — Assign quick slot</color>";
        detailContentHeight = detailText.GetPreferredValues(detailText.text, detailText.rectTransform.rect.width, 0).y;
        float height = Mathf.Clamp(detailContentHeight + 64, 240, 620);
        detail.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        detail.anchoredPosition = new Vector2(detail.anchoredPosition.x, detailTop - height * .5f);
        detailContentHeight = Mathf.Max(detailContentHeight, height - 64);
        detailText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, detailContentHeight);
        detailText.overflowMode = TextOverflowModes.Overflow;
        detailScroll = 0; ScrollDetails(0); detailText.ForceMeshUpdate();
    }
    public void ScrollDetails(float amount)
    {
        detailScroll = Mathf.Clamp(detailScroll + amount, 0, Mathf.Max(0, detailContentHeight - (detail.rect.height - 64)));
        var rect = detailText.rectTransform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, (detail.rect.height - detailContentHeight) * .5f - 32 + detailScroll);
    }
    void Use(ItemDefinition item) { if (item != null && Inventory.Use(item, health)) RefreshInventory(); }
    public void Restart() { reloading = true; Time.timeScale = 1; SceneManager.LoadScene(SceneManager.GetActiveScene().path); }
    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    void OnDestroy()
    {
        if (health != null) { health.HealthChanged -= RefreshHealth; health.Damaged -= Damaged; health.Died -= Died; }
        if (Inventory != null) Inventory.Changed -= RefreshInventory;
        if (Instance == this) { Instance = null; Time.timeScale = reloading ? 1 : previousTimeScale; }
    }
}

