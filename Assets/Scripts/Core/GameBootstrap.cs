using System;
using OC.Characters;
using OC.Dialogue;
using OC.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.Core
{
    /// <summary>
    /// 싱글플레이 진입점. 빈 씬의 빈 GameObject 에 이 컴포넌트만 붙이고 Play.
    /// P1/P2 선택 → 커스텀 화면(이름·사진) → 시작하기 → 시나리오 진행. (네트워크는 GameSession 이 대체)
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("커스텀 화면을 건너뛰고 바로 시작(빠른 테스트)")]
        [SerializeField] private bool skipCustomization = false;

        [Header("커스텀/선택을 건너뛸 때 쓸 기본값")]
        [SerializeField] private PlayerSlot localSlot = PlayerSlot.Player1;
        [SerializeField] private string player1Name = "아렌린";
        [SerializeField] private string player2Name = "지하윤";

        private DialogueRunner _runner;
        private ScenarioData _data;

        private void Start()
        {
            _data = ScenarioLoader.LoadFromResources();
            if (_data == null)
            {
                Debug.LogError("[GameBootstrap] 시나리오 로드 실패. Assets/Resources/Data/scenario.json 확인.");
                return;
            }

            PlayerNames.Reset();
            CharacterStore.ResetAll();

            if (skipCustomization)
            {
                PlayerNames.Set(PlayerSlot.Player1, player1Name);
                PlayerNames.Set(PlayerSlot.Player2, player2Name);
                StartGame(localSlot);
                return;
            }

            // 먼저 P1/P2 중 어느 쪽으로 플레이할지 고른다.
            ShowSlotPicker(slot =>
                CustomizationScreen.Show(bothSlots: true, localSlot: slot, onConfirm: () => StartGame(slot)));
        }

        private void StartGame(PlayerSlot slot)
        {
            _runner = new DialogueRunner(_data);
            var view = DialogueView.Init(_runner);
            view.LocalSlot = slot;
            view.SetActions(new LocalDialogueActions(_runner, view));
            _runner.Begin();
            view.MaybeShowP1Briefing();   // P1 로 플레이하면 과거 비밀 사전 브리핑
        }

        // ── P1/P2 선택 화면(간단 오버레이) ──
        private void ShowSlotPicker(Action<PlayerSlot> onPick)
        {
            var font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 28);

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
            }

            var go = new GameObject("SlotPicker", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 70;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();

            var dim = NewImage(go.transform, "Dim", font); StretchRT((RectTransform)dim.transform);
            dim.color = new Color(0.08f, 0.09f, 0.12f, 1f);

            var title = NewText(go.transform, "Title", 40, font, TextAnchor.MiddleCenter);
            AnchorRT(title.rectTransform, new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.78f));
            title.text = "누구로 플레이할까요?";

            MakeButton(go.transform, font, "플레이어 1 (P1)", new Vector2(0.30f, 0.44f), new Vector2(0.70f, 0.55f),
                () => { Destroy(go); onPick(PlayerSlot.Player1); });
            MakeButton(go.transform, font, "플레이어 2 (P2)", new Vector2(0.30f, 0.30f), new Vector2(0.70f, 0.41f),
                () => { Destroy(go); onPick(PlayerSlot.Player2); });

            var note = NewText(go.transform, "Note", 22, font, TextAnchor.MiddleCenter);
            AnchorRT(note.rectTransform, new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.27f));
            note.color = new Color(0.7f, 0.74f, 0.8f);
            note.text = "P1 으로 시작하면 시작 전에 ‘당신만 아는 과거’ 브리핑이 표시됩니다.";
        }

        private Image NewImage(Transform parent, string name, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        private Text NewText(Transform parent, string name, int size, Font font, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>(); t.font = font; t.fontSize = size; t.alignment = anchor;
            t.color = Color.white; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private void MakeButton(Transform parent, Font font, string label, Vector2 min, Vector2 max, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform)); go.transform.SetParent(parent, false);
            AnchorRT((RectTransform)go.transform, min, max);
            var img = go.AddComponent<Image>(); img.color = new Color(0.18f, 0.22f, 0.32f, 0.98f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var t = NewText(go.transform, "L", 30, font, TextAnchor.MiddleCenter); StretchRT(t.rectTransform, 8); t.text = label;
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private static void StretchRT(RectTransform rt, float pad = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad); }

        private static void AnchorRT(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
