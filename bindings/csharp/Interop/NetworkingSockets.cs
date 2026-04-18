using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GameNetworkingSockets
{
    /// <summary>Base class for GNS socket wrappers. Handles send, receive, connection management, and real-time stats.</summary>
    public abstract class NetworkingSockets : IDisposable
    {
        protected IntPtr _iface;
        private bool _disposed;

        private IntPtr[] _ptrBuffer = new IntPtr[NetworkingConstants.MessageBufferSize];

        protected NetworkingSockets()
        {
            if (!NetworkingLibrary.IsInitialized)
                throw new InvalidOperationException("Call NetworkingLibrary.Initialize() first.");

            _iface = Native.SteamAPI_SteamNetworkingSockets_v009();
            if (_iface == IntPtr.Zero)
                throw new InvalidOperationException("Failed to get ISteamNetworkingSockets interface.");

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

        /// <summary>Sends data to a connection using the full data array length.</summary>
        public EResult SendMessage(uint hConn, byte[] data, SendType sendType = SendType.Reliable)
            => SendMessage(hConn, data, data.Length, sendType, out _);

        /// <summary>Sends data to a connection with an explicit byte count. Returns the assigned message number via out param.</summary>
        public EResult SendMessage(uint hConn, byte[] data, int length, SendType sendType,
            out long messageNumber)
        {
            if (_disposed) { messageNumber = 0; return EResult.NoConnection; }

            return (EResult)Native.SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(
                _iface, hConn, data, (uint)length, (int)sendType, out messageNumber);
        }

        /// <summary>Flushes any queued messages on the connection, bypassing Nagle buffering.</summary>
        public EResult FlushMessages(uint hConn)
        {
            if (_disposed) return EResult.NoConnection;
            return (EResult)Native.SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(_iface, hConn);
        }

        // ── Receive ──────────────────────────────────────────────────────────────

        /// <summary>Receives pending messages on a single connection into the provided buffer. Returns the number of messages received.</summary>
        public int ReceiveMessages(uint hConn, NetworkMessage[] messages)
        {
            if (_disposed) return 0;
            return ReceiveFrom(messages,
                buf => Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(
                    _iface, hConn, buf, buf.Length));
        }

        protected int ReceiveMessagesOnPollGroup(uint hPollGroup, NetworkMessage[] messages)
        {
            if (_disposed) return 0;
            return ReceiveFrom(messages,
                buf => Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup(
                    _iface, hPollGroup, buf, buf.Length));
        }

        private int ReceiveFrom(NetworkMessage[] messages, Func<IntPtr[], int> nativeReceive)
        {
            // Grow if caller passes a larger buffer than our default; never shrink to avoid repeated allocs
            if (_ptrBuffer.Length < messages.Length)
                _ptrBuffer = new IntPtr[messages.Length];
            int count = nativeReceive(_ptrBuffer);

            for (int i = 0; i < count; i++)
            {
                IntPtr p = _ptrBuffer[i];
                try
                {
                    int size = Native.Msg_cbSize(p);
                    byte[] data = new byte[size];
                    Marshal.Copy(Native.Msg_pData(p), data, 0, size);
                    messages[i] = new NetworkMessage
                    {
                        connection    = Native.Msg_conn(p),
                        messageNumber = Native.Msg_messageNumber(p),
                        flags         = Native.Msg_flags(p),
                        data          = data,
                    };
                }
                finally
                {
                    // Release must always run even if Marshal.Copy throws
                    Native.SteamAPI_SteamNetworkingMessage_t_Release(p);
                }
            }
            return count;
        }

        // ── Connection management ─────────────────────────────────────────────────

        /// <summary>Closes a connection, optionally sending a reason code and debug string to the remote peer.</summary>
        public bool CloseConnection(uint hConn, int reason = 0, string? debug = null, bool linger = false)
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

        // SteamNetConnectionRealTimeStatus_t layout — identical on Windows and Linux x64:
        // offset 0:  m_eState  (int, 4)
        // offset 4:  m_nPing   (int, 4)  ← milliseconds
        // offset 8:  m_flConnectionQualityLocal  (float, 4)  ← 0..1, 1=no loss
        // total struct: 120 bytes
        /// <summary>Reads real-time connection stats. Returns false if the connection is invalid or stats are unavailable.</summary>
        public bool GetConnectionStatus(uint hConn, out int pingMs, out float packetLoss)
        {
            pingMs     = 0;
            packetLoss = 0f;
            if (_disposed) return false;

            IntPtr buf = Marshal.AllocHGlobal(NetworkingConstants.RealTimeStatusStructSize);
            try
            {
                int result = Native.SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(
                    _iface, hConn, buf, 0, IntPtr.Zero);
                if (result != 1) return false; // EResult.OK == 1, not 0

                pingMs     = Marshal.ReadInt32(buf, 4);
                packetLoss = 1f - Marshal.PtrToStructure<float>(buf + 8); // quality is 0..1 where 1 = no loss; invert to get loss ratio
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
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
        public static string IPAddrToString(ref SteamNetworkingIPAddr addr, bool withPort = true)
        {
            var buf = new byte[NetworkingConstants.IPAddrStringBufferSize];
            Native.SteamAPI_SteamNetworkingIPAddr_ToString(ref addr, buf, (UIntPtr)buf.Length, withPort);
            return Encoding.UTF8.GetString(buf).TrimEnd('\0');
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NetworkingLibrary.Unregister(this);
        }
    }
}
