namespace OC.Dialogue
{
    /// <summary>클릭 기반 미니게임 뷰. 서버가 중계한 클릭을 양쪽에서 동일하게 적용한다.</summary>
    public interface IMinigameView
    {
        /// <summary>slot 플레이어가 index 번째 핫스팟을 클릭한 것을 보드에 적용(락스텝 — 양쪽 동일하게).
        /// index == -1 은 'slot 플레이어가 조사를 마침' 신호. slot 0=P1, 1=P2.</summary>
        void ApplyClick(int index, int slot);

        /// <summary>서버가 보내는 미니게임 신호(예: 1='둘 다 요리 완성'). 해당 없으면 무시.</summary>
        void NetSignal(int code);
    }

    /// <summary>
    /// DialogueView 의 사용자 입력을 처리하는 이음새.
    /// - 싱글플레이: LocalDialogueActions 가 DialogueRunner 를 직접 호출.
    /// - 네트워크: NetworkDialogueActions 가 GameSession 으로 보내 양쪽에 중계.
    /// 표시(렌더링)는 각 피어의 DialogueRunner 이벤트가 담당하므로 여기엔 입력만 둔다.
    /// </summary>
    public interface IDialogueActions
    {
        /// <summary>네트워크 플레이면 true(미니게임 클릭을 서버로 중계).</summary>
        bool IsNetworkPlay { get; }
        /// <summary>미니게임 핫스팟 클릭을 서버에 보내 양쪽에 중계(네트워크 전용). slot=누른 플레이어, index=-1은 '조사 마침'.</summary>
        void MinigameClick(int index, int slot);
        /// <summary>현재 떠 있는 미니게임 뷰를 등록(서버 중계 클릭을 적용받기 위해). 로컬은 무시.</summary>
        void RegisterMinigame(IMinigameView view);

        /// <summary>일반 대사(Line/Narration/추리 해설)에서 다음으로 진행.</summary>
        void Advance();
        /// <summary>선택지 선택.</summary>
        void Choose(int index);
        /// <summary>추리 퍼즐 보기 선택(정답이면 진행, 오답이면 힌트).</summary>
        void Deduce(int index);
        /// <summary>RP 대사 한 줄 추가(상대 화면에도 표시).</summary>
        void RpAdd(string speakerToken, string text);
        /// <summary>상시 채팅 한 줄(언제든 가능, 상대 화면에도 표시).</summary>
        void Chat(string speakerToken, string text);
        /// <summary>요리 미니게임에서 내 접시를 완성했음을 알림(둘 다 끝나야 진행).</summary>
        void CookDone(int slot);
        /// <summary>RP 종료, 다음으로.</summary>
        void RpDone();
        /// <summary>미니게임/심문 등 스텁에서 계속.</summary>
        void StubContinue();
        /// <summary>범인 지목.</summary>
        void Accuse(string characterId);
        /// <summary>엔딩 후 다시 시작 — 네트워크면 같은 방 두 사람 모두 처음부터(슬롯·커스텀 유지), 싱글이면 씬 리로드.</summary>
        void Restart();
    }
}
