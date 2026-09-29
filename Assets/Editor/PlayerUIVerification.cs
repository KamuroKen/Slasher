using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PlayerUIVerification
{
    const string Report = "Library/PlayerUIVerification.txt";
    static PlayerUIVerification() => EditorApplication.playModeStateChanged += StateChanged;
    [MenuItem("Tools/UI/Verify Saved UI")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) { Debug.LogWarning("Save scene before UI verification."); return; }
        File.WriteAllText(Report, "START\n"); SessionState.SetBool("PlayerUIVerification", true); EditorApplication.isPlaying = true;
    }
    static void Check(bool value, string label)
    {
        File.AppendAllText(Report, (value ? "PASS " : "FAIL ") + label + "\n");
        if (!value) throw new Exception(label);
    }
    static async void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("PlayerUIVerification", false)) return;
        SessionState.EraseBool("PlayerUIVerification"); Keyboard keyboard = null;
        // The virtual keyboard must reach the game even if the Game view is not focused.
        var previousInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        ItemDefinition item = null, key = null; Sprite icon = null;
        try
        {
            await Task.Delay(300);
            var ui = PlayerUI.Instance; Check(ui != null && ui.enabled, "Controller binds manually saved hierarchy");
            var inv = ui.Inventory; var health = UnityEngine.Object.FindFirstObjectByType<PlayerHealth>().GetComponent<Damageable>();
            var canvas = ui.GetComponent<Canvas>();
            int ObjectCount() => canvas.GetComponentsInChildren<Transform>(true).Count(t => !t.name.StartsWith("TMP SubMesh"));
            int before = ObjectCount();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab)); await Task.Delay(120);
            Check(ui.CurrentScreen == PlayerUI.Screen.Inventory && Time.timeScale == 1, "Tab opens inventory without pausing the game");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Task.Delay(80);
            var p = health.transform.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Space)); await Task.Delay(150);
            Check(health.transform.position == p && PlayerUI.GameplayBlocked, "Player stands still with inventory open");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Task.Delay(80);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); await Task.Delay(100);
            Check(ui.CurrentScreen == PlayerUI.Screen.Gameplay && Time.timeScale == 1, "Escape closes inventory");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Task.Delay(80);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); await Task.Delay(100);
            Check(ui.CurrentScreen == PlayerUI.Screen.Pause && Time.timeScale == 0, "Escape opens pause menu");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Task.Delay(80);
            inv.Clear(); // the debug kit fills the inventory on start
            icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
            item = ScriptableObject.CreateInstance<ItemDefinition>(); item.displayName = "Test Potion"; item.maxStack = 5; item.healing = 25; item.useCooldown = 0; item.icon = icon;
            key = ScriptableObject.CreateInstance<ItemDefinition>(); key.displayName = "Test Key"; key.category = ItemCategory.Key;
            Check(inv.Add(item, 7) == 0 && inv.Get(0).count == 5 && inv.Get(1).count == 2, "Stack splitting");
            var bagView = ui.SlotView(0);
            Check(bagView != null && bagView.icon != null && bagView.icon.enabled && bagView.icon.sprite == icon, "Bag slot shows the item icon");
            inv.Move(1, 2); Check(inv.Get(1)?.item == null && inv.Get(2).count == 2, "Move to empty slot");
            Check(inv.Assign(0, item) && inv.Quick(0) == item, "Quick slot assignment");
            Check(inv.Assign(1, item) && inv.Quick(0) == null && inv.Quick(1) == item, "Reassigning moves the item instead of duplicating it");
            inv.Assign(0, item);
            var quickView = ui.SlotView(PlayerInventory.Capacity);
            Check(quickView != null && quickView.icon != null && quickView.icon.sprite == icon, "Quick slot shows the item icon");
            Check(inv.Add(key, 1) == 0 && !inv.Assign(2, key) && inv.Quick(2) == null, "Keys cannot go into quick slots");
            Check(inv.Remove(key, 1) == 1 && inv.Count(key) == 0, "Removing a key consumes it");
            Check(!inv.Use(item, health) && inv.Count(item) == 7, "Full HP does not consume item");
            health.TryTakeDamage(40, Vector2.zero);
            Check(inv.Use(item, health) && health.CurrentHealth == 85 && inv.Count(item) == 6, "Healing consumes one item and updates health");
            ui.SetScreen(PlayerUI.Screen.Gameplay); await Task.Delay(100);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit1)); await Task.Delay(100);
            Check(health.CurrentHealth == 100 && inv.Count(item) == 5, "Key 1 uses assigned consumable");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); await Task.Delay(600); // wait out invulnerability
            item.useCooldown = 5; health.TryTakeDamage(40, Vector2.zero);
            Check(inv.Use(item, health) && !inv.Use(item, health) && inv.Count(item) == 4 && inv.CooldownFraction > 0, "Shared cooldown blocks a second potion");
            int stored = inv.Count(item);
            Check(inv.Add(item, 100) == stored && inv.Count(item) == 100, "Capacity retains overflow");
            Check(ObjectCount() == before, "Window transitions create no UI objects");
            Check(!health.GetComponent<PlayerHealthDisplay>().enabled, "Legacy HUD disabled");
            File.AppendAllText(Report, "COMPLETE\n");
        }
        catch (Exception e) { File.AppendAllText(Report, "ERROR " + e + "\n"); Debug.LogException(e); }
        finally
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode = previousInputBehavior;
            if (item != null) UnityEngine.Object.Destroy(item);
            if (key != null) UnityEngine.Object.Destroy(key);
            if (icon != null) UnityEngine.Object.Destroy(icon);
            EditorApplication.isPlaying = false;
        }
    }
}
