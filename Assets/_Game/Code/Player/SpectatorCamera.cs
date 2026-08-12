using System.Collections.Generic;
using _Game.Code.AI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Game.Code.Player
{
    /// <summary>
    /// What a shot player watches (MECHANICS.md 3.7): the raid goes on, seen out of a
    /// living teammate's head, with Previous/Next to change whose.
    ///
    /// It moves our own camera instead of reparenting it to theirs. Two reasons, both
    /// measured elsewhere in this project: the avatar root is scaled 0.1, so
    /// reparenting either shrinks the camera tenfold or has to fight the scale back
    /// out; and the carried pet already solves the same problem the same way
    /// (Pet.LateUpdate). Copying a world transform is immune to both.
    ///
    /// Belongs to the local avatar alone and rides in LocalAvatar's list for that: on
    /// somebody else's avatar it would drag a camera nobody looks through, and read a
    /// keyboard that is not theirs.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        [Tooltip("Our own camera. Driven only after death — PlayerController owns it " +
                 "while alive and is switched off by PlayerLife.")]
        [SerializeField] private Transform cameraRoot;

        private PlayerLife _life;
        private InputSystem_Actions _input;

        // Who we are watching, and the transform we copy. The SensedPlayer rather than
        // an index: the roster is live — a player can leave mid-raid — and an index
        // into a list that shrinks points at somebody else the frame afterwards.
        private SensedPlayer _target;
        private Transform _eyes;

        // The target's own head, hidden while we are inside it. Held so it can be put
        // back the moment we look at somebody else — the camera sits between their eyes,
        // so without this a spectator sees the inside of their teammate's face.
        private FirstPersonBody _targetBody;

        // Where our own camera sits when it is ours. Copying a teammate's world
        // transform overwrites it, and nothing else ever puts it back: PlayerController
        // is switched off by death, so the moment there is nobody left to watch the view
        // would freeze at the last head it was inside — which is what "the client's
        // camera disappears" was (reported 2026-08-05, both players dead).
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private bool _detached;

        /// <summary>Key names for the HUD hint. Binding 0 is the keyboard, 1 the gamepad.</summary>
        public string PreviousDisplayName => DisplayName(_input == null ? null : _input.Player.Previous);

        public string NextDisplayName => DisplayName(_input == null ? null : _input.Player.Next);

        // _input is null after a domain reload that left this component disabled — see
        // OnDestroy.
        private static string DisplayName(InputAction action)
        {
            return action == null
                ? string.Empty
                : action.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        private void Awake()
        {
            _life = GetComponent<PlayerLife>();
            _input = new InputSystem_Actions();

            // Local, not world: this is the pose relative to an avatar that keeps
            // moving, and the avatar root is scaled 0.1 on top of it.
            if (cameraRoot != null)
            {
                _homePosition = cameraRoot.localPosition;
                _homeRotation = cameraRoot.localRotation;
            }
        }

        private void OnEnable()
        {
            // A domain reload re-runs OnEnable on a live instance without re-running
            // Awake, and _input is not serialized — the wrapper Awake made is gone.
            if (_input == null)
            {
                _input = new InputSystem_Actions();
            }

            _input.Player.Enable();
        }

        private void OnDisable()
        {
            _input.Player.Disable();

            // Leaving, the level ending — whatever switched us off, the teammate we were
            // looking out of is somebody else's view now and needs their head back.
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_targetBody != null)
            {
                _targetBody.RestoreFullBody();
            }
        }

        private void OnDestroy()
        {
            // Null after a domain reload if this instance stayed disabled throughout
            // (LocalAvatar keeps it off on every avatar but ours), so OnEnable never
            // rebuilt it.
            if (_input != null)
            {
                _input.Dispose();
            }
        }

        private void Update()
        {
            if (_life == null || !_life.IsDead)
            {
                return;
            }

            // The one we were watching was shot too, or left. Asked every frame rather
            // than subscribed to, because both of those happen through paths that know
            // nothing about a spectator.
            if (!CanWatch(_target))
            {
                Step(1);
            }

            if (_input.Player.Next.WasPressedThisFrame())
            {
                Step(1);
            }
            else if (_input.Player.Previous.WasPressedThisFrame())
            {
                Step(-1);
            }
        }

        // LateUpdate, after every avatar has moved and after their own Look has pitched
        // the head: a frame late would show the target's previous position, which reads
        // as the camera lagging behind the player being watched.
        private void LateUpdate()
        {
            // Unity objects: a destroyed one compares == null but is not a real null.
            if (cameraRoot == null)
            {
                return;
            }

            var watching = _life != null && _life.IsDead && _eyes != null;

            if (watching)
            {
                cameraRoot.SetPositionAndRotation(_eyes.position, _eyes.rotation);
                _detached = true;
                return;
            }

            // Nobody left to look through — everyone is dead, or the one we were
            // watching has gone. The camera comes home rather than staying stuck in the
            // air where their head used to be, which reads as the view going dead. Once,
            // not every frame: while alive PlayerController owns this transform and must
            // not be fought.
            if (_detached)
            {
                _detached = false;
                cameraRoot.localPosition = _homePosition;
                cameraRoot.localRotation = _homeRotation;
            }
        }

        // One step round the living, in either direction, starting from whoever we are
        // watching now. Wraps, and gives up after a full turn — which is the case where
        // nobody is left alive; the raid is already lost by then (MECHANICS.md 6), so
        // there is nothing to show and we simply keep our own view.
        private void Step(int direction)
        {
            var players = LevelBootstrapper.Current == null ? null : LevelBootstrapper.Current.SensedPlayers;
            if (players == null || players.Count == 0)
            {
                SetTarget(null);
                return;
            }

            var from = IndexOf(players, _target);

            for (var step = 1; step <= players.Count; step++)
            {
                // Positive remainder: C# keeps the sign of the dividend, so a backwards
                // step from index 0 would otherwise land on -1.
                var at = ((from + direction * step) % players.Count + players.Count) % players.Count;
                if (CanWatch(players[at]))
                {
                    SetTarget(players[at]);
                    return;
                }
            }

            SetTarget(null);
        }

        private void SetTarget(SensedPlayer player)
        {
            // Whoever we were watching gets their head back first, and unconditionally:
            // they may already be dead or gone, and either way they must not be left
            // headless on our screen.
            // Unity object: a destroyed one compares == null but is not a real null.
            if (_targetBody != null)
            {
                _targetBody.RestoreFullBody();
            }

            _target = player;
            _eyes = null;
            _targetBody = null;

            if (player == null || player.Transform == null)
            {
                return;
            }

            // Resolved on the switch rather than every frame, and from the component
            // that owns the reference rather than by looking for a child called
            // "CameraRoot" — the avatar is a prefab that exists up to four times over.
            var controller = player.Transform.GetComponent<PlayerController>();
            if (controller != null)
            {
                _eyes = controller.CameraRoot;
            }

            // Only on this machine: renderers are local, so hiding a teammate's head
            // here is invisible to them and to everybody else.
            _targetBody = player.Transform.GetComponent<FirstPersonBody>();
            if (_targetBody != null)
            {
                _targetBody.ApplyFirstPersonView();
            }
        }

        // Alive, and not us: watching our own corpse is what we are trying to get away
        // from.
        private bool CanWatch(SensedPlayer player)
        {
            return player != null && player.IsAlive && player.Transform != transform;
        }

        private static int IndexOf(IReadOnlyList<SensedPlayer> players, SensedPlayer player)
        {
            if (player == null)
            {
                // Nobody yet: the first step lands on index 0.
                return -1;
            }

            for (var i = 0; i < players.Count; i++)
            {
                if (players[i] == player)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
