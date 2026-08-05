using UnityEngine;
using UnityEngine.Rendering;

namespace _Game.Code.Player
{
    /// <summary>
    /// Hides the parts of the avatar that a first-person camera sits inside.
    /// The camera sits at eye height, which is inside the Head, Eyes and Ear meshes,
    /// with Hair directly above it — Body stays visible so a player can still see
    /// themselves, and whatever they are carrying, when looking down.
    /// <para>
    /// Applied only to the avatar somebody is looking through, which is not always the
    /// same avatar: <see cref="LocalAvatar"/> applies it to our own, and
    /// <see cref="SpectatorCamera"/> moves it from teammate to teammate as a dead
    /// player changes who they are watching. That is why it is reversible — a head
    /// hidden for a spectator has to come back the moment they look elsewhere, or the
    /// teammate they were watching stays headless for the rest of the raid.
    /// </para>
    /// </summary>
    public class FirstPersonBody : MonoBehaviour
    {
        [Tooltip("Renderers the first-person camera sits inside or under.")]
        [SerializeField] private Renderer[] hiddenInFirstPerson;

        // What the artist set, before anybody hid anything. Captured once, on the first
        // hide, rather than in Awake: a renderer's shadow mode is authored data and this
        // is the only thing that ever changes it, so the first capture is always the
        // original.
        private ShadowCastingMode[] _shadows;
        private bool[] _enabled;
        private bool _captured;

        private bool _hidden;

        /// <summary>
        /// Drops the listed renderers out of the camera while keeping them in the
        /// shadow pass, so the avatar still casts a complete silhouette.
        /// </summary>
        public void ApplyFirstPersonView()
        {
            if (_hidden)
            {
                return;
            }

            Capture();
            _hidden = true;

            for (var i = 0; i < hiddenInFirstPerson.Length; i++)
            {
                var meshRenderer = hiddenInFirstPerson[i];

                // Unity object: a destroyed one compares == null but is not a real
                // null, so `?.` would lie about it.
                if (meshRenderer == null)
                {
                    continue;
                }

                if (_shadows[i] == ShadowCastingMode.Off)
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

        /// <summary>
        /// Puts the head back. Called when a spectator looks away from this avatar —
        /// everybody else has to see a whole character again.
        /// </summary>
        public void RestoreFullBody()
        {
            if (!_hidden || !_captured)
            {
                return;
            }

            _hidden = false;

            for (var i = 0; i < hiddenInFirstPerson.Length; i++)
            {
                var meshRenderer = hiddenInFirstPerson[i];
                if (meshRenderer == null)
                {
                    continue;
                }

                meshRenderer.shadowCastingMode = _shadows[i];
                meshRenderer.enabled = _enabled[i];
            }
        }

        private void Capture()
        {
            if (_captured)
            {
                return;
            }

            _captured = true;
            _shadows = new ShadowCastingMode[hiddenInFirstPerson.Length];
            _enabled = new bool[hiddenInFirstPerson.Length];

            for (var i = 0; i < hiddenInFirstPerson.Length; i++)
            {
                var meshRenderer = hiddenInFirstPerson[i];
                if (meshRenderer == null)
                {
                    continue;
                }

                _shadows[i] = meshRenderer.shadowCastingMode;
                _enabled[i] = meshRenderer.enabled;
            }
        }
    }
}
