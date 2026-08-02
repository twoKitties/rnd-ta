using UnityEngine;
using UnityEngine.Rendering;

namespace _Game.Code.Player
{
    /// <summary>
    /// Hides the parts of the avatar that the first-person camera sits inside.
    /// The camera sits at eye height, which is inside the Head, Eyes and Ear meshes,
    /// with Hair directly above it — Body stays visible so a player can still see
    /// themselves, and whatever they are carrying, when looking down.
    /// <para>
    /// Applied only to the avatar the local player looks through: everyone else must
    /// see a whole character. That is why <see cref="PlayerController"/> calls it
    /// rather than this component applying itself on Awake.
    /// </para>
    /// </summary>
    public class FirstPersonBody : MonoBehaviour
    {
        [Tooltip("Renderers the first-person camera sits inside or under.")]
        [SerializeField] private Renderer[] hiddenInFirstPerson;

        /// <summary>
        /// Drops the listed renderers out of the camera while keeping them in the
        /// shadow pass, so the avatar still casts a complete silhouette.
        /// </summary>
        public void ApplyFirstPersonView()
        {
            foreach (var meshRenderer in hiddenInFirstPerson)
            {
                // Unity object: a destroyed one compares == null but is not a real
                // null, so `?.` would lie about it.
                if (meshRenderer == null)
                {
                    continue;
                }

                if (meshRenderer.shadowCastingMode == ShadowCastingMode.Off)
                {
                    // This one was already excluded from shadows by the artist (Hair is),
                    // so there is no silhouette to preserve. Switch it off outright rather
                    // than silently turning its shadow on via ShadowsOnly.
                    meshRenderer.enabled = false;
                    continue;
                }

                meshRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }
    }
}
