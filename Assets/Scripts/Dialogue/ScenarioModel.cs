using System;
using System.Collections.Generic;

namespace OC.Dialogue
{
    /// <summary>
    /// 시나리오 전체 데이터. scenario.json 의 루트.
    /// 사람이 읽고 쓰기 좋게 Newtonsoft Json 으로 (역)직렬화한다. (ScenarioLoader 참고)
    /// </summary>
    [Serializable]
    public class ScenarioData
    {
        public string id;
        public string title;

        /// <summary>고정 조연(NPC) 정의. 주인공 커플은 플레이어가 런타임에 커스텀하므로 여기 없음.</summary>
        public List<CharacterDef> cast = new List<CharacterDef>();

        /// <summary>사건의 모든 단서 정의(증거 수집/심문에서 획득).</summary>
        public List<ClueDef> clues = new List<ClueDef>();

        public List<SceneData> scenes = new List<SceneData>();

        /// <summary>정답 범인의 캐릭터 id (Accusation 판정에 사용).</summary>
        public string culpritId;

        public List<EndingDef> endings = new List<EndingDef>();

        public SceneData FindScene(string sceneId) => scenes.Find(s => s.id == sceneId);
        public EndingDef FindEnding(string endingId) => endings.Find(e => e.id == endingId);
        public CharacterDef FindCharacter(string id) => cast.Find(c => c.id == id);
        public ClueDef FindClue(string id) => clues.Find(c => c.id == id);
    }

    /// <summary>고정 조연(호스트/용의자/관리인 등) 정의.</summary>
    [Serializable]
    public class CharacterDef
    {
        public string id;
        public string displayName;
        /// <summary>host / suspect / staff / victim 등 역할 태그(연출·판정 참고용).</summary>
        public string role;
        /// <summary>Resources 하위 포트레이트 경로(선택). 없으면 플레이스홀더.</summary>
        public string portrait;
        public string description;
        /// <summary>용의자라면 동기 요약(심문/추리 참고용).</summary>
        public string motive;
        /// <summary>심문 시 들려줄 증언들(여러 줄). 일부는 단서와 모순되도록 작성.</summary>
        public List<string> testimony = new List<string>();
    }

    [Serializable]
    public class ClueDef
    {
        public string id;
        public string name;
        public string description;
        /// <summary>이 단서가 (표면적으로) 가리키는 인물 id. 비어있으면 중립 단서.</summary>
        public string pointsToId;
        /// <summary>true 면 진범을 직접 가리키는 결정적 단서.</summary>
        public bool decisive;
    }

    [Serializable]
    public class SceneData
    {
        public string id;
        public string title;
        /// <summary>Resources 하위 배경 이미지 경로(선택).</summary>
        public string background;
        public List<Beat> beats = new List<Beat>();
    }

    /// <summary>한 장면을 구성하는 최소 진행 단위. type 에 따라 의미 있는 필드가 달라진다.</summary>
    [Serializable]
    public class Beat
    {
        public BeatType type = BeatType.Line;

        // --- Line / Narration ---
        /// <summary>화자. 조연 id, 또는 특수값 PLAYER1 / PLAYER2 / NARRATION.</summary>
        public string speaker;
        public string text;

        // --- Choice ---
        public List<ChoiceOption> choices;

        // --- RpBeat (자유 대사 입력) ---
        /// <summary>입력 안내문. 예: "둘의 첫인사를 RP 하세요".</summary>
        public string rpPrompt;
        /// <summary>입력 주체: PLAYER1 / PLAYER2 / BOTH.</summary>
        public string rpActor = "BOTH";

        // --- Minigame ---
        /// <summary>cooking / evidence(search).</summary>
        public string minigameId;
        /// <summary>탐색(evidence) 미니게임: 이 횟수만큼만 조사 가능(예산). 0이면 제한 없음.</summary>
        public int searchBudget;
        /// <summary>탐색(evidence) 미니게임: 조사 지점 목록. 각 지점의 label 로 '무엇을 살피는지' 미리 보여 주고,
        /// 고르면 clueId 단서를 얻는다. 지점이 예산보다 많으면 일부는 끝내 살펴보지 못한다 — 단, 무엇을 볼지는 '판단'이다.</summary>
        public List<SearchSpot> searchSpots;

        // --- GiveClue ---
        public string clueId;

        /// <summary>이 단서를 보유했을 때만 이 비트를 재생한다(없으면 건너뜀). 곁가지 단서를 놓치면
        /// 그에 딸린 통찰/누명 해소/뒷이야기 장면도 못 보게 만들어 '못 얻는 단서'에 의미를 준다.</summary>
        public string requiresClue;

        /// <summary>이 플래그가 설정돼 있을 때만 이 비트를 재생한다(없으면 건너뜀).
        /// 같은 엔딩 안에서 신뢰/의심 상태에 따라 대사 그룹을 분기할 때 쓴다(예: P1 지목 엔딩의 톤 분기).</summary>
        public string requiresFlag;

        // --- GoToScene (조건 없는 이동) ---
        public string nextSceneId;

        // --- Ending ---
        public string endingId;

        // --- Interrogation / Accusation ---
        /// <summary>이 장면에서 심문/지목 대상이 되는 캐릭터 id 목록(그 시점 생존자 등).
        /// 비어 있으면 host 가 아닌 모든 조연을 대상으로 한다.</summary>
        public List<string> suspects;
        /// <summary>기본 후보엔 없지만, unlockClueIds 단서를 모두 확보했을 때만 후보로 추가되는 용의자 id.
        /// (예: '죽은 줄 안' 진범 — 살아있다는 단서를 찾아야 비로소 지목할 수 있다.)</summary>
        public List<string> lockedSuspects;
        /// <summary>lockedSuspects 를 공개하기 위해 모두 보유해야 하는 단서 id 목록.</summary>
        public List<string> unlockClueIds;
        /// <summary>지목(Accusation) 화면에서 '사망 / 사망 추정'으로 표시할 suspect id 목록.
        /// 죽은(혹은 죽은 듯한) 사람도 후보에 두되, 특정 한 명만 잠금 해제돼 정답이 노출되지 않도록 —
        /// 여러 사망자를 함께 올려 '죽은 사람 중 하나가 살아 있을 수 있다'를 플레이어가 직접 의심하게 한다.</summary>
        public List<string> presumedDead;
        /// <summary>이 심문(=이 사건/이 비트)에서만 들려줄 증언. suspect id → 그 사건에서의 증언 줄들.
        /// 여기 항목이 있으면 캐릭터 기본 testimony 대신 이걸 쓴다(여기 없는 인물은 기본 testimony 로 폴백).
        /// 사건마다 같은 인물이 다른 말을 하도록 — 심문이 매번 똑같이 반복되지 않게 한다.</summary>
        public Dictionary<string, List<string>> testimonies;
    }

    public enum BeatType
    {
        Line,           // 조연/내레이션 대사
        Narration,      // 지문(화자 없음)
        Choice,         // 협력 선택지
        Deduction,      // 추리 퍼즐(단서로 추론하는 객관식)
        RpBeat,         // 플레이어 자유 대사 입력
        Minigame,       // 미니게임 진입
        GiveClue,       // 단서 획득
        Interrogation,  // 심문 진입(증언↔단서 대조)
        Accusation,     // 범인 지목
        GoToScene,      // 다음 장면으로 이동
        Ending          // 엔딩으로 분기
    }

    /// <summary>탐색 미니게임의 조사 지점. label='무엇을 살피는지'(미리 보임), clueId=고르면 얻는 단서.</summary>
    [Serializable]
    public class SearchSpot
    {
        public string label;
        public string clueId;
    }

    [Serializable]
    public class ChoiceOption
    {
        public string text;
        /// <summary>선택 시 이동할 장면 id(분기). 비어있으면 다음 비트로 진행.</summary>
        public string nextSceneId;
        /// <summary>설정할 플래그 이름(선택). 분위기/엔딩 판정 참고.</summary>
        public string setFlag;
        /// <summary>분위기 태그(선택). 예: warm / tense / cold.</summary>
        public string toneTag;

        // --- Deduction(추리 퍼즐) 전용 ---
        /// <summary>Deduction 비트에서 이 보기가 정답인지.</summary>
        public bool correct;
        /// <summary>선택 시 보여줄 피드백(정답이면 해설, 오답이면 힌트).</summary>
        public string feedback;
    }

    [Serializable]
    public class EndingDef
    {
        public string id;
        public string title;
        /// <summary>truth(진실+대가) / salvation(구원) / bad(파국).</summary>
        public string kind;
        public List<Beat> beats = new List<Beat>();
    }

    /// <summary>화자/주체 특수 상수.</summary>
    public static class Speakers
    {
        public const string Narration = "NARRATION";
        public const string Player1 = "PLAYER1";
        public const string Player2 = "PLAYER2";
        public const string Both = "BOTH";
    }
}
