using _Game.Code.App;
using _Game.Code.Level;
using FishNet.Object;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Everything on an avatar that belongs to the person sitting in front of this
    /// screen, and nothing else: the camera, the ear, the HUD and the input. One
    /// process will hold up to four avatars and exactly one of them is ours.
    ///
    /// Unity allows a single active <see cref="AudioListener"/> per scene, and a
    /// Screen Space – Overlay canvas ignores whose avatar it hangs on — four of
    /// either is not a degraded picture, it is a broken one. The same goes for
    /// <see cref="FirstPersonBody"/>: applied to somebody else's avatar it would take
    /// their head off on our screen.
    ///
    /// Written as one list of references, the same idiom as PlayerLife.disableOnDeath,
    /// because the alternative — each component asking "am I local?" — spreads the
    /// question over seven files that have no business knowing the answer.
    /// </summary>
    public class LocalAvatar : NetworkBehaviour
    {
        [Tooltip("Switched on only for our own avatar: camera, AudioListener, the HUD " +
                 "canvas, PlayerController, PlayerInteractor and the two HUD scripts.")]
        [SerializeField] private Behaviour[] localOnly;

        [Tooltip("Hides the head we are looking out of. Ours only — everyone else " +
                 "must be seen whole.")]
        [SerializeField] private FirstPersonBody body;

        /// <summary>True once this avatar has been claimed as the local player's.</summary>
        public bool IsLocal { get; private set; }

        /// <summary>
        /// Ownership is the answer, and FishNet is the only thing that knows it: the
        /// avatar the server spawned for this connection is ours, the other three are
        /// not. Done here rather than in the level's entry point because a client
        /// receives the other avatars as replicated objects and never spawns them.
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply(IsOwner);

            // The session watches for this one going missing — it is what tells a client
            // that the raid it is standing in no longer exists. Told from here rather
            // than looked for from there, so the session layer keeps knowing nothing
            // about the level.
            if (IsOwner && App.RaidSession.Active != null)
            {
                App.RaidSession.Active.LocalAvatarSpawned();
            }

            // Registered from the avatar rather than by whoever created it, for the
            // same reason: three of the four were not created here at all. The scene's
            // entry point exists on every peer, so both the AI's view of the players
            // and the outcome's list are filled the same way everywhere. Exactly one of
            // the two is ever non-null — an avatar stands either in the hub or in a raid.
            if (LevelBootstrapper.Current != null)
            {
                LevelBootstrapper.Current.AddPlayer(gameObject);
            }

            if (Hub.HubBootstrapper.Current != null)
            {
                Hub.HubBootstrapper.Current.AddPlayer(gameObject);
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (IsOwner && App.RaidSession.Active != null)
            {
                App.RaidSession.Active.LocalAvatarLost();
            }

            if (LevelBootstrapper.Current != null)
            {
                LevelBootstrapper.Current.RemovePlayer(gameObject);
            }

            if (Hub.HubBootstrapper.Current != null)
            {
                Hub.HubBootstrapper.Current.RemovePlayer(gameObject);
            }
        }

        /// <summary>
        /// Claim this avatar, or disown it. Public because a scene without any
        /// networking — pressing Play straight into the level — still has to get a
        /// working camera, and there is nobody to ask about ownership there.
        /// </summary>
        public void Apply(bool isLocal)
        {
            IsLocal = isLocal;

            for (var i = 0; i < localOnly.Length; i++)
            {
                // Unity object: a destroyed one compares == null but is not a real
                // null, so `?.` and `??` would lie about it.
                if (localOnly[i] != null)
                {
                    localOnly[i].enabled = isLocal;
                }
            }

            if (isLocal && body != null)
            {
                body.ApplyFirstPersonView();
            }

            // The cursor is one per process, so it cannot be owned by a component that
            // exists four times over. It used to be locked and unlocked by
            // PlayerController.OnEnable/OnDisable, which meant that disabling three
            // other people's avatars unlocked ours at the start of every match, and
            // that a teammate being shot unlocked it again mid-game.
            if (isLocal)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>
        /// Whatever locked the cursor has to be what unlocks it, and this is the only
        /// thing that always runs: the avatar can go away without anybody choosing it —
        /// the host ends the session, the connection drops, the level is reloaded — and
        /// then EndScreenUI never opened, so nothing else had a reason to hand the
        /// cursor back. Measured 2026-08-05: after a disconnect the lobby came up with a
        /// locked, invisible cursor and its popup could not be clicked.
        ///
        /// Free is the right default here, because everything an avatar can be replaced
        /// by — the lobby, or a fresh avatar that locks it again in Apply — either wants
        /// a mouse or takes it back immediately.
        /// </summary>
        private void OnDestroy()
        {
            if (!IsLocal)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
