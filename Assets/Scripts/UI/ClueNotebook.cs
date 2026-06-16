using OC.Core;
using OC.Dialogue;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.UI
{
    /// <summary>
    /// 단서 노트. 언제든 열어 지금까지 모은 단서를 확인한다(버튼 클릭 또는 Tab 토글).
    /// 수집한 단서는 내용 표시, 아직 못 찾은 단서는 잠김(???)으로 보여 추리의 길잡이가 된다.
    /// DialogueRunner 의 수집 단서 상태를 읽으므로 별도 동기화가 필요 없다(단서는 동기화된 GiveClue 로 적립됨).
    /// </summary>
    public class ClueNotebook : MonoBehaviour
    {
        private DialogueRunner _runner;
        private ScenarioData _data;
        private Font _font;
        private GameObject _panel;
        private RectTransform _listContent;
        private ScrollRect _scroll;
        private Text _toggleLabel;
        private RectTransform _toggleRect;
        private bool _open;

        /// <summary>노트가 열려 있는지(열려 있으면 그 위 입력은 진행이 아님).</summary>
        public bool IsOpen => _open;

        /// <summary>화면 좌표가 노트 토글 버튼/패널 위에 있는지 — 그 클릭은 진행이 아니라 노트 조작이다.</summary>
        public bool BlocksPointer(Vector2 screenPos)
        {
            if (_toggleRect != null && RectTransformUtility.RectangleContainsScreenPoint(_toggleRect, screenPos, null)) return true;
            if (_open && _panel != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_panel.transform, screenPos, null)) return true;
            return false;
        }

        public static ClueNotebook Init(DialogueRunner runner)
        {
            var go = new GameObject("ClueNotebook");
            var n = go.AddComponent<ClueNotebook>();
            n._runner = runner;
            n._data = runner.Data;
            n.Build();
            return n;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) Toggle();
            if (_toggleLabel != null)
                _toggleLabel.text = $"단서 노트 ({_runner.CollectedClues.Count})  [Tab]";
        }

        private void Toggle()
        {
            _open = !_open;
            if (_open) RebuildList();
            _panel.SetActive(_open);
        }

        private void RebuildList()
        {
            foreach (Transform t in _listContent) Destroy(t.gameObject);

            int total = _data.clues.Count;
            int got = _runner.CollectedClues.Count;
            AddEntry($"<b>단서 노트</b>   <color=#9AA0AA>수집 {got}/{total}</color>", "#FFFFFF", 30);

            foreach (var clue in _data.clues)
            {
                bool has = _runner.HasClue(clue.id);
                if (has)
                {
                    string name = PlayerNames.Substitute(clue.name);
                    string desc = PlayerNames.Substitute(clue.description);
                    string mark = clue.decisive ? " <color=#FF9A9A>(결정적)</color>" : "";
                    AddEntry($"<b><color=#FFD98C>· {name}</color></b>{mark}\n<color=#D0D4DA>{desc}</color>", null, 24);
                }
                else
                {
                    AddEntry("<b><color=#5A6068>· ??? </color></b><color=#5A6068>(아직 발견하지 못한 단서)</color>", null, 24);
                }
            }

            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private void AddEntry(string rich, string colorHex, int size)
        {
            var t = NewText(_listContent, "Entry", size, FontStyle.Normal, TextAnchor.UpperLeft);
            t.text = rich;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = size + 14;
        }

        // ── UI ──
        private void Build()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 26);

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
            canvas.sortingOrder = 100; // 항상 위
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // 토글 버튼(우상단)
            var btn = CreateButton(canvasGo.transform, "단서 노트  [Tab]", Toggle);
            Anchor((RectTransform)btn.transform, new Vector2(0.80f, 0.92f), new Vector2(0.99f, 0.99f));
            _toggleLabel = btn.GetComponentInChildren<Text>();
            _toggleRect = (RectTransform)btn.transform;

            // 패널(우측, 기본 숨김)
            _panel = new GameObject("Panel", typeof(RectTransform)).gameObject;
            _panel.transform.SetParent(canvasGo.transform, false);
            var pimg = _panel.AddComponent<Image>();
            pimg.color = new Color(0.06f, 0.07f, 0.10f, 0.97f);
            Anchor((RectTransform)_panel.transform, new Vector2(0.58f, 0.08f), new Vector2(0.99f, 0.9f));

            BuildScroll((RectTransform)_panel.transform);
            _panel.SetActive(false);
        }

        private void BuildScroll(RectTransform parent)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRT = (RectTransform)scrollGo.transform;
            Anchor(scrollRT, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.97f));
            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 28;

            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vp.transform.SetParent(scrollGo.transform, false);
            var vpRT = (RectTransform)vp.transform; Stretch(vpRT);
            vp.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(vp.transform, false);
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.offsetMin = Vector2.zero; _listContent.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 14; vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = content.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _scroll.viewport = vpRT;
            _scroll.content = _listContent;
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

        private Button CreateButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.2f, 0.28f, 0.96f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var t = NewText(go.transform, "L", 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 4); t.text = label;
            btn.onClick.AddListener(() => onClick?.Invoke());
            return btn;
        }

        private static void Stretch(RectTransform rt, float pad = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
