using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace OC.Net
{
    /// <summary>
    /// 서버 전용 방(로비) 레지스트리. 데디 서버 1대에서 여러 개의 독립된 2인 방을 운영한다.
    /// - 방마다 GameSession 프리팹을 스폰하고 고유 matchId(Guid)를 부여한다.
    /// - MatchInterestManagement 가 같은 matchId 끼리만 observer 로 묶어주므로,
    ///   각 방의 ClientRpc/Command 는 그 방의 두 사람에게만 전달된다.
    /// 배치: NetworkManager 오브젝트에 부착하고 gameSessionPrefab 를 지정한다(에디터 자동 셋업이 처리).
    /// </summary>
    public class RoomServer : MonoBehaviour
    {
        public static RoomServer Instance { get; private set; }

        [Tooltip("방마다 스폰할 GameSession 프리팹 (NetworkIdentity + NetworkMatch + GameSession).")]
        public GameObject gameSessionPrefab;

        private class Room
        {
            public string code;
            public Guid matchId;
            public GameSession session;
            public readonly List<int> conns = new List<int>(); // connectionId
        }

        private readonly Dictionary<string, Room> _byCode = new Dictionary<string, Room>();
        private readonly Dictionary<int, Room> _byConn = new Dictionary<int, Room>();

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public const int Capacity = 2;

        /// <summary>방 생성. 생성한 사람을 그 방에 넣고 방 코드를 돌려준다.</summary>
        public string CreateRoom(NetworkConnectionToClient conn)
        {
            if (gameSessionPrefab == null)
            {
                Debug.LogError("[RoomServer] gameSessionPrefab 미지정 — 방을 만들 수 없음.");
                return null;
            }
            if (_byConn.ContainsKey(conn.connectionId)) LeaveInternal(conn);

            string code = GenerateCode();
            Guid matchId = Guid.NewGuid();
            var room = new Room { code = code, matchId = matchId };

            var go = Instantiate(gameSessionPrefab);
            room.session = go.GetComponent<GameSession>();
            // 스폰 전에 matchId 를 채워두면 OnSpawned 가 곧장 매치에 등록한다.
            if (go.TryGetComponent(out NetworkMatch nm)) nm.matchId = matchId;
            NetworkServer.Spawn(go);

            _byCode[code] = room;
            AddConn(room, conn);
            Debug.Log($"[RoomServer] 방 생성 code={code} conn={conn.connectionId} (총 방 {_byCode.Count})");
            return code;
        }

        /// <summary>코드로 방 참가. 성공 시 정규화된 코드를 out 으로 준다.</summary>
        public bool TryJoin(string code, NetworkConnectionToClient conn, out string normalized, out string error)
        {
            normalized = null; error = null;
            code = Normalize(code);
            if (string.IsNullOrEmpty(code) || !_byCode.TryGetValue(code, out var room))
            { error = "그런 방이 없어요."; return false; }
            if (room.conns.Contains(conn.connectionId)) { normalized = code; return true; }
            if (room.conns.Count >= Capacity) { error = "방이 꽉 찼어요."; return false; }

            if (_byConn.ContainsKey(conn.connectionId)) LeaveInternal(conn);
            AddConn(room, conn);
            normalized = code;
            Debug.Log($"[RoomServer] 방 참가 code={code} conn={conn.connectionId}");
            return true;
        }

        /// <summary>접속 종료/방 나가기 정리. 방이 비면 GameSession 을 디스폰한다.</summary>
        public void Leave(NetworkConnectionToClient conn) => LeaveInternal(conn);

        private void AddConn(Room room, NetworkConnectionToClient conn)
        {
            room.conns.Add(conn.connectionId);
            _byConn[conn.connectionId] = room;
            SetMatch(conn, room.matchId);
        }

        // 2인 게임이라 한 명이라도 빠지면 방 자체가 성립하지 않는다.
        // → 방을 통째로 깨고, 남은 사람은 메인(방 메뉴)으로 되돌린다.
        private void LeaveInternal(NetworkConnectionToClient conn)
        {
            if (!_byConn.TryGetValue(conn.connectionId, out var room)) return;

            // 떠나는 사람 정리
            _byConn.Remove(conn.connectionId);
            room.conns.Remove(conn.connectionId);
            SetMatch(conn, Guid.Empty);

            // 남은 사람(있다면)에게 상대 이탈을 알린다 — 아직 GameSession 을 observe 중일 때 보낸다.
            if (room.session != null) room.session.OnPlayerLeft(conn.connectionId);

            // 남은 멤버 전원 퇴장: matchId 해제 + roomCode 초기화(클라 메뉴 복귀)
            foreach (int otherId in room.conns.ToArray())
            {
                _byConn.Remove(otherId);
                if (NetworkServer.connections.TryGetValue(otherId, out var other))
                {
                    SetMatch(other, Guid.Empty);
                    if (other.identity != null && other.identity.TryGetComponent(out RoomPlayer rp)) rp.roomCode = "";
                }
            }
            room.conns.Clear();

            _byCode.Remove(room.code);
            if (room.session != null) NetworkServer.Destroy(room.session.gameObject);
            Debug.Log($"[RoomServer] 방 정리 code={room.code} (남은 방 {_byCode.Count})");
        }

        private static void SetMatch(NetworkConnectionToClient conn, Guid matchId)
        {
            if (conn != null && conn.identity != null && conn.identity.TryGetComponent(out NetworkMatch nm))
                nm.matchId = matchId;
        }

        // 헷갈리는 글자(0/O/1/I 등) 제외한 4자리 코드, 충돌 없을 때까지 재생성.
        private string GenerateCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string code;
            int guard = 0;
            do
            {
                var c = new char[4];
                for (int i = 0; i < 4; i++) c[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
                code = new string(c);
            } while (_byCode.ContainsKey(code) && ++guard < 100);
            return code;
        }

        private static string Normalize(string code) =>
            string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    }
}
