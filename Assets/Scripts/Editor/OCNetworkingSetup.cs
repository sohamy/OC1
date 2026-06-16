#if UNITY_EDITOR
using Mirror;
using OC.Net;
using UnityEditor;
using UnityEngine;

namespace OC.EditorTools
{
    /// <summary>
    /// 멀티룸 데디 서버 구성을 씬/프리팹에 자동 셋업한다.
    /// 메뉴: OC ▸ 네트워킹 자동 셋업
    ///  - Assets/Prefabs/NetPlayer.prefab (NetworkIdentity + NetworkMatch + RoomPlayer) 생성/보강
    ///  - Assets/Prefabs/GameSessionRoom.prefab (NetworkIdentity + NetworkMatch + GameSession) 생성
    ///  - NetworkManager: playerPrefab/autoCreatePlayer, MatchInterestManagement(AOI), RoomServer, spawnPrefabs 등록
    ///  - 씬에 남아있는 GameSession 오브젝트 제거(이제 방마다 스폰)
    /// </summary>
    public static class OCNetworkingSetup
    {
        private const string Dir = "Assets/Prefabs";
        private const string PlayerPath = Dir + "/NetPlayer.prefab";
        private const string SessionPath = Dir + "/GameSessionRoom.prefab";

        [MenuItem("OC/네트워킹 자동 셋업")]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

            var playerPrefab = EnsurePlayerPrefab();
            var sessionPrefab = EnsureSessionPrefab();

            var nm = Object.FindObjectOfType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[OCNetworkingSetup] 씬에 NetworkManager 가 없습니다. 먼저 NetworkManager 오브젝트를 만드세요.");
                return;
            }

            // 플레이어 프리팹 + 자동 생성
            nm.playerPrefab = playerPrefab;
            nm.autoCreatePlayer = true;

            // GameSessionRoom 을 스폰 가능 목록에 등록
            if (!nm.spawnPrefabs.Contains(sessionPrefab))
                nm.spawnPrefabs.Add(sessionPrefab);

            // AOI: MatchInterestManagement (OnEnable 에서 NetworkServer.aoi 등록)
            EnsureComponent<MatchInterestManagement>(nm.gameObject);

            // 방 레지스트리
            var roomServer = EnsureComponent<RoomServer>(nm.gameObject);
            roomServer.gameSessionPrefab = sessionPrefab;

            EditorUtility.SetDirty(nm);
            EditorUtility.SetDirty(roomServer);

            // 씬에 남은 GameSession 오브젝트 제거(이제 방마다 스폰)
            var sceneSession = Object.FindObjectOfType<GameSession>();
            if (sceneSession != null && !EditorUtility.IsPersistent(sceneSession))
            {
                Debug.Log("[OCNetworkingSetup] 씬의 GameSession 오브젝트 제거(방마다 스폰으로 대체).");
                Object.DestroyImmediate(sceneSession.gameObject);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[OCNetworkingSetup] 완료 — 멀티룸 구성: NetPlayer(+RoomPlayer/NetworkMatch), GameSessionRoom, MatchInterestManagement, RoomServer 셋업됨. 씬을 저장(Ctrl+S)하세요.");
        }

        private static GameObject EnsurePlayerPrefab()
        {
            var contents = LoadOrCreate(PlayerPath, "NetPlayer");
            EnsureOn(contents, typeof(NetworkIdentity), typeof(NetworkMatch), typeof(RoomPlayer));
            var saved = PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
            Object.DestroyImmediate(contents);
            Debug.Log($"[OCNetworkingSetup] 플레이어 프리팹 준비: {PlayerPath}");
            return saved;
        }

        private static GameObject EnsureSessionPrefab()
        {
            var contents = LoadOrCreate(SessionPath, "GameSessionRoom");
            EnsureOn(contents, typeof(NetworkIdentity), typeof(NetworkMatch), typeof(GameSession));
            var saved = PrefabUtility.SaveAsPrefabAsset(contents, SessionPath);
            Object.DestroyImmediate(contents);
            Debug.Log($"[OCNetworkingSetup] GameSession 프리팹 준비: {SessionPath}");
            return saved;
        }

        // 프리팹이 있으면 편집용 인스턴스로 로드, 없으면 새 GameObject 생성.
        private static GameObject LoadOrCreate(string path, string name)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return PrefabUtility.LoadPrefabContents(path);
            return new GameObject(name);
        }

        private static void EnsureOn(GameObject go, params System.Type[] types)
        {
            foreach (var t in types)
                if (go.GetComponent(t) == null) go.AddComponent(t);
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }
    }
}
#endif
