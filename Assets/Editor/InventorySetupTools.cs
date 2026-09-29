using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UI;

// One-time setup for the inventory: icon sheet import, starter item assets and slot visuals.
// Every step is safe to run again. Save the scene after step 3.
public static class InventorySetupTools
{
    const string SheetFolder = "Assets/icons";
    const string SheetName = "items_16";
    const string ItemsFolder = "Assets/Items";
    const int Cell = 16;
    // The 9-slice panel in the bottom-right corner of the sheet, in texture pixels (origin bottom-left).
    static readonly RectInt Panel = new RectInt(208, 16, 32, 32);

    struct Spec
    {
        public string id, name, description, iconPath;
        public ItemCategory category;
        public ItemRarity rarity;
        public int healing, maxStack, row, column;
        public Spec(string id, string name, ItemCategory category, ItemRarity rarity, int healing, int maxStack, int row, int column, string description)
        {
            this.id = id; this.name = name; this.category = category; this.rarity = rarity;
            this.healing = healing; this.maxStack = maxStack; this.row = row; this.column = column; this.description = description;
            iconPath = null;
        }
    }

    // row/column count from the top-left cell of the sheet; iconPath overrides the sheet with a separate sprite.
    static readonly Spec[] Starter =
    {
        new Spec("potion_heal_common", "Healing Draught", ItemCategory.Consumable, ItemRarity.Common, 25, 5, 6, 2,
            "Bitter, thick, and tastes of iron. It closes wounds faster than you'd like to know."),
        new Spec("potion_heal_rare", "Warden's Tonic", ItemCategory.Consumable, ItemRarity.Rare, 45, 3, 6, 3,
            "Brewed for the castle guard. None of them lived long enough to need a second."),
        new Spec("potion_heal_epic", "Alchemist's Elixir", ItemCategory.Consumable, ItemRarity.Epic, 80, 1, 6, 4,
            "The last work of a master sealed in the tower. Light still flickers at the bottom."),
        new Spec("food_apple", "Withered Apple", ItemCategory.Consumable, ItemRarity.Common, 10, 20, 2, 6,
            "From a garden no one has tended in years. Still sweet, which is worrying."),
        new Spec("key_gate", "Gate Key", ItemCategory.Key, ItemRarity.Common, 0, 1, 4, 10,
            "Heavy and cold. The crest on the back belongs to a house that no longer exists.")
            { iconPath = "Assets/Gothic Castle Tileset/The Crypt/Props/Individual_Props/golden_key_01.png" },
    };

    // item id, count, quick slot (0 = bag only)
    static readonly (string id, int count, int quickSlot)[] DebugKit =
    {
        ("potion_heal_common", 5, 1), ("potion_heal_rare", 3, 2), ("potion_heal_epic", 1, 3), ("food_apple", 25, 0), ("key_gate", 1, 0),
    };

    [MenuItem("Tools/Inventory/Run Full Setup")]
    public static void RunAll()
    {
        if (!SliceIconSheet()) return;
        CreateStarterItems();
        SetupSlotsInOpenScene();
    }

    [MenuItem("Tools/Inventory/1. Slice Icon Sheet")]
    public static void SliceIconSheetMenu() => SliceIconSheet();

    static string FindSheet()
    {
        string target = SheetFolder + "/" + SheetName + ".png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(target) != null) return target;
        return AssetDatabase.FindAssets("t:Texture2D", new[] { SheetFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => Path.GetFileName(p).StartsWith("Untitled_"));
    }

    public static bool SliceIconSheet()
    {
        string path = FindSheet();
        if (path == null) { Debug.LogError("Icon sheet not found in " + SheetFolder + "."); return false; }
        if (Path.GetFileNameWithoutExtension(path) != SheetName)
        {
            // RenameAsset keeps the GUID, so nothing that references the texture breaks.
            string error = AssetDatabase.RenameAsset(path, SheetName);
            if (!string.IsNullOrEmpty(error)) { Debug.LogError(error); return false; }
            path = SheetFolder + "/" + SheetName + ".png";
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Cell;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;

        var pixels = new Texture2D(2, 2);
        pixels.LoadImage(File.ReadAllBytes(path));
        var colors = pixels.GetPixels32();
        int width = pixels.width, height = pixels.height;
        Object.DestroyImmediate(pixels);

        var rects = new List<SpriteRect>();
        for (int row = 0; row < height / Cell; row++)
            for (int column = 0; column < width / Cell; column++)
            {
                var cell = new RectInt(column * Cell, height - (row + 1) * Cell, Cell, Cell);
                if (Panel.Overlaps(cell) || IsEmpty(colors, width, cell)) continue;
                rects.Add(NewRect($"{SheetName}_r{row:00}_c{column:00}", cell, Vector4.zero));
            }
        if (Panel.xMax <= width && Panel.yMax <= height && !IsEmpty(colors, width, Panel))
            rects.Add(NewRect(SheetName + "_panel", Panel, new Vector4(6, 6, 6, 6)));

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        provider.SetSpriteRects(rects.ToArray());
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (names != null) names.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
        provider.Apply();
        importer.SaveAndReimport();
        Debug.Log($"Sliced {path} into {rects.Count} sprites ({Cell}x{Cell} grid + panel).");
        return true;
    }

    static bool IsEmpty(Color32[] colors, int width, RectInt rect)
    {
        for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
                if (colors[y * width + x].a > 0) return false;
        return true;
    }

    static SpriteRect NewRect(string name, RectInt rect, Vector4 border) => new SpriteRect
    {
        name = name,
        rect = new Rect(rect.x, rect.y, rect.width, rect.height),
        alignment = SpriteAlignment.Center,
        pivot = new Vector2(.5f, .5f),
        border = border,
        spriteID = GUID.Generate(),
    };

    [MenuItem("Tools/Inventory/2. Create Starter Items")]
    public static void CreateStarterItems()
    {
        string sheet = FindSheet();
        var texture = sheet != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(sheet) : null;
        var sprites = sheet != null ? AssetDatabase.LoadAllAssetsAtPath(sheet).OfType<Sprite>().ToArray() : new Sprite[0];
        if (!AssetDatabase.IsValidFolder(ItemsFolder)) AssetDatabase.CreateFolder("Assets", "Items");
        int created = 0;
        foreach (var spec in Starter)
        {
            string path = ItemsFolder + "/" + spec.id + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool isNew = item == null;
            if (isNew)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.id = spec.id; item.displayName = spec.name; item.description = spec.description;
                item.category = spec.category; item.rarity = spec.rarity;
                item.healing = spec.healing; item.maxStack = spec.maxStack; item.useCooldown = 0.5f;
            }
            // Existing assets keep your edits; only a missing icon is filled in.
            if (item.icon == null && !string.IsNullOrEmpty(spec.iconPath))
                item.icon = AssetDatabase.LoadAllAssetsAtPath(spec.iconPath).OfType<Sprite>().FirstOrDefault();
            if (item.icon == null && texture != null)
            {
                int x = spec.column * Cell, y = texture.height - (spec.row + 1) * Cell;
                item.icon = sprites.FirstOrDefault(s => Mathf.RoundToInt(s.rect.x) == x && Mathf.RoundToInt(s.rect.y) == y);
                if (item.icon == null) Debug.LogWarning($"No {Cell}x{Cell} sprite at row {spec.row}, column {spec.column} for {spec.id}. Run '1. Slice Icon Sheet' first.");
            }
            if (isNew) { AssetDatabase.CreateAsset(item, path); created++; }
            else EditorUtility.SetDirty(item);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Starter items ready in {ItemsFolder} ({created} created).");
    }

    [MenuItem("Tools/Inventory/3. Setup Slots In Open Scene")]
    public static void SetupSlotsInOpenScene()
    {
        var ui = Object.FindFirstObjectByType<PlayerUI>(FindObjectsInactive.Include);
        if (ui == null) { Debug.LogError("Open the level scene with the PlayerUI canvas first."); return; }
        var root = ui.transform;
        var group = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "inventory slots");
        var bagSlots = group == null ? new Transform[0] : group.Cast<Transform>().Where(t => t.GetComponent<Image>() != null).ToArray();
        var quickSlots = root.Cast<Transform>().Where(t => t.name.StartsWith("inventory slot bordered") && t.GetComponent<Image>() != null).ToArray();
        if (bagSlots.Length != PlayerInventory.Capacity || quickSlots.Length != PlayerInventory.QuickCapacity)
            Debug.LogWarning($"Expected {PlayerInventory.Capacity} bag slots and {PlayerInventory.QuickCapacity} quick slots, found {bagSlots.Length} and {quickSlots.Length}.");

        var font = FindFont(quickSlots);
        foreach (var slot in bagSlots) SetupSlot(slot, 96f, false, font);
        foreach (var slot in quickSlots) SetupSlot(slot, 64f, true, font);

        var ghostTransform = root.Find("Drag Ghost");
        Image ghost;
        if (ghostTransform == null)
        {
            ghost = CreateChild<Image>(root, "Drag Ghost");
            ghost.rectTransform.sizeDelta = new Vector2(64f, 64f);
        }
        else ghost = ghostTransform.GetComponent<Image>();
        if (ghost != null)
        {
            Undo.RecordObject(ghost, "Setup Drag Ghost");
            ghost.raycastTarget = false; ghost.preserveAspect = true; ghost.enabled = false;
            ghost.color = new Color(1f, 1f, 1f, .85f);
            ghost.transform.SetAsLastSibling();
        }

        SetupDebugKit();
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Debug.Log($"Inventory slots set up ({bagSlots.Length} bag, {quickSlots.Length} quick). Save the scene to keep the changes.");
    }

    static TMP_FontAsset FindFont(Transform[] quickSlots)
    {
        foreach (var slot in quickSlots)
            foreach (var text in slot.GetComponentsInChildren<TMP_Text>(true))
                if (text.font != null) return text.font;
        return TMP_Settings.defaultFontAsset;
    }

    static void SetupSlot(Transform slot, float iconSize, bool quickSlot, TMP_FontAsset font)
    {
        var view = slot.GetComponent<InventorySlotView>();
        if (view == null) view = Undo.AddComponent<InventorySlotView>(slot.gameObject);
        Undo.RecordObject(view, "Setup Inventory Slot");
        view.frame = slot.GetComponent<Image>();

        var icon = EnsureImage(slot, "Icon", iconSize);
        icon.sprite = null; icon.enabled = false; icon.color = Color.white;
        icon.preserveAspect = true; icon.raycastTarget = false;
        view.icon = icon;

        if (quickSlot)
        {
            var cooldown = EnsureImage(slot, "Cooldown", iconSize);
            cooldown.sprite = null; cooldown.enabled = false;
            cooldown.type = Image.Type.Filled; cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)Image.Origin360.Top; cooldown.fillClockwise = false; cooldown.fillAmount = 1f;
            cooldown.color = new Color(0f, 0f, 0f, .65f); cooldown.preserveAspect = true; cooldown.raycastTarget = false;
            view.cooldown = cooldown;
        }

        view.count = EnsureText(slot, "Count", font, quickSlot ? 24f : 28f, TextAlignmentOptions.BottomRight);
        if (quickSlot) view.key = EnsureText(slot, "Key", font, 24f, TextAlignmentOptions.TopLeft);

        // Draw order inside the slot: icon, cooldown, count, key.
        icon.transform.SetAsLastSibling();
        if (view.cooldown != null) view.cooldown.transform.SetAsLastSibling();
        if (view.count != null) view.count.transform.SetAsLastSibling();
        if (view.key != null) view.key.transform.SetAsLastSibling();
        EditorUtility.SetDirty(view);
    }

    static T CreateChild<T>(Transform parent, string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go.GetComponent<T>();
    }

    static Image EnsureImage(Transform slot, string name, float size)
    {
        var child = slot.Find(name);
        Image image;
        if (child == null) image = CreateChild<Image>(slot, name);
        else
        {
            image = child.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(child.gameObject);
            Undo.RecordObject(image, "Setup " + name);
        }
        var rect = image.rectTransform;
        Undo.RecordObject(rect, "Setup " + name);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
        rect.localScale = Vector3.one;
        return image;
    }

    static TMP_Text EnsureText(Transform slot, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
    {
        var child = slot.Find(name);
        TMP_Text text;
        if (child == null)
        {
            text = CreateChild<TextMeshProUGUI>(slot, name);
            text.text = "";
            if (font != null) text.font = font;
        }
        else
        {
            text = child.GetComponent<TMP_Text>();
            if (text == null) { Debug.LogWarning($"{slot.name}/{name} has no TextMeshPro component.", child); return null; }
            Undo.RecordObject(text, "Setup " + name);
        }
        var rect = text.rectTransform;
        Undo.RecordObject(rect, "Setup " + name);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = new Vector2(8f, 4f); rect.offsetMax = new Vector2(-8f, -4f);
        rect.localScale = Vector3.one;
        text.fontSize = size; text.alignment = alignment; text.raycastTarget = false;
        return text;
    }

    static void SetupDebugKit()
    {
        var inventory = Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
        if (inventory == null) { Debug.LogWarning("No PlayerInventory in the open scene, debug kit skipped."); return; }
        var kit = inventory.GetComponent<PlayerDebugInventory>();
        if (kit == null) kit = Undo.AddComponent<PlayerDebugInventory>(inventory.gameObject);
        var serialized = new SerializedObject(kit);
        var entries = serialized.FindProperty("kit");
        if (entries.arraySize > 0) return; // keep a kit you already edited
        var items = DebugKit.Select(e => (item: AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemsFolder + "/" + e.id + ".asset"), e.count, e.quickSlot))
            .Where(e => e.item != null).ToArray();
        if (items.Length == 0) { Debug.LogWarning("Starter items not found. Run '2. Create Starter Items', then this step again."); return; }
        entries.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("item").objectReferenceValue = items[i].item;
            entry.FindPropertyRelative("count").intValue = items[i].count;
            entry.FindPropertyRelative("quickSlot").intValue = items[i].quickSlot;
        }
        serialized.ApplyModifiedProperties();
    }
}
