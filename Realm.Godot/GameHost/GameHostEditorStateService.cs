using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using System;

public class GameHostEditorStateService
{
	private readonly World _ecsWorld;

	public GameHostEditorStateService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	public bool GetBlockMode(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.BlockMode, true);
	}

	public void SetBlockMode(Entity worldEntity, bool value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(value, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetBlockLevelHeight(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.BlockLevelHeight, 4.0f);
	}

	public void SetBlockLevelHeight(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, value, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsLeft(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsLeft, -95.0f);
	}

	public void SetCameraBoundsLeft(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, value, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsRight(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsRight, 95.0f);
	}

	public void SetCameraBoundsRight(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, value, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsTop(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsTop, -95.0f);
	}

	public void SetCameraBoundsTop(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, value, s.CameraBoundsBottom, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public float GetCameraBoundsBottom(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsBottom, 125.0f);
	}

	public void SetCameraBoundsBottom(Entity worldEntity, float value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, value, s.SkyboxPath, s.HasUnsavedChanges)));
	}

	public string GetSkyboxPath(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, string>(worldEntity, s => s.SkyboxPath, "res://Assets/skybox_panoramic.jpg");
	}

	public void SetSkyboxPath(Entity worldEntity, string value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, value, s.HasUnsavedChanges)));
	}

	public bool GetHasUnsavedChanges(Entity worldEntity)
	{
		return _ecsWorld.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.HasUnsavedChanges, false);
	}

	public void SetHasUnsavedChanges(Entity worldEntity, bool value)
	{
		_ecsWorld.Mutate<EditorState>(worldEntity, (ref EditorState s) =>
			_ecsWorld.Set(worldEntity, new EditorState(s.BlockMode, s.BlockLevelHeight, s.CameraBoundsLeft, s.CameraBoundsRight, s.CameraBoundsTop, s.CameraBoundsBottom, s.SkyboxPath, value)));
	}
}
