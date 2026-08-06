using UnityEngine;

namespace _Game.Code.Audio
{
    /// <summary>What is underfoot, as far as the ear is concerned.</summary>
    public enum SurfaceKind
    {
        /// <summary>The yard. Also the fallback for anything unmarked.</summary>
        Grass,

        /// <summary>Inside the house.</summary>
        Wood
    }

    /// <summary>
    /// Marks a piece of the world as a surface you can hear yourself walk on. Same
    /// idiom as <see cref="_Game.Code.Spawning.SpawnPoint"/>: the object names its own
    /// kind, so new geometry is picked up by dropping a component on it rather than by
    /// editing a list somewhere else.
    ///
    /// **It is found with `GetComponentInParent`, not on the collider itself**, and
    /// that is what keeps this cheap: one marker on the root of HouseOneFloor.prefab
    /// covers all 25 floor tiles, and the lawn needs none at all because Grass is the
    /// fallback. It also makes the exception free — a rug in one room is this component
    /// on that room's floor group, which wins because GetComponentInParent returns the
    /// *nearest* ancestor. Data, not code.
    ///
    /// A layer would have been the other candidate and is worse here: a new floor layer
    /// would have to be added to the NavMeshSurface's Include Layers and the mesh
    /// rebaked, which is a lot of risk to a load-bearing setting in exchange for a
    /// footstep.
    /// </summary>
    public class FootstepSurface : MonoBehaviour
    {
        [SerializeField] private SurfaceKind kind = SurfaceKind.Wood;

        public SurfaceKind Kind => kind;
    }
}
