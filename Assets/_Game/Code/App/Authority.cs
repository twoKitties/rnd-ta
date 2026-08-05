using FishNet;

namespace _Game.Code.App
{
    /// <summary>
    /// The one answer to "does this process decide": the server, or a process with no
    /// networking at all — pressing Play straight into Level is a supported mode, not
    /// an accident.
    ///
    /// Asked of the <em>process</em> (InstanceFinder) rather than of any object's
    /// spawn state, and that distinction has already been paid for twice. PlayerLife
    /// needs the process answer because a player leaving is killed on the way out,
    /// when its own object's answer is already changing; LevelGoal asked "has
    /// RaidState spawned" instead and a client in the window before the spawn message
    /// took the authority path, silently swallowing a delivery (netcode audit
    /// 2026-08-05). One property, so the formula cannot drift apart again.
    /// </summary>
    public static class Authority
    {
        public static bool DecidesHere =>
            InstanceFinder.NetworkManager == null || !InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted;
    }
}
