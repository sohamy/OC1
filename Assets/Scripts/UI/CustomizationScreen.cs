using System;
using OC.Characters;
using OC.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.UI
{
    /// <summary>
    /// 시작 전 커플 커스텀(이름 + 표정별 사진). 절차적 UI.
    /// 표정 프리셋(평온·긴장·분노…) 칸에 각각 사진을 지정한다. 첫 지정 사진이 크게 미리보기로 뜬다.
    /// - 싱글(bothSlots=true): P1·P2 둘 다. 네트워크(false): 내 슬롯만.
    /// </summary>
    public class CustomizationScreen : MonoBehaviour
    {
        private Font _font;
        private Action _onConfirm;
        private bool _bothSlots;
        private PlayerSlot _localSlot;

        private InputField _name1, _name2;

        public static CustomizationScreen Show(bool bothSlots, PlayerSlot localSlot, Action onConfirm)
        {
            var go = new GameObject("CustomizationScreen");
            var s = go.AddComponent<CustomizationScreen>();
            s._bothSlots = bothSlots; s._localSlot = localSlot; s._onConfirm = onConfirm;
            s.Build();
            return s;
        }

        private void Build()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 26);

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var bg = NewImage(canvasGo.transform, "BG"); Stretch(bg.rectTransform);
            bg.color = new Color(0.08f, 0.09f, 0.12f, 1f);

            var title = NewText(canvasGo.transform, "Title", 42, FontStyle.Bold, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.1f, 0.9f), new Vector2(0.9f, 0.98f));
            title.text = "자캐 커플 커스텀";

            if (_bothSlots || _localSlot == PlayerSlot.Player1)
                BuildColumn(canvasGo.transform, PlayerSlot.Player1, new Vector2(0.06f, 0.16f), new Vector2(0.49f, 0.88f), out _name1);
            if (_bothSlots || _localSlot == PlayerSlot.Player2)
                BuildColumn(canvasGo.transform, PlayerSlot.Player2, new Vector2(0.51f, 0.16f), new Vector2(0.94f, 0.88f), out _name2);

            var start = CreateButton(canvasGo.transform, "시작하기 ▶", OnConfirm, out _);
            Anchor((RectTransform)start.transform, new Vector2(0.37f, 0.04f), new Vector2(0.63f, 0.12f));
        }

        private void BuildColumn(Transform parent, PlayerSlot slot, Vector2 min, Vector2 max, out InputField nameField)
        {
            var panel = NewImage(parent, $"Col_{slot}");
            panel.color = new Color(1f, 1f, 1f, 0.05f);
            Anchor(panel.rectTransform, min, max);

            string col = slot == PlayerSlot.Player1 ? "#8CC8FF" : "#FFB0C8";
            var label = NewText(panel.transform, "Label", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Anchor(label.rectTransform, new Vector2(0.05f, 0.92f), new Vector2(0.95f, 0.99f));
            label.text = $"<color={col}>{(slot == PlayerSlot.Player1 ? "플레이어 1" : "플레이어 2")}</color>";

            // 이름
            var nameBg = NewImage(panel.transform, "NameBg");
            nameBg.color = new Color(1f, 1f, 1f, 0.95f);
            Anchor(nameBg.rectTransform, new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.91f));
            nameField = nameBg.gameObject.AddComponent<InputField>();
            var nt = NewText(nameBg.transform, "T", 24, FontStyle.Normal, TextAnchor.MiddleLeft);
            nt.color = Color.black; nt.supportRichText = false; Stretch(nt.rectTransform, 10);
            var ph = NewText(nameBg.transform, "P", 24, FontStyle.Italic, TextAnchor.MiddleLeft);
            ph.color = new Color(0, 0, 0, 0.4f); ph.text = "자캐 이름"; Stretch(ph.rectTransform, 10);
            nameField.textComponent = nt; nameField.placeholder = ph; nameField.lineType = InputField.LineType.SingleLine;
            nameField.text = slot == PlayerSlot.Player1 ? "아렌린" : "지하윤";

            // 미리보기(기본=가장 크게)
            var preview = NewImage(panel.transform, "Preview");
            preview.color = new Color(0.2f, 0.2f, 0.24f, 1f);
            preview.preserveAspect = true;
            Anchor(preview.rectTransform, new Vector2(0.18f, 0.42f), new Vector2(0.82f, 0.82f));

            var hint = NewText(panel.transform, "Hint", 20, FontStyle.Italic, TextAnchor.MiddleCenter);
            Anchor(hint.rectTransform, new Vector2(0.05f, 0.37f), new Vector2(0.95f, 0.41f));
            hint.text = "표정 칸을 눌러 사진을 지정하세요";
            hint.color = new Color(1, 1, 1, 0.6f);

            // 표정 칸 (이름 있는 버튼 그리드)
            var grid = NewRect("ExprGrid", panel.transform);
            Anchor(grid, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.36f));
            var glg = grid.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(150, 56); glg.spacing = new Vector2(10, 10);
            glg.childAlignment = TextAnchor.MiddleCenter;
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; glg.constraintCount = 4;

            foreach (var expr in CharacterStore.Expressions)
            {
                string e = expr;
                Button btn = CreateButton(grid, expr, null, out Text btnLabel);
                btn.onClick.AddListener(() => OnAssign(slot, e, btnLabel, preview));
            }
        }

        private void OnAssign(PlayerSlot slot, string expression, Text btnLabel, Image preview)
        {
            var paths = FileDialog.PickImages(false);
            if (paths == null || paths.Length == 0) return;
            var tex = ImageUtil.LoadResized(paths[0]);
            if (tex == null) return;
            var sprite = ImageUtil.ToSprite(tex);
            CharacterStore.SetPortrait(slot, expression, sprite);
            btnLabel.text = $"{expression} ✔";
            preview.sprite = sprite; preview.color = Color.white;
        }

        private void OnConfirm()
        {
            if (_name1 != null) CharacterStore.SetName(PlayerSlot.Player1, Trim(_name1.text, "아렌린"));
            if (_name2 != null) CharacterStore.SetName(PlayerSlot.Player2, Trim(_name2.text, "지하윤"));
            var cb = _onConfirm;
            Destroy(gameObject);
            cb?.Invoke();
        }

        private static string Trim(string s, string fb) => string.IsNullOrWhiteSpace(s) ? fb : s.Trim();

        // ── UI 팩토리 ──
        private RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
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

        private Button CreateButton(Transform parent, string label, Action onClick, out Text labelText)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.22f, 0.3f, 0.98f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            labelText = NewText(go.transform, "L", 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(labelText.rectTransform, 4); labelText.text = label;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
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
