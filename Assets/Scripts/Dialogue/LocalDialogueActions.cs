using OC.UI;
using UnityEngine.SceneManagement;

namespace OC.Dialogue
{
    /// <summary>싱글플레이용 입력 처리 — DialogueRunner 를 직접 호출한다.</summary>
    public class LocalDialogueActions : IDialogueActions
    {
        private readonly DialogueRunner _runner;
        private readonly DialogueView _view;

        public LocalDialogueActions(DialogueRunner runner, DialogueView view)
        {
            _runner = runner;
            _view = view;
        }

        public void Advance() => _runner.Advance();
        public void Choose(int index) => _runner.ChooseOption(index);

        public void Deduce(int index)
        {
            bool correct = _runner.CheckDeduction(index, out var feedback);
            _view.ShowDeductionResult(correct, feedback);
        }

        public void RpAdd(string speakerToken, string text) => _view.AppendRpLine(speakerToken, text);
        public void Chat(string speakerToken, string text) => _view.AppendChatLine(speakerToken, text);
        public void CookDone(int slot) { }   // 싱글: 자기 접시만 끝내면 즉시 진행
        public void RpDone() => _runner.Continue();
        public void StubContinue() => _runner.Continue();
        public void Accuse(string characterId) => _runner.ResolveAccusation(characterId);
        // 싱글: 씬 리로드로 처음부터(슬롯/이름 다시 선택).
        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        // 싱글플레이: 미니게임 클릭은 뷰가 직접 적용하므로 중계 불필요.
        public bool IsNetworkPlay => false;
        public void MinigameClick(int index, int slot) { }
        public void RegisterMinigame(IMinigameView view) { }
    }
}
