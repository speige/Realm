using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

namespace Realm.Godot.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MapEditorUxTests
{
    //[TestCase]
    public async Task CaptureMapEditorScreenshot()
    {
        // Prevent the creator agreement modal from showing and darkening the screen
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var hud = MapEditorHUD.Instance;
        if (hud != null)
        {
            var btnPaint = hud.GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnTextureBrush");
            hud.TriggerToolSelection(GameHost.EditorTool.PaintTexture, btnPaint);
            await runner.AwaitMillis(1000);
        }
        var sw = new StringWriter();
        if (hud != null)
        {
            sw.WriteLine($"--- DIAGNOSTICS START ---");
            sw.WriteLine($"hud: not null");
            sw.WriteLine($"ActiveModule in ViewModel: {hud.ViewModel?.ActiveModule}");
            sw.WriteLine($"ActiveEditorTool in GameHost: {GameHost.Instance?.ActiveEditorTool}");
            
            var panelTerrain = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox");
            var panelDeco = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox");
            var panelPathing = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox");
            var panelClip = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard");
            
            sw.WriteLine($"panelTerrain visible: {panelTerrain?.Visible}, null: {panelTerrain == null}");
            sw.WriteLine($"panelDeco visible: {panelDeco?.Visible}, null: {panelDeco == null}");
            sw.WriteLine($"panelPathing visible: {panelPathing?.Visible}, null: {panelPathing == null}");
            sw.WriteLine($"panelClip visible: {panelClip?.Visible}, null: {panelClip == null}");
            
            var minimap = hud.GetNodeOrNull<Control>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame");
            sw.WriteLine($"minimap visible: {minimap?.Visible}, null: {minimap == null}, size: {minimap?.Size}");
            
            var containerPathing = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing");
            sw.WriteLine($"containerPathing visible: {containerPathing?.Visible}, null: {containerPathing == null}");
            
            sw.WriteLine($"--- DIAGNOSTICS END ---");
        }
        else
        {
            sw.WriteLine("HUD IS NULL!");
        }

        File.WriteAllText(@"C:\temp\Realm\diagnostics.txt", sw.ToString());

        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
        string artifactDir = @"C:\Users\Devin\.gemini\antigravity-cli\brain\3f6febad-2673-47d8-b400-9b856eaa33d0";
        Directory.CreateDirectory(artifactDir);
        string filePath = Path.Combine(artifactDir, "map_editor_ux.png");
        image.SavePng(filePath);
    }

    //[TestCase]
    public void ConvertDefaultTileSheets()
    {
        var terrain = new EditableTerrain();
        var files = Directory.GetFiles(@"C:\temp\Realm\Realm.Godot\Assets\2d\TileSheets", "*.png");
        foreach (var file in files)
        {
            string ktx2Path = file.Replace(".png", ".ktx2");
            System.Console.WriteLine($"Converting {file} to {ktx2Path}");
            terrain.ProcessAndSaveRawTexture(file, ktx2Path);
            if (!File.Exists(ktx2Path))
            {
                throw new System.Exception($"Failed to convert {file} to {ktx2Path}.");
            }
        }
    }

    [TestCase]
    public async Task VerifyTerrainSplatTransitions()
    {
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var gameHost = GameHost.Instance;
        var camera = gameHost.MainCamera;
        if (camera != null)
        {
            camera.Position = new Vector3(0.0f, 35.0f, 25.0f);
            camera.RotationDegrees = new Vector3(-55.0f, 0.0f, 0.0f);
        }

        var terrain = gameHost.GroundTerrain;

        int width = terrain.Width;
        int depth = terrain.Depth;
        var splatMap = terrain.SplatMap;

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x <= width / 2)
                {
                    splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
                }
                else
                {
                    splatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
                }
            }
        }

        var editorService = ServiceLocator.Get<EditorService>();
        editorService.SetTerrainSplatMap(splatMap);

        terrain.UpdateMeshAndPhysics(true, true);
        await runner.AwaitMillis(1000);

        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
        string artifactDir = @"C:\Users\Devin\.gemini\antigravity-cli\brain\7943d49b-03f6-4917-bd98-2a87e32bae94";
        Directory.CreateDirectory(artifactDir);
        string filePath = Path.Combine(artifactDir, "terrain_transition_test.png");
        image.SavePng(filePath);
    }

    //[TestCase]
    public async Task TestClickTestButtonOnBlankMap()
    {
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var hud = MapEditorHUD.Instance;
        Assertions.AssertThat(hud).IsNotNull();

        var btnTestMap = hud!.GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BtnTestMap");
        Assertions.AssertThat(btnTestMap).IsNotNull();
        
        // Trigger TestMapAction on blank map
        btnTestMap.EmitSignal("pressed");
        await runner.AwaitMillis(500);

        // Find the confirmation dialog overlay created by ShowConfirmationDialog
        Node? overlay = hud.GetNodeOrNull<Node>("ConfirmationDialogOverlay");

        if (overlay != null)
        {
            var btnConfirm = overlay.FindChild("BtnConfirm", true, false) as Button
                ?? overlay.FindChild("*Confirm*", true, false) as Button;

            if (btnConfirm != null)
            {
                btnConfirm.EmitSignal("pressed");
            }
            else
            {
                // Fallback: directly proceed if button search fails
                await hud.ProceedToTestMap();
            }
        }
        else
        {
            await hud.ProceedToTestMap();
        }

        await runner.AwaitMillis(2000);
        Assertions.AssertThat(UI.WasmConsoleWindow.Instance).IsNotNull();
    }

    [TestCase]
    public async Task RandomGenCustomSeedScreenshot()
    {
        //NOTE: there seems to be a race condition in this test, if mesh screenshot is flat run a 2nd time.
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var gameHost = GameHost.Instance;
        Assertions.AssertThat(gameHost).IsNotNull();

        MapGenerator.GenerateMap(
            gameHost,
            hillsDensity: 9,
            terrainRoughness: 7,
            mountainHeight: 9,
            chokeWidth: 7,
            waterLevel: 9,
            treeDensity: 8,
            resourceAbundance: 9,
            decoDensity: 10,
            seedString: "246272"
        );

        await runner.AwaitMillis(2000);

        var camera = gameHost.MainCamera;
        if (camera != null)
        {
            camera.Position = new Vector3(0.0f, 95.0f, 75.0f);
            camera.RotationDegrees = new Vector3(-55.0f, 0.0f, 0.0f);
        }

        await runner.AwaitMillis(1000);

        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
        string tempDir = @"C:\temp\realm_screenshots";
        Directory.CreateDirectory(tempDir);

        string baseFileName = "terrain_screenshot";
        int index = 1;
        string filePath = Path.Combine(tempDir, $"{baseFileName}_{index}.png");
        while (File.Exists(filePath))
        {
            index++;
            filePath = Path.Combine(tempDir, $"{baseFileName}_{index}.png");
        }

        image.SavePng(filePath);
    }

    [TestCase]
    public async Task TestBlockModeRaiseTexturePainting()
    {
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var gameHost = GameHost.Instance;
        Assertions.AssertThat(gameHost).IsNotNull();
        var hud = MapEditorHUD.Instance;
        Assertions.AssertThat(hud).IsNotNull();
        Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

        // 1. Select swatch "Lava Vein" for cliff face & "Deep Moss" for standard brush
        int deepMossIndex = 1;
        int lavaVeinIndex = 3;

        var displayNamesField = typeof(MapEditorHUD).GetField("_swatchDisplayNames", BindingFlags.NonPublic | BindingFlags.Instance);
        if (displayNamesField?.GetValue(hud) is System.Collections.Generic.List<string> displayNames)
        {
            for (int i = 0; i < displayNames.Count; i++)
            {
                if (displayNames[i].Equals("Deep Moss", System.StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
                if (displayNames[i].Equals("Lava Vein", System.StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
            }
        }

        gameHost.EditorPaintTextureIndex = deepMossIndex;
        gameHost.EditorCliffPaintTextureIndex = lavaVeinIndex;

        // 2. Configure Raise tool in Block mode with 8.0f height step
        gameHost.ActiveEditorTool = GameHost.EditorTool.Raise;
        gameHost.EditorBlockMode = true;
        gameHost.EditorBlockLevelHeight = 8.0f;
        gameHost.EditorBrushRadius = 14.0f;
        gameHost.EditorBrushIsSquare = true;

        var editorService = ServiceLocator.Get<EditorService>();
        editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

        // 3. Raise terrain step at Z = -10.0f to create a distinct cliff wall & top terrace
        Vector3 raiseCenter = new Vector3(0.0f, 0.0f, -10.0f);

        editorService.BeginTerrainDraw(
            raiseCenter,
            gameHost.ActiveEditorTool,
            gameHost.EditorBlockMode,
            gameHost.EditorBlockLevelHeight,
            gameHost.GroundTerrain.Heights,
            gameHost.GroundTerrain.SplatMap,
            gameHost.GroundTerrain.PathingCodes);

        var res = editorService.ApplyContinuousTerrainEditing(
            raiseCenter, 1.0f,
            gameHost.ActiveEditorTool,
            gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
            gameHost.EditorBrushIsSquare,
            gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
            gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
            0, true, true);

        editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

        gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
        await runner.AwaitMillis(1000);

        // 4. Position camera up close to view the front cliff face and top terrace step
        var camera = gameHost.MainCamera;
        if (camera != null)
        {
            camera.Position = new Vector3(0.0f, 14.0f, 18.0f);
            camera.RotationDegrees = new Vector3(-25.0f, 0.0f, 0.0f);
        }

        await runner.AwaitMillis(1000);

        // 5. Capture screenshot
        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();

        string tempDir = @"C:\temp\realm_screenshots";
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, "block_mode_raise_cliff_test.png");
        image.SavePng(tempFilePath);

        string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\02ee3010-057f-4f99-96f3-b7a61c9ffebc";
        if (Directory.Exists(artifactDir))
        {
            string artifactFilePath = Path.Combine(artifactDir, "block_mode_raise_cliff_test.png");
            image.SavePng(artifactFilePath);
        }
    }

    [TestCase]
    public async Task TestExactMouseClickInEditorGUI()
    {
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var gameHost = GameHost.Instance;
        Assertions.AssertThat(gameHost).IsNotNull();
        var hud = MapEditorHUD.Instance;
        Assertions.AssertThat(hud).IsNotNull();
        Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

        // Match user's UI settings from uploaded image:
        // Brush Swatch = Lava Vein
        // Cliff Swatch = Deep Moss
        int lavaVeinIndex = 3;
        int deepMossIndex = 1;

        var displayNamesField = typeof(MapEditorHUD).GetField("_swatchDisplayNames", BindingFlags.NonPublic | BindingFlags.Instance);
        if (displayNamesField?.GetValue(hud) is System.Collections.Generic.List<string> displayNames)
        {
            for (int i = 0; i < displayNames.Count; i++)
            {
                if (displayNames[i].Equals("Lava Vein", System.StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
                if (displayNames[i].Equals("Deep Moss", System.StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
            }
        }

        gameHost.EditorPaintTextureIndex = lavaVeinIndex;
        gameHost.EditorCliffPaintTextureIndex = deepMossIndex;

        // Tool: Raise in Block Mode, Step Height 4.0m, Size 6, Square Brush
        gameHost.ActiveEditorTool = GameHost.EditorTool.Raise;
        gameHost.EditorBlockMode = true;
        gameHost.EditorBlockLevelHeight = 4.0f;
        gameHost.EditorBrushRadius = 6.0f;
        gameHost.EditorBrushIsSquare = true;

        var editorService = ServiceLocator.Get<EditorService>();
        editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

        // Single click at center position (0, 0, 0)
        Vector3 clickPos = new Vector3(0.0f, 0.0f, 0.0f);

        editorService.BeginTerrainDraw(
            clickPos,
            gameHost.ActiveEditorTool,
            gameHost.EditorBlockMode,
            gameHost.EditorBlockLevelHeight,
            gameHost.GroundTerrain.Heights,
            gameHost.GroundTerrain.SplatMap,
            gameHost.GroundTerrain.PathingCodes);

        var res = editorService.ApplyContinuousTerrainEditing(
            clickPos, 1.0f,
            gameHost.ActiveEditorTool,
            gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
            gameHost.EditorBrushIsSquare,
            gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
            gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
            0, true, true);

        editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

        gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
        await runner.AwaitMillis(1000);
        gameHost.GroundTerrain.SetWireframeMode(false);
        await runner.AwaitMillis(1000);

        // Diagnostic logging of terrain heights, splat maps, and rendered GPU wall slope geometry
        int w = gameHost.GroundTerrain.Width;
        int d = gameHost.GroundTerrain.Depth;
        int centerGridX = (w - 1) / 2;
        int centerGridZ = (d - 1) / 2;

        var sw = new System.IO.StringWriter();
        sw.WriteLine("=================== CPU DATA STRUCTURE DIAGNOSTICS ===================");
        sw.WriteLine($"Terrain Width={w}, Depth={d}, QuadSize={gameHost.GroundTerrain.QuadSize:F1}");
        sw.WriteLine($"Center Grid: X={centerGridX}, Z={centerGridZ}");

        float[,] heights = gameHost.GroundTerrain.Heights;

        sw.WriteLine("\n--- Heights[,] Array (-5 to +5 around center) ---");
        for (int z = centerGridZ - 5; z <= centerGridZ + 5; z++)
        {
            var line = new System.Text.StringBuilder();
            for (int x = centerGridX - 5; x <= centerGridX + 5; x++)
            {
                line.Append($"{heights[x, z],5:F1} ");
            }
            sw.WriteLine(line.ToString());
        }

        sw.WriteLine("\n=================== GPU RENDERED MESH DIAGNOSTICS ===================");
        var allVertices = new System.Collections.Generic.List<Vector3>();
        var allNormals = new System.Collections.Generic.List<Vector3>();

        var terrainField = typeof(EditableTerrain).GetField("_chunks", BindingFlags.NonPublic | BindingFlags.Instance);
        if (terrainField?.GetValue(gameHost.GroundTerrain) is System.Collections.IEnumerable chunksEnum)
        {
            int cCount = 0;
            foreach (var chunkObj in chunksEnum)
            {
                if (chunkObj == null) continue;
                cCount++;
                var meshField = chunkObj.GetType().GetField("ArrayMesh");
                if (meshField?.GetValue(chunkObj) is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
                {
                    var surfArrays = arrayMesh.SurfaceGetArrays(0);
                    if (surfArrays != null && surfArrays.Count > (int)Mesh.ArrayType.Normal)
                    {
                        var verts = surfArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var norms = surfArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                        allVertices.AddRange(verts);
                        allNormals.AddRange(norms);
                    }
                }
            }
            sw.WriteLine($"Total Terrain Chunks Processed: {cCount}");
        }

        sw.WriteLine($"Total Rendered Mesh Vertices Across Chunks: {allVertices.Count}");

        var groundVerts = new System.Collections.Generic.List<Vector3>();
        var apexVerts = new System.Collections.Generic.List<Vector3>();
        var wallVerts = new System.Collections.Generic.List<Vector3>();
        var wallNormals = new System.Collections.Generic.List<Vector3>();

        for (int i = 0; i < allVertices.Count; i++)
        {
            Vector3 v = allVertices[i];
            Vector3 n = allNormals[i];

            if (v.Y > 0.05f && v.Y < 3.95f)
            {
                wallVerts.Add(v);
                wallNormals.Add(n);
            }
            else if (v.Y >= 3.95f)
            {
                apexVerts.Add(v);
            }
            else if (v.Y <= 0.05f)
            {
                groundVerts.Add(v);
            }
        }

        sw.WriteLine($"Count of Ground Vertices (Y ~ 0): {groundVerts.Count}");
        sw.WriteLine($"Count of Apex Vertices (Y ~ 4): {apexVerts.Count}");
        sw.WriteLine($"Count of Vertical Wall Vertices: {wallVerts.Count}");

        sw.WriteLine("\n--- VERTICAL WALL COLUMN & SLOPE ANGLE ANALYSIS ---");
        var wallColumns = new System.Collections.Generic.Dictionary<(int xKey, int zKey), System.Collections.Generic.List<Vector3>>();
        foreach (var wv in wallVerts)
        {
            int xKey = (int)System.Math.Round(wv.X * 100.0f);
            int zKey = (int)System.Math.Round(wv.Z * 100.0f);
            var key = (xKey, zKey);
            if (!wallColumns.ContainsKey(key))
            {
                wallColumns[key] = new System.Collections.Generic.List<Vector3>();
            }
            wallColumns[key].Add(wv);
        }

        sw.WriteLine($"Total Wall Grid Column Locations (XZ): {wallColumns.Count}");
        foreach (var kvp in wallColumns)
        {
            float posX = kvp.Key.xKey / 100.0f;
            float posZ = kvp.Key.zKey / 100.0f;
            var list = kvp.Value;
            float minY = 999f, maxY = -999f;
            foreach (var v in list)
            {
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }

            sw.WriteLine($"Wall Column at (X={posX:F2}, Z={posZ:F2}): Min Y={minY:F2}, Max Y={maxY:F2}, Height Delta={maxY - minY:F2}");
        }

        sw.WriteLine("\n--- FOOTPRINT BOUNDS ANALYSIS ---");
        if (apexVerts.Count > 0)
        {
            float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
            foreach (var v in apexVerts)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }
            sw.WriteLine($"Apex Footprint: X=[{minX:F2} to {maxX:F2}] (Width={(maxX - minX):F2}), Z=[{minZ:F2} to {maxZ:F2}] (Depth={(maxZ - minZ):F2})");
        }

        if (wallVerts.Count > 0)
        {
            float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
            foreach (var v in wallVerts)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }
            sw.WriteLine($"Wall Rim/Base Outer Bounds: X=[{minX:F2} to {maxX:F2}], Z=[{minZ:F2} to {maxZ:F2}]");
        }

        sw.WriteLine("\n--- CLIFF WALL PERIMETER ENCLOSURE DIAGNOSTICS ---");
        // Count Apex quads and evaluate perimeter wall edges
        int apexQuadCount = 0;
        int expectedWestWalls = 0, expectedEastWalls = 0, expectedNorthWalls = 0, expectedSouthWalls = 0;

        for (int z = 0; z < d - 1; z++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                if (heights[x, z] > heights[x, z] + 0.001f)
                {
                    apexQuadCount++;
                    if (x == 0 || heights[x - 1, z] <= heights[x - 1, z] + 0.001f) expectedWestWalls++;
                    if (x == w - 2 || heights[x + 1, z] <= heights[x + 1, z] + 0.001f) expectedEastWalls++;
                    if (z == 0 || heights[x, z - 1] <= heights[x, z - 1] + 0.001f) expectedNorthWalls++;
                    if (z == d - 2 || heights[x, z + 1] <= heights[x, z + 1] + 0.001f) expectedSouthWalls++;
                }
            }
        }

        int totalExpectedWallQuads = expectedWestWalls + expectedEastWalls + expectedNorthWalls + expectedSouthWalls;
        sw.WriteLine($"Total Apex Quads: {apexQuadCount}");
        sw.WriteLine($"Expected Perimeter Walls -> West: {expectedWestWalls}, East: {expectedEastWalls}, North: {expectedNorthWalls}, South: {expectedSouthWalls}");
        sw.WriteLine($"Total Expected Wall Quad Segments: {totalExpectedWallQuads}");
        sw.WriteLine($"Cliff Perimeter Fully Enclosed: {expectedWestWalls > 0 && expectedEastWalls > 0 && expectedNorthWalls > 0 && expectedSouthWalls > 0}");

        string diagText = sw.ToString();
        System.IO.File.WriteAllText(@"C:\temp\realm_screenshots\wall_slope_diagnostics.txt", diagText);
        global::Godot.GD.Print(diagText);

        // Position camera to match the angle from uploaded image
        var camera = gameHost.MainCamera;
        if (camera != null)
        {
            camera.Position = new Vector3(0.0f, 25.0f, 35.0f);
            camera.RotationDegrees = new Vector3(-35.0f, 0.0f, 0.0f);
        }

        await runner.AwaitMillis(1000);

        // Capture screenshot
        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();

        string tempDir = @"C:\temp\realm_screenshots";
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, "exact_ui_click_reproduction_test.png");
        image.SavePng(tempFilePath);

        string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\02ee3010-057f-4f99-96f3-b7a61c9ffebc";
        if (Directory.Exists(artifactDir))
        {
            string artifactFilePath = Path.Combine(artifactDir, "exact_ui_click_reproduction_test.png");
            image.SavePng(artifactFilePath);
        }
    }

    [TestCase]
    public async Task TestExactMouseClickLowerToolInEditorGUI()
    {
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var gameHost = GameHost.Instance;
        Assertions.AssertThat(gameHost).IsNotNull();

        gameHost.ActiveEditorTool = GameHost.EditorTool.Lower;
        gameHost.EditorBlockMode = true;
        gameHost.EditorBlockLevelHeight = 4.0f;
        gameHost.EditorBrushRadius = 6.0f;
        gameHost.EditorBrushIsSquare = true;

        var editorService = ServiceLocator.Get<EditorService>();
        editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

        Vector3 clickPos = new Vector3(0.0f, 0.0f, 0.0f);

        editorService.BeginTerrainDraw(
            clickPos,
            gameHost.ActiveEditorTool,
            gameHost.EditorBlockMode,
            gameHost.EditorBlockLevelHeight,
            gameHost.GroundTerrain.Heights,
            gameHost.GroundTerrain.SplatMap,
            gameHost.GroundTerrain.PathingCodes);

        var res = editorService.ApplyContinuousTerrainEditing(
            clickPos, 1.0f,
            gameHost.ActiveEditorTool,
            gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
            gameHost.EditorBrushIsSquare,
            gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
            gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
            0, true, true);

        editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

        gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
        await runner.AwaitMillis(1000);
        // GameHost.Instance.GroundTerrain.SetWireframeMode(true);
        await runner.AwaitMillis(1000);

        int w = gameHost.GroundTerrain.Width;
        int d = gameHost.GroundTerrain.Depth;
        int centerGridX = (w - 1) / 2;
        int centerGridZ = (d - 1) / 2;

        var sw = new System.IO.StringWriter();
        sw.WriteLine("=================== CPU DATA STRUCTURE DIAGNOSTICS (LOWER TOOL) ===================");
        sw.WriteLine($"Terrain Width={w}, Depth={d}, QuadSize={gameHost.GroundTerrain.QuadSize:F1}");
        sw.WriteLine($"Center Grid: X={centerGridX}, Z={centerGridZ}");

        float[,] heights = gameHost.GroundTerrain.Heights;

        sw.WriteLine("\n--- Heights[,] Array (-5 to +5 around center) ---");
        for (int z = centerGridZ - 5; z <= centerGridZ + 5; z++)
        {
            var line = new System.Text.StringBuilder();
            for (int x = centerGridX - 5; x <= centerGridX + 5; x++)
            {
                line.Append($"{heights[x, z],5:F1} ");
            }
            sw.WriteLine(line.ToString());
        }

        sw.WriteLine("\n=================== GPU RENDERED MESH DIAGNOSTICS (LOWER TOOL) ===================");
        var allVertices = new System.Collections.Generic.List<Vector3>();
        var allNormals = new System.Collections.Generic.List<Vector3>();

        var terrainField = typeof(EditableTerrain).GetField("_chunks", BindingFlags.NonPublic | BindingFlags.Instance);
        if (terrainField?.GetValue(gameHost.GroundTerrain) is System.Collections.IEnumerable chunksEnum)
        {
            int cCount = 0;
            foreach (var chunkObj in chunksEnum)
            {
                if (chunkObj == null) continue;
                cCount++;
                var meshField = chunkObj.GetType().GetField("ArrayMesh");
                if (meshField?.GetValue(chunkObj) is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
                {
                    var surfArrays = arrayMesh.SurfaceGetArrays(0);
                    if (surfArrays != null && surfArrays.Count > (int)Mesh.ArrayType.Normal)
                    {
                        var verts = surfArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var norms = surfArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                        allVertices.AddRange(verts);
                        allNormals.AddRange(norms);
                    }
                }
            }
            sw.WriteLine($"Total Terrain Chunks Processed: {cCount}");
        }

        sw.WriteLine($"Total Rendered Mesh Vertices Across Chunks: {allVertices.Count}");

        var groundVerts = new System.Collections.Generic.List<Vector3>();
        var trenchVerts = new System.Collections.Generic.List<Vector3>();
        var wallVerts = new System.Collections.Generic.List<Vector3>();
        var wallNormals = new System.Collections.Generic.List<Vector3>();

        for (int i = 0; i < allVertices.Count; i++)
        {
            Vector3 v = allVertices[i];
            Vector3 n = allNormals[i];

            if (v.Y < -0.05f && v.Y > -3.95f)
            {
                wallVerts.Add(v);
                wallNormals.Add(n);
            }
            else if (v.Y <= -3.95f)
            {
                trenchVerts.Add(v);
            }
            else if (v.Y >= -0.05f)
            {
                groundVerts.Add(v);
            }
        }

        sw.WriteLine($"Count of Ground Vertices (Y ~ 0): {groundVerts.Count}");
        sw.WriteLine($"Count of Trench Floor Vertices (Y ~ -4): {trenchVerts.Count}");
        sw.WriteLine($"Count of Vertical Wall Vertices: {wallVerts.Count}");

        sw.WriteLine("\n--- VERTICAL WALL COLUMN & SLOPE ANGLE ANALYSIS ---");
        var wallColumns = new System.Collections.Generic.Dictionary<(int xKey, int zKey), System.Collections.Generic.List<Vector3>>();
        foreach (var wv in wallVerts)
        {
            int xKey = (int)System.Math.Round(wv.X * 100.0f);
            int zKey = (int)System.Math.Round(wv.Z * 100.0f);
            var key = (xKey, zKey);
            if (!wallColumns.ContainsKey(key))
            {
                wallColumns[key] = new System.Collections.Generic.List<Vector3>();
            }
            wallColumns[key].Add(wv);
        }

        sw.WriteLine($"Total Wall Grid Column Locations (XZ): {wallColumns.Count}");
        foreach (var kvp in wallColumns)
        {
            float posX = kvp.Key.xKey / 100.0f;
            float posZ = kvp.Key.zKey / 100.0f;
            var list = kvp.Value;
            float minY = 999f, maxY = -999f;
            foreach (var v in list)
            {
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }

            sw.WriteLine($"Wall Column at (X={posX:F2}, Z={posZ:F2}): Min Y={minY:F2}, Max Y={maxY:F2}, Height Delta={maxY - minY:F2}");
        }

        sw.WriteLine("\n--- FOOTPRINT BOUNDS ANALYSIS ---");
        if (trenchVerts.Count > 0)
        {
            float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
            foreach (var v in trenchVerts)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }
            sw.WriteLine($"Trench Floor Footprint: X=[{minX:F2} to {maxX:F2}] (Width={(maxX - minX):F2}), Z=[{minZ:F2} to {maxZ:F2}] (Depth={(maxZ - minZ):F2})");
        }

        if (wallVerts.Count > 0)
        {
            float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
            foreach (var v in wallVerts)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }
            sw.WriteLine($"Wall Rim/Base Outer Bounds: X=[{minX:F2} to {maxX:F2}], Z=[{minZ:F2} to {maxZ:F2}]");
        }

        string diagText = sw.ToString();
        System.IO.File.WriteAllText(@"C:\temp\realm_screenshots\lower_wall_slope_diagnostics.txt", diagText);
        global::Godot.GD.Print(diagText);

        var camera = gameHost.MainCamera;
        if (camera != null)
        {
            camera.Position = new Vector3(0.0f, 25.0f, 35.0f);
            camera.RotationDegrees = new Vector3(-35.0f, 0.0f, 0.0f);
        }

        await runner.AwaitMillis(1000);

        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
        string tempDir = @"C:\temp\realm_screenshots";
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, "lower_exact_ui_click_reproduction_test.png");
        image.SavePng(tempFilePath);
    }

    [TestCase]
    public async Task TestMapEdgeRaiseCliffHairlineFractures()
    {
        var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null)
        {
            field.SetValue(null, true);
        }

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var gameHost = GameHost.Instance;
        Assertions.AssertThat(gameHost).IsNotNull();
        var hud = MapEditorHUD.Instance;
        Assertions.AssertThat(hud).IsNotNull();
        Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

        int lavaVeinIndex = 3;
        int deepMossIndex = 1;

        var displayNamesField = typeof(MapEditorHUD).GetField("_swatchDisplayNames", BindingFlags.NonPublic | BindingFlags.Instance);
        if (displayNamesField?.GetValue(hud) is System.Collections.Generic.List<string> displayNames)
        {
            for (int i = 0; i < displayNames.Count; i++)
            {
                if (displayNames[i].Equals("Lava Vein", System.StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
                if (displayNames[i].Equals("Deep Moss", System.StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
            }
        }

        gameHost.EditorPaintTextureIndex = lavaVeinIndex;
        gameHost.EditorCliffPaintTextureIndex = deepMossIndex;

        // Tool: Raise in Block Mode, step height 24.0m, small cursor radius 1.0f
        gameHost.ActiveEditorTool = GameHost.EditorTool.Raise;
        gameHost.EditorBlockMode = true;
        gameHost.EditorBlockLevelHeight = 24.0f;
        gameHost.EditorBrushRadius = 1.0f;
        gameHost.EditorBrushIsSquare = true;

        var editorService = ServiceLocator.Get<EditorService>();
        editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

        float halfW = (gameHost.GroundTerrain.Width - 1) / 2.0f * gameHost.GroundTerrain.QuadSize;
        float halfD = (gameHost.GroundTerrain.Depth - 1) / 2.0f * gameHost.GroundTerrain.QuadSize;

        // Draw raised terrain along the edge of the map border
        float startX = -halfW + 10.0f;
        float endX = halfW - 10.0f;
        float startZ = -halfD + 2.0f;
        float endZ = -halfD + 10.0f;
        Vector3 strokeStart = new Vector3(startX, 0.0f, startZ);

        editorService.BeginTerrainDraw(
            strokeStart,
            gameHost.ActiveEditorTool,
            gameHost.EditorBlockMode,
            gameHost.EditorBlockLevelHeight,
            gameHost.GroundTerrain.Heights,
            gameHost.GroundTerrain.SplatMap,
            gameHost.GroundTerrain.PathingCodes);

        int steps = 120;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float currX = Mathf.Lerp(startX, endX, t);
            float currZ = Mathf.Lerp(startZ, endZ, t);
            Vector3 currPos = new Vector3(currX, 0.0f, currZ);
            editorService.ApplyContinuousTerrainEditing(
                currPos, 1.0f,
                gameHost.ActiveEditorTool,
                gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
                gameHost.EditorBrushIsSquare,
                gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
                gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
                0, true, true);
        }

        editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

        gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
        await runner.AwaitMillis(1000);

        // Pan camera to view the map border cliff face
        var camera = gameHost.MainCamera;
        if (camera != null)
        {
            camera.Position = new Vector3(0.0f, 35.0f, -90.0f);
            camera.RotationDegrees = new Vector3(-35.0f, 0.0f, 0.0f);
        }

        await runner.AwaitMillis(1000);

        global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();

        string tempDir = @"C:\temp\realm_screenshots";
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, "map_edge_tall_step_cliff_reproduction.png");
        image.SavePng(tempFilePath);

        string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\b8ffea97-4c62-4552-b4df-4098a5c5f68c";
        Directory.CreateDirectory(artifactDir);
        string artifactFilePath = Path.Combine(artifactDir, "map_edge_tall_step_cliff_reproduction.png");
        image.SavePng(artifactFilePath);
    }
}
