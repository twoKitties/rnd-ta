using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using FishNet;
using FishNet.Transporting;
using UnityEngine;

namespace _Game.Code.App
{
    /// <summary>
    /// Finding a host on the same LAN without typing its address. Tugboat has no
    /// discovery of its own, so this is a plain UDP beacon beside it: the host
    /// announces while it is hosting, a searching client listens and keeps a list.
    /// </summary>
    public sealed class LanDiscovery : MonoBehaviour
    {
        /// <summary>One host heard from. The address is the packet's sender, never its payload.</summary>
        public struct Found
        {
            public string Address;
            public ushort Port;
            public float LastSeen;

            public string Endpoint => $"{Address}:{Port}";
        }

        // Magic plus the game port. Everything else a client needs is the packet's own
        // sender address, which is the only one guaranteed routable from where it landed.
        private const string Magic = "RNDTA1:";

        [Tooltip("UDP port the beacon uses. Not the game port — both ends bind it, so it " +
                 "must differ from RaidSession's.")]
        [SerializeField] private ushort discoveryPort = 47770;

        [Tooltip("How often a host repeats its announcement, seconds.")]
        [SerializeField] private float announceInterval = 1f;

        [Tooltip("How long an entry survives without a fresh announcement, seconds. " +
                 "Several intervals, or one dropped datagram blinks the row out.")]
        [SerializeField] private float entryTimeout = 4f;

        [Tooltip("Announced when there is no RaidSession to ask for the real one.")]
        [SerializeField] private ushort fallbackGamePort = 7770;

        private readonly List<Found> _found = new();

        // Written by the socket callback, drained in Update. Everything Unity touches is
        // on the far side of this queue: a receive callback runs on a thread pool thread,
        // where Time, Debug and every other UnityEngine call are illegal.
        private readonly Queue<Found> _incoming = new();

        private readonly List<IPEndPoint> _targets = new();

        private UdpClient _announcer;
        private UdpClient _listener;
        private float _nextAnnounce;

        public static LanDiscovery Active { get; private set; }

        public IReadOnlyList<Found> Sessions => _found;

        public bool IsSearching { get; private set; }

        /// <summary>The list gained or lost a host.</summary>
        public event Action Changed;

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(this);
                return;
            }

            Active = this;
        }

        private void OnEnable()
        {
            var manager = InstanceFinder.NetworkManager;
            if (manager == null)
            {
                return;
            }

            manager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }

        private void Update()
        {
            if (_announcer != null && Time.unscaledTime >= _nextAnnounce)
            {
                Announce();
                _nextAnnounce = Time.unscaledTime + announceInterval;
            }

            if (IsSearching)
            {
                Collect();
            }
        }

        private void OnDisable()
        {
            var manager = InstanceFinder.NetworkManager;
            if (manager != null)
            {
                manager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            }

            // A UdpClient left open holds its port until the process dies, and the next
            // Host or the next search then fails on a port busy for no visible reason.
            StopAnnouncing();
            StopSearch();
        }

        private void OnApplicationQuit()
        {
            StopAnnouncing();
            StopSearch();
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void OnValidate()
        {
            announceInterval = Mathf.Max(0.1f, announceInterval);
            entryTimeout = Mathf.Max(announceInterval * 2f, entryTimeout);
        }

        public void StartSearch()
        {
            if (IsSearching)
            {
                return;
            }

            try
            {
                _listener = new UdpClient();
                _listener.EnableBroadcast = true;

                // Bound by hand for ReuseAddress: a host and a client on one machine —
                // which is how this gets tested — both want this port.
                _listener.ExclusiveAddressUse = false;
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"LanDiscovery: cannot listen on {discoveryPort} ({exception.SocketErrorCode}).");
                CloseListener();
                return;
            }

            IsSearching = true;
            _found.Clear();
            lock (_incoming)
            {
                _incoming.Clear();
            }

            Receive();
            Changed?.Invoke();
        }

        public void StopSearch()
        {
            IsSearching = false;
            CloseListener();

            if (_found.Count == 0)
            {
                return;
            }

            _found.Clear();
            Changed?.Invoke();
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                StartAnnouncing();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                StopAnnouncing();
            }
        }

        private void StartAnnouncing()
        {
            if (_announcer != null)
            {
                return;
            }

            try
            {
                _announcer = new UdpClient();
                _announcer.EnableBroadcast = true;
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"LanDiscovery: cannot open the beacon ({exception.SocketErrorCode}).");
                _announcer = null;
                return;
            }

            CollectTargets();
            _nextAnnounce = 0f;
        }

        private void StopAnnouncing()
        {
            if (_announcer == null)
            {
                return;
            }

            _announcer.Close();
            _announcer = null;
            _targets.Clear();
        }

        // One datagram per interface rather than one to 255.255.255.255: the limited
        // broadcast leaves through the default route only, and on a machine with a VPN
        // or Hyper-V up that route is not the LAN the other players are on.
        private void CollectTargets()
        {
            _targets.Clear();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var entry in nic.GetIPProperties().UnicastAddresses)
                {
                    // 169.254.x is what Windows invents for an adapter with no DHCP.
                    if (entry.Address.AddressFamily != AddressFamily.InterNetwork ||
                        entry.IPv4Mask == null || entry.Address.ToString().StartsWith("169.254"))
                    {
                        continue;
                    }

                    var address = entry.Address.GetAddressBytes();
                    var mask = entry.IPv4Mask.GetAddressBytes();
                    if (mask.Length != address.Length)
                    {
                        continue;
                    }

                    for (var i = 0; i < address.Length; i++)
                    {
                        address[i] |= (byte)~mask[i];
                    }

                    _targets.Add(new IPEndPoint(new IPAddress(address), discoveryPort));
                }
            }

            // Loopback too, so two builds on one machine still find each other.
            _targets.Add(new IPEndPoint(IPAddress.Loopback, discoveryPort));
        }

        private void Announce()
        {
            var gamePort = RaidSession.Active == null ? fallbackGamePort : RaidSession.Active.Port;
            var payload = Encoding.ASCII.GetBytes(Magic + gamePort);

            for (var i = 0; i < _targets.Count; i++)
            {
                try
                {
                    _announcer.Send(payload, payload.Length, _targets[i]);
                }
                catch (SocketException)
                {
                    // A blocked or downed interface; the others are still worth trying.
                }
            }
        }

        private void Receive()
        {
            var listener = _listener;
            if (listener == null)
            {
                return;
            }

            try
            {
                listener.BeginReceive(OnPacket, listener);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        // Thread pool thread. Nothing here may touch the Unity API, and the socket comes
        // from the state object rather than the field, which the main thread may null.
        private void OnPacket(IAsyncResult result)
        {
            var listener = result.AsyncState as UdpClient;
            if (listener == null)
            {
                return;
            }

            var from = new IPEndPoint(IPAddress.Any, 0);
            byte[] data;
            try
            {
                data = listener.EndReceive(result, ref from);
            }
            catch (ObjectDisposedException)
            {
                // Closed under us. The one case where re-arming would resurrect a socket.
                return;
            }
            catch (SocketException)
            {
                Receive();
                return;
            }

            ushort port;
            if (Parse(data, out port))
            {
                lock (_incoming)
                {
                    _incoming.Enqueue(new Found { Address = from.Address.ToString(), Port = port });
                }
            }

            Receive();
        }

        private static bool Parse(byte[] data, out ushort port)
        {
            port = 0;
            if (data == null || data.Length <= Magic.Length || data.Length > 64)
            {
                return false;
            }

            var text = Encoding.ASCII.GetString(data);
            if (!text.StartsWith(Magic))
            {
                return false;
            }

            return ushort.TryParse(text.Substring(Magic.Length), NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out port) && port != 0;
        }

        private void Collect()
        {
            var changed = false;
            var now = Time.unscaledTime;

            lock (_incoming)
            {
                while (_incoming.Count > 0)
                {
                    var heard = _incoming.Dequeue();
                    heard.LastSeen = now;
                    changed |= Merge(heard);
                }
            }

            for (var i = _found.Count - 1; i >= 0; i--)
            {
                if (now - _found[i].LastSeen <= entryTimeout)
                {
                    continue;
                }

                _found.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        // More hosts than a co-op game of 2-4 could ever mean (MECHANICS.md section 1).
        private const int MaxSessions = 32;

        // True when the list gained a row; a refreshed timestamp is not a repaint.
        private bool Merge(Found heard)
        {
            for (var i = 0; i < _found.Count; i++)
            {
                if (_found[i].Address != heard.Address || _found[i].Port != heard.Port)
                {
                    continue;
                }

                _found[i] = heard;
                return false;
            }

            // Anything on the LAN can send these, and every distinct sender is a row.
            // A full list stops growing rather than filling the panel.
            if (_found.Count >= MaxSessions)
            {
                return false;
            }

            _found.Add(heard);
            return true;
        }

        private void CloseListener()
        {
            if (_listener == null)
            {
                return;
            }

            _listener.Close();
            _listener = null;
        }
    }
}
