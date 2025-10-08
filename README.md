PacmanGame 🎮

C# WinForms 기반의 팩맨 게임 프로젝트입니다.  
객체지향(SOLID) 원칙을 적용하여 구조적으로 설계되었습니다.

기술 스택
- C# (.NET Framework)
- Windows Forms
- Visual Studio 2022

게임 기능
- 플레이어 이동 및 충돌 감지
- 코인 수집 및 점수 시스템
- 라운드/스테이지 전환
- 유령 AI 추적 시스템

설계 특징
- SOLID 원칙 준수
- 클래스를 통한 역할 분리
- 유지보수와 확장성에 유리한 구조 설계

프로젝트 구조
PacmanSolution.sln
├─ PacmanGame (WinForms 클라이언트)
│ ├─ Form1.cs
│ ├─ Ghost.cs
│ └─ GameLogic.cs
│
└─ PacmanServer (서버 로직)
├─ Server.cs
├─ GameRoom.cs
└─ Program.cs
