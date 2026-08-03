using System.Collections.Generic;
using _Game.Code.Pets;
using _Game.Code.Player;
using UnityEngine;

namespace _Game.Code.Level
{
    /// <summary>
    /// Owns the outcome of the match: how many animals have been handed over, and
    /// whether the raid is won (MECHANICS.md section 6). Lives next to Bootstrapper,
    /// which binds it to the actors it spawned — nothing here searches the scene
    /// (7.6). Under 7.4 this is the authority: today it runs locally, tomorrow only
    /// on the host.
    /// </summary>
    public class LevelGoal : MonoBehaviour
    {
        [SerializeField] private BeamZone beamZone;

        public int Delivered { get; private set; }
        public int Total { get; private set; }
        public bool IsWon { get; private set; }
        public bool IsLost { get; private set; }

        private readonly List<GameObject> _players = new List<GameObject>();

        public void Bind(IReadOnlyList<GameObject> players, IReadOnlyList<GameObject> pets)
        {
            _players.Clear();
            foreach (var player in players)
            {
                if (player != null)
                {
                    _players.Add(player);
                }
            }

            Total = pets == null ? 0 : pets.Count;
            Delivered = 0;
        }

        /// <summary>
        /// The one place an animal is put down. Whether it counts is decided by where
        /// the carrier stood, not by where the animal ended up — MECHANICS.md 4.5.
        /// An animal that merely runs through the beam hands itself over to nobody.
        /// </summary>
        public void ReleaseCarried(PlayerHands hands)
        {
            if (hands == null || hands.IsEmpty)
            {
                return;
            }

            var pet = hands.Carried;
            var handedOver = beamZone != null && beamZone.Contains(hands.transform.position);

            pet.Release();

            if (!handedOver)
            {
                return;
            }

            pet.Deliver();
            Delivered++;
            Debug.Log($"Beam: {pet.name} handed over ({Delivered}/{Total}).");
        }

        private void Update()
        {
            if (IsWon || IsLost)
            {
                return;
            }

            // The order is not cosmetic (MECHANICS.md section 6): "everyone left is
            // inside the beam" is true of an empty set, so the death of the last
            // player after the last animal would otherwise be a win and a loss at
            // once. There is no death in the game yet, so the living list is still
            // everyone — block 5 fills this in.
            var living = 0;
            var livingInsideBeam = 0;
            foreach (var player in _players)
            {
                if (player == null || !player.activeInHierarchy)
                {
                    continue;
                }

                living++;
                if (beamZone != null && beamZone.Contains(player.transform.position))
                {
                    livingInsideBeam++;
                }
            }

            if (living == 0)
            {
                IsLost = true;
                Debug.Log("Raid lost: every player is dead.");
                return;
            }

            if (Total > 0 && Delivered >= Total && livingInsideBeam == living)
            {
                IsWon = true;
                Debug.Log($"Raid won: {Delivered}/{Total} animals aboard and every player is in the beam.");
            }
        }
    }
}
