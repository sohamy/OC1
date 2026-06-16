using System;
using System.Collections;
using System.Collections.Generic;
using OC.Dialogue;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.Minigames
{
    /// <summary>
    /// 증거 조사 미니게임(정보형·동기화·인당 예산제). 한 현장에 조사 지점이 여럿(최대 11) 깔려 있고,
    /// 두 플레이어가 '각자' 정해진 횟수(인당 예산, 보통 3)만 조사할 수 있다 — 합쳐도 일부만 본다.
    /// 펼친 단서는 양쪽 보드에 동일하게 드러나 '공유'된다(논의해서 무엇을 볼지 고른다).
    /// 클릭은 ClickSink 로 내보내고(네트워크면 서버가 (index,slot) 으로 중계) 적용은 ApplyClick.
    /// index == -1 은 'slot 플레이어가 조사를 마침' 신호. 두 사람이 모두 끝내면(또는 예산 소진) 종료.
    /// 배치는 결정적(셔플 없음)이라 두 클라가 같은 인덱스→같은 지점을 본다.
    /// </summary>
    public class EvidenceMinigame : MonoBehaviour, IMinigameView
    {
        // 최대 11개 조사 지점 좌표(정규화). 왼쪽(x≤0.205)은 상시 채팅창이 있으므로 비워 둔다.
        private static readonly Vector2[] Spots =
        {
            new Vector2(0.34f, 0.76f), new Vector2(0.51f, 0.76f), new Vector2(0.68f, 0.76f), new Vector2(0.85f, 0.76f),
            new Vector2(0.34f, 0.56f), new Vector2(0.51f, 0.56f), new Vector2(0.68f, 0.56f), new Vector2(0.85f, 0.56f),
            new Vector2(0.40f, 0.34f), new Vector2(0.62f, 0.34f), new Vector2(0.84f, 0.34f),
        };

        /// <summary>핫스팟 클릭 출구. 인자는 핫스팟 index(또는 -1='조사 마침'). 네트워크면 서버가 (index, 내 slot)으로 중계.</summary>
        public Action<int> ClickSink;

        private Font _font;
        private Action _onComplete;
        private List<SearchSpot> _spots;
        private Func<string, ClueDef> _lookup;
        private Action<string> _onClueFound;
        private bool _net;
        private int _localSlot;
        private bool _finished;

        private readonly int[] _picksLeft = { 0, 0 };
        private readonly bool[] _slotDone = { false, false };
        private readonly HashSet<int> _done = new HashSet<int>();
        private Button[] _btns;
        private Text[] _labels;
        private Text _statusText;
        private Button _doneBtn;

        public static EvidenceMinigame Launch(List<SearchSpot> spots, int perPlayerBudget, bool net, int localSlot,
                                              Func<string, ClueDef> lookup, Action<string> onClueFound, Action onComplete)
        {
            var go = new GameObject("EvidenceMinigame");
            var m = go.AddComponent<EvidenceMinigame>();
            m._spots = spots ?? new List<SearchSpot>();
            int budget = perPlayerBudget > 0 ? perPlayerBudget : m._spots.Count;
            m._picksLeft[0] = m._picksLeft[1] = budget;
            m._net = net;
            m._localSlot = Mathf.Clamp(localSlot, 0, 1);
            m._lookup = lookup;
            m._onClueFound = onClueFound;
            m._onComplete = onComplete;
            m.Build();
            return m;
        }

        private void Build()
        {
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 26);
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var dim = NewImage(canvasGo.transform, "Dim"); Stretch(dim.rectTransform);
            dim.color = new Color(0f, 0f, 0f, 0.4f); dim.raycastTarget = false;

            var topBar = NewImage(canvasGo.transform, "TopBar");
            Anchor(topBar.rectTransform, new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f));
            topBar.color = new Color(0.08f, 0.10f, 0.14f, 0.92f); topBar.raycastTarget = false;
            var title = NewText(topBar.transform, "Title", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(title.rectTransform, 6);
            title.text = "현장을 조사하라 — 각자 정해진 횟수만 볼 수 있다. 무엇을 펼칠지 의논해 고르자. (펼친 단서는 둘이 공유)";

            _statusText = NewText(canvasGo.transform, "Status", 24, FontStyle.Italic, TextAnchor.MiddleCenter);
            Anchor(_statusText.rectTransform, new Vector2(0.08f, 0.045f), new Vector2(0.7f, 0.105f));
            _statusText.color = new Color(1f, 0.9f, 0.6f); _statusText.raycastTarget = false;

            // 조사 마치기 버튼(내 몫을 다 안 써도 먼저 끝낼 수 있음 — 둘 다 끝내야 종료).
            _doneBtn = CreateButtonAt(canvasGo.transform, "조사 마치기 ▶", new Vector2(0.72f, 0.045f), new Vector2(0.92f, 0.105f),
                                      () => ClickSink?.Invoke(-1));

            int n = _spots.Count;
            _btns = new Button[n];
            _labels = new Text[n];
            for (int i = 0; i < n && i < Spots.Length; i++)
            {
                string label = _spots[i] != null ? _spots[i].label : $"지점 {i + 1}";
                CreateHotspot(canvasGo.transform, label, Spots[i], i, out _btns[i], out _labels[i]);
            }
            UpdateStatus("어디를 조사할지 고르세요.");
        }

        public void NetSignal(int code) { }   // 조사 미니게임은 서버 신호를 쓰지 않음

        // 핫스팟 클릭 → 출구로(네트워크면 서버가 (index, 내 slot)으로 양쪽에 중계). 실제 적용은 ApplyClick.
        private void OnHotspot(int index) => ClickSink?.Invoke(index);

        public void ApplyClick(int index, int slot)
        {
            if (_finished) return;
            int s = slot < 0 ? _localSlot : Mathf.Clamp(slot, 0, 1);

            // -1 = 해당 플레이어가 '조사 마침'
            if (index == -1)
            {
                _slotDone[s] = true;
                if (s == _localSlot && _doneBtn != null) { _doneBtn.interactable = false; }
                CheckFinish();
                if (!_finished) UpdateStatus("상대가 조사를 마치길 기다리는 중…");
                return;
            }

            if (_done.Contains(index)) return;
            if (index < 0 || index >= _spots.Count || _spots[index] == null) return;
            if (_picksLeft[s] <= 0) return;          // 그 플레이어의 몫 소진
            if (_slotDone[s]) return;                // 이미 끝낸 플레이어

            _done.Add(index);
            _picksLeft[s]--;

            string clueId = _spots[index].clueId;
            var def = !string.IsNullOrEmpty(clueId) ? _lookup?.Invoke(clueId) : null;
            string nm = def != null ? def.name : clueId;

            if (_btns[index] != null) { _btns[index].interactable = false; _btns[index].targetGraphic.color = new Color(0.18f, 0.4f, 0.24f, 0.92f); }
            if (!string.IsNullOrEmpty(clueId))
            {
                if (_labels[index] != null) _labels[index].text = $"✔ {nm}";
                _onClueFound?.Invoke(clueId);
                UpdateStatus($"단서 확보 — {nm}");
            }
            else
            {
                if (_labels[index] != null) _labels[index].text += "\n(별것 없음)";
                UpdateStatus("…여긴 별다른 게 없다.");
            }

            CheckFinish();
        }

        private void CheckFinish()
        {
            bool s0 = _slotDone[0] || _picksLeft[0] <= 0;
            bool s1 = _slotDone[1] || _picksLeft[1] <= 0;
            bool allDone = _done.Count >= _spots.Count;
            // 네트워크: 둘 다 끝내야 종료. 로컬(1인): 한쪽만 끝나도 종료.
            if (allDone || (_net ? (s0 && s1) : (s0 || s1))) Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_doneBtn != null) _doneBtn.interactable = false;
            for (int i = 0; i < _btns.Length; i++)
                if (_btns[i] != null && !_done.Contains(i))
                {
                    _btns[i].interactable = false;
                    _btns[i].targetGraphic.color = new Color(0.12f, 0.13f, 0.16f, 0.6f);
                    if (_labels[i] != null) _labels[i].text += "  (못 살펴봄)";
                }
            UpdateStatus("조사를 마쳤다. 살펴보지 못한 자리도 있다 — 가진 것으로 추리하자.");
            StartCoroutine(FinishSoon());
        }

        private IEnumerator FinishSoon() { yield return new WaitForSeconds(1.6f); var cb = _onComplete; Destroy(gameObject); cb?.Invoke(); }

        private void UpdateStatus(string msg)
        {
            int mine = _picksLeft[Mathf.Clamp(_localSlot, 0, 1)];
            int other = _picksLeft[_localSlot == 0 ? 1 : 0];
            _statusText.text = $"내 남은 조사 {mine}   ·   상대 {other}   ·   {msg}";
        }

        // ── UI ──
        private Image NewImage(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.AddComponent<Image>(); }

        private Text NewText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>(); t.font = _font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor;
            t.color = Color.white; t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private void CreateHotspot(Transform parent, string label, Vector2 center, int index, out Button btn, out Text labelText)
        {
            var go = new GameObject("Hotspot", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = center; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(230, 86); rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>(); img.color = new Color(0.16f, 0.18f, 0.26f, 0.9f);
            btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            labelText = NewText(go.transform, "L", 20, FontStyle.Normal, TextAnchor.MiddleCenter); Stretch(labelText.rectTransform, 6); labelText.text = label;
            int idx = index;
            btn.onClick.AddListener(() => OnHotspot(idx));
        }

        private Button CreateButtonAt(Transform parent, string label, Vector2 min, Vector2 max, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform)); go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, min, max);
            var img = go.AddComponent<Image>(); img.color = new Color(0.2f, 0.22f, 0.3f, 0.95f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var t = NewText(go.transform, "L", 22, FontStyle.Bold, TextAnchor.MiddleCenter); Stretch(t.rectTransform, 4); t.text = label;
            btn.onClick.AddListener(() => onClick?.Invoke());
            return btn;
        }

        private static void Stretch(RectTransform rt, float pad = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad); }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
