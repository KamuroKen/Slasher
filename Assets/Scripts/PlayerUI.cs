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
    public enum Screen { Gameplay, Inventory, Pause, Settings, MainMenu, Defeat, Victory }
    const int SlotCount = PlayerInventory.Capacity;
    const int QuickCount = PlayerInventory.QuickCapacity;
    const int TotalSlots = SlotCount + QuickCount;
    public static PlayerUI Instance { get; private set; }
    static int blockedThroughFrame;
    static bool skipMainMenuOnce;
    // The inventory no longer pauses time, but the player still stands still while it is open.
    public static bool GameplayBlocked => Instance != null && (Instance.CurrentScreen != Screen.Gameplay || Time.frameCount <= blockedThroughFrame);
    public Screen CurrentScreen { get; private set; }
    public PlayerInventory Inventory { get; private set; }
    [SerializeField] bool startAtMainMenu;
    GameObject inventoryWindow, menuWindow, healthRoot, manaRoot;
    RectTransform detail;
    TMP_Text detailText;
    Image hpFill, heart, dragGhost;
    Canvas canvas;
    InventorySlotView[] views;
    GameObject[] quickObjects;
    InputAction[] quickActions;
    Button[] menuButtons;
    TMP_Text menuTitle;
    Damageable health;
    Screen settingsReturn;
    int hovered = -1, selected = -1, pressed = -1, lastClickSlot = -1;
    float lastClickTime = float.NegativeInfinity;
    const float DoubleClickSeconds = .35f;
    Vector2 pressPosition;
    bool dragging, reloading, runStarted, levelComplete;
    bool? fullscreenTarget;
    float previousTimeScale = 1, detailTop, flash, detailScroll, detailContentHeight;
    readonly List<RaycastResult> hits = new();
    static readonly Color Cream = new(1, .91f, .79f);
    static readonly Color Depleted = new(.55f, .55f, .55f);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; blockedThroughFrame = -1; skipMainMenuOnce = false; }
    static bool Pauses(Screen screen) => screen != Screen.Gameplay && screen != Screen.Inventory;
    Transform Find(string n) => GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
    void Awake()
    {
        Instance = this;
        canvas = GetComponent<Canvas>();
        var player = FindFirstObjectByType<PlayerHealth>();
        if (player != null) { health = player.GetComponent<Damageable>(); Inventory = player.GetComponent<PlayerInventory>(); }
        inventoryWindow = Find("inventory UI")?.gameObject; menuWindow = Find("main menu")?.gameObject;
        detail = Find("main panel 9-slice") as RectTransform;
        detailText = detail != null ? detail.GetComponentInChildren<TMP_Text>(true) : null;
        healthRoot = Find("health UI")?.gameObject; manaRoot = Find("stamina frame")?.gameObject;
        hpFill = Find("health")?.GetComponent<Image>(); heart = Find("heart")?.GetComponent<Image>();
        dragGhost = Find("Drag Ghost")?.GetComponent<Image>();
        // Only direct children of the group are slots, so icons and labels inside a slot are never counted as slots.
        var group = Find("inventory slots");
        var slotImages = group == null ? new Image[0] : ReadingOrder(group.Cast<Transform>().Select(t => t.GetComponent<Image>()).Where(i => i != null));
        var quickImages = transform.Cast<Transform>().Where(t => t.name.StartsWith("inventory slot bordered")).Select(t => t.GetComponent<Image>())
            .Where(i => i != null).OrderBy(i => i.rectTransform.anchoredPosition.x).ToArray();
        if (health == null || Inventory == null || inventoryWindow == null || menuWindow == null || detailText == null || hpFill == null || slotImages.Length != SlotCount || quickImages.Length != QuickCount)
        { Debug.LogError($"PlayerUI needs PlayerInventory, HP, {SlotCount} inventory slots, {QuickCount} quick slots, description and menu in the saved scene.", this); Instance = null; enabled = false; return; }
        views = slotImages.Concat(quickImages).Select(BindView).ToArray();
        quickObjects = quickImages.Select(i => i.gameObject).ToArray();
        detailTop = detail.anchoredPosition.y + detail.rect.height * .5f;
        menuButtons = new[] { "play", "options", "quit" }.Select(n => menuWindow.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == n)).ToArray();
        if (menuButtons.Any(b => b == null)) { Debug.LogError("Menu requires play, options and quit buttons.", this); Instance = null; enabled = false; return; }
        menuTitle = menuWindow.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.transform.parent.name == "paused banner");
        for (int i = 0; i < 3; i++) { int action = i; menuButtons[i].onClick.AddListener(() => MenuAction(action)); }
        foreach (var button in inventoryWindow.GetComponentsInChildren<Button>(true))
        {
            // The old Use and Close buttons are hidden: potions are used by double-click or quick slots, the X closes.
            if (button.name == "button 1" || button.name == "button 2") button.gameObject.SetActive(false);
            else button.onClick.AddListener(() => SetScreen(Screen.Gameplay));
        }
        foreach (var button in GetComponentsInChildren<Button>(true)) button.navigation = new Navigation { mode = Navigation.Mode.None };
        quickActions = new InputAction[QuickCount];
        for (int i = 0; i < QuickCount; i++) quickActions[i] = new InputAction("Quick Slot " + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
        if (dragGhost != null) { dragGhost.raycastTarget = false; dragGhost.preserveAspect = true; dragGhost.enabled = false; }
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat("UI.MasterVolume", 1));
    }
    // Bag slots in reading order: rows top to bottom, left to right inside a row. Slots of one row may sit a few
    // pixels apart vertically, so a row is everything within half a slot height of its highest slot.
    static Image[] ReadingOrder(IEnumerable<Image> images)
    {
        var rows = new List<List<Image>>();
        foreach (var image in images.OrderByDescending(i => i.rectTransform.localPosition.y))
        {
            var row = rows.Count > 0 ? rows[rows.Count - 1] : null;
            float tolerance = Mathf.Max(10f, image.rectTransform.rect.height * .5f);
            if (row == null || row[0].rectTransform.localPosition.y - image.rectTransform.localPosition.y > tolerance)
                rows.Add(row = new List<Image>());
            row.Add(image);
        }
        return rows.SelectMany(r => r.OrderBy(i => i.rectTransform.localPosition.x)).ToArray();
    }
    static InventorySlotView BindView(Image image)
    {
        var view = image.GetComponent<InventorySlotView>();
        if (view == null) view = image.gameObject.AddComponent<InventorySlotView>();
        view.Bind();
        return view;
    }
    void OnEnable() { if (quickActions != null) foreach (var action in quickActions) action.Enable(); }
    void OnDisable() { if (quickActions != null) foreach (var action in quickActions) action.Disable(); }
    void Start()
    {
        if (!enabled) return;
        health.HealthChanged += RefreshHealth; health.Damaged += Damaged; health.Died += Died; Inventory.Changed += RefreshInventory;
        bool showMenu = startAtMainMenu && !skipMainMenuOnce;
        skipMainMenuOnce = false;
        RefreshHealth(); SetScreen(showMenu ? Screen.MainMenu : Screen.Gameplay);
    }
    void Update()
    {
        UpdatePointer(); var k = Keyboard.current;
        if (k != null)
        {
            if (k.escapeKey.wasPressedThisFrame) Back();
            else if (k.tabKey.wasPressedThisFrame) { if (CurrentScreen == Screen.Gameplay) SetScreen(Screen.Inventory); else if (CurrentScreen == Screen.Inventory) SetScreen(Screen.Gameplay); }
        }
        for (int i = 0; i < QuickCount; i++) if (quickActions[i].WasPressedThisFrame()) QuickKey(i);
        UpdateCooldowns(); UpdateDragGhost();
        flash = Mathf.Max(0, flash - Time.unscaledDeltaTime * 3);
        if (heart != null) heart.color = Color.Lerp(Color.white, new Color(1, .45f, .5f), flash);
    }
    // In the inventory: 1-4 over an item assigns it; anywhere else it uses the quick slot.
    void QuickKey(int index)
    {
        if (CurrentScreen == Screen.Inventory)
        {
            var item = ItemAt(hovered);
            if (item != null) { Feedback(SlotCount + index, Inventory.Assign(index, item)); return; }
            UseQuick(index);
        }
        else if (!GameplayBlocked) UseQuick(index);
    }
    void UseQuick(int index)
    {
        var item = Inventory.Quick(index);
        if (item != null) Feedback(SlotCount + index, Inventory.Use(item, health));
    }
    // Double-click in the inventory: uses the item in that bag or quick slot; keys and offerings refuse (Deny).
    void UseSlot(int slot)
    {
        var item = ItemAt(slot);
        if (item != null) Feedback(slot, Inventory.Use(item, health));
    }
    void Feedback(int slot, bool success) { if (success) views[slot].Pulse(); else views[slot].Deny(); }
    ItemDefinition ItemAt(int i) => i < 0 ? null : i < SlotCount ? Inventory.Get(i)?.item : Inventory.Quick(i - SlotCount);
    void UpdatePointer()
    {
        var mouse = Mouse.current; if (mouse == null || EventSystem.current == null) return;
        int target = -1; hits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() }, hits);
        if (hits.Count > 0)
        {
            var hit = hits[0].gameObject.transform;
            for (int i = 0; i < TotalSlots; i++) { var slot = views[i].transform; if (hit == slot || hit.IsChildOf(slot)) { target = i; break; } }
        }
        if (hovered != target)
        {
            hovered = target;
            if (CurrentScreen == Screen.Inventory)
            {
                var item = ItemAt(target);
                if (item == null) item = ItemAt(selected);
                if (item != null) ShowItem(item);
            }
            RefreshInventory();
        }
        if (CurrentScreen != Screen.Gameplay && CurrentScreen != Screen.Inventory) return;
        if (mouse.leftButton.wasPressedThisFrame) { pressed = target; pressPosition = mouse.position.ReadValue(); dragging = false; }
        if (pressed >= 0 && !dragging && mouse.leftButton.isPressed && Vector2.Distance(pressPosition, mouse.position.ReadValue()) > 8) { dragging = true; RefreshInventory(); }
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (CurrentScreen == Screen.Inventory)
            {
                var item = ItemAt(pressed);
                if (dragging && pressed >= 0 && item != null)
                {
                    if (target >= SlotCount) Feedback(target, Inventory.Assign(target - SlotCount, item));
                    else if (target >= 0 && pressed < SlotCount) Inventory.Move(pressed, target);
                    // Dragging a quick slot out onto nothing clears it.
                    else if (target < 0 && pressed >= SlotCount) Inventory.Assign(pressed - SlotCount, null);
                }
                else if (target >= 0 && target == pressed)
                {
                    // A second click on the same slot soon after the first uses the item (elixirs, potions, food).
                    bool doubleClick = target == lastClickSlot && Time.unscaledTime - lastClickTime <= DoubleClickSeconds;
                    lastClickSlot = doubleClick ? -1 : target; lastClickTime = Time.unscaledTime;
                    selected = target;
                    if (doubleClick) UseSlot(target);
                    ShowItem(ItemAt(target));
                }
            }
            else if (!GameplayBlocked && !dragging && target >= SlotCount && target == pressed) UseQuick(target - SlotCount);
            pressed = -1; dragging = false; RefreshInventory();
        }
        if (CurrentScreen == Screen.Inventory && target >= SlotCount && mouse.rightButton.wasPressedThisFrame) Inventory.Assign(target - SlotCount, null);
        if (CurrentScreen == Screen.Inventory && mouse.scroll.ReadValue().y != 0 &&
            RectTransformUtility.RectangleContainsScreenPoint(detail, mouse.position.ReadValue()))
            ScrollDetails(-Mathf.Sign(mouse.scroll.ReadValue().y) * 48);
    }
    void UpdateCooldowns()
    {
        float fraction = Inventory.CooldownFraction;
        for (int i = 0; i < QuickCount; i++) views[SlotCount + i].SetCooldown(Inventory.Quick(i) != null ? fraction : 0);
    }
    void UpdateDragGhost()
    {
        if (dragGhost == null) return;
        var item = dragging && CurrentScreen == Screen.Inventory ? ItemAt(pressed) : null;
        bool visible = item != null && item.icon != null && Mouse.current != null;
        if (dragGhost.enabled != visible) dragGhost.enabled = visible;
        if (!visible) return;
        dragGhost.sprite = item.icon;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)transform, Mouse.current.position.ReadValue(), cam, out var world))
            dragGhost.rectTransform.position = world;
    }
    public void SetScreen(Screen screen)
    {
        if (health.IsDead && screen == Screen.Gameplay) screen = Screen.Defeat;
        if (levelComplete && screen == Screen.Gameplay) screen = Screen.Victory;
        if (Pauses(screen)) { if (!Pauses(CurrentScreen)) previousTimeScale = Time.timeScale; Time.timeScale = 0; }
        else if (Pauses(CurrentScreen)) Time.timeScale = previousTimeScale;
        if (!Pauses(screen)) runStarted = true;
        CurrentScreen = screen; blockedThroughFrame = Time.frameCount + 1; hovered = pressed = selected = -1; dragging = false;
        inventoryWindow.SetActive(screen == Screen.Inventory); detail.gameObject.SetActive(false);
        menuWindow.SetActive(Pauses(screen));
        bool hud = !Pauses(screen);
        if (healthRoot != null) healthRoot.SetActive(hud); if (manaRoot != null) manaRoot.SetActive(hud);
        foreach (var quickObject in quickObjects) quickObject.SetActive(hud);
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
            case Screen.Settings:
                bool fullscreen = fullscreenTarget ?? UnityEngine.Screen.fullScreen;
                title = "Settings"; labels = new[] { "Volume: " + Mathf.RoundToInt(AudioListener.volume * 100) + "%", fullscreen ? "Windowed" : "Fullscreen", "Back" }; break;
            case Screen.Pause: title = "Paused"; labels = new[] { "Resume", "Settings", "Main Menu" }; break;
            case Screen.Defeat: title = "Defeated"; labels = new[] { "Retry", "Main Menu", "Quit" }; break;
            case Screen.Victory: title = "Victory"; labels = new[] { "Retry", "Main Menu", "Quit" }; break;
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
            // Screen.fullScreen applies at the end of the frame, so the label uses the requested value.
            else if (action == 1) { fullscreenTarget = !(fullscreenTarget ?? UnityEngine.Screen.fullScreen); UnityEngine.Screen.fullScreen = fullscreenTarget.Value; }
            else SetScreen(settingsReturn);
            RefreshMenu(); return;
        }
        if (action == 0)
        {
            if (health.IsDead || levelComplete) Restart();
            // Play from the main menu starts a new run once a run is in progress; Resume in the pause menu continues it.
            else if (CurrentScreen == Screen.MainMenu && runStarted) Restart();
            else SetScreen(Screen.Gameplay);
        }
        else if (action == 1) { if (CurrentScreen == Screen.Defeat || CurrentScreen == Screen.Victory) SetScreen(Screen.MainMenu); else { settingsReturn = CurrentScreen; SetScreen(Screen.Settings); } }
        else if (CurrentScreen == Screen.Pause) SetScreen(Screen.MainMenu); else Quit();
    }
    void RefreshHealth() => hpFill.fillAmount = Mathf.Clamp01((float)health.CurrentHealth / health.MaxHealth);
    void Damaged(Vector2 source) => flash = 1;
    void Died() { RefreshHealth(); SetScreen(Screen.Defeat); }
    // The level exit calls this: the run is over, the menu offers Retry, Main Menu and Quit.
    public void ShowVictory() { levelComplete = true; SetScreen(Screen.Victory); }
    public void RefreshInventory()
    {
        if (views == null) return;
        for (int i = 0; i < TotalSlots; i++)
        {
            bool quickSlot = i >= SlotCount; var item = ItemAt(i);
            int count = quickSlot ? Inventory.Count(item) : Inventory.Get(i)?.count ?? 0;
            bool depleted = item != null && count == 0;
            var frame = depleted ? Depleted : i == selected || i == hovered ? Cream : item != null ? ItemDefinition.SlotTint(item.rarity) : Color.white;
            bool showCount = quickSlot || (item != null && item.maxStack > 1);
            views[i].Show(item, count, depleted, frame, quickSlot ? (i - SlotCount + 1).ToString() : null, showCount, dragging && i == pressed);
        }
    }
    void ShowItem(ItemDefinition item)
    {
        detail.gameObject.SetActive(true);
        detailText.text = item == null
            ? "<size=32>Item Details</size>\n\nHover over an item to inspect it.\n\n<color=#9A8F84>Double-click an item to use it.\nPress 1-4 over a potion to put it in a quick slot.\nPress 1-4 anywhere else to use that quick slot.</color>"
            : Describe(item);
        detailContentHeight = detailText.GetPreferredValues(detailText.text, detailText.rectTransform.rect.width, 0).y;
        float height = Mathf.Clamp(detailContentHeight + 64, 240, 620);
        detail.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        detail.anchoredPosition = new Vector2(detail.anchoredPosition.x, detailTop - height * .5f);
        detailContentHeight = Mathf.Max(detailContentHeight, height - 64);
        detailText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, detailContentHeight);
        detailText.overflowMode = TextOverflowModes.Overflow;
        detailScroll = 0; ScrollDetails(0); detailText.ForceMeshUpdate();
    }
    static string Describe(ItemDefinition item)
    {
        string name = "#" + ColorUtility.ToHtmlStringRGB(ItemDefinition.RarityColor(item.rarity));
        string text = "<size=32><color=" + name + ">" + ShortDashes(item.displayName) + "</color></size>\n<size=20><color=#9A8F84>" + item.rarity + " " + item.category + "</color></size>";
        if (!string.IsNullOrWhiteSpace(item.description)) text += "\n\n" + ShortDashes(item.description);
        string effect = item.EffectText;
        if (item.maxStack > 1) effect += (effect.Length > 0 ? "\n" : "") + "Stacks up to " + item.maxStack;
        if (effect.Length > 0) text += "\n\n<color=#FFE3AD>" + effect + "</color>";
        if (item.CanQuickSlot) text += "\n\n<color=#9A8F84>Double-click - use\n1-4 while hovering - assign quick slot</color>";
        return text;
    }
    // Item texts always use the short dash, even if a long one was typed in the asset.
    static string ShortDashes(string value) => value == null ? "" : value.Replace('\u2014', '-').Replace('\u2013', '-');
    public void ScrollDetails(float amount)
    {
        detailScroll = Mathf.Clamp(detailScroll + amount, 0, Mathf.Max(0, detailContentHeight - (detail.rect.height - 64)));
        var rect = detailText.rectTransform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, (detail.rect.height - detailContentHeight) * .5f - 32 + detailScroll);
    }
    // Retry and "Play" from the main menu both restart the level straight into gameplay.
    public void Restart() { reloading = true; skipMainMenuOnce = true; Time.timeScale = 1; SceneManager.LoadScene(SceneManager.GetActiveScene().path); }
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
        if (quickActions != null) foreach (var action in quickActions) action.Dispose();
        if (Instance == this) { Instance = null; if (reloading) Time.timeScale = 1; else if (Pauses(CurrentScreen)) Time.timeScale = previousTimeScale; }
    }
}
