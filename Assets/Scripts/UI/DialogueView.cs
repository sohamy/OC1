using System.Collections;
using System.Collections.Generic;
using OC.Characters;
using OC.Core;
using OC.Dialogue;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.UI
{
    /// <summary>
    /// DialogueRunner 를 화면에 그리는 절차적 VN UI.
    /// 표시는 각 피어의 DialogueRunner 이벤트가 구동하고, 사용자 입력은 IDialogueActions 로 위임한다
    /// (싱글=LocalDialogueActions, 네트워크=NetworkDialogueActions).
    /// - RP: 입력한 대사가 "이름 + 대사" 로 쌓이고(스크롤), "다음으로 ▶" 로 진행. 내 슬롯일 때만 입력.
    /// - Deduction: 단서로 추론하는 객관식. 오답이면 힌트 후 재시도, 정답이면 해설 후 진행.
    /// </summary>
    public class DialogueView : MonoBehaviour
    {
        private enum State { Idle, AwaitClick, Choices, Rp, Stub, Accuse, Deduction, Finished }

        private const string ColP1 = "#8CC8FF";
        private const string ColP2 = "#FFB0C8";
        private const string ColNpc = "#FFD98C";
        private const string ColNarr = "#C8C8C8";

        /// <summary>이 화면을 보는 로컬 플레이어의 슬롯(RP 입력 권한 판정). 기본 P1(싱글).</summary>
        public PlayerSlot LocalSlot = PlayerSlot.Player1;

        private DialogueRunner _runner;
        private ScenarioData _data;
        private IDialogueActions _actions;
        private State _state = State.Idle;
        private int _stateFrame;

        private Font _font;
        private Text _bodyText;
        private ScrollRect _bodyScroll;
        private Text _continueHint;
        private Text _toast;
        private Image _background;
        private Image _portrait;
        private Text _portraitLabel;
        private Sprite _whiteSprite;
        private RectTransform _choicePanel;
        private RectTransform _rpPanel;
        private InputField _rpInput;

        // RP 상태
        private string _rpActor;            // PLAYER1 / PLAYER2 / BOTH
        private string _rpPrompt;
        private readonly List<string> _rpLog = new List<string>();
        private RectTransform _rpExprRow;

        // 상시 채팅(RP 와 무관하게 항상 가능)
        private ClueNotebook _notebook;
        private GameObject _chatPanel;
        private RectTransform _chatPanelRect;
        private InputField _chatInput;
        private Text _chatText;
        private readonly List<string> _chatLog = new List<string>();
        private int _suppressAdvanceUntil;   // 채팅 전송 프레임에 진행키가 새지 않도록

        // P1 전용 사전 브리핑 모달
        private GameObject _briefing;

        private const string P1Briefing =
            "[당신만 아는 과거 — {P1}]\n\n" +
            "· 5년 전 공방 화재의 ‘최초 신고자’는 당신이다.\n" +
            "· 하지만 불을 보고도 곧장 신고하지 못했다. 신고까지 약 10분의 공백이 있다.\n" +
            "· 그 사이, 공방 안에서 누가 문을 두드리는 소리를 들었고 — 보안실에서 나오는 누군가의 그림자도 봤다. 얼굴은 보지 못했다(혹은 보고도 부정하고 싶었다).\n" +
            "· 무서워서 몇 분간 아무것도 못 했다. 결국 신고했지만 이미 늦었다.\n" +
            "· 이후 당신은 ‘불을 보자마자 신고했다’고 거짓말했다.\n" +
            "· 시신 곁에서 나온 장신구는 당신 것이지만, 식사 전 이미 잃어버렸다고 해 둔다.\n" +
            "· 오늘 밤, 윤세현은 ‘신고 전 본 것을 말하라’며 당신을 협박했다.\n" +
            "· 심태오가 사라지기 전, 당신은 5년 전 기억을 확인하러 보안실 근처에 갔었다.\n\n" +
            "당신의 죄는 살인이 아니라 — 망설임과 거짓말이다.\n" +
            "이 비밀을 안고 RP 하세요. (상대 {P2}는 이 내용을 모릅니다.)";

        public static DialogueView Init(DialogueRunner runner)
        {
            var go = new GameObject("DialogueView");
            var view = go.AddComponent<DialogueView>();
            view._runner = runner;
            view._data = runner.Data;
            view.BuildUI();
            view.Subscribe();
            view._notebook = ClueNotebook.Init(runner);   // 단서 노트(우상단 버튼 / Tab)
            return view;
        }

        public void SetActions(IDialogueActions actions) => _actions = actions;

        private void Subscribe()
        {
            _runner.OnSceneChanged += OnSceneChanged;
            _runner.OnBeat += OnBeat;
            _runner.OnClueGained += OnClueGained;
            _runner.OnMinigame += OnMinigame;
            _runner.OnEnding += OnEnding;
            _runner.OnFinished += OnFinished;
        }

        private void OnDestroy()
        {
            // 노트는 별도 GameObject 라 뷰를 지울 때 같이 정리(재시작 시 중복 생성 방지).
            if (_notebook != null) { Destroy(_notebook.gameObject); _notebook = null; }
            if (_runner == null) return;
            _runner.OnSceneChanged -= OnSceneChanged;
            _runner.OnBeat -= OnBeat;
            _runner.OnClueGained -= OnClueGained;
            _runner.OnMinigame -= OnMinigame;
            _runner.OnEnding -= OnEnding;
            _runner.OnFinished -= OnFinished;
        }

        // ───────────────────────── 표시: 런너 이벤트 ─────────────────────────

        private void OnSceneChanged(SceneData scene)
        {
            Sprite bg = string.IsNullOrEmpty(scene.background) ? null : Resources.Load<Sprite>(scene.background);
            if (bg != null) { _background.sprite = bg; _background.color = Color.white; }
            else
            {
                _background.sprite = null;
                var c = SceneColor(string.IsNullOrEmpty(scene.background) ? scene.id : scene.background);
                _background.color = new Color(c.r, c.g, c.b, 1f); // 아트 없을 땐 장면별 분위기 색
            }
            ShowToast($"— {scene.title} —", 2.4f);
        }

        private void OnBeat(Beat beat)
        {
            // RP 중에는 채팅창이 입력칸을 가리므로 숨긴다(그 외 비트·조사 중엔 항상 보이게).
            if (_chatPanel != null) _chatPanel.SetActive(beat.type != BeatType.RpBeat);

            switch (beat.type)
            {
                case BeatType.GiveClue:
                    return; // OnClueGained 토스트로 처리, 자동 진행

                case BeatType.Choice:
                    SetBodyLine(Speakers.Narration, beat.text);
                    ShowChoices(beat.choices);
                    return;

                case BeatType.Deduction:
                    SetBodyLine(Speakers.Narration, beat.text);
                    ShowDeduction(beat.choices);
                    return;

                case BeatType.RpBeat:
                    ShowRp(beat);
                    return;

                case BeatType.Interrogation:
                    ShowInterrogation(beat);
                    return;

                case BeatType.Accusation:
                    SetBodyLine(Speakers.Narration, beat.text);
                    ShowAccusation(beat);
                    return;

                default: // Line / Narration
                    SetBodyLine(beat.speaker, beat.text);
                    _continueHint.text = "▶  클릭 / Space";
                    SetState(State.AwaitClick);
                    return;
            }
        }

        private void OnClueGained(ClueDef clue) =>
            ShowToast($"🔎 단서 획득 — {PlayerNames.Substitute(clue.name)}", 3f);

        private void OnMinigame(Beat beat)
        {
            string id = beat.minigameId;
            HideInteractables();
            if (_chatPanel != null) _chatPanel.SetActive(true);   // 조사·요리 중에도 채팅 가능
            SetState(State.Stub); // 미니게임 동안 클릭 진행 방지
            bool net = _actions != null && _actions.IsNetworkPlay;
            if (id == "cooking")
            {
                SetBodyLine(Speakers.Narration, "두 사람은 각자 다른 요리를 맡았다. 내 접시를 완성하고, 상대도 끝내길 기다리자.");
                var v = OC.Minigames.CookingMinigame.Launch(() => { _actions?.StubContinue(); AfterLocalProceed(); }, net, (int)LocalSlot);
                v.ClickSink = i => v.ApplyClick(i, (int)LocalSlot);   // 각자 자기 보드(중계 안 함)
                if (net) { v.DoneSink = s => _actions.CookDone(s); _actions.RegisterMinigame(v); }
            }
            else if (id == "evidence" || id == "search")
            {
                SetBodyLine(Speakers.Narration, "피 냄새와 먼지가 뒤섞인 방. 둘이서 각자 정해진 횟수만 살필 수 있다 — 어디를 뒤질지 의논해 고르자.");
                var v = OC.Minigames.EvidenceMinigame.Launch(
                    beat.searchSpots, beat.searchBudget, net, (int)LocalSlot,
                    cid => _runner.Data.FindClue(cid),
                    cid => _runner.GainClueExternal(cid),
                    () => { _actions?.StubContinue(); AfterLocalProceed(); });
                WireMinigame(v, i => v.ApplyClick(i, (int)LocalSlot), net);
            }
            else
            {
                SetBodyLine(Speakers.Narration, $"[미니게임: {id}] (임시 스텁)");
                ShowStub("계속 ▶", () => _actions?.StubContinue());
            }
        }

        // 미니게임 클릭 배선: 네트워크면 서버로 중계(양쪽 동기화), 로컬이면 직접 적용.
        private void WireMinigame(IMinigameView view, System.Action<int> applyLocal, bool net)
        {
            if (net)
            {
                if (view is OC.Minigames.CookingMinigame c) c.ClickSink = i => _actions.MinigameClick(i, (int)LocalSlot);
                else if (view is OC.Minigames.EvidenceMinigame e) e.ClickSink = i => _actions.MinigameClick(i, (int)LocalSlot);
                _actions.RegisterMinigame(view);
            }
            else
            {
                if (view is OC.Minigames.CookingMinigame c) c.ClickSink = applyLocal;
                else if (view is OC.Minigames.EvidenceMinigame e) e.ClickSink = applyLocal;
            }
        }

        private void OnEnding(EndingDef ending) => ShowToast($"❄ {ending.title}", 3.5f);

        private void OnFinished()
        {
            SetBodyLine(Speakers.Narration, "— 끝 —");
            _continueHint.text = "";
            ShowStub("다시 시작", () => _actions?.Restart());
            SetState(State.Finished);
        }

        // ───────────────────────── 입력 ─────────────────────────

        private void Update()
        {
            if (_state != State.AwaitClick) return;
            if (Time.frameCount <= _stateFrame) return;
            if (Time.frameCount <= _suppressAdvanceUntil) return;     // 채팅 전송 직후 프레임 보호
            if (IsTypingInInput()) return;                           // 채팅/입력 포커스 중엔 키보드 진행 무시
            if (_notebook != null && _notebook.IsOpen) return;       // 단서 노트 열려 있으면(읽는 중) 진행 안 함

            bool overUi = IsPointerOverChat() ||
                          (_notebook != null && _notebook.BlocksPointer(Input.mousePosition)); // 채팅/노트 위 클릭은 진행 아님
            bool key = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
            bool click = Input.GetMouseButtonDown(0) && !overUi;
            if (key || click)
            {
                SetState(State.Idle);
                _actions?.Advance();
                AfterLocalProceed();
            }
        }

        /// <summary>지금 텍스트 입력창(채팅/RP)에 포커스가 있는지 — 그러면 Space/Enter 로 진행하면 안 된다.</summary>
        private static bool IsTypingInInput()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var sel = es.currentSelectedGameObject;
            return sel != null && sel.GetComponent<InputField>() != null;
        }

        /// <summary>마우스 포인터가 채팅 패널 영역 위에 있는지 — 그 클릭은 진행이 아니라 채팅 조작이다.</summary>
        private bool IsPointerOverChat()
        {
            if (_chatPanelRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_chatPanelRect, Input.mousePosition, null);
        }

        // ───────────────────────── 진행 동의(양쪽 다 눌러야 다음으로) ─────────────────────────

        /// <summary>로컬이 '다음으로'를 보냈을 때 — 네트워크면 상대 동의를 기다리는 상태로 둔다.</summary>
        private void AfterLocalProceed()
        {
            if (_actions == null || !_actions.IsNetworkPlay) return;   // 싱글은 즉시 진행
            HideInteractables();
            _continueHint.text = "나 ✓ — 상대를 기다리는 중…";
            SetState(State.Idle);
        }

        /// <summary>서버가 양쪽 동의 상태를 알려줌(둘 다면 곧 RpcProceed 로 진행).</summary>
        public void ShowProceedWaiting(bool p1Ready, bool p2Ready)
        {
            bool meReady = LocalSlot == PlayerSlot.Player1 ? p1Ready : p2Ready;
            bool otherReady = LocalSlot == PlayerSlot.Player1 ? p2Ready : p1Ready;
            if (meReady && !otherReady) _continueHint.text = "나 ✓ — 상대를 기다리는 중…";
            else if (!meReady && otherReady) _continueHint.text = "상대 ✓ — 이제 당신 차례";
        }

        public void ClearProceedWaiting() { _continueHint.text = ""; }

        // ───────────────────────── 상시 채팅 ─────────────────────────

        private void OnChatSend()
        {
            if (_chatInput == null) return;
            string text = _chatInput.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            _actions?.Chat(SlotToken(LocalSlot), text.Trim());   // 네트워크면 상대에게도 중계, 내 줄도 Rpc 로 돌아옴
            _chatInput.text = "";
            _suppressAdvanceUntil = Time.frameCount + 1;          // 전송 프레임에 Enter 가 진행으로 새지 않도록
            _chatInput.ActivateInputField();
        }

        /// <summary>채팅 한 줄 추가(로컬/상대 양쪽에서 호출). 항상 보이는 채팅창에 쌓인다.</summary>
        public void AppendChatLine(string speakerToken, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var (name, col) = ResolveSpeaker(speakerToken);
            _chatLog.Add($"<color={col}><b>{name}</b></color> : {PlayerNames.Substitute(text.Trim())}");
            if (_chatLog.Count > 40) _chatLog.RemoveAt(0);
            if (_chatText != null) _chatText.text = string.Join("\n", _chatLog);
        }

        // ───────────────────────── P1 사전 비밀 브리핑 ─────────────────────────

        /// <summary>게임 시작 시 P1 에게만 과거 비밀을 한 번 보여 준다(상대는 못 봄).</summary>
        public void MaybeShowP1Briefing()
        {
            if (LocalSlot != PlayerSlot.Player1 || _briefing != null) return;

            _briefing = new GameObject("P1Briefing", typeof(RectTransform));
            _briefing.transform.SetParent(transform, false);
            var canvas = _briefing.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
            var sc = _briefing.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);
            _briefing.AddComponent<GraphicRaycaster>();

            var dim = NewImage(_briefing.transform, "Dim"); Stretch(dim.rectTransform); dim.color = new Color(0f, 0f, 0f, 0.88f);
            var box = NewImage(_briefing.transform, "Box");
            Anchor(box.rectTransform, new Vector2(0.13f, 0.12f), new Vector2(0.87f, 0.9f));
            box.color = new Color(0.10f, 0.11f, 0.15f, 0.99f);

            var t = NewText(box.transform, "Txt", 26, FontStyle.Normal, TextAnchor.UpperLeft);
            Anchor(t.rectTransform, new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.95f));
            t.text = PlayerNames.Substitute(P1Briefing);

            var btn = CreateButton((RectTransform)box.transform, "확인했어요 — 시작", () => { Destroy(_briefing); _briefing = null; });
            Anchor((RectTransform)btn.transform, new Vector2(0.30f, 0.04f), new Vector2(0.70f, 0.135f));
        }

        // ───────────────────────── 외부(actions)에서 호출하는 표시 ─────────────────────────

        /// <summary>추리 퍼즐 판정 결과 표시. (로컬/네트워크 양쪽에서 호출)</summary>
        public void ShowDeductionResult(bool correct, string feedback)
        {
            feedback = PlayerNames.Substitute(feedback);
            if (correct)
            {
                HideInteractables();
                SetBodyRich($"<i><color=#A8E6B0>{(string.IsNullOrEmpty(feedback) ? "맞아. 앞뒤가 들어맞는다." : feedback)}</color></i>");
                _continueHint.text = "▶  클릭 / Space";
                SetState(State.AwaitClick);
            }
            else
            {
                ShowToast(string.IsNullOrEmpty(feedback) ? "…뭔가 들어맞지 않아. 다시 생각해 보자." : feedback, 3f);
            }
        }

        /// <summary>RP 대사 한 줄을 로그에 추가해 표시. (입력한 본인·상대 양쪽에서 호출)</summary>
        public void AppendRpLine(string speakerToken, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            UpdatePortrait(speakerToken);
            _rpLog.Add(FormatLine(speakerToken, text.Trim()));
            RenderRp();
        }

        // ───────────────────────── 표시: 일반 대사 ─────────────────────────

        private void SetBodyLine(string speaker, string text)
        {
            HideInteractables();
            UpdatePortrait(speaker);
            SetBodyRich(FormatLine(speaker, PlayerNames.Substitute(text)));
        }

        /// <summary>화자 포트레이트 표시. 플레이어=커스텀 사진, 조연=Resources/Portraits/{id} 또는
        /// 색+이름 플레이스홀더. 지문/내레이션은 숨김.</summary>
        private void UpdatePortrait(string speaker)
        {
            if (string.IsNullOrEmpty(speaker) || speaker == Speakers.Narration)
            {
                _portrait.enabled = false; _portraitLabel.enabled = false; return;
            }

            Sprite s = null; string label; string colorKey = speaker;
            if (speaker == Speakers.Player1) { s = CharacterStore.Current(PlayerSlot.Player1); label = PlayerNames.P1; colorKey = "P1"; }
            else if (speaker == Speakers.Player2) { s = CharacterStore.Current(PlayerSlot.Player2); label = PlayerNames.P2; colorKey = "P2"; }
            else
            {
                var def = _data.FindCharacter(speaker);
                label = def != null ? def.displayName : speaker;
                s = LoadNpcSprite(def, speaker);
            }

            _portrait.enabled = true;
            if (s != null)
            {
                _portrait.sprite = s; _portrait.color = Color.white; _portrait.preserveAspect = true;
                _portraitLabel.enabled = false;
            }
            else // 색 + 이름 플레이스홀더(아트 전)
            {
                _portrait.sprite = WhiteSprite(); _portrait.color = CharColor(colorKey); _portrait.preserveAspect = false;
                _portraitLabel.text = label; _portraitLabel.enabled = true;
            }
        }

        private Sprite LoadNpcSprite(CharacterDef def, string id)
        {
            if (def != null && !string.IsNullOrEmpty(def.portrait))
            {
                var sp = Resources.Load<Sprite>(def.portrait);
                if (sp != null) return sp;
            }
            return Resources.Load<Sprite>($"Portraits/{id}");
        }

        private Sprite WhiteSprite()
        {
            if (_whiteSprite == null)
            {
                var t = new Texture2D(8, 8);
                var px = new Color[64];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                t.SetPixels(px); t.Apply();
                _whiteSprite = Sprite.Create(t, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
            }
            return _whiteSprite;
        }

        private static Color CharColor(string key)
        {
            int h = Mathf.Abs(key.GetHashCode());
            return Color.HSVToRGB((h % 360) / 360f, 0.38f, 0.5f);
        }

        private static Color SceneColor(string key)
        {
            if (string.IsNullOrEmpty(key)) return new Color(0.10f, 0.11f, 0.14f);
            if (key.Contains("exterior")) return new Color(0.15f, 0.19f, 0.28f);   // 차가운 눈밤
            if (key.Contains("kitchen")) return new Color(0.22f, 0.16f, 0.12f);    // 따뜻한 벽난로
            if (key.Contains("study")) return new Color(0.20f, 0.11f, 0.11f);      // 피 묻은 서재
            if (key.Contains("hall")) return new Color(0.12f, 0.13f, 0.17f);       // 어두운 복도
            int h = Mathf.Abs(key.GetHashCode());
            return Color.HSVToRGB((h % 360) / 360f, 0.25f, 0.18f);
        }

        private string FormatLine(string speaker, string text)
        {
            if (string.IsNullOrEmpty(speaker) || speaker == Speakers.Narration)
                return $"<i><color={ColNarr}>{text}</color></i>";
            var (name, col) = ResolveSpeaker(speaker);
            return $"<b><color={col}>{name}</color></b>\n{text}";
        }

        private (string name, string color) ResolveSpeaker(string speaker)
        {
            if (speaker == Speakers.Player1) return (PlayerNames.P1, ColP1);
            if (speaker == Speakers.Player2) return (PlayerNames.P2, ColP2);
            if (speaker == Speakers.Both) return ($"{PlayerNames.P1} & {PlayerNames.P2}", ColNpc);
            var def = _data.FindCharacter(speaker);
            return (def != null ? def.displayName : speaker, ColNpc);
        }

        // ───────────────────────── 표시: 선택지 / 추리 / 지목 / 스텁 ─────────────────────────

        private void ShowChoices(List<ChoiceOption> choices)
        {
            HideInteractables();
            _continueHint.text = "";
            _choicePanel.gameObject.SetActive(true);
            for (int i = 0; i < choices.Count; i++)
            {
                int idx = i;
                CreateButton(_choicePanel, PlayerNames.Substitute(choices[i].text), () =>
                {
                    SetState(State.Idle);
                    _actions?.Choose(idx);
                });
            }
            SetState(State.Choices);
        }

        private void ShowDeduction(List<ChoiceOption> options)
        {
            HideInteractables();
            _continueHint.text = "";
            _choicePanel.gameObject.SetActive(true);
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                CreateButton(_choicePanel, PlayerNames.Substitute(options[i].text), () => _actions?.Deduce(idx));
            }
            SetState(State.Deduction);
        }

        // ───────────────────────── 표시: 심문 ─────────────────────────

        private void ShowInterrogation(Beat beat)
        {
            HideInteractables();
            UpdatePortrait(Speakers.Narration);
            SetBodyRich(FormatLine(Speakers.Narration, PlayerNames.Substitute(beat.text))
                        + "\n\n<color=#9AA0AA>용의자를 골라 증언을 들어보자. (단서 노트는 Tab — 증언과 대조해 모순을 찾아라)</color>");
            _continueHint.text = "";
            _choicePanel.gameObject.SetActive(true);
            foreach (var c in SuspectsFor(beat))
            {
                var cc = c;
                var bb = beat;
                CreateButton(_choicePanel, $"{c.displayName} 심문", () => OnQuestion(bb, cc));
            }
            CreateButton(_choicePanel, "심문을 마친다 ▶", () => { SetState(State.Idle); _actions?.StubContinue(); AfterLocalProceed(); });
            SetState(State.Stub);
        }

        private void OnQuestion(Beat beat, CharacterDef c)
        {
            UpdatePortrait(c.id);
            var (name, col) = ResolveSpeaker(c.id);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b><color={col}>{name}</color></b>");
            // 이 사건(beat) 전용 증언이 있으면 그것을, 없으면 캐릭터 기본 증언으로 폴백.
            var lines = TestimonyFor(beat, c);
            if (lines != null && lines.Count > 0)
                foreach (var line in lines)
                    sb.Append("\n“").Append(PlayerNames.Substitute(line)).Append("”");
            else
                sb.Append("\n…묵묵부답이다.");
            SetBodyRich(sb.ToString());   // 용의자 버튼은 그대로 둬서 계속 다른 사람도 심문 가능
        }

        /// <summary>이 심문 비트에서 해당 인물이 할 증언. beat.testimonies 에 사건별 증언이 있으면 우선,
        /// 없으면 캐릭터의 기본 testimony 로 폴백한다(사건마다 같은 인물이 다른 말을 하도록).</summary>
        private static System.Collections.Generic.List<string> TestimonyFor(Beat beat, CharacterDef c)
        {
            if (beat.testimonies != null &&
                beat.testimonies.TryGetValue(c.id, out var perEvent) &&
                perEvent != null && perEvent.Count > 0)
                return perEvent;
            return c.testimony;
        }

        private void ShowStub(string label, System.Action onClick)
        {
            HideInteractables();
            _choicePanel.gameObject.SetActive(true);
            CreateButton(_choicePanel, label, () => { SetState(State.Idle); onClick?.Invoke(); AfterLocalProceed(); });
            SetState(State.Stub);
        }

        private void ShowAccusation(Beat beat)
        {
            HideInteractables();
            _continueHint.text = "";
            _choicePanel.gameObject.SetActive(true);
            foreach (var c in SuspectsFor(beat))
            {
                string id = c.id;
                // 죽은(혹은 죽은 듯한) 사람도 지목 후보로 두되, 라벨에 사망 상태를 덧붙여
                // '죽은 사람도 고를 수 있는' 판이라는 걸 보이게 한다(특정 한 명만 튀지 않도록).
                bool dead = beat.presumedDead != null && beat.presumedDead.Contains(id);
                string label = dead
                    ? $"\"{c.displayName}\" 를 지목한다  <color=#8A8F98><size=20>— 사망한 것으로 추정됨</size></color>"
                    : $"\"{c.displayName}\" 를 지목한다";
                CreateButton(_choicePanel, label, () =>
                {
                    SetState(State.Idle);
                    _actions?.Accuse(id);
                });
            }
            SetState(State.Accuse);
        }

        /// <summary>심문/지목 대상 캐릭터. beat.suspects 가 있으면 그것을, 없으면 host 가 아닌 조연 전부.</summary>
        private IEnumerable<CharacterDef> SuspectsFor(Beat beat)
        {
            if (beat.suspects != null && beat.suspects.Count > 0)
            {
                foreach (var id in beat.suspects)
                {
                    // 플레이어 본인(P1)도 지목 후보가 될 수 있다 — cast 에 없으므로 즉석 정의로 만든다.
                    if (id == Speakers.Player1) { yield return PlayerSuspect(); continue; }
                    var c = _data.FindCharacter(id);
                    if (c != null) yield return c;
                }
                // 조건부(숨김) 용의자: 지정 단서를 모두 확보했을 때만 후보로 등장.
                // (예: '죽은 줄 안' 심태오 — 시신 없음 + 도면 단서를 찾아야 살아있음을 깨닫고 지목 가능)
                if (beat.lockedSuspects != null && HasAllClues(beat.unlockClueIds))
                {
                    foreach (var id in beat.lockedSuspects)
                    {
                        var c = _data.FindCharacter(id);
                        if (c != null) yield return c;
                    }
                }
            }
            else
            {
                foreach (var c in _data.cast)
                    if (c.role != "host" && c.role != "victim") yield return c;
            }
        }

        /// <summary>지목 화면 전용: 플레이어 본인(P1)을 가리키는 즉석 용의자 정의.</summary>
        private static CharacterDef PlayerSuspect() =>
            new CharacterDef { id = Speakers.Player1, displayName = OC.Core.PlayerNames.P1 };

        /// <summary>주어진 단서를 모두 보유했는지(조건부 용의자 공개 판정용).</summary>
        private bool HasAllClues(System.Collections.Generic.List<string> clueIds)
        {
            if (clueIds == null) return true;
            foreach (var id in clueIds)
                if (!_runner.HasClue(id)) return false;
            return true;
        }

        // ───────────────────────── 표시: RP ─────────────────────────

        private void ShowRp(Beat beat)
        {
            HideInteractables();
            _rpActor = string.IsNullOrEmpty(beat.rpActor) ? Speakers.Both : beat.rpActor;
            _rpPrompt = PlayerNames.Substitute(beat.rpPrompt);
            _rpLog.Clear();
            _continueHint.text = "";
            RenderRp();
            BuildRpPanel();
            SetState(State.Rp);
            if (_rpInput != null) _rpInput.ActivateInputField();
            UpdatePortrait(SlotToken(LocalSlot)); // 항상 내 자캐 얼굴
        }

        private string SlotToken(PlayerSlot s) =>
            s == PlayerSlot.Player1 ? Speakers.Player1 : Speakers.Player2;

        /// <summary>내 자캐의 '이름 있는 표정' 선택 버튼들(2개 이상 지정했을 때만).</summary>
        private void PopulateExprRow(PlayerSlot slot)
        {
            if (_rpExprRow == null) return;
            foreach (Transform t in _rpExprRow) Destroy(t.gameObject);

            int n = CharacterStore.Count(slot);
            if (n <= 1) { _rpExprRow.gameObject.SetActive(false); return; }
            _rpExprRow.gameObject.SetActive(true);

            var label = NewText(_rpExprRow, "L", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
            label.text = "표정";
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 56;

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                string name = CharacterStore.NameAt(slot, i) ?? (i + 1).ToString();
                CreateButton(_rpExprRow, name, () =>
                {
                    CharacterStore.SetExpression(slot, idx);
                    UpdatePortrait(SlotToken(slot));
                }, 1);
            }
        }

        private bool CanLocalInputRp()
        {
            if (_rpActor == Speakers.Both) return true;           // 둘 다 말하는 장면: 각자 자기 대사
            if (_rpActor == Speakers.Player1) return LocalSlot == PlayerSlot.Player1;
            if (_rpActor == Speakers.Player2) return LocalSlot == PlayerSlot.Player2;
            return true;
        }

        private void RenderRp()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<i><color=#9AA0AA>※ {_rpPrompt}</color></i>");
            foreach (var line in _rpLog) sb.Append("\n\n").Append(line);
            if (_rpLog.Count == 0)
                sb.Append("\n\n<color=#6A7078>(대사 입력 후 Enter 또는 [전송]. 다 했으면 [다음으로])</color>");
            SetBodyRich(sb.ToString(), scrollBottom: true); // 최신 대사가 보이도록 아래로
        }

        private void OnRpAdd()
        {
            if (_rpInput == null) return;
            string text = _rpInput.text;
            if (string.IsNullOrWhiteSpace(text)) return;

            _actions?.RpAdd(SlotToken(LocalSlot), text.Trim()); // 항상 내 슬롯으로 말함
            _rpInput.text = "";
            _rpInput.ActivateInputField();
        }

        private void OnRpDone()
        {
            SetState(State.Idle);
            _actions?.RpDone();
            AfterLocalProceed();
        }

        private void BuildRpPanel()
        {
            foreach (Transform t in _rpPanel) Destroy(t.gameObject);
            _rpPanel.gameObject.SetActive(true);
            _rpInput = null;
            _rpExprRow = null;

            if (CanLocalInputRp())
            {
                // 표정 바(이름 있는 표정) — 얇은 한 줄
                _rpExprRow = NewRow(_rpPanel, 44);
                PopulateExprRow(LocalSlot);

                // 한 줄 입력 + 버튼 (대화창 위 얇은 바, 포트레이트 안 가림)
                var inputRow = NewRow(_rpPanel, 48);
                _rpInput = CreateInputField((RectTransform)inputRow.transform, 4);
                CreateButton((RectTransform)inputRow.transform, "전송", OnRpAdd, 1);
                CreateButton((RectTransform)inputRow.transform, "다음으로 ▶", OnRpDone, 1);
            }
            else
            {
                var noteRow = NewRow(_rpPanel, 48);
                string who = _rpActor == Speakers.Player1 ? PlayerNames.P1 : PlayerNames.P2;
                var note = NewText(noteRow, "Note", 22, FontStyle.Italic, TextAnchor.MiddleCenter);
                note.text = $"{who} 가 입력 중…";
                note.color = new Color(0.8f, 0.84f, 0.9f);
                CreateButton((RectTransform)noteRow.transform, "다음으로 ▶", OnRpDone, 1);
            }
        }

        // ───────────────────────── 공통 ─────────────────────────

        private void HideInteractables()
        {
            foreach (Transform t in _choicePanel) Destroy(t.gameObject);
            _choicePanel.gameObject.SetActive(false);
            if (_rpPanel != null)
            {
                foreach (Transform t in _rpPanel) Destroy(t.gameObject);
                _rpPanel.gameObject.SetActive(false);
            }
        }

        private void SetState(State s) { _state = s; _stateFrame = Time.frameCount; }

        private void SetBodyRich(string rich, bool scrollBottom = false)
        {
            _bodyText.text = rich;
            StartCoroutine(ScrollRoutine(scrollBottom));
        }

        private IEnumerator ScrollRoutine(bool bottom)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_bodyScroll != null) _bodyScroll.verticalNormalizedPosition = bottom ? 0f : 1f;
        }

        private void ShowToast(string msg, float seconds)
        {
            StopCoroutine(nameof(ToastRoutine));
            StartCoroutine(ToastRoutine(msg, seconds));
        }

        private IEnumerator ToastRoutine(string msg, float seconds)
        {
            _toast.text = msg; _toast.enabled = true;
            yield return new WaitForSeconds(seconds);
            _toast.enabled = false;
        }

        // ───────────────────────── UI 생성 ─────────────────────────

        private void BuildUI()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 28);

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _background = NewImage(canvasGo.transform, "Background");
            Stretch(_background.rectTransform);
            _background.color = new Color(0.10f, 0.11f, 0.14f, 1f);
            _background.raycastTarget = false;

            // 화자 포트레이트(자캐 사진) — 대사창 위 중앙
            _portrait = NewImage(canvasGo.transform, "Portrait");
            Anchor(_portrait.rectTransform, new Vector2(0.34f, 0.47f), new Vector2(0.66f, 0.97f));
            _portrait.preserveAspect = true;
            _portrait.raycastTarget = false;
            _portrait.enabled = false;
            _portraitLabel = NewText(_portrait.transform, "PortraitName", 40, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(_portraitLabel.rectTransform, 8);
            _portraitLabel.raycastTarget = false;
            _portraitLabel.enabled = false;

            var panel = NewImage(canvasGo.transform, "DialoguePanel");
            panel.color = new Color(0f, 0f, 0f, 0.74f);
            Anchor(panel.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.36f));

            BuildScrollBody(panel.transform);

            _continueHint = NewText(canvasGo.transform, "ContinueHint", 22, FontStyle.Normal, TextAnchor.LowerRight);
            Anchor(_continueHint.rectTransform, new Vector2(0.6f, 0.045f), new Vector2(0.94f, 0.075f));
            _continueHint.color = new Color(1f, 1f, 1f, 0.6f);

            _toast = NewText(canvasGo.transform, "Toast", 32, FontStyle.Bold, TextAnchor.MiddleCenter);
            Anchor(_toast.rectTransform, new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.96f));
            _toast.color = new Color(0.85f, 0.9f, 1f);
            _toast.enabled = false;

            _choicePanel = NewRect("ChoicePanel", canvasGo.transform);
            Anchor(_choicePanel, new Vector2(0.22f, 0.40f), new Vector2(0.78f, 0.84f));
            var vlg = _choicePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12; vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            _choicePanel.gameObject.SetActive(false);

            _rpPanel = NewRect("RpPanel", canvasGo.transform);
            Anchor(_rpPanel, new Vector2(0.05f, 0.355f), new Vector2(0.95f, 0.46f));
            var rvlg = _rpPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rvlg.spacing = 6; rvlg.childControlHeight = true; rvlg.childControlWidth = true;
            rvlg.childForceExpandWidth = true; rvlg.childForceExpandHeight = false;
            _rpPanel.gameObject.SetActive(false);

            BuildChatPanel(canvasGo.transform);
        }

        /// <summary>항상 떠 있는 채팅창(왼쪽 세로 패널) — RP 와 무관하게 언제든 대화.</summary>
        private void BuildChatPanel(Transform canvas)
        {
            var panel = NewImage(canvas, "ChatPanel");
            Anchor(panel.rectTransform, new Vector2(0.005f, 0.40f), new Vector2(0.205f, 0.965f));
            panel.color = new Color(0f, 0f, 0f, 0.78f);
            _chatPanel = panel.gameObject;
            _chatPanelRect = panel.rectTransform;

            // 채팅을 미니게임(조사·요리, sortingOrder 50)보다 위로 — 딤 아래로 묻히지 않게.
            var chatCanvas = panel.gameObject.AddComponent<Canvas>();
            chatCanvas.overrideSorting = true;
            chatCanvas.sortingOrder = 60;
            panel.gameObject.AddComponent<GraphicRaycaster>();

            var title = NewText(panel.transform, "ChatTitle", 18, FontStyle.Bold, TextAnchor.UpperLeft);
            Anchor(title.rectTransform, new Vector2(0.06f, 0.93f), new Vector2(0.97f, 0.99f));
            title.text = "💬 대화 (상시)";
            title.color = new Color(0.8f, 0.85f, 0.95f);

            _chatText = NewText(panel.transform, "ChatLog", 18, FontStyle.Normal, TextAnchor.LowerLeft);
            Anchor(_chatText.rectTransform, new Vector2(0.06f, 0.10f), new Vector2(0.97f, 0.92f));
            _chatText.color = new Color(0.92f, 0.94f, 0.98f);

            var inputBg = NewImage(panel.transform, "ChatInput");
            Anchor(inputBg.rectTransform, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.09f));
            inputBg.color = new Color(1f, 1f, 1f, 0.95f);
            _chatInput = inputBg.gameObject.AddComponent<InputField>();
            var txt = NewText(inputBg.transform, "Text", 18, FontStyle.Normal, TextAnchor.MiddleLeft);
            txt.color = Color.black; txt.supportRichText = false; Stretch(txt.rectTransform, 6);
            var ph = NewText(inputBg.transform, "Placeholder", 18, FontStyle.Italic, TextAnchor.MiddleLeft);
            ph.color = new Color(0f, 0f, 0f, 0.4f); ph.text = "메시지 입력 후 Enter"; Stretch(ph.rectTransform, 6);
            _chatInput.textComponent = txt; _chatInput.placeholder = ph; _chatInput.lineType = InputField.LineType.SingleLine;
            _chatInput.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnChatSend();
            });
        }

        private void BuildScrollBody(Transform panel)
        {
            var scrollGo = NewRect("BodyScroll", panel);
            Anchor(scrollGo, new Vector2(0.03f, 0.06f), new Vector2(0.97f, 0.94f));
            _bodyScroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            _bodyScroll.horizontal = false; _bodyScroll.vertical = true;
            _bodyScroll.movementType = ScrollRect.MovementType.Clamped;
            _bodyScroll.scrollSensitivity = 26;

            var vp = NewImage(scrollGo, "Viewport");
            Stretch(vp.rectTransform);
            vp.color = new Color(0f, 0f, 0f, 0.001f);
            vp.gameObject.AddComponent<RectMask2D>();

            var content = NewRect("Content", vp.transform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _bodyText = content.gameObject.AddComponent<Text>();
            _bodyText.font = _font; _bodyText.fontSize = 28; _bodyText.alignment = TextAnchor.UpperLeft;
            _bodyText.color = Color.white; _bodyText.supportRichText = true;
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.verticalOverflow = VerticalWrapMode.Overflow;

            _bodyScroll.viewport = vp.rectTransform;
            _bodyScroll.content = content;
        }

        // ── 저수준 UI 팩토리 ──

        private RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private RectTransform NewRow(RectTransform parent, float height)
        {
            var row = NewRect("Row", parent);
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10; hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
            return row;
        }

        private Image NewImage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        private Text NewText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor;
            t.color = Color.white; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private InputField CreateInputField(RectTransform parent, float flexibleWidth)
        {
            var bg = NewImage(parent, "Input");
            bg.color = new Color(1f, 1f, 1f, 0.96f);
            var le = bg.gameObject.AddComponent<LayoutElement>();
            if (flexibleWidth > 0) le.flexibleWidth = flexibleWidth;

            var input = bg.gameObject.AddComponent<InputField>();
            var textComp = NewText(bg.transform, "Text", 24, FontStyle.Normal, TextAnchor.MiddleLeft);
            textComp.color = Color.black; textComp.supportRichText = false;
            Stretch(textComp.rectTransform, 8);
            var ph = NewText(bg.transform, "Placeholder", 24, FontStyle.Italic, TextAnchor.MiddleLeft);
            ph.color = new Color(0f, 0f, 0f, 0.4f);
            ph.text = "대사 입력 후 Enter";
            Stretch(ph.rectTransform, 8);

            input.textComponent = textComp;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnRpAdd();
            });
            return input;
        }

        private Button CreateButton(RectTransform parent, string label, System.Action onClick, float flexibleWidth = 0)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.18f, 0.24f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 56; le.preferredHeight = 56;
            if (flexibleWidth > 0) le.flexibleWidth = flexibleWidth;

            var t = NewText(go.transform, "Label", 26, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 8);
            t.text = label;
            btn.onClick.AddListener(() => onClick?.Invoke());
            return btn;
        }

        private static void Stretch(RectTransform rt, float padding = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
