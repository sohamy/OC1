using System;
using Mirror;
using OC.Core;
using OC.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.UI
{
    /// <summary>
    /// 접속 후 클라이언트 화면: 방 메뉴(만들기/코드 참가) + 방 로비(코드 표시·복사, P1/P2 선택, 대기).
    /// GameSession(방마다 스폰) 이 슬롯/시작 이벤트를 이쪽으로 넘겨준다.
    /// </summary>
    public class RoomUI : MonoBehaviour
    {
        public static RoomUI Instance { get; private set; }

        private RoomPlayer _player;
        private GameSession _session;

        private Font _font;
        private GameObject _menuPanel, _roomPanel;
        private InputField _codeInput;
        private Text _menuStatus, _roomStatus, _codeLabel;
        private Button _p1Btn, _p2Btn;

        public static void ShowFor(RoomPlayer player)
        {
            if (Instance == null)
            {
                var go = new GameObject("RoomUI");
                Instance = go.AddComponent<RoomUI>();
                Instance.Build();
            }
            Instance.Bind(player);
        }

        private void Bind(RoomPlayer player)
        {
            if (_player != null)
            {
                _player.RoomCodeChanged -= OnRoomCode;
                _player.ErrorReceived -= OnError;
            }
            _player = player;
            _player.RoomCodeChanged += OnRoomCode;
            _player.ErrorReceived += OnError;
            ShowMenu();
            if (_menuStatus != null) _menuStatus.text = "";
        }

        private void OnDestroy()
        {
            if (_player != null) { _player.RoomCodeChanged -= OnRoomCode; _player.ErrorReceived -= OnError; }
            if (Instance == this) Instance = null;
        }

        // ───────── 상태 전환 ─────────
        private void ShowMenu()
        {
            _session = null;
            if (_menuPanel != null) _menuPanel.SetActive(true);
            if (_roomPanel != null) _roomPanel.SetActive(false);
            // 상태 메시지는 여기서 지우지 않는다(상대 이탈 안내가 메뉴 복귀 후에도 남도록). 시작 시점은 Bind 에서 초기화.
        }

        private void ShowRoom()
        {
            if (_menuPanel != null) _menuPanel.SetActive(false);
            if (_roomPanel != null) _roomPanel.SetActive(true);
        }

        private void OnRoomCode(string code)
        {
            if (string.IsNullOrEmpty(code)) { ShowMenu(); return; }
            GUIUtility.systemCopyBuffer = code; // 클립보드 자동 복사
            if (_codeLabel != null) _codeLabel.text = $"방 코드 (복사됨): <b>{code}</b>";
            if (_roomStatus != null) _roomStatus.text = "상대를 기다리는 중… 위 코드를 친구에게 전달하세요.";
            ShowRoom();
        }

        private void OnError(string msg)
        {
            if (_menuStatus != null) _menuStatus.text = msg;
            if (_roomStatus != null) _roomStatus.text = msg;
        }

        // ───────── GameSession → RoomUI (클라) ─────────
        public void BeginSlotPick(GameSession session)
        {
            _session = session;
            ShowRoom();
            SetSlotState(false, false);
            if (_roomStatus != null) _roomStatus.text = "역할(P1/P2)을 선택하세요.";
        }

        public void SetSlotState(bool p1Taken, bool p2Taken)
        {
            SetButton(_p1Btn, !p1Taken);
            SetButton(_p2Btn, !p2Taken);
        }

        public void OnSlotRejected(string reason)
        {
            if (_roomStatus != null) _roomStatus.text = reason;
            // 버튼은 SetSlotState 로 곧 갱신됨
        }

        public void OnSlotAssigned()
        {
            // 커스터마이즈 화면이 전체를 덮으므로 방 패널은 숨긴다.
            if (_roomPanel != null) _roomPanel.SetActive(false);
        }

        public void OnGameBegan()
        {
            if (_menuPanel != null) _menuPanel.SetActive(false);
            if (_roomPanel != null) _roomPanel.SetActive(false);
        }

        public void OnPartnerLeft()
        {
            ShowMenu();
            if (_menuStatus != null) _menuStatus.text = "상대가 방을 나갔어요. 다시 방을 만들거나 참가하세요.";
        }

        // ───────── 입력 핸들러 ─────────
        private void OnCreate()
        {
            if (_player == null) return;
            if (_menuStatus != null) _menuStatus.text = "방 만드는 중…";
            _player.CmdCreateRoom();
        }

        private void OnJoin()
        {
            if (_player == null) return;
            var code = _codeInput != null ? _codeInput.text : null;
            if (string.IsNullOrWhiteSpace(code)) { if (_menuStatus != null) _menuStatus.text = "방 코드를 입력하세요."; return; }
            if (_menuStatus != null) _menuStatus.text = "참가 중…";
            _player.CmdJoinRoom(code.Trim());
        }

        private void OnPick(int slot)
        {
            if (_session == null) return;
            SetButton(_p1Btn, false); SetButton(_p2Btn, false); // 응답 대기
            if (_roomStatus != null) _roomStatus.text = "자리 확인 중…";
            _session.PickSlot(slot);
        }

        // ───────── UI 빌드 ─────────
        private void Build()
        {
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 28);
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("RoomCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 180;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            BuildMenu(canvasGo.transform);
            BuildRoom(canvasGo.transform);
            _roomPanel.SetActive(false);
        }

        private void BuildMenu(Transform parent)
        {
            _menuPanel = NewPanel(parent, "MenuPanel");

            var title = NewText(_menuPanel.transform, "Title", 46, FontStyle.Bold, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.86f));
            title.text = "눈은 모든 걸 덮는다";

            var create = CreateButton(_menuPanel.transform, "방 만들기", OnCreate);
            Anchor((RectTransform)create.transform, new Vector2(0.35f, 0.56f), new Vector2(0.65f, 0.64f));

            var codeBg = NewImage(_menuPanel.transform, "CodeBg"); codeBg.color = new Color(1, 1, 1, 0.95f);
            Anchor(codeBg.rectTransform, new Vector2(0.35f, 0.45f), new Vector2(0.65f, 0.52f));
            _codeInput = codeBg.gameObject.AddComponent<InputField>();
            var t = NewText(codeBg.transform, "T", 24, FontStyle.Normal, TextAnchor.MiddleLeft); t.color = Color.black; t.supportRichText = false; Stretch(t.rectTransform, 10);
            var ph = NewText(codeBg.transform, "P", 24, FontStyle.Italic, TextAnchor.MiddleLeft); ph.color = new Color(0, 0, 0, 0.4f); ph.text = "친구가 준 방 코드"; Stretch(ph.rectTransform, 10);
            _codeInput.textComponent = t; _codeInput.placeholder = ph; _codeInput.lineType = InputField.LineType.SingleLine; _codeInput.characterLimit = 8;

            var join = CreateButton(_menuPanel.transform, "코드로 참가", OnJoin);
            Anchor((RectTransform)join.transform, new Vector2(0.35f, 0.36f), new Vector2(0.65f, 0.44f));

            _menuStatus = NewText(_menuPanel.transform, "Status", 24, FontStyle.Italic, TextAnchor.MiddleCenter);
            Anchor(_menuStatus.rectTransform, new Vector2(0.1f, 0.26f), new Vector2(0.9f, 0.33f));
            _menuStatus.color = new Color(1f, 0.9f, 0.6f);

            var quit = CreateButton(_menuPanel.transform, "게임 종료", AppControl.Quit);
            Anchor((RectTransform)quit.transform, new Vector2(0.84f, 0.04f), new Vector2(0.98f, 0.1f));
        }

        private void BuildRoom(Transform parent)
        {
            _roomPanel = NewPanel(parent, "RoomPanel");

            _codeLabel = NewText(_roomPanel.transform, "Code", 40, FontStyle.Bold, TextAnchor.MiddleCenter);
            Anchor(_codeLabel.rectTransform, new Vector2(0.1f, 0.7f), new Vector2(0.9f, 0.82f));
            _codeLabel.color = new Color(1f, 0.92f, 0.6f);

            var copy = CreateButton(_roomPanel.transform, "코드 복사", () =>
            {
                if (_player != null && !string.IsNullOrEmpty(_player.roomCode)) GUIUtility.systemCopyBuffer = _player.roomCode;
            });
            Anchor((RectTransform)copy.transform, new Vector2(0.42f, 0.62f), new Vector2(0.58f, 0.68f));

            _p1Btn = CreateButton(_roomPanel.transform, "P1 (플레이어 1)", () => OnPick(0));
            Anchor((RectTransform)_p1Btn.transform, new Vector2(0.25f, 0.46f), new Vector2(0.48f, 0.56f));
            _p2Btn = CreateButton(_roomPanel.transform, "P2 (플레이어 2)", () => OnPick(1));
            Anchor((RectTransform)_p2Btn.transform, new Vector2(0.52f, 0.46f), new Vector2(0.75f, 0.56f));

            _roomStatus = NewText(_roomPanel.transform, "Status", 24, FontStyle.Italic, TextAnchor.MiddleCenter);
            Anchor(_roomStatus.rectTransform, new Vector2(0.1f, 0.34f), new Vector2(0.9f, 0.42f));
            _roomStatus.color = new Color(1f, 0.9f, 0.6f);
        }

        // ── UI 팩토리 ──
        private GameObject NewPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var bg = go.AddComponent<Image>(); bg.color = new Color(0.06f, 0.07f, 0.10f, 1f);
            return go;
        }

        private Image NewImage(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.AddComponent<Image>(); }

        private Text NewText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>(); t.font = _font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor;
            t.color = Color.white; t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private Button CreateButton(Transform parent, string label, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(0.18f, 0.22f, 0.3f, 0.98f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var t = NewText(go.transform, "L", 26, FontStyle.Normal, TextAnchor.MiddleCenter); Stretch(t.rectTransform, 6); t.text = label;
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static void SetButton(Button b, bool interactable)
        {
            if (b == null) return;
            b.interactable = interactable;
            var img = b.targetGraphic as Image;
            if (img != null) img.color = interactable ? new Color(0.18f, 0.22f, 0.3f, 0.98f) : new Color(0.12f, 0.13f, 0.16f, 0.7f);
        }

        private static void Stretch(RectTransform rt, float pad = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad); }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
