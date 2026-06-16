using System;
using Mirror;
using UnityEngine;

namespace OC.Net
{
    /// <summary>
    /// 프로젝트용 NetworkManager.
    /// - 접속/해제 이벤트를 로그로 찍어 진단을 쉽게 한다.
    /// - 클라가 확실히 Ready 가 되도록 보장한다(Ready 가 안 되면 씬 오브젝트 스폰을 못 받아
    ///   GameSession.OnStartClient 가 호출되지 않는다).
    /// 사용: 씬의 NetworkManager 컴포넌트를 제거하고 이 컴포넌트를 붙인다(같은 NetworkManager 계열).
    /// </summary>
    public class OCNetworkManager : NetworkManager
    {
        [Header("OC")]
        [Tooltip("커맨드라인 -server 또는 배치모드(헤드리스)면 전용 서버로 자동 시작")]
        [SerializeField] private bool autoStartHeadlessServer = true;

        public override void Start()
        {
            base.Start(); // Mirror 의 헤드리스 자동시작 처리(headlessStartMode, 기본 DoNothing)

            // 리눅스 데디서버에서 KCP 듀얼모드(IPv6)로 응답이 안 가는 이슈 회피 → IPv4 전용
            if (Transport.active is kcp2k.KcpTransport kcp)
            {
                kcp.DualMode = false;
                Debug.Log("[OCNet] KCP DualMode = false (IPv4 전용)");
            }

            if (!autoStartHeadlessServer) return;
            bool headless = false;
            foreach (var a in Environment.GetCommandLineArgs())
                if (a == "-server" || a == "-batchmode") { headless = true; break; }
            if (headless && !NetworkServer.active)
            {
                Debug.Log("[OCNet] 헤드리스 감지 — 전용 서버 자동 시작");
                StartServer();
            }
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            Debug.Log($"[OCNet] 서버: 클라 접속됨 connId={conn.connectionId}, 총 연결수={NetworkServer.connections.Count}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[OCNet] 서버: 클라 해제 connId={conn.connectionId}");
            // 방에서 제거(빈 방이면 GameSession 디스폰, 상대에게 이탈 알림) — base 가 플레이어를 파괴하기 전에.
            if (RoomServer.Instance != null) RoomServer.Instance.Leave(conn);
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            Debug.Log("[OCNet] 클라: 서버에 접속 성공");
            base.OnClientConnect();
            if (!NetworkClient.ready)
            {
                NetworkClient.Ready();
                Debug.Log("[OCNet] 클라: NetworkClient.Ready() 호출 — 이제 스폰 수신 가능");
            }
        }

        public override void OnClientDisconnect()
        {
            Debug.Log("[OCNet] 클라: 서버와 연결 끊김");
            base.OnClientDisconnect();
        }

        public override void OnClientError(TransportError error, string reason)
        {
            Debug.LogError($"[OCNet] 클라 트랜스포트 오류: {error} / {reason}");
            base.OnClientError(error, reason);
        }
    }
}
