# 전용 서버를 클라우드 VM에 올리기 (CGNAT 우회)

목표: 공인 IP 가진 VM에 **데디케이티드 서버**를 띄우고, 두 플레이어가 그 IP로 접속.
서버는 진행을 권위적으로 시퀀싱·중계만 한다(뷰/러너 없음 → 헤드리스 OK). 슬롯은 접속순 P1/P2 자동배정.

---

## 0. 사전: 씬 구성 확인
- Online 씬에 **OCNetworkManager + KcpTransport + GameSession(NetworkIdentity) + ConnectUI** 있고,
  `GameSession.Required Players = 2`.
- **Build Settings 의 Scenes 목록 맨 위(index 0) = Online 씬**.
- `OCNetworkManager.autoStartHeadlessServer = true` (기본값) → 서버 빌드가 `-server`/배치모드면 자동 StartServer.

## 1. Unity: 리눅스 서버 빌드 만들기
Oracle 무료 VM은 보통 리눅스라 **Linux 빌드**를 만든다.
1. **Unity Hub ▸ Installs ▸ 2022.3.62f1 ▸ Add Modules** 에서
   **Linux Dedicated Server Build Support**(없으면 **Linux Build Support (IL2CPP/Mono)**) 설치.
2. **File ▸ Build Settings**
   - Platform: **Dedicated Server ▸ Linux** (모듈 있으면) — 렌더링 없이 가벼움.
   - 모듈이 없으면 **Linux** 선택 후, (구버전이면) **Server Build** 체크. 일반 빌드도 `-nographics`로 서버 가능.
3. **Build** → 예: `Server/villa.x86_64` + `villa_Data/` 폴더 생성.

> 우리 코드: 뷰/폰트는 **클라(OnStartClient)에서만** 생성 → 서버 헤드리스에서 그래픽/폰트 안 건드림. 안전.

## 2. 클라우드 VM 준비 (Oracle Cloud Always Free 예시)
1. Oracle Cloud 가입 → **Always Free** Ubuntu 인스턴스 생성(ARM Ampere 또는 AMD micro).
2. **공인 IP 확인**(인스턴스 상세의 Public IP).
3. **포트 열기 (UDP 7777 두 군데 다)**:
   - **VCN Security List(또는 NSG)**: Ingress 규칙 추가 → Source `0.0.0.0/0`, **UDP**, Dest Port **7777**.
   - **OS 방화벽**(우분투): 
     ```
     sudo iptables -I INPUT -p udp --dport 7777 -j ACCEPT
     sudo netfilter-persistent save     # 재부팅 후 유지
     ```
     (ufw 쓰면 `sudo ufw allow 7777/udp`)

## 3. 업로드 & 실행
1. 빌드를 VM으로 복사:
   ```
   scp -r Server/* ubuntu@<VM_공인IP>:~/villa/
   ```
2. SSH 접속 후 실행 권한 + 실행:
   ```
   chmod +x ~/villa/villa.x86_64
   cd ~/villa
   ./villa.x86_64 -batchmode -nographics -server
   ```
   → `[OCNet] 헤드리스 감지 — 전용 서버 자동 시작`, `[GameSession] OnStartServer` 로그가 뜨면 OK.
3. **상시 실행**(SSH 끊겨도 유지): `tmux` 또는 systemd 서비스.
   ```
   tmux new -s villa
   ./villa.x86_64 -batchmode -nographics -server
   # Ctrl+B, D 로 분리. 다시 보려면 tmux attach -t villa
   ```

## 4. 클라이언트 접속
- 두 플레이어: 게임 실행 → **참가** → 주소 칸에 **VM 공인 IP** 입력 (포트 7777이면 IP만, 다르면 `IP:포트`).
- 둘 다 접속·커스텀하면 자동 시작. 서버 콘솔에 `슬롯 배정 … → P1/P2`, `커스텀 완료 → 2/2`, `RpcBegin` 로그.

---

## 점검 / 트러블슈팅
- 클라가 타임아웃 → VM **UDP 7777**이 VCN+OS 양쪽 다 열렸는지(특히 Oracle은 OS iptables가 기본 차단).
- 서버 콘솔에 `서버: 클라 접속됨`이 안 뜨면 패킷이 VM까지 못 온 것(포트/보안그룹).
- 접속은 되는데 시작 안 됨 → `Required Players` 값 확인(2명 다 커스텀해야 시작).
- 로그 위치: 서버 stdout(tmux) 또는 `~/.config/unity3d/<회사>/<제품>/Player.log`.

## 무료 VM 옵션
- **Oracle Cloud Always Free**(가장 넉넉, ARM/AMD) — 추천.
- **Google Cloud e2-micro**(무료 등급), **AWS EC2 t2.micro**(12개월 무료).
