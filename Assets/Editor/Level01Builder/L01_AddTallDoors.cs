// Level 01 doors (editor-only, no gameplay code).
// Front doors = 3x3 tiles (top row = wall top that closes the passage, 2 rows of door), 7 frames closed -> open.
// Side doors = 2x3 tiles (door in the first column, the open leaf swings into the second; flip X in the palette
// for the other wall), 6 frames closed -> open.
//   Crypt colours      -> Pal_L01_Props_Crypt   (door_03_front_tall, door_03_left_tall)
//   Grand Hall colours -> Pal_L01_Props_Castle  (grand_hall_door_front, grand_hall_door_side)
// Runs once automatically after Unity compiles this file; Tools > Level 01 > Add doors to palettes re-runs it.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

[InitializeOnLoad]
public static class L01_AddTallDoors
{
    const string CryptSrc = "Assets/Gothic Castle Tileset/The Crypt/Props/Animated_Props";
    const string HallSrc = "Assets/Gothic Castle Tileset/Grand Hall/Props/Animated_Props";
    const string CryptTiles = "Assets/Tiles/Level01/Props/Crypt";
    const string HallTiles = "Assets/Tiles/Level01/Props/Castle";
    const string CryptPal = "Assets/Palettes/Level01/Pal_L01_Props_Crypt.prefab";
    const string HallPal = "Assets/Palettes/Level01/Pal_L01_Props_Castle.prefab";
    const int Version = 4;   // bump to re-run once after the sprites change

    static readonly (string src, string file, int w, int h, int n, string tiles, string pal)[] Sheets =
    {
        (CryptSrc, "door_03_front_tall", 48, 48, 7, CryptTiles, CryptPal),
        (CryptSrc, "door_03_left_tall", 32, 48, 6, CryptTiles, CryptPal),
        (HallSrc, "grand_hall_door_front", 48, 48, 7, HallTiles, HallPal),
        (HallSrc, "grand_hall_door_side", 32, 48, 6, HallTiles, HallPal),
    };

    static L01_AddTallDoors()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetInt("L01_TallDoors_ver", 0) >= Version) return;
            if (Run()) EditorPrefs.SetInt("L01_TallDoors_ver", Version);
        };
    }

    [MenuItem("Tools/Level 01/Add doors to palettes", false, 20)]
    static void RunMenu() { Run(); }

    static bool Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return false;
        var factory = new SpriteDataProviderFactories(); factory.Init();
        var perPalette = new Dictionary<string, List<(TileBase tile, int w)>>();
        foreach (var s in Sheets)
        {
            string path = s.src + "/" + s.file + ".png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[L01] not imported yet: " + path); return false; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.spritePixelsPerUnit = 16;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            var dp = factory.GetSpriteEditorDataProviderFromObject(ti);
            dp.InitSpriteEditorDataProvider();
            var old = dp.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
            var rects = new List<SpriteRect>();
            for (int i = 0; i < s.n; i++)
            {
                string n = s.file + "_" + i;
                rects.Add(new SpriteRect
                {
                    name = n,
                    rect = new Rect(i * s.w, 0, s.w, s.h),
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(8f / s.w, 8f / s.h),      // centre of the bottom-left 16x16 cell
                    spriteID = old.TryGetValue(n, out var id) ? id : GUID.Generate()
                });
            }
            dp.SetSpriteRects(rects.ToArray());
            var nf = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nf != null) nf.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            dp.Apply();
            ti.SaveAndReimport();

            if (!AssetDatabase.IsValidFolder(s.tiles)) { Debug.LogWarning("[L01] missing folder " + s.tiles + " (run Build everything first)"); return false; }
            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().ToDictionary(x => x.name);
            if (!perPalette.TryGetValue(s.pal, out var list)) perPalette[s.pal] = list = new List<(TileBase, int)>();
            for (int i = 0; i < s.n; i++)
            {
                string n = s.file + "_" + i;
                if (!sprites.TryGetValue(n, out var sp)) continue;
                string ap = s.tiles + "/" + n + ".asset";
                var t = AssetDatabase.LoadAssetAtPath<Tile>(ap);
                bool create = t == null;
                if (create) t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sp; t.colliderType = Tile.ColliderType.None; t.flags = TileFlags.LockColor;
                if (create) AssetDatabase.CreateAsset(t, ap); else EditorUtility.SetDirty(t);
                list.Add((t, s.w / 16));
            }
        }
        AssetDatabase.SaveAssets();

        // the right-facing copy is not needed any more (flip the left one in the palette instead)
        var removed = new HashSet<TileBase>();
        for (int i = 0; i < 3; i++)
        {
            var t = AssetDatabase.LoadAssetAtPath<TileBase>(CryptTiles + "/door_03_right_tall_" + i + ".asset");
            if (t != null) removed.Add(t);
        }

        foreach (var kv in perPalette)
        {
            var pal = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Key);
            if (pal == null) { Debug.LogWarning("[L01] palette not found: " + kv.Key); continue; }
            var tm = pal.GetComponentInChildren<Tilemap>();
            var mine = new HashSet<TileBase>(kv.Value.Select(x => x.tile));
            foreach (var p in tm.cellBounds.allPositionsWithin)          // remove old copies first
            {
                var t = tm.GetTile(p);
                if (t != null && (mine.Contains(t) || removed.Contains(t))) tm.SetTile(p, null);
            }
            tm.CompressBounds();
            int y = tm.cellBounds.yMin - 4, x = tm.cellBounds.xMin;
            foreach (var (tile, w) in kv.Value) { tm.SetTile(new Vector3Int(x, y, 0), tile); x += w + 1; }
            tm.CompressBounds();
            EditorUtility.SetDirty(tm);
            PrefabUtility.SavePrefabAsset(pal);
        }

        foreach (var t in removed) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(t));
        if (AssetDatabase.LoadMainAssetAtPath(CryptSrc + "/door_03_right_tall.png") != null)
            AssetDatabase.DeleteAsset(CryptSrc + "/door_03_right_tall.png");
        AssetDatabase.SaveAssets();
        Debug.Log("[L01] Doors added: crypt doors -> Pal_L01_Props_Crypt, Grand Hall doors -> Pal_L01_Props_Castle (bottom rows; frames go closed -> open, left to right).");
        return true;
    }
}
