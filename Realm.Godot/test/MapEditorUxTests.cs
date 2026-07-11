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
}
