using OC.Dialogue;

namespace OC.Net
{
    /// <summary>네트워크용 입력 처리 — 모든 입력을 GameSession 으로 보내 양쪽 피어에 중계한다.</summary>
    public class NetworkDialogueActions : IDialogueActions
    {
        private readonly GameSession _session;

        public NetworkDialogueActions(GameSession session) => _session = session;

        // 진행(다음으로)은 둘 다 동의해야 넘어간다 — Advance/RpDone/StubContinue 모두 동의 한 표.
        public void Advance() => _session.SubmitProceed();
        public void RpDone() => _session.SubmitProceed();
        public void StubContinue() => _session.SubmitProceed();

        public void Choose(int index) => _session.SubmitState(NetInput.Choose, index, null);
        public void Deduce(int index) => _session.SubmitDeduce(index);
        public void RpAdd(string speakerToken, string text) => _session.SubmitRpAdd(speakerToken, text);
        public void Chat(string speakerToken, string text) => _session.SubmitChat(speakerToken, text);
        public void CookDone(int slot) => _session.CookDone(slot);
        public void Accuse(string characterId) => _session.SubmitState(NetInput.Accuse, 0, characterId);
        // 한 명이 누르면 같은 방 두 사람 모두 처음부터(슬롯/커스텀 유지) — 서버가 양쪽에 중계.
        public void Restart() => _session.SubmitRestart();

        // 미니게임 클릭은 서버가 양쪽에 중계 → 두 클라가 동일 보드에 적용(동기화).
        public bool IsNetworkPlay => true;
        public void MinigameClick(int index, int slot) => _session.MinigameClick(index, slot);
        public void RegisterMinigame(IMinigameView view) => _session.SetActiveMinigame(view);
    }
}
