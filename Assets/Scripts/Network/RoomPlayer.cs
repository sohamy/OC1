using System;
using Mirror;
using OC.UI;
using UnityEngine;

namespace OC.Net
{
    /// <summary>
    /// 각 접속의 플레이어 오브젝트에 붙는다(autoCreatePlayer). 방 생성/참가의 진입점.
    /// Command 는 '자기 소유 오브젝트'에서 호출하므로 아직 어떤 방의 observer 가 아니어도 동작한다.
    /// 방에 들어가면 NetworkMatch.matchId 가 서버에서 설정되어 그 방의 GameSession 을 보게 된다.
    /// </summary>
    public class RoomPlayer : NetworkBehaviour
    {
        public static RoomPlayer Local { get; private set; }

        /// <summary>현재 방 코드(빈 문자열이면 방 없음). 서버가 설정 → 클라가 hook 으로 수신.</summary>
        [SyncVar(hook = nameof(OnRoomCodeChanged))]
        public string roomCode = "";

        /// <summary>로컬 클라 UI 가 구독: 방 코드 변경 / 에러 안내.</summary>
        public event Action<string> RoomCodeChanged;
        public event Action<string> ErrorReceived;

        public override void OnStartLocalPlayer()
        {
            Local = this;
            RoomUI.ShowFor(this);
        }

        public override void OnStopLocalPlayer()
        {
            if (Local == this) Local = null;
        }

        private void OnRoomCodeChanged(string _, string value) => RoomCodeChanged?.Invoke(value);

        // ── 클라 → 서버 ──
        [Command]
        public void CmdCreateRoom()
        {
            if (RoomServer.Instance == null) { TargetError(connectionToClient, "서버에 방 시스템이 없습니다."); return; }
            string code = RoomServer.Instance.CreateRoom(connectionToClient);
            if (string.IsNullOrEmpty(code)) { TargetError(connectionToClient, "방 생성 실패."); return; }
            roomCode = code;
        }

        [Command]
        public void CmdJoinRoom(string code)
        {
            if (RoomServer.Instance == null) { TargetError(connectionToClient, "서버에 방 시스템이 없습니다."); return; }
            if (RoomServer.Instance.TryJoin(code, connectionToClient, out string normalized, out string error))
                roomCode = normalized;
            else
                TargetError(connectionToClient, error);
        }

        [TargetRpc]
        private void TargetError(NetworkConnectionToClient target, string message) => ErrorReceived?.Invoke(message);
    }
}
