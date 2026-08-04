namespace _Game.Code.Spawning
{
    /// <summary>
    /// Draws which spawn point each actor gets — points without repeats, one per
    /// actor. A plain class, not a MonoBehaviour: it holds no scene references, and
    /// the level's entry point owns it.
    ///
    /// Under MECHANICS.md 7.4 and 7.7 the layout is shared state: it is drawn once, by
    /// one side, and the result is what travels. That is why this draws indices and
    /// creates nothing — instantiating is the state change, and it belongs to whoever
    /// applies the layout, not to whoever chose it.
    /// </summary>
    public class ActorSpawner
    {
        private readonly System.Random _random;

        /// <param name="seed">Zero draws a fresh layout every run; anything else
        /// repeats the same one, which is what makes a spawn bug reproducible.</param>
        public ActorSpawner(int seed)
        {
            // A zero seed is resolved to a real number here rather than left to
            // new System.Random()'s own clock, so that the layout a host drew can be
            // named, logged and sent — an unseeded sequence cannot be repeated by
            // anybody, including us.
            Seed = seed == 0 ? new System.Random().Next(1, int.MaxValue) : seed;

            // System.Random, not UnityEngine.Random: the latter is one global sequence
            // for the whole process, and MECHANICS.md 7.3 keeps state off statics.
            _random = new System.Random(Seed);
        }

        /// <summary>The seed actually in use, never zero. This is the layout's name.</summary>
        public int Seed { get; }

        /// <summary>
        /// Which point each actor stands on: entry <c>i</c> is the point index for
        /// actor <c>i</c>. Shorter than <paramref name="actorCount"/> when there are
        /// not enough points — the caller decides how loudly to complain, because only
        /// it knows what was left unplaced.
        ///
        /// Pure and side-effect free on purpose (MECHANICS.md 7.4): the same seed gives
        /// the same array on any machine, so a host can re-run it over a request, or
        /// send the array instead.
        /// </summary>
        public int[] Draw(int actorCount, int pointCount)
        {
            if (actorCount <= 0 || pointCount <= 0)
            {
                return new int[0];
            }

            // Fisher-Yates over the point indices, stopped once enough are drawn.
            var order = new int[pointCount];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            var take = actorCount < pointCount ? actorCount : pointCount;
            var drawn = new int[take];
            for (var i = 0; i < take; i++)
            {
                var j = _random.Next(i, order.Length);
                (order[i], order[j]) = (order[j], order[i]);
                drawn[i] = order[i];
            }

            return drawn;
        }
    }
}
