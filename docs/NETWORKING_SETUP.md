# 네트워킹 씬 설정 (Mirror)

방식: **GameSession 씬 오브젝트** + 락스텝(입력만 동기화). 호스트=Player1, 참가자=Player2.

## 1. 씬
- 싱글용 `GameBootstrap` 씬과는 **별도 Online 씬**(둘을 한 씬에 같이 두지 말 것).

## 2. NetworkManager 오브젝트
빈 GameObject 에:
1. **OCNetworkManager** (접속 로그 + 클라 Ready 보장. 기본 NetworkManager 대신 이걸 사용)
2. **KcpTransport**
3. **NetworkManagerHUD** (테스트용 Host/Client 버튼)

설정:
- `Player Prefab` = NetPlayer (자동 셋업이 생성·할당), `Auto Create Player` = ✔
- `Online/Offline Scene` = 비움, `Network Address` = `localhost` 또는 호스트 IP

## 3. GameSession 씬 오브젝트
- 빈 GameObject "GameSession" + **NetworkIdentity** + **GameSession**.
- (혼자 테스트면 `Required Players = 1`, 2인이면 `2`)

## 4. 자동 셋업
메뉴 **`OC ▸ 네트워킹 자동 셋업`** → NetPlayer 프리팹 생성 + playerPrefab 할당 + autoCreatePlayer=true.
실행 후 **씬 저장(Ctrl+S)**.

## 5. 실행
- 한쪽 **Host**, 다른쪽 **Client**. 2개 띄우기: 빌드 .exe + 에디터, 또는 ParrelSync.
- .exe 테스트면 씬 수정 후 **재빌드**.

## 정상 로그
```
(서버) [GameSession] OnStartServer → BuildGame
(클라) [GameSession] OnStartClient → BuildGame
(서버) [GameSession] 원격 준비 → readyCount=2/2 → 모두 준비 → 시작
```

## 한계
- 직접 연결(LAN/localhost). 원거리는 릴레이 필요(추후).
- 이름/사진 동기화(#6)는 아직 — 지금은 양쪽 임시 기본 이름. 접속 확정 후 청크 전송으로 붙임.
- 중간 합류/재접속 동기화 미지원(둘이 같이 시작 전제).
