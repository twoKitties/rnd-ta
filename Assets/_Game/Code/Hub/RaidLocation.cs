using System;

namespace _Game.Code.Hub
{
    /// <summary>One place the saucer can fly to: the name on the button and the scene behind it.</summary>
    [Serializable]
    public struct RaidLocation
    {
        public string DisplayName;
        public string SceneName;
    }
}
