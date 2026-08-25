using UnityEngine;

/// <summary>
/// Static data bag written by GameManager before loading the Loading scene,
/// read and cleared by LoadingScreenUI once it starts.
/// </summary>
public static class SceneLoader
{
    public static string TargetScene   { get; private set; }
    public static Vector2? SpawnOverride { get; private set; }
    public static int? EntryWalkDirection { get; private set; }
    public static float EntryWalkDuration { get; private set; }

    public static void Set(string scene, Vector2? spawn = null, int? entryWalkDirection = null, float entryWalkDuration = 0.3f)
    {
        TargetScene   = scene;
        SpawnOverride = spawn;
        EntryWalkDirection = entryWalkDirection;
        EntryWalkDuration = entryWalkDuration;
    }

    public static void Clear()
    {
        TargetScene   = null;
        SpawnOverride = null;
        EntryWalkDirection = null;
    }
}
