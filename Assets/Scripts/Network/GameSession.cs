using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using OC.Characters;
using OC.Core;
using OC.Dialogue;
using OC.UI;
using UnityEngine;

namespace OC.Net
{
    public enum NetInput { Advance, Choose, RpDone, StubContinue, Accuse }

    /// <summary>
    /// 한 방(2인)의 세션 코디네이터 — 데디 서버에 방마다 1개씩 스폰된다(RoomServer).
    /// NetworkMatch + MatchInterestManagement 로 같은 방 멤버끼리만 observer 가 되므로,
    /// 아래 Command/ClientRpc 는 모두 '그 방의 두 사람'에게만 전달된다.
    /// 인스턴스 필드(_slotConn/_customizedCount/_begun/_seq)가 곧 방별 상태다.
    ///
    /// 서버(플레이어 없음): 슬롯 선택 중재, 입력 시퀀싱/중계, 이름·사진 중계, 시작 신호. 뷰/러너 없음.
    /// 클라(2명): 각자 DialogueRunner+DialogueView 를 만들고(락스텝), P1/P2 를 직접 골라 커스텀→플레이.
    /// </summary>
    public class GameSession : NetworkBehaviour
    {
        [Tooltip("이 인원이 모두 커스텀을 마치면 시작. 2인 협력.")]
        [SerializeField] private int requiredPlayers = 2;

        private const int ChunkSize = 4096;

        // 클라 전용
        private DialogueRunner _runner;
        private DialogueView _view;
        private PlayerSlot _localSlot;
        private bool _slotAssigned;
        private int _seq;
        private readonly Dictionary<(int slot, int img), byte[][]> _imgBuffers = new Dictionary<(int, int), byte[][]>();

        // 서버 전용
        private readonly int[] _slotConn = { -1, -1 }; // 슬롯별 점유 connectionId (-1=빈칸)
        private int _customizedCount;
        private bool _begun;

        private void Awake() => Debug.Log("[GameSession] Awake — 방 스폰됨");

        // 각 클라(이 방을 보게 된 사람)에서 호출 — 게임을 빌드하고 슬롯 선택 로비로.
        public override void OnStartClient()
        {
            Debug.Log("[GameSession] OnStartClient — 클라 빌드 + 슬롯 선택");
            BuildGame();
            RoomUI.Instance?.BeginSlotPick(this);
        }

        public override void OnStartServer() => Debug.Log("[GameSession] OnStartServer — 방 코디네이터 시작");

        // 방이 깨지면(상대 이탈/디스폰) 이 방의 클라 화면을 정리한다.
        public override void OnStopClient()
        {
            if (_view != null) { Destroy(_view.gameObject); _view = null; }
            foreach (var cs in FindObjectsOfType<CustomizationScreen>()) Destroy(cs.gameObject);
            _runner = null;
        }

        private void BuildGame()
        {
            if (_runner != null) return;
            PlayerNames.Reset();
            CharacterStore.ResetAll();

            var data = ScenarioLoader.LoadFromResources();
            if (data == null) { Debug.LogError("[GameSession] 시나리오 로드 실패."); return; }

            _runner = new DialogueRunner(data);
            _view = DialogueView.Init(_runner);
            _view.SetActions(new NetworkDialogueActions(this));
        }

        // ───────── 슬롯 선택 (서버 중재: 먼저 고른 사람이 차지) ─────────

        /// <summary>클라가 P1(0)/P2(1) 를 직접 고를 때 호출.</summary>
        public void PickSlot(int slot) => CmdPickSlot(slot);

        [Command(requiresAuthority = false)]
        private void CmdPickSlot(int slot, NetworkConnectionToClient sender = null)
        {
            if (sender == null || slot < 0 || slot > 1) return;

            // 이미 이 사람이 어떤 슬롯을 차지했으면 무시(변경 불가).
            if (_slotConn[0] == sender.connectionId || _slotConn[1] == sender.connectionId) return;

            if (_slotConn[slot] != -1)
            {
                TargetSlotRejected(sender, "이미 선택된 자리예요.");
                return;
            }

            _slotConn[slot] = sender.connectionId;
            Debug.Log($"[GameSession] 슬롯 선택 conn={sender.connectionId} → P{slot + 1}");
            RpcSlotState(_slotConn[0] != -1, _slotConn[1] != -1);
            TargetAssignSlot(sender, slot);
        }

        [TargetRpc]
        private void TargetSlotRejected(NetworkConnectionToClient target, string reason)
            => RoomUI.Instance?.OnSlotRejected(reason);

        [ClientRpc]
        private void RpcSlotState(bool p1Taken, bool p2Taken)
            => RoomUI.Instance?.SetSlotState(p1Taken, p2Taken);

        [TargetRpc]
        private void TargetAssignSlot(NetworkConnectionToClient target, int slotIndex)
        {
            _localSlot = (PlayerSlot)slotIndex;
            _slotAssigned = true;
            if (_view != null) _view.LocalSlot = _localSlot;
            Debug.Log($"[GameSession] 내 슬롯 = {_localSlot} → 커스텀 시작");
            RoomUI.Instance?.OnSlotAssigned();
            CustomizationScreen.Show(bothSlots: false, localSlot: _localSlot, onConfirm: OnLocalCustomizationConfirmed);
        }

        private void OnLocalCustomizationConfirmed()
        {
            CmdSetName((int)_localSlot, PlayerNames.Of(_localSlot));
            StartCoroutine(SendPhotos(_localSlot));
            CmdClientCustomized();
        }

        // ───────── 상대 이탈 ─────────

        /// <summary>서버: 한 사람이 방을 나가면 호출(RoomServer). 슬롯을 비우고 남은 상대에게 알린다.</summary>
        [Server]
        public void OnPlayerLeft(int connId)
        {
            for (int i = 0; i < _slotConn.Length; i++)
                if (_slotConn[i] == connId) _slotConn[i] = -1;
            RpcPartnerLeft();
        }

        [ClientRpc]
        private void RpcPartnerLeft() => RoomUI.Instance?.OnPartnerLeft();

        // ───────── 이름 + 시작 ─────────

        [Command(requiresAuthority = false)]
        private void CmdSetName(int slot, string name) => RpcApplyName(slot, name);

        [ClientRpc]
        private void RpcApplyName(int slot, string name) => PlayerNames.Set((PlayerSlot)slot, name);

        [Command(requiresAuthority = false)]
        private void CmdClientCustomized()
        {
            _customizedCount++;
            Debug.Log($"[GameSession] 커스텀 완료 → {_customizedCount}/{requiredPlayers}");
            if (!_begun && _customizedCount >= requiredPlayers)
            {
                _begun = true;
                Debug.Log("[GameSession] 모두 완료 → 시작");
                RpcBegin();
            }
        }

        [ClientRpc]
        private void RpcBegin()
        {
            Debug.Log("[GameSession] RpcBegin → 게임 시작");
            RoomUI.Instance?.OnGameBegan();
            _runner?.Begin();
            _view?.MaybeShowP1Briefing();   // P1 에게만 과거 비밀 사전 브리핑(RP 대비)
        }

        // ───────── 다시 시작 (둘 다 눌러야 — 슬롯/커스텀 유지, 시나리오만 처음부터) ─────────

        private readonly HashSet<int> _restartConns = new HashSet<int>();

        /// <summary>클라: 엔딩 후 '다시 시작'. 두 사람이 모두 눌러야 같은 방이 처음부터(슬롯 재선택 없음) 다시 시작된다.</summary>
        public void SubmitRestart() => CmdRestart();

        [Command(requiresAuthority = false)]
        private void CmdRestart(NetworkConnectionToClient sender = null)
        {
            if (!_begun || sender == null) return;
            _restartConns.Add(sender.connectionId);
            bool p1 = _slotConn[0] != -1 && _restartConns.Contains(_slotConn[0]);
            bool p2 = _slotConn[1] != -1 && _restartConns.Contains(_slotConn[1]);
            RpcRestartState(p1, p2);
            if (p1 && p2)
            {
                _restartConns.Clear();
                _proceedConns.Clear();
                _cookDone.Clear();
                _seq = 0;                 // 시퀀스 리셋(클라도 0으로 맞춤)
                RpcRestart();
            }
        }

        // 동의 현황 표시는 '다음으로 ▶'와 동일한 대기 UI 를 재사용("나 ✓ — 상대를 기다리는 중…").
        [ClientRpc]
        private void RpcRestartState(bool p1Ready, bool p2Ready) => _view?.ShowProceedWaiting(p1Ready, p2Ready);

        [ClientRpc]
        private void RpcRestart()
        {
            _seq = 0;
            RebuildKeepCustomization();
        }

        // BuildGame 과 달리 PlayerNames/CharacterStore 를 리셋하지 않는다 → 이름·사진(커스텀) 유지.
        private void RebuildKeepCustomization()
        {
            if (_view != null) { Destroy(_view.gameObject); _view = null; }

            var data = ScenarioLoader.LoadFromResources();
            if (data == null) { Debug.LogError("[GameSession] 재시작: 시나리오 로드 실패."); return; }

            _runner = new DialogueRunner(data);
            _view = DialogueView.Init(_runner);
            _view.LocalSlot = _localSlot;
            _view.SetActions(new NetworkDialogueActions(this));
            _runner.Begin();
            _view.MaybeShowP1Briefing();
        }

        // ───────── 사진 청크 전송 ─────────

        private IEnumerator SendPhotos(PlayerSlot slot)
        {
            int count = CharacterStore.Count(slot);
            for (int img = 0; img < count; img++)
            {
                var sp = CharacterStore.At(slot, img);
                if (sp == null || sp.texture == null) continue;
                string expr = CharacterStore.NameAt(slot, img) ?? "평온";

                byte[] jpg;
                try { jpg = sp.texture.EncodeToJPG(80); }
                catch (Exception e) { Debug.LogError($"[GameSession] 사진 인코딩 실패: {e.Message}"); continue; }

                int total = Mathf.Max(1, Mathf.CeilToInt(jpg.Length / (float)ChunkSize));
                for (int ci = 0; ci < total; ci++)
                {
                    int off = ci * ChunkSize;
                    int len = Mathf.Min(ChunkSize, jpg.Length - off);
                    var part = new byte[len];
                    Array.Copy(jpg, off, part, 0, len);
                    CmdImageChunk((int)slot, img, expr, ci, total, part);
                }
                yield return null;
            }
        }

        [Command(requiresAuthority = false)]
        private void CmdImageChunk(int slot, int img, string expr, int ci, int total, byte[] data) => RpcImageChunk(slot, img, expr, ci, total, data);

        [ClientRpc]
        private void RpcImageChunk(int slot, int img, string expr, int ci, int total, byte[] data)
        {
            if (_slotAssigned && (PlayerSlot)slot == _localSlot) return; // 내 사진은 이미 보유

            var key = (slot, img);
            if (!_imgBuffers.TryGetValue(key, out var arr) || arr.Length != total) { arr = new byte[total][]; _imgBuffers[key] = arr; }
            if (ci < 0 || ci >= total) return;
            arr[ci] = data;

            foreach (var c in arr) if (c == null) return;

            int totalLen = 0; foreach (var c in arr) totalLen += c.Length;
            var full = new byte[totalLen]; int o = 0;
            foreach (var c in arr) { Array.Copy(c, 0, full, o, c.Length); o += c.Length; }
            _imgBuffers.Remove(key);

            var tex = ImageUtil.DecodeResized(full);
            if (tex != null) CharacterStore.SetPortrait((PlayerSlot)slot, expr, ImageUtil.ToSprite(tex));
        }

        // ───────── 상태 변경 입력 (시퀀스 가드, 클라에서 적용) ─────────

        public void SubmitState(NetInput type, int index, string str) => CmdState(type, index, str, _seq);

        [Command(requiresAuthority = false)]
        private void CmdState(NetInput type, int index, string str, int clientSeq)
        {
            if (!_begun) return;
            if (clientSeq != _seq) return;   // 중복/오래된 입력 무시
            _proceedConns.Clear();           // 다른 상태 변경이 일어나면 진행 동의는 초기화
            _seq++;
            RpcState(type, index, str, _seq);
        }

        // ───────── 진행 동의(양쪽이 모두 눌러야 다음으로) ─────────

        private readonly HashSet<int> _proceedConns = new HashSet<int>();

        /// <summary>클라: '다음으로' 동의. 두 사람이 모두 보내야 실제로 진행한다.</summary>
        public void SubmitProceed() => CmdProceed(_seq);

        [Command(requiresAuthority = false)]
        private void CmdProceed(int clientSeq, NetworkConnectionToClient sender = null)
        {
            if (!_begun || sender == null) return;
            if (clientSeq != _seq) return;   // 이미 진행된(오래된) 비트의 동의는 무시
            _proceedConns.Add(sender.connectionId);
            bool p1 = _slotConn[0] != -1 && _proceedConns.Contains(_slotConn[0]);
            bool p2 = _slotConn[1] != -1 && _proceedConns.Contains(_slotConn[1]);
            RpcProceedState(p1, p2);
            if (p1 && p2)
            {
                _proceedConns.Clear();
                _seq++;
                RpcProceed(_seq);
            }
        }

        [ClientRpc]
        private void RpcProceedState(bool p1Ready, bool p2Ready) => _view?.ShowProceedWaiting(p1Ready, p2Ready);

        [ClientRpc]
        private void RpcProceed(int seq)
        {
            _seq = seq;
            _view?.ClearProceedWaiting();
            if (_runner == null) return;
            try { _runner.Advance(); }
            catch (Exception e) { Debug.LogError($"[GameSession] 진행 오류: {e}"); }
        }

        // ───────── 상시 채팅 ─────────

        public void SubmitChat(string speakerToken, string text) => CmdChat(speakerToken, text);

        [Command(requiresAuthority = false)]
        private void CmdChat(string speakerToken, string text) { if (_begun) RpcChat(speakerToken, text); }

        [ClientRpc]
        private void RpcChat(string speakerToken, string text) => _view?.AppendChatLine(speakerToken, text);

        // ───────── 요리: 둘 다 완성해야 진행 ─────────

        private readonly HashSet<int> _cookDone = new HashSet<int>();

        public void CookDone(int slot) => CmdCookDone(slot);

        [Command(requiresAuthority = false)]
        private void CmdCookDone(int slot)
        {
            if (!_begun || slot < 0 || slot > 1) return;
            _cookDone.Add(slot);
            if (_cookDone.Contains(0) && _cookDone.Contains(1)) { _cookDone.Clear(); RpcCookBothDone(); }
        }

        [ClientRpc]
        private void RpcCookBothDone() => _activeMinigame?.NetSignal(1);

        [ClientRpc]
        private void RpcState(NetInput type, int index, string str, int seq)
        {
            _seq = seq;
            if (_runner == null) return;
            try
            {
                switch (type)
                {
                    case NetInput.Advance: _runner.Advance(); break;
                    case NetInput.Choose: _runner.ChooseOption(index); break;
                    case NetInput.RpDone: _runner.Continue(); break;
                    case NetInput.StubContinue: _runner.Continue(); break;
                    case NetInput.Accuse: _runner.ResolveAccusation(str); break;
                }
            }
            catch (Exception e) { Debug.LogError($"[GameSession] 입력 적용 오류: {e}"); }
        }

        // ───────── 비상태 입력 ─────────

        public void SubmitDeduce(int index) => CmdDeduce(index);

        [Command(requiresAuthority = false)]
        private void CmdDeduce(int index) { if (_begun) RpcDeduce(index); }

        [ClientRpc]
        private void RpcDeduce(int index)
        {
            if (_runner == null || _view == null) return;
            bool correct = _runner.CheckDeduction(index, out var feedback);
            _view.ShowDeductionResult(correct, feedback);
        }

        // ───────── 미니게임 동기화 (클릭 중계 → 양쪽 동일 보드에 적용) ─────────

        private IMinigameView _activeMinigame;

        /// <summary>클라: 현재 떠 있는 미니게임 뷰 등록. 서버가 중계한 클릭을 이쪽에 적용한다.</summary>
        public void SetActiveMinigame(IMinigameView view) => _activeMinigame = view;

        /// <summary>클라: 미니게임 핫스팟 클릭 → 서버로. 서버가 양쪽에 중계한다. slot=누른 플레이어(인당 예산 판정용).</summary>
        public void MinigameClick(int index, int slot) => CmdMinigameClick(index, slot);

        [Command(requiresAuthority = false)]
        private void CmdMinigameClick(int index, int slot) { if (_begun) RpcMinigameClick(index, slot); }

        [ClientRpc]
        private void RpcMinigameClick(int index, int slot) => _activeMinigame?.ApplyClick(index, slot);

        public void SubmitRpAdd(string speakerToken, string text) => CmdRpAdd(speakerToken, text);

        [Command(requiresAuthority = false)]
        private void CmdRpAdd(string speakerToken, string text) { if (_begun) RpcRpAdd(speakerToken, text); }

        [ClientRpc]
        private void RpcRpAdd(string speakerToken, string text) => _view?.AppendRpLine(speakerToken, text);
    }
}
