# PacmanGame 🎮

C# WinForms 기반으로 제작된 팩맨(Pac-Man) 스타일 미니게임입니다.  
플레이어가 미로를 이동하며 코인을 수집하고, 유령을 피해 높은 점수를 획득하는 방식으로 설계되었습니다.

---

## 주요 기능

- 플레이어 이동 및 충돌 감지  
- 코인 수집 & 점수 시스템  
- 라운드 / 스테이지 전환  
- 유령 추적 AI (간단한 패턴 기반 추적 로직)

---

## 설계 특징

- 클래스 기반 역할 분리  
  - Player, Ghost, GameManager 등 구조적 구성
- 유지보수성과 확장성을 고려한 구조  
  - 스테이지 추가, 유령 로직 변경 등 기능 확장 용이
- WinForms 이벤트 기반 루프  
  - KeyDown, Timer를 활용한 게임 흐름 제어

---

## 사용 기술

- C# (.NET Framework)
- Windows Forms (WinForms)
- Visual Studio 2022

---

## 실행 환경 (Environment)

본 프로젝트는 아래 환경에서 개발 및 테스트되었습니다.

- 운영체제: Windows 10
- 프레임워크: **.NET Framework 8.0**
- IDE: **Visual Studio 2022**
- UI: **Windows Forms 기반**

**주의:**  
Windows 10 환경에서만 테스트되었습니다.  
Windows 11에서도 동작할 가능성은 있으나, 별도 검증은 진행되지 않았습니다.  
Linux / macOS에서는 WinForms 기반 프로그램이 정상적으로 실행되지 않습니다.  
Windows 환경에서 실행하는 것을 권장합니다.

---
