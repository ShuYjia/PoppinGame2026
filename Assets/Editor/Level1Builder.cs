using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// 第一关白盒生成器（菜单 Poppin → Level1 → Rebuild Level1）。
///
/// 做的事：
/// 1. 把野猪的 Walk / Run / Hit 三张序列帧切成 48×32 的帧（与 Idle-Sheet 同规格：PPU 16、点采样、脚底锚点）；
/// 2. 以 FirstLevel 为模板「另存为」Assets/Scenes/Level1.unity（FirstLevel 本身不改）；
/// 3. 生成预制体到 Assets/Prefabs/Level/（Boar / Star / Heart / Spikes / Checkpoint / Sign / Goal）；
/// 4. 按下面的字符地图铺地形（九宫格自动选边角）、摆放敌人 / 拾取物 / 机关 / 存档点 / 终点；
/// 5. 加上 LevelManager、跟随相机、死亡区、左右边界墙、关卡 HUD 与结算面板；
/// 6. 把 Level1 加进 Build Settings（重开关卡要用）。
///
/// **注意：重跑会覆盖 Level1.unity**。开始在 Level1 里手工摆东西之后，就别再用它重建了——
/// 改字符地图重建只适合白盒阶段快速迭代布局。
/// </summary>
public static class Level1Builder
{
    private const string TemplateScene = "Assets/Scenes/FirstLevel.unity";
    private const string LevelScene = "Assets/Scenes/Level1.unity";
    private const string PrefabFolder = "Assets/Prefabs/Level";
    private const string BoarArtFolder = "Assets/Art2/Enemy/Boar";
    private const string TileSheet = "Assets/mapTile/Tilemap/JapSprites.png";
    private const string TileFolder = "Assets/mapTile/Tile";
    private const string ItemArt = "Assets/20/png/items";
    private const string PropArt = "Assets/20/png/tiles";

    private const int PlayerLayer = 6;
    private const int EnemyLayer = 7;

    // ───────────────────────────── 关卡布局 ─────────────────────────────
    //
    // 每个字符 = 1 格（1 世界单位）。最上面一行 y = MapTop，往下每行 y - 1；最左一列 x = 0。
    //   #  地面（自动选九宫格边角）      .  空
    //   P  玩家出生点                    B  野猪
    //   *  星屑（计分）                  +  爱心（回 30 HP）
    //   ^  尖刺（10 伤害 + 击退）        C  存档点
    //   S  告示牌（按从左到右的顺序用 SignMessages 里的文字）
    //   G  终点（仓库门）
    // 地面最下一行以下视为实心；坑 = 整列没有 #，掉下去由死亡区判死。
    // 跳跃能力（按 PlayerController 参数算）：原地最高约 2.8 格，跑动能跳过约 8 格、走路约 5 格；
    // 所以台阶 ≤ 2 格、坑 ≤ 4 格。

    private const int MapTop = 8;

    private static readonly string[] Map =
    {
        ".........................................................................................................................................................................................................................",
        ".........................................................................................................................................................................................................................",
        ".........................................................................................................................................................................................................................",
        ".........................................................................................................................................................................................................................",
        ".........................................................................................................................................................................................................................",
        ".................................................***..............................................................*.................................................*....................................................",
        ".................................*...............................................................................................................................................................*..*..*.....S..G........",
        "..............................######...............................................................................................................C.............................C..########...##########################",
        "...P...S....*..*..*...S.############..S......................S........*...B...*.........+...C....S...^^...######.^^^............*....B...*......########........^^......B..*.+..############...##########################",
        "##########################################...##########....###########################################################....###################...########...#################################...##########################",
        "##########################################...##########....###########################################################....###################...########...#################################...##########################",
        "##########################################...##########....###########################################################....###################...########...#################################...##########################",
        "##########################################...##########....###########################################################....###################...########...#################################...##########################",
        "##########################################...##########....###########################################################....###################...########...#################################...##########################",
    };

    private static readonly string[] SignMessages =
    {
        "<b>Move</b>\nA / D   ·   Left Stick",
        "<b>Jump</b>\nSpace   ·   (A) / Cross\nHold for a higher jump",
        "<b>Run</b> and <b>jump</b>\nover the gap!",
        "<b>Attack</b>\nJ   ·   (Y) / Triangle\nThe boar charges - hit it twice!",
        "<b>Spikes!</b>  Jump over them\n<b>Dodge</b>  Shift   ·   R1 / RB\n(invincible while dodging)",
        "The warehouse!\nEveryone is waiting inside",
    };

    // 九宫格草地在 JapSprites.png 里的位置（列, 行；行从图片顶部数，每格 16 像素）。
    private static readonly Vector2Int GroundTopLeft = new Vector2Int(1, 26);
    private static readonly Vector2Int[] DecorCells =
    {
        new Vector2Int(17, 31), new Vector2Int(18, 31), new Vector2Int(19, 31),
        new Vector2Int(17, 32), new Vector2Int(18, 32), new Vector2Int(19, 32),
        new Vector2Int(17, 30), new Vector2Int(19, 30),
    };

    // ───────────────────────────── 入口 ─────────────────────────────

    [MenuItem("Poppin/Level1/Rebuild Level1 (overwrites Level1.unity)")]
    private static void RebuildFromMenu()
    {
        if (File.Exists(LevelScene) &&
            !EditorUtility.DisplayDialog("Rebuild Level1",
                "会用字符地图重新生成 Level1.unity，场景里手工改过的内容会被覆盖。继续吗？", "重建", "取消"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string result = Build();
        Debug.Log(result);
    }

    /// <summary>
    /// 生成入口（不弹任何对话框，MCP / 批处理也可调用）。当前场景有未保存修改时直接中止并返回原因。
    /// </summary>
    public static string Build()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.isDirty)
            return "Level1Builder: 当前场景有未保存的修改，已中止（先保存或放弃修改）。";

        int widthCheck = Map[0].Length;
        if (Map.Any(r => r.Length != widthCheck))
            return "Level1Builder: 字符地图每行长度不一致。";

        ImportBoarSprites();

        Scene scene = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(scene, LevelScene))
            return "Level1Builder: 另存为 Level1.unity 失败。";
        scene = SceneManager.GetActiveScene();

        Tilemap ground = FindInScene<Tilemap>(scene, "Plattom");
        if (ground == null)
            return "Level1Builder: 模板场景里找不到地面 Tilemap「Plattom」。";
        TilemapRenderer groundRenderer = ground.GetComponent<TilemapRenderer>();
        int sortLayer = groundRenderer.sortingLayerID;
        int sortOrder = groundRenderer.sortingOrder;

        // 模板里的测试物体（旧野猪、散落的测试精灵）在正式关卡里不要。
        foreach (string junk in new[] { "Boar", "frame_100", "ksm_hurt_grid_0" })
        {
            GameObject go = FindRoot(scene, junk);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        // 模板其他 Tilemap 里的测试图块（起点附近悬空的水块等）也清掉，只保留空图层给美术后续铺背景。
        foreach (Tilemap tm in ground.transform.parent.GetComponentsInChildren<Tilemap>(true))
        {
            if (tm != ground)
                tm.ClearAllTiles();
        }

        Prefabs prefabs = BuildPrefabs(sortLayer, sortOrder + 5);

        PaintGround(ground, out Dictionary<int, int> surface);
        PaintDecor(ground, surface, sortLayer, sortOrder + 1);

        GameObject levelRoot = new GameObject("Level");
        PlaceEntities(levelRoot.transform, prefabs, out Vector2 playerStart, out int stars);

        PlayerController player = Object.FindObjectOfType<PlayerController>();
        if (player != null)
            player.transform.position = new Vector3(playerStart.x, playerStart.y, player.transform.position.z);

        int width = Map[0].Length;
        int bottom = MapTop - Map.Length + 1;
        BuildBoundsAndKillZone(levelRoot.transform, width, bottom);
        CameraFollow follow = SetupCamera(width, bottom);
        BuildLevelManager(playerStart, follow);
        BuildHud(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();

        return $"Level1Builder: 完成。地图 {width}×{Map.Length}，星屑 {stars} 个，已保存 {LevelScene}。";
    }

    // ───────────────────────────── 1. 野猪序列帧 ─────────────────────────────

    private static readonly (string path, int frames)[] BoarSheets =
    {
        (BoarArtFolder + "/Walk/Walk-Base-Sheet.png", 6),
        (BoarArtFolder + "/Run/Run-Sheet.png", 6),
        (BoarArtFolder + "/Hit-Vanish/Hit-Sheet.png", 4),
    };

    private static void ImportBoarSprites()
    {
        foreach ((string path, int frames) in BoarSheets)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning("Level1Builder: 找不到 " + path);
                continue;
            }

#pragma warning disable 618
            bool ready = ti.spriteImportMode == SpriteImportMode.Multiple
                         && Mathf.Approximately(ti.spritePixelsToUnits, 16f)
                         && ti.filterMode == FilterMode.Point
                         && ti.spritesheet != null && ti.spritesheet.Length == frames;
            if (ready)
                continue;

            string baseName = Path.GetFileNameWithoutExtension(path);
            SpriteMetaData[] metas = new SpriteMetaData[frames];
            for (int i = 0; i < frames; i++)
            {
                metas[i] = new SpriteMetaData
                {
                    name = baseName + "_" + i,
                    rect = new Rect(i * 48, 0, 48, 32),
                    alignment = (int)SpriteAlignment.BottomCenter,
                    pivot = new Vector2(0.5f, 0f),
                };
            }

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.spritePixelsPerUnit = 16f;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.spritesheet = metas;
#pragma warning restore 618
            ti.SaveAndReimport();
        }
    }

    private static Sprite[] LoadFrames(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1)))
            .ToArray();
    }

    // ───────────────────────────── 2. 预制体 ─────────────────────────────

    private class Prefabs
    {
        public GameObject boar, star, heart, spikes, checkpoint, sign, goal;
    }

    private static Prefabs BuildPrefabs(int sortLayer, int sortOrder)
    {
        Directory.CreateDirectory(PrefabFolder);

        Prefabs p = new Prefabs();
        p.boar = SavePrefab(CreateBoar(sortLayer, sortOrder), "Boar");
        p.star = SavePrefab(CreatePickup("Star", Collectible.Kind.Star, ItemArt + "/star.png", sortLayer, sortOrder), "Star");
        p.heart = SavePrefab(CreatePickup("Heart", Collectible.Kind.Heart, ItemArt + "/heart.png", sortLayer, sortOrder), "Heart");
        p.spikes = SavePrefab(CreateSpikes(sortLayer, sortOrder), "Spikes");
        p.checkpoint = SavePrefab(CreateCheckpoint(sortLayer, sortOrder - 1), "Checkpoint");
        p.sign = SavePrefab(CreateSign(sortLayer, sortOrder - 1), "Sign");
        p.goal = SavePrefab(CreateGoal(sortLayer, sortOrder - 1), "Goal");
        return p;
    }

    private static GameObject SavePrefab(GameObject go, string name)
    {
        string path = PrefabFolder + "/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static GameObject CreateBoar(int sortLayer, int sortOrder)
    {
        GameObject go = new GameObject("Boar");
        go.layer = EnemyLayer;

        Sprite[] idle = LoadFrames(BoarArtFolder + "/Idle/Idle-Sheet.png");
        Sprite[] walk = LoadFrames(BoarArtFolder + "/Walk/Walk-Base-Sheet.png");
        Sprite[] run = LoadFrames(BoarArtFolder + "/Run/Run-Sheet.png");
        Sprite[] hit = LoadFrames(BoarArtFolder + "/Hit-Vanish/Hit-Sheet.png");

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = idle.Length > 0 ? idle[0] : null;
        sr.sortingLayerID = sortLayer;
        sr.sortingOrder = sortOrder;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // 实体碰撞体：站地面 / 挡墙用（与玩家之间在运行时 IgnoreCollision）。
        BoxCollider2D body = go.AddComponent<BoxCollider2D>();
        body.size = new Vector2(1.3f, 0.8f);
        body.offset = new Vector2(0f, 0.4f);

        // 触发器：接触伤害 + 被音波打中。
        CapsuleCollider2D hurtBox = go.AddComponent<CapsuleCollider2D>();
        hurtBox.isTrigger = true;
        hurtBox.direction = CapsuleDirection2D.Horizontal;
        hurtBox.size = new Vector2(1.8f, 1.3f);
        hurtBox.offset = new Vector2(0f, 0.7f);

        CharacterState stats = go.AddComponent<CharacterState>();
        stats.maxHP = 100f;
        stats.maxSP = 100f;
        stats.spRegenPerFrame = 0f;
        stats.invincibleTime = 0.5f;

        Attack attack = go.AddComponent<Attack>();
        attack.damage = 10f;
        attack.destroyHostOnHit = false;

        SpriteSequencePlayer seq = go.AddComponent<SpriteSequencePlayer>();
        seq.clips = new[]
        {
            new SpriteSequencePlayer.Clip { name = "Idle", frames = idle, fps = 8f, loop = true },
            new SpriteSequencePlayer.Clip { name = "Walk", frames = walk, fps = 10f, loop = true },
            new SpriteSequencePlayer.Clip { name = "Run", frames = run, fps = 14f, loop = true },
            new SpriteSequencePlayer.Clip { name = "Hit", frames = hit, fps = 12f, loop = true },
        };
        seq.defaultClip = "Walk";

        go.AddComponent<BoarController>();
        return go;
    }

    private static GameObject CreatePickup(string name, Collectible.Kind kind, string art, int sortLayer, int sortOrder)
    {
        GameObject go = new GameObject(name);
        CircleCollider2D trigger = go.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.4f;
        Collectible c = go.AddComponent<Collectible>();
        c.kind = kind;
        AddSpriteChild(go.transform, "Sprite", art, Vector2.zero, 0.7f, sortLayer, sortOrder);
        return go;
    }

    private static GameObject CreateSpikes(int sortLayer, int sortOrder)
    {
        GameObject go = new GameObject("Spikes");
        BoxCollider2D trigger = go.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(0.9f, 0.45f);
        trigger.offset = new Vector2(0f, 0.22f);
        Attack attack = go.AddComponent<Attack>();
        attack.damage = 10f;
        // 128 像素 @ PPU 100 = 1.28 单位，缩到正好 1 格。
        AddSpriteChild(go.transform, "Sprite", PropArt + "/spikes.png", new Vector2(0f, 0.5f), 0.78125f, sortLayer, sortOrder);
        return go;
    }

    private static GameObject CreateCheckpoint(int sortLayer, int sortOrder)
    {
        GameObject go = new GameObject("Checkpoint");
        BoxCollider2D trigger = go.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(1.5f, 3f);
        trigger.offset = new Vector2(0f, 1.5f);
        go.AddComponent<Checkpoint>();
        AddSpriteChild(go.transform, "Crystal", PropArt + "/crystal-bottom-1.png", new Vector2(0f, 0.58f), 0.9f, sortLayer, sortOrder);
        return go;
    }

    private static GameObject CreateSign(int sortLayer, int sortOrder)
    {
        GameObject go = new GameObject("Sign");
        BoxCollider2D trigger = go.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(5f, 4f);
        trigger.offset = new Vector2(0f, 2f);
        TutorialSign sign = go.AddComponent<TutorialSign>();
        AddSpriteChild(go.transform, "Board", PropArt + "/signboard.png", new Vector2(0f, 0.58f), 0.9f, sortLayer, sortOrder);

        GameObject hint = new GameObject("Hint");
        hint.transform.SetParent(go.transform, false);
        hint.transform.localPosition = new Vector3(0f, 3.4f, 0f);
        TextMeshPro text = hint.AddComponent<TextMeshPro>();
        text.text = "Hint";
        text.fontSize = 4f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.rectTransform.sizeDelta = new Vector2(9f, 3f);
        text.enableWordWrapping = false;
        text.sortingLayerID = SortingLayer.NameToID("Player");
        text.sortingOrder = 50;
        sign.label = text;
        return go;
    }

    private static GameObject CreateGoal(int sortLayer, int sortOrder)
    {
        GameObject go = new GameObject("Goal");
        BoxCollider2D trigger = go.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(1.2f, 2.6f);
        trigger.offset = new Vector2(0f, 1.3f);
        go.AddComponent<LevelGoal>();
        AddSpriteChild(go.transform, "Door", PropArt + "/door.png", new Vector2(0f, 1.28f), 1f, sortLayer, sortOrder);
        return go;
    }

    private static void AddSpriteChild(Transform parent, string name, string art, Vector2 localPos, float scale,
        int sortLayer, int sortOrder)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPos;
        child.transform.localScale = Vector3.one * scale;
        SpriteRenderer sr = child.AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(art);
        sr.sortingLayerID = sortLayer;
        sr.sortingOrder = sortOrder;
        if (sr.sprite == null)
            Debug.LogWarning("Level1Builder: 找不到精灵 " + art);
    }

    // ───────────────────────────── 3. 地形 ─────────────────────────────

    private static bool IsSolid(int x, int y)
    {
        int width = Map[0].Length;
        int bottom = MapTop - Map.Length + 1;
        if (x < 0 || x >= width)
            return false;
        if (y > MapTop)
            return false;
        if (y < bottom)
            return IsSolid(x, bottom); // 地图下方延续最下一行
        return Map[MapTop - y][x] == '#';
    }

    private static void PaintGround(Tilemap ground, out Dictionary<int, int> surface)
    {
        Dictionary<Vector2Int, TileBase> tiles = LoadTilesByCell();
        ground.ClearAllTiles();

        int width = Map[0].Length;
        int bottom = MapTop - Map.Length + 1;
        surface = new Dictionary<int, int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = bottom; y <= MapTop; y++)
            {
                if (!IsSolid(x, y))
                    continue;

                bool top = !IsSolid(x, y + 1);
                bool left = !IsSolid(x - 1, y);
                bool right = !IsSolid(x + 1, y);
                bool down = y > bottom && !IsSolid(x, y - 1);

                int col = GroundTopLeft.x + (left ? 0 : right ? 2 : 1);
                int row = GroundTopLeft.y + (top ? 0 : down ? 2 : 1);
                if (tiles.TryGetValue(new Vector2Int(col, row), out TileBase tile))
                    ground.SetTile(new Vector3Int(x, y, 0), tile);

                if (top && !surface.ContainsKey(x))
                    surface[x] = y + 1;
            }
        }

        ground.CompressBounds();
        TilemapCollider2D tc = ground.GetComponent<TilemapCollider2D>();
        if (tc != null)
            tc.ProcessTilemapChanges();
        CompositeCollider2D composite = ground.GetComponent<CompositeCollider2D>();
        if (composite != null)
            composite.GenerateGeometry();
    }

    private static void PaintDecor(Tilemap ground, Dictionary<int, int> surface, int sortLayer, int sortOrder)
    {
        Dictionary<Vector2Int, TileBase> tiles = LoadTilesByCell();
        GameObject decorGo = new GameObject("Decor");
        decorGo.transform.SetParent(ground.transform.parent, false);
        Tilemap decor = decorGo.AddComponent<Tilemap>();
        decor.tileAnchor = ground.tileAnchor;
        TilemapRenderer r = decorGo.AddComponent<TilemapRenderer>();
        r.sortingLayerID = sortLayer;
        r.sortingOrder = sortOrder;

        // 固定种子：每次重建装饰位置一致，diff 才看得懂。
        System.Random rng = new System.Random(2026);
        foreach (KeyValuePair<int, int> kv in surface)
        {
            int x = kv.Key;
            int y = kv.Value;
            int mapRow = MapTop - y;
            bool occupied = mapRow >= 0 && mapRow < Map.Length && Map[mapRow][x] != '.';
            if (occupied || rng.NextDouble() > 0.35)
                continue;
            Vector2Int cell = DecorCells[rng.Next(DecorCells.Length)];
            if (tiles.TryGetValue(cell, out TileBase tile))
                decor.SetTile(new Vector3Int(x, y, 0), tile);
        }
    }

    private static Dictionary<Vector2Int, TileBase> tileCache;

    /// <summary>JapSprites.png 的（列, 行）→ 已有的 Tile 资产（mapTile/Tile 下，按引用的精灵反查）。</summary>
    private static Dictionary<Vector2Int, TileBase> LoadTilesByCell()
    {
        if (tileCache != null)
            return tileCache;

        Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(TileSheet);
        int sheetHeight = sheet != null ? sheet.height : 640;

        Dictionary<Sprite, Vector2Int> cellOfSprite = new Dictionary<Sprite, Vector2Int>();
        foreach (Sprite s in AssetDatabase.LoadAllAssetsAtPath(TileSheet).OfType<Sprite>())
        {
            int col = Mathf.RoundToInt(s.rect.x / 16f);
            int row = Mathf.RoundToInt((sheetHeight - s.rect.y - s.rect.height) / 16f);
            cellOfSprite[s] = new Vector2Int(col, row);
        }

        tileCache = new Dictionary<Vector2Int, TileBase>();
        foreach (string guid in AssetDatabase.FindAssets("t:Tile", new[] { TileFolder }))
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(AssetDatabase.GUIDToAssetPath(guid));
            if (tile != null && tile.sprite != null && cellOfSprite.TryGetValue(tile.sprite, out Vector2Int cell)
                && !tileCache.ContainsKey(cell))
                tileCache[cell] = tile;
        }
        return tileCache;
    }

    // ───────────────────────────── 4. 摆放 ─────────────────────────────

    private static void PlaceEntities(Transform root, Prefabs p, out Vector2 playerStart, out int stars)
    {
        playerStart = new Vector2(3.5f, 0f);
        stars = 0;

        Transform enemies = Group(root, "Enemies");
        Transform pickups = Group(root, "Pickups");
        Transform hazards = Group(root, "Hazards");
        Transform checkpoints = Group(root, "Checkpoints");
        Transform signs = Group(root, "Signs");

        for (int row = 0; row < Map.Length; row++)
        {
            int y = MapTop - row;
            for (int x = 0; x < Map[row].Length; x++)
            {
                char ch = Map[row][x];
                Vector2 feet = new Vector2(x + 0.5f, y);
                switch (ch)
                {
                    case 'P':
                        playerStart = feet;
                        break;
                    case 'B':
                        Spawn(p.boar, enemies, feet);
                        break;
                    case '*':
                        Spawn(p.star, pickups, feet + new Vector2(0f, 0.9f));
                        stars++;
                        break;
                    case '+':
                        Spawn(p.heart, pickups, feet + new Vector2(0f, 0.9f));
                        break;
                    case '^':
                        Spawn(p.spikes, hazards, feet);
                        break;
                    case 'C':
                        Spawn(p.checkpoint, checkpoints, feet);
                        break;
                    case 'G':
                        Spawn(p.goal, root, feet);
                        break;
                    case 'S':
                        Spawn(p.sign, signs, feet);
                        break;
                }
            }
        }

        // 地图按行从上往下扫，高台上的牌子会先被扫到；文字要按从左到右的顺序分配。
        TutorialSign[] ordered = signs.GetComponentsInChildren<TutorialSign>()
            .OrderBy(s => s.transform.position.x).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            TutorialSign ts = ordered[i];
            ts.message = i < SignMessages.Length ? SignMessages[i] : "...";
            if (ts.label != null)
                ts.label.text = ts.message;
            ts.transform.SetSiblingIndex(i);
        }
    }

    private static Transform Group(Transform root, string name)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(root, false);
        return g.transform;
    }

    private static GameObject Spawn(GameObject prefab, Transform parent, Vector2 position)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = position;
        return go;
    }

    // ───────────────────────────── 5. 流程物体 ─────────────────────────────

    private static void BuildBoundsAndKillZone(Transform root, int width, int bottom)
    {
        GameObject bounds = new GameObject("Bounds");
        bounds.transform.SetParent(root, false);
        BoxCollider2D leftWall = bounds.AddComponent<BoxCollider2D>();
        leftWall.offset = new Vector2(-0.5f, 20f);
        leftWall.size = new Vector2(1f, 60f);
        BoxCollider2D rightWall = bounds.AddComponent<BoxCollider2D>();
        rightWall.offset = new Vector2(width + 0.5f, 20f);
        rightWall.size = new Vector2(1f, 60f);

        GameObject kill = new GameObject("KillZone");
        kill.transform.SetParent(root, false);
        BoxCollider2D k = kill.AddComponent<BoxCollider2D>();
        k.isTrigger = true;
        k.offset = new Vector2(width * 0.5f, bottom - 4f);
        k.size = new Vector2(width + 40f, 4f);
        kill.AddComponent<KillZone>();
    }

    private static CameraFollow SetupCamera(int width, int bottom)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return null;

        // 策划案 1.5：参考分辨率 480×270、PPU 16 → 视野高 16.875 格（1080p 下 4 倍整数放大）。
        cam.orthographic = true;
        cam.orthographicSize = 8.4375f;

        CameraFollow follow = cam.GetComponent<CameraFollow>();
        if (follow == null)
            follow = cam.gameObject.AddComponent<CameraFollow>();
        follow.minX = 0f;
        follow.maxX = width;
        follow.minY = bottom;
        follow.maxY = MapTop + 20f;
        return follow;
    }

    private static void BuildLevelManager(Vector2 playerStart, CameraFollow follow)
    {
        GameObject go = new GameObject("LevelManager");
        LevelManager lm = go.AddComponent<LevelManager>();
        GameObject start = new GameObject("PlayerStart");
        start.transform.SetParent(go.transform, false);
        start.transform.position = playerStart;
        lm.startPoint = start.transform;
        lm.player = Object.FindObjectOfType<PlayerController>();
        lm.cameraFollow = follow;
    }

    // ───────────────────────────── 6. HUD ─────────────────────────────

    private static void BuildHud(Scene scene)
    {
        PlayerStateBar bar = Object.FindObjectOfType<PlayerStateBar>();
        Canvas canvas = bar != null ? bar.GetComponentInParent<Canvas>() : Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("Level1Builder: 找不到 Canvas，跳过 HUD。");
            return;
        }

        GameObject hudGo = NewUI("LevelHUD", canvas.transform);
        Stretch(hudGo.GetComponent<RectTransform>());
        LevelHUD hud = hudGo.AddComponent<LevelHUD>();

        // 右上角：星屑 / 分数 / 时间
        GameObject stats = NewUI("Stats", hudGo.transform);
        RectTransform sr = stats.GetComponent<RectTransform>();
        sr.anchorMin = sr.anchorMax = new Vector2(1f, 1f);
        sr.pivot = new Vector2(1f, 1f);
        sr.anchoredPosition = new Vector2(-30f, -24f);
        sr.sizeDelta = new Vector2(380f, 150f);
        Image statsBg = stats.AddComponent<Image>();
        statsBg.color = new Color(0f, 0f, 0f, 0.35f);

        hud.starsText = NewText("Stars", stats.transform, 34f, TextAlignmentOptions.Right, new Vector2(-20f, -12f), new Vector2(340f, 42f));
        hud.scoreText = NewText("Score", stats.transform, 34f, TextAlignmentOptions.Right, new Vector2(-20f, -56f), new Vector2(340f, 42f));
        hud.timeText = NewText("Time", stats.transform, 34f, TextAlignmentOptions.Right, new Vector2(-20f, -100f), new Vector2(340f, 42f));
        hud.starsText.color = new Color(1f, 0.85f, 0.3f);

        // 中上：提示条
        hud.toastText = NewText("Toast", hudGo.transform, 44f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(900f, 70f));
        RectTransform tr = hud.toastText.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0f, -150f);

        // 结算面板
        GameObject panel = NewUI("ResultPanel", hudGo.transform);
        Stretch(panel.GetComponent<RectTransform>());
        panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        GameObject box = NewUI("Box", panel.transform);
        RectTransform br = box.GetComponent<RectTransform>();
        br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0.5f);
        br.sizeDelta = new Vector2(920f, 640f);
        box.AddComponent<Image>().color = new Color(0.13f, 0.11f, 0.2f, 0.96f);

        TMP_Text title = NewCentered("Title", box.transform, 76f, new Vector2(0f, 240f), new Vector2(860f, 100f));
        title.text = "STAGE CLEAR!";
        title.color = new Color(1f, 0.85f, 0.3f);
        title.fontStyle = FontStyles.Bold;

        hud.resultBody = NewCentered("Body", box.transform, 38f, new Vector2(0f, 20f), new Vector2(760f, 300f));
        hud.resultBody.alignment = TextAlignmentOptions.Left;
        hud.resultTotal = NewCentered("Total", box.transform, 58f, new Vector2(0f, -185f), new Vector2(860f, 80f));
        hud.resultTotal.fontStyle = FontStyles.Bold;
        hud.resultHint = NewCentered("Hint", box.transform, 30f, new Vector2(0f, -265f), new Vector2(860f, 50f));
        hud.resultHint.color = new Color(0.75f, 0.75f, 0.8f);

        hud.resultPanel = panel;
        panel.SetActive(false);
    }

    private static GameObject NewUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions align,
        Vector2 pos, Vector2 box)
    {
        GameObject go = NewUI(name, parent);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        return t;
    }

    private static TMP_Text NewCentered(string name, Transform parent, float size, Vector2 pos, Vector2 box)
    {
        TMP_Text t = NewText(name, parent, size, TextAlignmentOptions.Center, pos, box);
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        return t;
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == LevelScene))
            return;
        scenes.Insert(0, new EditorBuildSettingsScene(LevelScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static T FindInScene<T>(Scene scene, string name) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (T c in root.GetComponentsInChildren<T>(true))
            {
                if (c.gameObject.name == name)
                    return c;
            }
        }
        return null;
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
    }
}
