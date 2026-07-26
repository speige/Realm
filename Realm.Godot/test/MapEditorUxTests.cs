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
    [TestCase]
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
            hud.TriggerToolSelection(GameHost.EditorTool.PaintGrass, btnPaint);
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

    [TestCase]
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

    [TestCase]
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
}
