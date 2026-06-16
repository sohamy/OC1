using Mirror;
using OC.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OC.Net
{
    /// <summary>
    /// 접속 화면(데디 서버 모델). 상시 서버에 '접속'하면 RoomUI 가 인계받아 방 만들기/참가로 이어진다.
    /// 주소 칸 기본값 = NetworkManager.networkAddress(빌드에 박아둔 서버 공인 IP). LAN 테스트는 직접 입력.
    /// '서버 시작'은 헤드리스가 아닌 에디터/PC 에서 데디 서버를 띄울 때(테스트 포함) 쓴다.
    /// 사용: NetworkManager 오브젝트에 이 컴포넌트를 붙이고 NetworkManagerHUD 는 제거.
    /// </summary>
    public class ConnectUI : MonoBehaviour
    {
        private Font _font;
        private GameObject _panel;
        private InputField _addr;
        private Text _status;

        private void Start() => Build();

        private void Update()
        {
            // 접속/서버가 시작되면 접속 화면 숨김(RoomUI/게임이 인계받음)
            bool active = NetworkServer.active || NetworkClient.active || NetworkClient.isConnecting;
            if (_panel != null && _panel.activeSelf == active) _panel.SetActive(!active);
        }

        private void Build()
        {
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS", "Arial" }, 28);
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("ConnectCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();
            _panel = canvasGo;

            var bg = NewImage(canvasGo.transform, "BG"); Stretch(bg.rectTransform);
            bg.color = new Color(0.06f, 0.07f, 0.10f, 1f);

            var title = NewText(canvasGo.transform, "Title", 48, FontStyle.Bold, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.86f));
            title.text = "눈은 모든 걸 덮는다";

            // 서버 주소
            var addrBg = NewImage(canvasGo.transform, "AddrBg"); addrBg.color = new Color(1, 1, 1, 0.95f);
            Anchor(addrBg.rectTransform, new Vector2(0.32f, 0.55f), new Vector2(0.68f, 0.62f));
            _addr = addrBg.gameObject.AddComponent<InputField>();
            var at = NewText(addrBg.transform, "T", 24, FontStyle.Normal, TextAnchor.MiddleLeft); at.color = Color.black; at.supportRichText = false; Stretch(at.rectTransform, 10);
            var ph = NewText(addrBg.transform, "P", 24, FontStyle.Italic, TextAnchor.MiddleLeft); ph.color = new Color(0, 0, 0, 0.4f); ph.text = "서버 주소"; Stretch(ph.rectTransform, 10);
            _addr.textComponent = at; _addr.placeholder = ph; _addr.lineType = InputField.LineType.SingleLine;
            _addr.text = NetworkManager.singleton != null ? NetworkManager.singleton.networkAddress : "localhost";

            var connect = CreateButton(canvasGo.transform, "접속", OnConnect);
            Anchor((RectTransform)connect.transform, new Vector2(0.35f, 0.44f), new Vector2(0.65f, 0.52f));

            var server = CreateButton(canvasGo.transform, "서버 시작 (데디·테스트)", OnServer);
            Anchor((RectTransform)server.transform, new Vector2(0.37f, 0.35f), new Vector2(0.63f, 0.42f));

            _status = NewText(canvasGo.transform, "Status", 24, FontStyle.Italic, TextAnchor.MiddleCenter);
            Anchor(_status.rectTransform, new Vector2(0.1f, 0.25f), new Vector2(0.9f, 0.32f));
            _status.color = new Color(1f, 0.9f, 0.6f);

            var quit = CreateButton(canvasGo.transform, "게임 종료", AppControl.Quit);
            Anchor((RectTransform)quit.transform, new Vector2(0.84f, 0.04f), new Vector2(0.98f, 0.1f));

            var hint = NewText(canvasGo.transform, "Hint", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
            Anchor(hint.rectTransform, new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.2f));
            hint.color = new Color(1, 1, 1, 0.5f);
            hint.text = "접속하면 방을 만들거나 친구의 방 코드로 참가할 수 있어요.\n같은 PC 테스트: 한 쪽은 '서버 시작', 다른 쪽은 'localhost'로 접속.";
        }

        private void OnConnect()
        {
            if (NetworkManager.singleton == null) { _status.text = "NetworkManager 가 없습니다."; return; }
            var raw = string.IsNullOrWhiteSpace(_addr.text) ? "localhost" : _addr.text.Trim();

            // "IP:포트" 형식이면 포트도 적용
            string ip = raw;
            int colon = raw.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(raw.Substring(colon + 1), out ushort port))
            {
                ip = raw.Substring(0, colon);
                if (Transport.active is kcp2k.KcpTransport kcp) kcp.Port = port;
            }

            NetworkManager.singleton.networkAddress = ip;
            _status.text = $"{raw} 에 접속 중…";
            NetworkManager.singleton.StartClient();
        }

        private void OnServer()
        {
            if (NetworkManager.singleton == null) { _status.text = "NetworkManager 가 없습니다."; return; }
            _status.text = "서버 시작됨 — 클라이언트 접속 대기 중…";
            NetworkManager.singleton.StartServer();
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

        private Button CreateButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(0.18f, 0.22f, 0.3f, 0.98f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var t = NewText(go.transform, "L", 26, FontStyle.Normal, TextAnchor.MiddleCenter); Stretch(t.rectTransform, 6); t.text = label;
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static void Stretch(RectTransform rt, float pad = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad); }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    }
}
