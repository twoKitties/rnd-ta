using System.Collections.Generic;
using _Game.Code.AI;
using _Game.Code.Pets;
using _Game.Code.Player;
using UnityEngine;

namespace _Game.Code.Level
{
    /// <summary>
    /// Owns the outcome of the match: how many animals have been handed over, and
    /// whether the raid is won (MECHANICS.md section 6). Lives next to LevelBootstrapper,
    /// which binds it to the actors it spawned — nothing here searches the scene
    /// (7.6). Under 7.4 this is the authority: today it runs locally, tomorrow only
    /// on the host.
    /// </summary>
    public class LevelGoal : MonoBehaviour
    {
        [SerializeField] private BeamZone beamZone;

        // Replicated when a raid is networked, local when it is not. Deliberately a
        // plain MonoBehaviour holding a reference rather than a NetworkBehaviour: this
        // component shares a GameObject with LevelBootstrapper, and FishNet deactivates
        // a scene object whose NetworkObject fails to come up — see the note on
        // RaidState.
        private static RaidState Shared => RaidState.Current;

        public int Delivered => Shared == null ? _delivered : Shared.Delivered;
        public int Total => Shared == null ? _total : Shared.Total;
        public bool IsWon => Shared == null ? _isWon : Shared.IsWon;
        public bool IsLost => Shared == null ? _isLost : Shared.IsLost;

        // The same four, for a level played with no networking at all. One of the two
        // sets is live at a time and the properties above pick which.
        private int _delivered;
        private int _total;
        private bool _isWon;
        private bool _isLost;

        // Whether the animal count has been handed to RaidState yet. It cannot be done
        // in Bind: the object is spawned in the same frame and does not exist for us
        // until FishNet says so.
        private bool _totalPushed;

        // Who decides. Off the network we do; on it, the server.
        private static bool IsAuthority => Shared == null || Shared.IsAuthority;

        // The same view of a player the brains get. It already answers the only two
        // questions the outcome asks — where are they, and are they still alive — and
        // it caches the PlayerLife lookup, which matters because this runs every
        // frame (MECHANICS.md 7.6).
        //
        // The live list, not a copy of it — the same reference both brains hold. The
        // roster is not fixed any more: a player who joins after this was bound has to
        // exist for the outcome, and one who left has to stop existing for it.
        private IReadOnlyList<SensedPlayer> _players;

        public void Bind(IReadOnlyList<SensedPlayer> players, IReadOnlyList<GameObject> pets)
        {
            // The roster is read locally by everyone — the AI and the HUD both look at
            // it. The counters are shared state, and only the authority sets them.
            _players = players;

            if (!IsAuthority)
            {
                return;
            }

            // Recorded locally either way. Spawning RaidState does not make it exist
            // this frame — FishNet raises OnStartClient later — so binding cannot rely
            // on it being there yet, and Update below pushes the number across as soon
            // as it is.
            _total = pets == null ? 0 : pets.Count;
            _delivered = 0;
            _totalPushed = false;
        }

        /// <summary>
        /// The rule, on its own: would letting go right now hand the animal over.
        /// Whether it counts is decided by where the carrier stands, not by where the
        /// animal ends up (MECHANICS.md 4.5) — an animal that merely runs through the
        /// beam hands itself over to nobody.
        ///
        /// Pure, and split out for the same reason Pet.CanBeTakenBy is: under 7.4 the
        /// host has to be able to re-run this over a request that arrived over the
        /// wire, without the asking client's answer applying anything.
        /// </summary>
        public bool CountsAsDelivery(PlayerHands hands)
        {
            return hands != null && !hands.IsEmpty && beamZone != null &&
                   beamZone.Contains(hands.transform.position);
        }

        /// <summary>
        /// The one place an animal is put down. Answers whether letting go counted as
        /// handing the animal over, so that the caller — a dying carrier, a player
        /// pressing the button, tomorrow the host answering a request — does not have
        /// to ask the rule a second time.
        /// </summary>
        public bool ReleaseCarried(PlayerHands hands)
        {
            if (hands == null || hands.IsEmpty)
            {
                return false;
            }

            // Asked before the animal leaves the hands, since the rule is about the
            // carrier and Release empties them.
            var handedOver = CountsAsDelivery(hands);
            var pet = hands.Carried;

            pet.Release();

            if (!handedOver)
            {
                return false;
            }

            ApplyDelivery(pet);

            // Deliberately outside ApplyDelivery. That method is the state change every
            // peer will run — the animal disappears into the saucer on all four
            // screens. This line is bookkeeping owned by whoever decides, and running
            // it everywhere would count the same animal up to four times.
            if (IsAuthority)
            {
                if (Shared == null)
                {
                    _delivered++;
                }
                else
                {
                    Shared.AddDelivery();
                }

                Debug.Log($"Beam: {pet.name} handed over ({Delivered}/{Total}).");
            }

            return true;
        }

        // The state change itself, the way Pet.ApplyCarry is: this is the line a
        // netcode pass drives from replicated state so every peer sees the same thing.
        private void ApplyDelivery(Pet pet)
        {
            pet.Deliver();
        }

        /// <summary>
        /// The rule, on its own: has the raid ended, and how. Pure — it reads the
        /// roster and the beam and writes nothing, so the authority can re-run it at
        /// any moment and a peer can be handed the answer instead of computing one
        /// (MECHANICS.md 7.4).
        /// </summary>
        public bool EvaluateOutcome(out bool won, out bool lost)
        {
            won = false;
            lost = false;

            // Nobody bound yet — an unopened scene, a missing reference on LevelBootstrapper,
            // and tomorrow a client whose player list fills in after the first frame.
            // Without this the count below reads "no living players" and latches the
            // loss on frame one, permanently: an empty set has no outcome to judge.
            if (_players == null || _players.Count == 0)
            {
                return false;
            }

            // The order is not cosmetic (MECHANICS.md section 6): "everyone left is
            // inside the beam" is true of an empty set, so the death of the last
            // player after the last animal would otherwise be a win and a loss at
            // once. Liveness is PlayerLife's answer, not activeInHierarchy's: a shot
            // player stays in the scene as a spectator camera (3.7), so the avatar is
            // still active and would otherwise be counted among the living for ever.
            var living = 0;
            var livingInsideBeam = 0;
            foreach (var player in _players)
            {
                if (!player.IsAlive)
                {
                    continue;
                }

                living++;
                if (beamZone != null && beamZone.Contains(player.Transform.position))
                {
                    livingInsideBeam++;
                }
            }

            if (living == 0)
            {
                lost = true;
                return true;
            }

            won = Total > 0 && Delivered >= Total && livingInsideBeam == living;
            return won;
        }

        /// <summary>
        /// Latch the outcome. The state change, the way ApplyDelivery is: every peer
        /// runs this, but only the authority decides what to pass in.
        /// </summary>
        public void ApplyOutcome(bool won, bool lost)
        {
            if (IsWon || IsLost || !IsAuthority)
            {
                return;
            }

            if (Shared == null)
            {
                _isWon = won;
                _isLost = lost;
            }
            else
            {
                Shared.SetOutcome(won, lost);
            }

            if (lost)
            {
                Debug.Log("Raid lost: every player is dead.");
            }
            else if (won)
            {
                Debug.Log($"Raid won: {Delivered}/{Total} animals aboard and every player is in the beam.");
            }
        }

        private void Update()
        {
            // Only the authority judges; everybody else is told, through RaidState. The
            // shape of the code did not change when that became true (MECHANICS.md
            // 7.4) — only who reaches this line.
            if (!IsAuthority)
            {
                return;
            }

            // The moment the replicated state arrives, it gets the number the level was
            // laid out with. Once only — after this the counter is its own.
            if (!_totalPushed && Shared != null)
            {
                Shared.SetTotal(_total);
                _totalPushed = true;
            }

            if (IsWon || IsLost)
            {
                return;
            }

            if (EvaluateOutcome(out var won, out var lost))
            {
                ApplyOutcome(won, lost);
            }
        }
    }
}
