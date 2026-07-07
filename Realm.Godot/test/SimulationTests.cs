namespace Realm.Godot.Tests;

using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class SimulationTests
{
    [TestCase]
    public async Task TestMeleePathingAroundTree()
    {
        if (LobbyManager.Instance == null)
        {
            return;
        }

        LobbyManager.Instance.IsSinglePlayer = true;
        
        PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
        isHostProp?.SetValue(LobbyManager.Instance, true);

        LobbyManager.Instance.IsGameStarted = true;
        LobbyManager.Instance.ActiveMapName = "melee";
        LobbyManager.Instance.PlayerList.Clear();

        LobbyManager.PlayerInfo playerInfo = new LobbyManager.PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
            Name = LobbyManager.Instance.AuthenticatedUsername,
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new global::Godot.Color(0.8f, 0.1f, 0.1f),
            IsHost = true,
            Latency = "0 ms",
            Jitter = "0 ms",
            PacketLoss = "0%",
            BinaryVersion = LobbyManager.GameBinaryVersion
        };

        PropertyInfo localPlayerProp = typeof(LobbyManager).GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Instance);
        localPlayerProp?.SetValue(LobbyManager.Instance, playerInfo);

        LobbyManager.Instance.PlayerList.Add(playerInfo);

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1000);

        GameHost gameHost = GameHost.Instance;
        if (gameHost == null)
        {
            return;
        }

        Unit3D worker = gameHost.AllUnits.FirstOrDefault(u => u.UnitId == "worker" && !u.IsEnemy);
        if (worker == null)
        {
            return;
        }

        gameHost.SelectedUnits.Clear();
        gameHost.SelectedUnits.Add(worker);
        worker.IsSelected = true;
        InGameHUD.Instance?.RefreshUI(gameHost.SelectedUnits);

        System.Numerics.Vector3 destination = new System.Numerics.Vector3(-20f, 0f, -50f);
        Realm.MapAPI.IUnit unitWrapper = gameHost.GetUnitWrapper(worker.Entity);
        unitWrapper.MoveTo(destination);

        string tempDir = Path.Combine(Path.GetTempPath(), "Realm_Simulation_Screenshots");
        Directory.CreateDirectory(tempDir);

        for (int i = 1; i <= 15; i++)
        {
            await runner.AwaitMillis(1000);

            global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
            string fileName = $"Simulation_Step_{i:00}.png";
            string filePath = Path.Combine(tempDir, fileName);
            image.SavePng(filePath);
        }
    }
}
