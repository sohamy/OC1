using System;
using System.Collections.Generic;
using OC.Core;
using UnityEngine;

namespace OC.Dialogue
{
    /// <summary>
    /// 시나리오 진행을 담당하는 권위적 상태 머신. UI·네트워크에 독립적인 순수 로직이다.
    /// 호스트가 이 인스턴스를 소유하고, 진행 상황(씬/비트 인덱스)을 클라이언트에 동기화한다.
    ///
    /// 진행 규칙:
    /// - 비트를 하나씩 소비하며 OnBeat 를 발행한다.
    /// - GiveClue 는 자동으로 단서를 적립하고 다음 비트로 넘어간다.
    /// - Choice / RpBeat / Minigame / Interrogation / Accusation 은 외부 입력을 기다린다(일시정지).
    /// - 장면 끝에 도달하면 다음 장면으로, 마지막 장면 끝이면 멈춘다(보통 Accusation 으로 종료).
    /// </summary>
    public class DialogueRunner
    {
        private readonly ScenarioData _data;

        private int _sceneIndex = -1;
        private int _beatIndex = -1;
        private EndingDef _activeEnding;          // 엔딩 시퀀스 진행 중이면 not null
        private int _endingBeatIndex = -1;

        public readonly HashSet<string> Flags = new HashSet<string>();
        public readonly HashSet<string> CollectedClues = new HashSet<string>();

        // --- 이벤트 (호스트 측에서 구독해 UI/RPC 로 중계) ---
        public event Action<SceneData> OnSceneChanged;
        public event Action<Beat> OnBeat;
        public event Action<ClueDef> OnClueGained;
        public event Action<Beat> OnMinigame;           // 미니게임 비트(쿠킹/탐색). 탐색은 beat.searchClueIds/searchBudget 사용
        public event Action<Beat> OnInterrogation;
        public event Action<Beat> OnAccusation;
        public event Action<EndingDef> OnEnding;
        public event Action OnFinished;

        public DialogueRunner(ScenarioData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public ScenarioData Data => _data;
        public bool IsWaitingForInput { get; private set; }
        public Beat CurrentBeat { get; private set; }
        public SceneData CurrentScene =>
            _activeEnding == null && _sceneIndex >= 0 && _sceneIndex < _data.scenes.Count
                ? _data.scenes[_sceneIndex] : null;

        public void Begin()
        {
            _sceneIndex = 0;
            _beatIndex = -1;
            _activeEnding = null;
            if (_data.scenes.Count == 0) { OnFinished?.Invoke(); return; }
            OnSceneChanged?.Invoke(_data.scenes[0]);
            Advance();
        }

        /// <summary>다음 비트로 진행. 입력 대기형 비트(Choice/Rp/Minigame/Interrogation/Accusation)에서는
        /// 외부에서 해당 입력을 처리한 뒤 다시 Advance 를 호출해야 한다.</summary>
        public void Advance()
        {
            IsWaitingForInput = false;

            if (_activeEnding != null) { AdvanceEnding(); return; }

            _beatIndex++;

            // 장면 끝 → 다음 장면
            while (_sceneIndex < _data.scenes.Count && _beatIndex >= _data.scenes[_sceneIndex].beats.Count)
            {
                _sceneIndex++;
                _beatIndex = 0;
                if (_sceneIndex >= _data.scenes.Count) { OnFinished?.Invoke(); return; }
                OnSceneChanged?.Invoke(_data.scenes[_sceneIndex]);
            }

            CurrentBeat = _data.scenes[_sceneIndex].beats[_beatIndex];
            if (ShouldSkip(CurrentBeat)) { Advance(); return; } // 단서 조건 미충족 → 건너뜀
            Dispatch(CurrentBeat);
        }

        /// <summary>requiresClue 단서를 못 얻었거나, requiresFlag 플래그가 없으면 이 비트는 건너뛴다.</summary>
        private bool ShouldSkip(Beat beat)
        {
            if (beat == null) return false;
            if (!string.IsNullOrEmpty(beat.requiresClue) && !CollectedClues.Contains(beat.requiresClue)) return true;
            if (!string.IsNullOrEmpty(beat.requiresFlag) && !Flags.Contains(beat.requiresFlag)) return true;
            return false;
        }

        private void Dispatch(Beat beat)
        {
            switch (beat.type)
            {
                case BeatType.GiveClue:
                    GainClue(beat.clueId);
                    OnBeat?.Invoke(beat);   // UI 가 '단서 획득' 연출
                    Advance();              // 단서는 자동 진행
                    return;

                case BeatType.GoToScene:
                    JumpToScene(beat.nextSceneId);
                    return;

                case BeatType.Ending:
                    StartEnding(beat.endingId);
                    return;

                case BeatType.Minigame:
                    IsWaitingForInput = true;
                    OnMinigame?.Invoke(beat);
                    return;

                case BeatType.Interrogation:
                    IsWaitingForInput = true;
                    OnBeat?.Invoke(beat);
                    OnInterrogation?.Invoke(beat);
                    return;

                case BeatType.Accusation:
                    IsWaitingForInput = true;
                    OnBeat?.Invoke(beat);
                    OnAccusation?.Invoke(beat);
                    return;

                case BeatType.Choice:
                case BeatType.RpBeat:
                case BeatType.Deduction:
                    IsWaitingForInput = true;
                    OnBeat?.Invoke(beat);
                    return;

                default: // Line / Narration
                    OnBeat?.Invoke(beat);
                    return;
            }
        }

        // --- 외부 입력 처리 ---

        /// <summary>Choice 비트에서 선택지를 고른 뒤 호출.</summary>
        public void ChooseOption(int index)
        {
            if (CurrentBeat == null || CurrentBeat.type != BeatType.Choice || CurrentBeat.choices == null) return;
            if (index < 0 || index >= CurrentBeat.choices.Count) return;

            var opt = CurrentBeat.choices[index];
            if (!string.IsNullOrEmpty(opt.setFlag)) Flags.Add(opt.setFlag);

            if (!string.IsNullOrEmpty(opt.nextSceneId))
                JumpToScene(opt.nextSceneId);
            else
                Advance();
        }

        /// <summary>RpBeat / Minigame / Interrogation 종료 후 호출(다음 비트로).</summary>
        public void Continue() => Advance();

        /// <summary>Deduction(추리 퍼즐) 보기 선택을 판정한다. 진행은 시키지 않는다.
        /// 정답이면 true(+플래그 적립), 오답이면 false. feedback 에 해설/힌트를 채운다.
        /// 정답 후에는 호출 측이 Advance() 로 다음 비트로 넘어가야 한다.</summary>
        public bool CheckDeduction(int index, out string feedback)
        {
            feedback = null;
            if (CurrentBeat == null || CurrentBeat.type != BeatType.Deduction || CurrentBeat.choices == null)
                return false;
            if (index < 0 || index >= CurrentBeat.choices.Count) return false;

            var opt = CurrentBeat.choices[index];
            feedback = opt.feedback;
            if (opt.correct && !string.IsNullOrEmpty(opt.setFlag)) Flags.Add(opt.setFlag);
            return opt.correct;
        }

        /// <summary>Accusation 결과 처리. 정답 여부 + 믿음 플래그로 엔딩을 고른다.</summary>
        public EndingDef ResolveAccusation(string accusedCharacterId)
        {
            bool correct = accusedCharacterId == _data.culpritId;
            string endingId;
            if (!correct)
            {
                // 플레이어 본인(P1)을 지목한 경우엔 전용 엔딩으로 — '신고자였으나 전부 말하지 않은' P1의 진실이 드러난다.
                // 같은 엔딩이지만, 그동안의 신뢰/의심 선택에 따라 대사 톤이 갈린다(requiresFlag 로 분기).
                if (accusedCharacterId == Speakers.Player1)
                {
                    int trust = (Flags.Contains("trust_yes") ? 1 : 0) + (Flags.Contains("stood_by_p1") ? 1 : 0);
                    int distrust = (Flags.Contains("trust_no") ? 1 : 0) + (Flags.Contains("pressed_p1") ? 1 : 0);
                    if (trust >= 2) Flags.Add("p1_end_betrayal");        // 끝까지 믿었는데 지목 → 배신감
                    else if (distrust >= 2) Flags.Add("p1_end_forced");  // 줄곧 의심했고 지목 → '그럴 수밖에'
                    else Flags.Add("p1_end_cold");                       // 어중간한 신뢰 → 차갑고 파국적
                    endingId = "ending_p1_accused";
                }
                else endingId = "ending_bad";
            }
            else
            {
                // 정답(심태오)을 맞힌 뒤엔, '관계 신뢰'와 '단서 완성도' 두 축으로 엔딩이 4갈래로 갈린다.
                int trust = 0;
                if (Flags.Contains("trust_yes")) trust++;     // 5장: 무슨 일이 있어도 믿는다
                if (Flags.Contains("stood_by_p1")) trust++;   // 8장: 흔들려도 곁에 선다
                // 심태오 생존·설비 조작의 진실 단서들(여러 사건에 분산 배치 — 하나 놓쳐도 보완 가능).
                int keyClues = 0;
                if (CollectedClues.Contains("clue_blood")) keyClues++;          // 보일러실: 끊긴 핏자국
                if (CollectedClues.Contains("clue_cut_wire")) keyClues++;       // 보일러실: 끊긴 전선
                if (CollectedClues.Contains("clue_emergency_light")) keyClues++;// 보일러실: 비상등 회로
                if (CollectedClues.Contains("clue_corridor_light")) keyClues++; // 진료실: 사후 복도등 점멸
                if (CollectedClues.Contains("clue_auto_lock")) keyClues++;      // 자료실: 사후 자동잠금 오류
                if (CollectedClues.Contains("clue_maint_log")) keyClues++;      // 자료실: 점검자 동선
                if (CollectedClues.Contains("clue_map")) keyClues++;            // 자료실: 구관 도면

                bool trustHigh = trust >= 2;       // 두 번의 신뢰 선택을 모두 지킴
                bool cluesEnough = keyClues >= 2;  // 생존 근거를 둘 이상 확보

                if (trustHigh && cluesEnough) endingId = "ending_salvation";    // 믿음 + 단서 충분
                else if (trustHigh) endingId = "ending_trust_blind";            // 믿음 + 단서 부족
                else if (cluesEnough) endingId = "ending_truth";                // 안 믿음 + 단서 충분
                else endingId = "ending_hollow";                                // 안 믿음 + 단서 부족
            }

            StartEnding(endingId);
            return _activeEnding;
        }

        // --- 단서 ---
        private void GainClue(string clueId)
        {
            if (string.IsNullOrEmpty(clueId)) return;
            if (CollectedClues.Add(clueId))
            {
                var def = _data.FindClue(clueId);
                if (def != null) OnClueGained?.Invoke(def);
            }
        }

        /// <summary>탐색 미니게임 등 외부에서 단서를 적립한다(OnClueGained 발행).</summary>
        public void GainClueExternal(string clueId) => GainClue(clueId);

        public bool HasClue(string clueId) => CollectedClues.Contains(clueId);

        public List<ClueDef> GetCollectedClueDefs()
        {
            var list = new List<ClueDef>();
            foreach (var id in CollectedClues)
            {
                var d = _data.FindClue(id);
                if (d != null) list.Add(d);
            }
            return list;
        }

        // --- 장면/엔딩 점프 ---
        private void JumpToScene(string sceneId)
        {
            int idx = _data.scenes.FindIndex(s => s.id == sceneId);
            if (idx < 0)
            {
                Debug.LogError($"[DialogueRunner] 알 수 없는 장면 id: {sceneId}");
                Advance();
                return;
            }
            _sceneIndex = idx;
            _beatIndex = -1;
            OnSceneChanged?.Invoke(_data.scenes[idx]);
            Advance();
        }

        private void StartEnding(string endingId)
        {
            var ending = _data.FindEnding(endingId);
            if (ending == null)
            {
                Debug.LogError($"[DialogueRunner] 알 수 없는 엔딩 id: {endingId}");
                OnFinished?.Invoke();
                return;
            }
            _activeEnding = ending;
            _endingBeatIndex = -1;
            OnEnding?.Invoke(ending);
            AdvanceEnding();
        }

        private void AdvanceEnding()
        {
            _endingBeatIndex++;
            if (_endingBeatIndex >= _activeEnding.beats.Count) { OnFinished?.Invoke(); return; }

            CurrentBeat = _activeEnding.beats[_endingBeatIndex];
            if (ShouldSkip(CurrentBeat)) { AdvanceEnding(); return; } // 단서 조건 미충족 → 건너뜀
            // 엔딩 안에서도 RpBeat 는 입력 대기
            if (CurrentBeat.type == BeatType.RpBeat)
            {
                IsWaitingForInput = true;
                OnBeat?.Invoke(CurrentBeat);
            }
            else
            {
                OnBeat?.Invoke(CurrentBeat);
            }
        }

        // --- 네트워크 동기화용 상태 스냅샷 ---
        public (int scene, int beat, string endingId, int endingBeat) Snapshot()
            => (_sceneIndex, _beatIndex, _activeEnding?.id, _endingBeatIndex);
    }
}
