using UnityEngine;

namespace _Game.Code.Spawning
{
    public enum SpawnKind
    {
        Player,
        Pet,
        OldMan
    }

    /// <summary>
    /// Marks a place an actor may start from. Lives on the three marker prefabs in
    /// Assets/_Game/Content/Level/Spawns/, so every marker dropped into the level
    /// carries it already and LevelBootstrapper picks new points up without rewiring.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private SpawnKind kind;

        public SpawnKind Kind => kind;
    }
}
