using System.Collections.Generic;
using _Game.Code.AI;
using UnityEngine;

namespace _Game.Code.OldMan
{
    /// <summary>
    /// One pellet of Old Man's shot, flying from the muzzle at a speed a player can
    /// dodge (MECHANICS.md 5.2, changed 2026-08-06: death moved from the moment of
    /// firing to the moment of impact). It flies a straight line towards where the
    /// victim stood when the trigger was pulled — a sidestep after the bang is the
    /// dodge — and is stopped by the same geometry that cuts his sight.
    ///
    /// Not a NetworkObject: every peer grows its own from ShotFlash's blink, which
    /// already travels, so the flight costs the wire nothing. Only the authority's
    /// copy is armed; a client's pellet is a picture that stops on walls and flies
    /// straight through players — a death it caused arrives through PlayerLife's
    /// replicated flag, the same as ever. PlayerLife.Kill drops a client's call
    /// anyway, so arming is about honesty, not safety.
    /// </summary>
    public class ShotPellet : MonoBehaviour
    {
        [Tooltip("Flight speed, world m/s. The player sprints at 4 (MECHANICS.md " +
                 "section 2): point blank is certain death, across a room there is " +
                 "time to step off the line.")]
        [SerializeField] private float speed = 10f;

        [Tooltip("How close to the middle of a player's capsule counts as a hit, m. " +
                 "The alien is 0.5 m tall, so this sphere is roughly the whole body.")]
        [SerializeField] private float hitRadius = 0.25f;

        [Tooltip("How far it flies before giving up, m. Sight range is 12, so " +
                 "nothing he can shoot at is ever farther than this.")]
        [SerializeField] private float maxDistance = 20f;

        [Tooltip("What stops it: BlockedArea + Door — exactly what breaks his line " +
                 "of sight, so cover against being seen is cover against being shot.")]
        [SerializeField] private LayerMask blockers;

        [Header("Impact")]
        [Tooltip("Spawned at the point it stops, aligned to the surface (or, on a " +
                 "player hit, facing back along the shot). No DecalProjector: URP's " +
                 "renderer assets carry no Decal Renderer Feature in this project. " +
                 "Its own AudioSource plays the impact clip — 3D, Logarithmic, same " +
                 "min 3 / max 60 as the shot's own source (MECHANICS.md section 2) — " +
                 "so the rolloff is set on the prefab, the same idiom as every other " +
                 "sound in this project, not passed in code.")]
        [SerializeField] private GameObject hitMarkPrefab;

        [Tooltip("Nudge along the normal so the mark does not z-fight the surface it " +
                 "sits on, world m.")]
        [SerializeField] private float hitMarkOffset = 0.01f;

        [Tooltip("Seconds before the spawned mark is destroyed.")]
        [SerializeField] private float hitMarkLifetime = 8f;

        [Tooltip("Played once, through hitMarkPrefab's own AudioSource, at full " +
                 "volume — the same way ShotFlash plays the bang.")]
        [SerializeField] private AudioClip impactClip;

        private Vector3 _direction;
        private float _flown;
        private bool _armed;
        private IReadOnlyList<SensedPlayer> _players;

        /// <summary>
        /// Sends it on its way. Armed means this process decides deaths — the
        /// server, or no networking at all, passed in by ShotFlash; the player list is
        /// LevelBootstrapper's live one, null on a peer that has none — which is
        /// only ever an unarmed one.
        /// </summary>
        public void Launch(Vector3 origin, Vector3 direction, bool armed,
            IReadOnlyList<SensedPlayer> players)
        {
            transform.position = origin;
            _direction = direction.normalized;
            transform.rotation = Quaternion.LookRotation(_direction);
            _armed = armed;
            _players = players;
        }

        private void Update()
        {
            var step = speed * Time.deltaTime;

            // Ray per step rather than a collider: at 10 m/s a frame is ~0.16 m, and
            // a swept test cannot tunnel through a wall the way an overlap could.
            RaycastHit wall;
            // Queries Hit Triggers is on project-wide: a trigger added to BlockedArea or
            // Door later would otherwise stop every shot.
            if (Physics.Raycast(transform.position, _direction, out wall, step, blockers,
                    QueryTriggerInteraction.Ignore))
            {
                SpawnImpact(wall.point, wall.normal);
                Destroy(gameObject);
                return;
            }

            // Tested on every peer, not only the armed one: the mark and the thud
            // are the only thing a victim ever sees of the shot, and a client whose
            // pellet flew on would see it strike the wall behind them instead.
            if (HitPlayer(step))
            {
                Destroy(gameObject);
                return;
            }

            transform.position += _direction * step;
            _flown += step;

            if (_flown >= maxDistance)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// A capsule-middle proximity test against this frame's flight segment, not
        /// physics: the player list is already in hand (MECHANICS.md 7.6), and the
        /// avatar's capsule middle is the same point his sight aims at. Runs on every
        /// peer for the picture; only the armed copy kills.
        /// </summary>
        private bool HitPlayer(float step)
        {
            if (_players == null)
            {
                return false;
            }

            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (!player.IsAlive)
                {
                    continue;
                }

                var toPlayer = player.AimPoint - transform.position;
                var along = Mathf.Clamp(Vector3.Dot(toPlayer, _direction), 0f, step);
                var miss = toPlayer - _direction * along;

                if (miss.sqrMagnitude > hitRadius * hitRadius)
                {
                    continue;
                }

                // Killing stays the armed copy's alone; the death reaches everyone
                // else through PlayerLife's replicated flag.
                if (_armed)
                {
                    player.Kill();
                    Debug.Log($"{name} hit {player.Transform.name} (MECHANICS.md 5.2).");
                }

                // No surface normal on a player hit — the mark faces back along the
                // shot, toward the muzzle it came from.
                SpawnImpact(transform.position + _direction * along, -_direction);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The mark: aligned to <paramref name="normal"/>, nudged off the surface to
        /// avoid z-fighting, destroyed after its own lifetime. Its own AudioSource
        /// plays the thud once — not AudioSource.PlayClipAtPoint, which would spin up
        /// a source with Unity's defaults instead of the rolloff set on the prefab.
        /// Runs on every peer — the pellet already does, no netcode added.
        /// </summary>
        private void SpawnImpact(Vector3 point, Vector3 normal)
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (hitMarkPrefab == null)
            {
                return;
            }

            var mark = Instantiate(hitMarkPrefab, point + normal * hitMarkOffset,
                Quaternion.LookRotation(normal));

            if (impactClip != null)
            {
                var markSource = mark.GetComponent<AudioSource>();
                if (markSource != null)
                {
                    markSource.PlayOneShot(impactClip);
                }
            }

            Destroy(mark, hitMarkLifetime);
        }
    }
}
