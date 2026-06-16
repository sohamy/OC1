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
    /// 협력 요리 미니게임(분담형). 두 사람이 '각자 다른 접시'를 맡는다 — P1=스테이크, P2=파스타.
    /// 각자 자기 보드에서 정해진 순서대로 재료를 클릭해 완성한다(클릭은 중계하지 않는다 — 보드가 서로 다름).
    /// 내 접시를 완성하면 DoneSink 로 알리고, '둘 다 완성'(NetSignal 1)이 와야 함께 다음으로 넘어간다.
    /// 싱글플레이(net=false)면 내 접시만 끝내면 바로 완료.
    /// </summary>
    public class CookingMinigame : MonoBehaviour, IMinigameView
    {
        // 슬롯별 접시. [0]=P1 스테이크, [1]=P2 파스타.
        private static readonly string[] SteakRecipe = { "굽기", "뒤집기", "소금", "버터", "담기" };
        private static readonly string[] SteakLayout = { "소금", "굽기", "버터", "뒤집기", "담기", "후추", "태우기", "치우기" };
        private static readonly string[] PastaRecipe = { "면 삶기", "물 빼기", "소스", "버무리기", "담기" };
        private static readonly string[] PastaLayout = { "소스", "면 삶기", "버무리기", "물 빼기", "담기", "치즈", "태우기", "치우기" };

        /// <summary>내 보드 클릭 출구(로컬 적용용). DialogueView 가 i => ApplyClick(i, localSlot) 로 연결.</summary>
        public Action<int> ClickSink;
        /// <summary>내 접시 완성 알림(네트워크 전용). 인자는 내 slot.</summary>
        public Action<int> DoneSink;

        // 왼쪽(x≤0.205)은 상시 채팅창 영역이므로 재료는 오른쪽에 배치.
        private static readonly Vector2[] Spots =
        {
            new Vector2(0.36f, 0.66f), new Vector2(0.53f, 0.66f), new Vector2(0.70f, 0.66f), new Vector2(0.86f, 0.66f),
            new Vector2(0.36f, 0.42f), new Vector2(0.53f, 0.42f), new Vector2(0.70f, 0.42f), new Vector2(0.86f, 0.42f),
        };

        private Font _font;
        private Action _onComplete;
        private bool _net;
        private int _localSlot;
        private string[] _recipe;
        private string[] _layout;
        private string _dishName;
        private int _step;
        private bool _myDishDone;
        private bool _finished;
        private Text _seqText, _statusText, _title;

        public static CookingMinigame Launch(Action onComplete, bool net, int localSlot)
        {
            var go = new GameObject("CookingMinigame");
            var m = go.AddComponent<CookingMinigame>();
            m._onComplete = onComplete;
            m._net = net;
            m._localSlot = Mathf.Clamp(localSlot, 0, 1);
            if (m._localSlot == 0) { m._recipe = SteakRecipe; m._layout = SteakLayout; m._dishName = "스테이크"; }
            else { m._recipe = PastaRecipe; m._layout = PastaLayout; m._dishName = "파스타"; }
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
            dim.color = new Color(0f, 0f, 0f, 0.35f); dim.raycastTarget = false;

            var topBar = NewImage(canvasGo.transform, "TopBar");
            Anchor(topBar.rectTransform, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.98f));
            topBar.color = new Color(0.08f, 0.10f, 0.14f, 0.92f); topBar.raycastTarget = false;

            _title = NewText(topBar.transform, "Title", 30, FontStyle.Bold, TextAnchor.UpperCenter);
            Anchor(_title.rectTransform, new Vector2(0.02f, 0.5f), new Vector2(0.98f, 0.98f));
            _title.text = $"내 담당: {_dishName} — 순서대로 재료를 클릭! (상대는 {(_localSlot == 0 ? "파스타" : "스테이크")})";
            _seqText = NewText(topBar.transform, "Seq", 26, FontStyle.Normal, TextAnchor.LowerCenter);
            Anchor(_seqText.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.5f));

            _statusText = NewText(canvasGo.transform, "Status", 26, FontStyle.Italic, TextAnchor.MiddleCenter);
            Anchor(_statusText.rectTransform, new Vector2(0.1f, 0.04f), new Vector2(0.9f, 0.1f));
            _statusText.color = new Color(1f, 0.9f, 0.6f); _statusText.raycastTarget = false;

            for (int i = 0; i < _layout.Length && i < Spots.Length; i++)
            {
                int idx = i;
                CreateHotspot(canvasGo.transform, _layout[i], Spots[i], idx);
            }
            Refresh();
        }

        private void OnHotspot(int index) => ClickSink?.Invoke(index);

        public void ApplyClick(int index, int slot)
        {
            // 각자 자기 보드만 조작(중계 안 함). 다른 슬롯의 클릭/완성 이후 입력은 무시.
            if (_myDishDone || _finished) return;
            if (index < 0 || index >= _layout.Length) return;

            string ingredient = _layout[index];
            if (ingredient == _recipe[_step])
            {
                _step++; _statusText.text = "좋아, 척척 맞네!";
                if (_step >= _recipe.Length) { MyDishComplete(); return; }
            }
            else { _step = 0; _statusText.text = $"앗, ‘{ingredient}’은 순서가 아니야. 처음부터!"; }
            Refresh();
        }

        // 서버 신호: 1 = 둘 다 완성 → 함께 다음으로.
        public void NetSignal(int code) { if (code == 1) Complete(); }

        private void MyDishComplete()
        {
            _myDishDone = true;
            Refresh();
            if (_net && DoneSink != null)
            {
                _statusText.text = $"{_dishName} 완성! ★  상대가 끝내길 기다리는 중…";
                DoneSink.Invoke(_localSlot);   // 둘 다 완성되면 서버가 NetSignal(1) 을 보낸다
            }
            else
            {
                _statusText.text = "완성! ★★★  맛있겠다.";
                Complete();
            }
        }

        private void Complete()
        {
            if (_finished) return;
            _finished = true;
            _statusText.text = "둘 다 완성! ★★★  같이 차린 저녁이다.";
            StartCoroutine(FinishSoon());
        }

        private void Refresh()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _recipe.Length; i++)
            {
                if (i > 0) sb.Append("  →  ");
                if (i < _step) sb.Append($"<color=#A8E6B0>{_recipe[i]} ✔</color>");
                else if (i == _step) sb.Append($"<b><color=#FFD98C>{_recipe[i]}</color></b>");
                else sb.Append($"<color=#7A8088>{_recipe[i]}</color>");
            }
            _seqText.text = sb.ToString();
        }

        private IEnumerator FinishSoon() { yield return new WaitForSeconds(1.3f); var cb = _onComplete; Destroy(gameObject); cb?.Invoke(); }

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

        private void CreateHotspot(Transform parent, string label, Vector2 center, int index)
        {
            var go = new GameObject("Hotspot", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = center; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(170, 72); rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>(); img.color = new Color(0.14f, 0.18f, 0.26f, 0.88f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var t = NewText(go.transform, "L", 26, FontStyle.Normal, TextAnchor.MiddleCenter); Stretch(t.rectTransform, 4); t.text = label;
            int idx = index;
            btn.onClick.AddListener(() => OnHotspot(idx));
        }

        private static void Stretch(RectTransform rt, float pad = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad); }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
