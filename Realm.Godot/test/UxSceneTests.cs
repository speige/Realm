namespace Realm.Godot.Tests;

using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class UxSceneTests
{
    [TestCase]
    public async Task CaptureUxScreenshots()
    {
        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1000);

        string tempDir = Path.Combine(Path.GetTempPath(), "Realm_UX_Screenshots");
        Directory.CreateDirectory(tempDir);

        GameScreen[] screens = new GameScreen[]
        {
            GameScreen.MainMenu,
            GameScreen.LobbyBrowser,
            GameScreen.LobbyCreate,
            GameScreen.LobbyRoom,
            GameScreen.Settings,
            GameScreen.MapDiscovery,
            GameScreen.MapDetails,
            GameScreen.MapEditorHUD,
            GameScreen.ReplayList,
            GameScreen.GameOver
        };

        foreach (GameScreen screen in screens)
        {
            if (screen == GameScreen.MapDetails)
            {
                MapData[] dummy = MapData.GetDummyMaps();
                MapData mapData = (dummy != null && dummy.Length > 0) ? dummy[0] : new MapData { Title = "Test Map" };
                UIManager.Instance.TransitionToMapDetails(mapData);
            }
            else
            {
                UIManager.Instance.TransitionTo(screen);
            }

            await runner.AwaitMillis(1000);

            global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
            string fileName = $"UX_Screen_{screen}.png";
            string filePath = Path.Combine(tempDir, fileName);
            image.SavePng(filePath);
        }
    }
}
