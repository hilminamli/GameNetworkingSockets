// N-API binding for Valve's GameNetworkingSockets.
//
// Design mirrors the C# binding (bindings/csharp):
//   - A single global library init (GameNetworkingSockets_Init) shared by all peers.
//   - "Peers" are server (listen socket + poll group) or client (single connection) handles,
//     each identified by a numeric peerId on the JS side.
//   - GNS is single-threaded and uses ONE global connection-status callback. We register it
//     once, route each status change to the owning peer (by listen socket / connection handle),
//     and surface it to JS as an event.
//   - Messages are drained per-peer on a libuv timer ("tick") and delivered to JS.
//
// Threading: GNS callbacks fire synchronously inside RunCallbacks(), which we call from the
// libuv timer on the JS thread. So we can touch JS objects directly — no cross-thread marshalling
// is needed here. (A ThreadSafeFunction would only be required if GNS called us from its own
// thread, which it does not in this polling model.)

#include <napi.h>
#include <steam/steamnetworkingsockets.h>       // GameNetworkingSockets_Init / _Kill
#include <steam/steamnetworkingsockets_flat.h>
#include <steam/steamnetworkingtypes.h>

#include <cstring>
#include <map>
#include <vector>

namespace {

ISteamNetworkingSockets* g_sockets = nullptr;
ISteamNetworkingUtils*   g_utils   = nullptr;
bool g_inited = false;

// Per-peer state. A peer is either a server (hListenSocket + hPollGroup set) or a
// client (hListenSocket == 0, single hConnection). peerId is the JS-facing handle.
struct Peer {
  uint32_t peerId        = 0;
  bool     isServer      = false;
  HSteamListenSocket hListen    = 0;
  HSteamNetPollGroup hPollGroup = 0;
  HSteamNetConnection hConn     = 0;   // client only: its single connection
  // JS callbacks, set from index.ts. These are persistent references.
  Napi::FunctionReference onStatus;    // (event:string, conn:number, endReason:number, endDebug:string)
  Napi::FunctionReference onMessage;   // (conn:number, data:Buffer, flags:number)
};

std::map<uint32_t, Peer*> g_peers;            // peerId  -> Peer
std::map<HSteamNetConnection, Peer*> g_connOwner; // conn -> owning peer (clients + accepted server conns)
std::map<HSteamListenSocket, Peer*>  g_listenOwner; // listen socket -> server peer
uint32_t g_nextPeerId = 1;

// ── Global status-changed callback ─────────────────────────────────────────────
// Routes a status change to the owning peer and invokes its onStatus JS callback.
void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t* cb) {
  const HSteamNetConnection hConn = cb->m_hConn;
  const int state    = cb->m_info.m_eState;       // ESteamNetworkingConnectionState
  const int endReason = cb->m_info.m_eEndReason;
  const char* endDebug = cb->m_info.m_szEndDebug;

  // Find the owning peer.
  Peer* peer = nullptr;
  auto it = g_connOwner.find(hConn);
  if (it != g_connOwner.end()) {
    peer = it->second;
  } else {
    // New connection we haven't seen — attribute by listen socket (server case).
    auto lit = g_listenOwner.find(cb->m_info.m_hListenSocket);
    if (lit != g_listenOwner.end()) peer = lit->second;
  }
  if (!peer) return;

  switch (state) {
    case k_ESteamNetworkingConnectionState_Connecting: {
      if (peer->isServer) {
        // Auto-accept incoming connections (matches C# server behaviour).
        SteamAPI_ISteamNetworkingSockets_AcceptConnection(g_sockets, hConn);
        g_connOwner[hConn] = peer;
      }
      break;
    }
    case k_ESteamNetworkingConnectionState_Connected: {
      if (peer->isServer) {
        SteamAPI_ISteamNetworkingSockets_SetConnectionPollGroup(g_sockets, hConn, peer->hPollGroup);
        g_connOwner[hConn] = peer;
      }
      if (!peer->onStatus.IsEmpty()) {
        Napi::Env env = peer->onStatus.Env();
        Napi::HandleScope scope(env);
        peer->onStatus.Call({ Napi::String::New(env, "connect"),
                              Napi::Number::New(env, hConn),
                              Napi::Number::New(env, 0),
                              Napi::String::New(env, "") });
      }
      break;
    }
    case k_ESteamNetworkingConnectionState_ClosedByPeer:
    case k_ESteamNetworkingConnectionState_ProblemDetectedLocally: {
      if (!peer->onStatus.IsEmpty()) {
        Napi::Env env = peer->onStatus.Env();
        Napi::HandleScope scope(env);
        peer->onStatus.Call({ Napi::String::New(env, "disconnect"),
                              Napi::Number::New(env, hConn),
                              Napi::Number::New(env, endReason),
                              Napi::String::New(env, endDebug ? endDebug : "") });
      }
      // GNS contract: handle still holds local resources until CloseConnection.
      SteamAPI_ISteamNetworkingSockets_CloseConnection(g_sockets, hConn, 0, nullptr, false);
      g_connOwner.erase(hConn);
      if (!peer->isServer && peer->hConn == hConn) peer->hConn = 0;
      break;
    }
    default:
      break;
  }
}

// ── Library init / shutdown ─────────────────────────────────────────────────────
Napi::Value Init(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  if (g_inited) return Napi::Boolean::New(env, true);

  SteamNetworkingErrMsg err = {0};
  if (!GameNetworkingSockets_Init(nullptr, err)) {
    Napi::Error::New(env, std::string("GNS init failed: ") + err).ThrowAsJavaScriptException();
    return env.Null();
  }
  g_sockets = SteamAPI_SteamNetworkingSockets_v009();
  g_utils   = SteamAPI_SteamNetworkingUtils_v003();
  SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(
      g_utils, OnConnectionStatusChanged);
  g_inited = true;
  return Napi::Boolean::New(env, true);
}

Napi::Value Kill(const Napi::CallbackInfo& info) {
  if (g_inited) {
    GameNetworkingSockets_Kill();
    g_inited = false;
    g_sockets = nullptr;
    g_utils = nullptr;
  }
  return info.Env().Undefined();
}

// ── Peer creation ───────────────────────────────────────────────────────────────
// createServer(port, onStatus) -> peerId
Napi::Value CreateServer(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  uint16_t port = static_cast<uint16_t>(info[0].As<Napi::Number>().Uint32Value());

  SteamNetworkingIPAddr addr;
  SteamAPI_SteamNetworkingIPAddr_Clear(&addr);
  addr.m_port = port;

  HSteamListenSocket hListen =
      SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(g_sockets, addr, 0, nullptr);
  if (hListen == 0) {
    Napi::Error::New(env, "Failed to create listen socket").ThrowAsJavaScriptException();
    return env.Null();
  }
  HSteamNetPollGroup hPg = SteamAPI_ISteamNetworkingSockets_CreatePollGroup(g_sockets);

  Peer* p = new Peer();
  p->peerId    = g_nextPeerId++;
  p->isServer  = true;
  p->hListen   = hListen;
  p->hPollGroup = hPg;
  p->onStatus  = Napi::Persistent(info[1].As<Napi::Function>());

  g_peers[p->peerId] = p;
  g_listenOwner[hListen] = p;
  return Napi::Number::New(env, p->peerId);
}

// createClient(onStatus) -> peerId
Napi::Value CreateClient(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  Peer* p = new Peer();
  p->peerId   = g_nextPeerId++;
  p->isServer = false;
  p->onStatus = Napi::Persistent(info[0].As<Napi::Function>());
  g_peers[p->peerId] = p;
  return Napi::Number::New(env, p->peerId);
}

Peer* FindPeer(uint32_t peerId) {
  auto it = g_peers.find(peerId);
  return it == g_peers.end() ? nullptr : it->second;
}

// connect(peerId, "ip:port") -> connHandle  (client peers)
Napi::Value Connect(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  Peer* p = FindPeer(info[0].As<Napi::Number>().Uint32Value());
  if (!p || p->isServer) {
    Napi::Error::New(env, "connect requires a client peer").ThrowAsJavaScriptException();
    return env.Null();
  }
  std::string target = info[1].As<Napi::String>();
  SteamNetworkingIPAddr addr;
  if (!SteamAPI_SteamNetworkingIPAddr_ParseString(&addr, target.c_str())) {
    Napi::Error::New(env, "Invalid address: " + target).ThrowAsJavaScriptException();
    return env.Null();
  }
  HSteamNetConnection h =
      SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(g_sockets, addr, 0, nullptr);
  p->hConn = h;
  if (h != 0) g_connOwner[h] = p;
  return Napi::Number::New(env, h);
}

// send(conn, Buffer, sendFlags) -> EResult
Napi::Value Send(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  HSteamNetConnection hConn = info[0].As<Napi::Number>().Uint32Value();
  Napi::Buffer<uint8_t> buf = info[1].As<Napi::Buffer<uint8_t>>();
  int flags = info[2].As<Napi::Number>().Int32Value();
  int64 msgNum = 0;  // GNS int64 (long long on Linux); not int64_t (long) — types must match exactly
  EResult r = SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(
      g_sockets, hConn, buf.Data(), static_cast<uint32_t>(buf.Length()), flags, &msgNum);
  return Napi::Number::New(env, static_cast<int>(r));
}

// poll(peerId) -> [ {conn, data:Buffer, flags}, ... ]
// Runs callbacks once, then drains pending messages for the peer.
Napi::Value Poll(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  Peer* p = FindPeer(info[0].As<Napi::Number>().Uint32Value());
  if (!p) return Napi::Array::New(env, 0);

  const int kMax = 256;
  SteamNetworkingMessage_t* msgs[kMax];
  int n = 0;
  if (p->isServer) {
    n = SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup(g_sockets, p->hPollGroup, msgs, kMax);
  } else if (p->hConn != 0) {
    n = SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(g_sockets, p->hConn, msgs, kMax);
  }

  Napi::Array out = Napi::Array::New(env, n);
  for (int i = 0; i < n; i++) {
    SteamNetworkingMessage_t* m = msgs[i];
    Napi::Object o = Napi::Object::New(env);
    o.Set("conn", Napi::Number::New(env, m->m_conn));
    o.Set("data", Napi::Buffer<uint8_t>::Copy(env, reinterpret_cast<uint8_t*>(m->m_pData), m->m_cbSize));
    o.Set("flags", Napi::Number::New(env, m->m_nFlags));
    out.Set(static_cast<uint32_t>(i), o);
    m->Release();
  }
  return out;
}

// runCallbacks() — process pending GNS callbacks (status changes) without draining messages.
Napi::Value RunCallbacks(const Napi::CallbackInfo& info) {
  if (g_sockets) SteamAPI_ISteamNetworkingSockets_RunCallbacks(g_sockets);
  return info.Env().Undefined();
}

// closeConnection(conn, reason, debug)
Napi::Value CloseConnection(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  HSteamNetConnection hConn = info[0].As<Napi::Number>().Uint32Value();
  int reason = info.Length() > 1 ? info[1].As<Napi::Number>().Int32Value() : 0;
  std::string dbg;
  if (info.Length() > 2 && info[2].IsString()) dbg = info[2].As<Napi::String>().Utf8Value();
  bool ok = SteamAPI_ISteamNetworkingSockets_CloseConnection(
      g_sockets, hConn, reason, dbg.empty() ? nullptr : dbg.c_str(), false);
  g_connOwner.erase(hConn);
  return Napi::Boolean::New(env, ok);
}

// getConnectionStatus(conn) -> {ping:number, packetLoss:number} | null
Napi::Value GetConnectionStatus(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  HSteamNetConnection hConn = info[0].As<Napi::Number>().Uint32Value();
  unsigned char buf[120] = {0};
  int r = SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(
      g_sockets, hConn, (SteamNetConnectionRealTimeStatus_t*)buf, 0, nullptr);
  if (r != k_EResultOK) return env.Null();
  Napi::Object o = Napi::Object::New(env);
  o.Set("ping", Napi::Number::New(env, *reinterpret_cast<int*>(buf + 4)));
  o.Set("packetLoss", Napi::Number::New(env, 1.0f - *reinterpret_cast<float*>(buf + 8)));
  return o;
}

// destroyPeer(peerId) — closes the listen socket / poll group / connection and frees state.
Napi::Value DestroyPeer(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  uint32_t peerId = info[0].As<Napi::Number>().Uint32Value();
  Peer* p = FindPeer(peerId);
  if (!p) return env.Undefined();

  if (p->isServer) {
    if (p->hPollGroup) SteamAPI_ISteamNetworkingSockets_DestroyPollGroup(g_sockets, p->hPollGroup);
    if (p->hListen)    SteamAPI_ISteamNetworkingSockets_CloseListenSocket(g_sockets, p->hListen);
    g_listenOwner.erase(p->hListen);
  } else if (p->hConn) {
    SteamAPI_ISteamNetworkingSockets_CloseConnection(g_sockets, p->hConn, 0, nullptr, false);
    g_connOwner.erase(p->hConn);
  }
  // Drop any accepted connections owned by this peer.
  for (auto it = g_connOwner.begin(); it != g_connOwner.end();) {
    if (it->second == p) it = g_connOwner.erase(it); else ++it;
  }
  g_peers.erase(peerId);
  delete p;
  return env.Undefined();
}

Napi::Object InitModule(Napi::Env env, Napi::Object exports) {
  exports.Set("init",                Napi::Function::New(env, Init));
  exports.Set("kill",                Napi::Function::New(env, Kill));
  exports.Set("createServer",        Napi::Function::New(env, CreateServer));
  exports.Set("createClient",        Napi::Function::New(env, CreateClient));
  exports.Set("connect",             Napi::Function::New(env, Connect));
  exports.Set("send",                Napi::Function::New(env, Send));
  exports.Set("poll",                Napi::Function::New(env, Poll));
  exports.Set("runCallbacks",        Napi::Function::New(env, RunCallbacks));
  exports.Set("closeConnection",     Napi::Function::New(env, CloseConnection));
  exports.Set("getConnectionStatus", Napi::Function::New(env, GetConnectionStatus));
  exports.Set("destroyPeer",         Napi::Function::New(env, DestroyPeer));
  return exports;
}

}  // namespace

NODE_API_MODULE(gns, InitModule)
