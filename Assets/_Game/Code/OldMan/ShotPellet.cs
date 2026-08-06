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
        [Tooltip("Flight speed, world m/s. The player sprints at 5: point blank is " +
                 "certain death, across a room there is time to step off the line.")]
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

        private Vector3 _direction;
        private float _flown;
        private bool _armed;
        private IReadOnlyList<SensedPlayer> _players;

        /// <summary>
        /// Sends it on its way. Armed means this process decides deaths
        /// (Authority.DecidesHere, passed in by ShotFlash); the player list is
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
            if (Physics.Raycast(transform.position, _direction, out wall, step, blockers))
            {
                Destroy(gameObject);
                return;
            }

            if (_armed && HitPlayer(step))
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
        /// avatar's capsule middle is the same point his sight aims at.
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

                player.Kill();
                Debug.Log($"{name} hit {player.Transform.name} (MECHANICS.md 5.2).");
                return true;
            }

            return false;
        }
    }
}
