using System;
using System.Text;

namespace GameNetworkingSockets
{
    /// <summary>Base class for GNS socket wrappers. Handles send, receive, connection management, and real-time stats.</summary>
    public abstract class NetworkingSockets : IDisposable
    {
        protected IntPtr _iface;
        private bool _disposed;

        private readonly IntPtr[] _ptrBuffer;

        /// <summary>
        /// Initializes the socket and allocates the receive pointer buffer used by <c>ReceiveMessages</c>.
        /// </summary>
        /// <param name="messageBufferSize">
        /// Max messages drained from native per <c>ReceiveMessages</c> call. Higher values reduce the number
        /// of native calls under burst traffic but allocate a larger pointer array per instance
        /// (8 bytes on x64). Must be greater than zero.
        /// </param>
        protected NetworkingSockets(int messageBufferSize)
        {
            if (messageBufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(messageBufferSize), messageBufferSize,
                    "Message buffer size must be greater than zero.");

            if (!NetworkingLibrary.IsInitialized)
                throw new InvalidOperationException("Call NetworkingLibrary.Initialize() first.");

            _iface = Native.SteamAPI_SteamNetworkingSockets_v009();
            if (_iface == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get ISteamNetworkingSockets interface.");

            _ptrBuffer = new IntPtr[messageBufferSize];
            NetworkingLibrary.Register(this);
        }

        // ── Dispatcher hooks (implemented by Server / Client) ────────────────────

        internal abstract bool OwnsConnection(uint hConn);
        internal abstract void HandleStatusChanged(ConnectionStatusInfo info);

        // ── Callbacks ────────────────────────────────────────────────────────────

        /// <summary>Processes pending GNS callbacks. Must be called regularly on the main thread.</summary>
        public void RunCallbacks()
        {
            if (_disposed) return;
            Native.SteamAPI_ISteamNetworkingSockets_RunCallbacks(_iface);
        }

        // ── Send ─────────────────────────────────────────────────────────────────

        /// <summary>Sends data to a connection from a span.</summary>
        public unsafe EResult SendMessage(uint hConn, ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable)
            => SendMessage(hConn, data, sendType, out _);

        /// <summary>Sends data to a connection from a span, returning the assigned message number.</summary>
        public unsafe EResult SendMessage(uint hConn, ReadOnlySpan<byte> data, SendType sendType,
            out long messageNumber)
        {
            if (_disposed) { messageNumber = 0; return EResult.NoConnection; }

            fixed (byte* ptr = data)
                return (EResult)Native.SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(
                    _iface, hConn, (IntPtr)ptr, (uint)data.Length, (int)sendType, out messageNumber);
        }

        /// <summary>Flushes any queued messages on the connection, bypassing Nagle buffering.</summary>
        public EResult FlushMessages(uint hConn)
        {
            if (_disposed) return EResult.NoConnection;
            return (EResult)Native.SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(_iface, hConn);
        }

        // ── Receive ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Receives pending messages on a single connection and invokes the callback for each one.
        /// The span passed to the callback points at native memory and is only valid for the duration
        /// of the call — copy anything you need to retain. Returns the number of messages processed.
        /// </summary>
        public int ReceiveMessages(uint hConn, MessageReceivedCallback receiver)
        {
            if (_disposed) return 0;
            int count = Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(
                _iface, hConn, _ptrBuffer, _ptrBuffer.Length);
            return Dispatch(count, receiver);
        }

        protected int ReceiveMessagesOnPollGroup(uint hPollGroup, MessageReceivedCallback receiver)
        {
            if (_disposed) return 0;
            int count = Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup(
                _iface, hPollGroup, _ptrBuffer, _ptrBuffer.Length);
            return Dispatch(count, receiver);
        }

        private unsafe int Dispatch(int count, MessageReceivedCallback receiver)
        {
            for (int i = 0; i < count; i++)
            {
                Native.SteamNetworkingMessage_t* msg = (Native.SteamNetworkingMessage_t*)_ptrBuffer[i];
                try
                {
                    var span = new ReadOnlySpan<byte>((void*)msg->pData, msg->cbSize);
                    receiver(msg->conn, span);
                }
                finally
                {
                    Native.SteamAPI_SteamNetworkingMessage_t_Release((IntPtr)msg);
                }
            }
            return count;
        }

        // ── Connection management ─────────────────────────────────────────────────

        /// <summary>Closes a connection, optionally sending a reason code and debug string to the remote peer.</summary>
        public bool CloseConnection(uint hConn, int reason = 0, string debug = null, bool linger = false)
        {
            if (_disposed) return false;
            return Native.SteamAPI_ISteamNetworkingSockets_CloseConnection(_iface, hConn, reason, debug, linger);
        }

        /// <summary>Attaches an arbitrary 64-bit value to a connection, retrievable via GetConnectionUserData.</summary>
        public bool SetConnectionUserData(uint hConn, long userData)
        {
            if (_disposed) return false;
            return Native.SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(_iface, hConn, userData);
        }

        /// <summary>Returns the user data value previously set on a connection, or -1 if not set.</summary>
        public long GetConnectionUserData(uint hConn)
        {
            if (_disposed) return -1;
            return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(_iface, hConn);
        }

        // ── Poll groups ──────────────────────────────────────────────────────────

        public uint CreatePollGroup()
            => Native.SteamAPI_ISteamNetworkingSockets_CreatePollGroup(_iface);

        public bool DestroyPollGroup(uint hPollGroup)
            => Native.SteamAPI_ISteamNetworkingSockets_DestroyPollGroup(_iface, hPollGroup);

        public bool SetConnectionPollGroup(uint hConn, uint hPollGroup)
            => Native.SteamAPI_ISteamNetworkingSockets_SetConnectionPollGroup(_iface, hConn, hPollGroup);

        // ── Real-time stats ──────────────────────────────────────────────────────

        // SteamNetConnectionRealTimeStatus_t layout (120 bytes, identical on Windows and Linux x64):
        // offset  0: m_eState                  (int,   4)
        // offset  4: m_nPing                   (int,   4)
        // offset  8: m_flConnectionQualityLocal (float, 4)  ← 0..1, 1=no loss
        // offset 12: m_flConnectionQualityRemote(float, 4)
        // offset 16: m_flOutPacketsPerSec       (float, 4)
        // offset 20: m_flOutBytesPerSec         (float, 4)
        // offset 24: m_flInPacketsPerSec        (float, 4)
        // offset 28: m_flInBytesPerSec          (float, 4)
        // offset 32: m_nSendRateBytesPerSecond  (int,   4)
        // offset 36: m_cbPendingUnreliable       (int,   4)
        // offset 40: m_cbPendingReliable         (int,   4)
        // offset 44: m_cbSentUnackedReliable     (int,   4)
        // offset 48: m_usecQueueTime             (int64, 8)
        // ... padding to 120 bytes

        /// <summary>Reads ping and packet loss for the connection. Returns false if unavailable.</summary>
        public unsafe bool GetConnectionStatus(uint hConn, out int pingMs, out float packetLoss)
        {
            pingMs     = 0;
            packetLoss = 0f;
            if (_disposed) return false;

            byte* buf = stackalloc byte[NetworkingConstants.RealTimeStatusStructSize];
            int result = Native.SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(
                _iface, hConn, (IntPtr)buf, 0, IntPtr.Zero);
            if (result != 1) return false;

            pingMs     = *(int*)(buf + 4);
            packetLoss = 1f - *(float*)(buf + 8);
            return true;
        }

        /// <summary>Reads all available real-time statistics for the connection. Returns false if unavailable.</summary>
        public unsafe bool GetConnectionStats(uint hConn, out ConnectionStats stats)
        {
            stats = default;
            if (_disposed) return false;

            byte* buf = stackalloc byte[NetworkingConstants.RealTimeStatusStructSize];
            int result = Native.SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(
                _iface, hConn, (IntPtr)buf, 0, IntPtr.Zero);
            if (result != 1) return false;

            stats.PingMs                 = *(int*)  (buf +  4);
            stats.PacketLossLocal        = 1f - *(float*)(buf +  8);
            stats.PacketLossRemote       = 1f - *(float*)(buf + 12);
            stats.OutPacketsPerSec       = *(float*)(buf + 16);
            stats.OutBytesPerSec         = *(float*)(buf + 20);
            stats.InPacketsPerSec        = *(float*)(buf + 24);
            stats.InBytesPerSec          = *(float*)(buf + 28);
            stats.SendRateBytesPerSecond = *(int*)  (buf + 32);
            stats.PendingUnreliable      = *(int*)  (buf + 36);
            stats.PendingReliable        = *(int*)  (buf + 40);
            stats.SentUnackedReliable    = *(int*)  (buf + 44);
            stats.QueueTimeMicroseconds  = *(long*) (buf + 48);
            return true;
        }

        // ── Utilities ────────────────────────────────────────────────────────────

        /// <summary>Parses an IP address string (e.g. "127.0.0.1:27015") into a SteamNetworkingIPAddr. Throws on invalid input.</summary>
        public static SteamNetworkingIPAddr ParseIPAddr(string str)
        {
            var addr = new SteamNetworkingIPAddr();
            if (!Native.SteamAPI_SteamNetworkingIPAddr_ParseString(ref addr, str))
                throw new ArgumentException($"Invalid IP address: {str}");
            return addr;
        }

        /// <summary>Converts a SteamNetworkingIPAddr to its string representation, optionally including the port.</summary>
        public static unsafe string IPAddrToString(ref SteamNetworkingIPAddr addr, bool withPort = true)
        {
            byte* buf = stackalloc byte[NetworkingConstants.IPAddrStringBufferSize];
            Native.SteamAPI_SteamNetworkingIPAddr_ToString(ref addr, buf, (UIntPtr)NetworkingConstants.IPAddrStringBufferSize, withPort);
            var span = new ReadOnlySpan<byte>(buf, NetworkingConstants.IPAddrStringBufferSize);
            int end  = span.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NetworkingLibrary.Unregister(this);
            GC.SuppressFinalize(this);
        }

    }
}
