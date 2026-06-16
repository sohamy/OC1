# 눈은 모든 걸 덮는다 — 자캐 커플 추리 비주얼 노벨

자캐 커뮤의 **커플 자캐**를 주제로 한 **온라인 2인 협력 추리 비주얼 노벨**.
두 사람이 각자 자기 자캐(커플)를 맡아, 눈 갇힌 별장의 밀실 살인 사건을 함께 푼다.

- 엔진: **Unity 2022.3 LTS** (데스크톱 .exe)
- 네트워킹: **Mirror**
- 플레이: 온라인 2인 협력, 자유 RP 대사, 협력 미니게임 2종(요리·증거 수집)
- 톤: 피폐(무겁고 비극적인) 밀실 추리

전체 기획서는 `docs/기획서.md` 참고.

---

## 처음 한 번 설정 (중요)

이 저장소에는 스크립트와 데이터만 들어있다. 아래 두 패키지를 설치해야 컴파일된다.

### 1) Unity 프로젝트 열기
- Unity Hub에서 `Add project from disk` → 이 폴더(`D:\OC`) 선택 → **2022.3.62f1**로 열기.
- 처음 열면 Unity가 `Library/`, 나머지 `ProjectSettings/`를 자동 생성한다.

### 2) Mirror 설치 (네트워킹)
- **Asset Store(권장, 무료):** Asset Store에서 "Mirror" 검색 → 임포트. `Assets/Mirror/`에 들어온다.
- Mirror는 `UnityEngine.Terrain`을 참조하므로 **Terrain 빌트인 모듈**이 필요하다.
  → `Packages/manifest.json`에 `com.unity.modules.terrain`/`com.unity.modules.terrainphysics` 가
    이미 추가돼 있다. (없으면 `CS1069: Terrain ...` 에러가 난다)

### 3) StandaloneFileBrowser (사진 파일 선택) — 이미 포함됨
- `Assets/Plugins/StandaloneFileBrowser/`에 이미 들어있다. (Windows는 Ookii.Dialogs 사용)
- **주의:** SFB는 UPM(git URL) 패키지가 **아니다.** Package Manager에 git 주소를 넣으면
  "no package manifest" 에러가 난다 — 소스를 직접 `Assets/Plugins/`에 두는 방식이다.

> Mirror를 넣기 전에는 `Network/` 스크립트에 에러가 보이는 게 정상이다.
> 데이터/시나리오/다이얼로그/UI 코어는 Mirror 없이도 컴파일된다.

---

## 빠른 실행 (싱글플레이 검증)
네트워크 붙이기 전, 시나리오 흐름을 혼자 끝까지 확인하는 방법:
1. 새 씬 생성(`File > New Scene`).
2. 빈 GameObject 생성 → `Assets/Scripts/Core/GameBootstrap.cs` 부착.
3. (선택) 인스펙터에서 두 캐릭터 임시 이름 입력.
4. **Play** → 도착~엔딩까지 진행. 클릭/Space로 진행, 선택지·RP 입력·범인 지목 동작.

> 미니게임/심문은 지금은 "계속" 버튼 스텁이다(흐름 확인용). 실제 미니게임은 #9·#10에서 구현.
> 배경은 `Resources/Backgrounds/...`에 이미지가 있으면 표시되고, 없으면 임시 단색으로 나온다.

---

## 폴더 구조
```
Assets/
  Scripts/
    Core/        # 공통 타입·유틸
    Dialogue/    # 시나리오 데이터 모델 + DialogueRunner
    Network/     # Mirror: GameSession, NetworkManager (Mirror 필요)
    Characters/  # 커플 커스텀, 사진 업로드, 이미지 동기화
    RP/          # 자유 대사 입력
    Minigames/   # 요리 / 증거 수집
    Mystery/     # 단서노트, 심문, 범인지목, 엔딩
    UI/          # 대사창, 선택지, 입력창 등
  Data/
    scenario.json   # 시나리오 본문 데이터
  Resources/        # 런타임 로드 리소스(배경 등)
  Plugins/          # StandaloneFileBrowser 등 외부 플러그인
docs/
  기획서.md
```

## 실행 / 테스트
- 에디터에서 부트 씬을 열고 Play → "방 만들기(Host)"로 시작.
- 두 번째 플레이어는 빌드(.exe) 또는 두 번째 에디터 인스턴스에서 "참가(Client)"로 호스트 주소 입력.
- 자세한 검증 체크리스트는 `docs/기획서.md`의 "검증 방법" 참고.
