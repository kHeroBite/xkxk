# 메뉴 아이콘 조합 데이터(메뉴 ID → 기본 글리프 + 보조 표지) — 생성기 menu_icons.py 가 읽는다
# 고칠 때는 이 파일의 값을 고치고 `python3 -B plan/_build/menu_icons.py` 로 다시 만든다. 메뉴 ID 는 menu_data.py MENU_ID 가 정본이다(ADR-085).
# 조합(글리프, 표지)은 메뉴 ID 마다 달라야 한다(생성기 --check 가 누락·중복·없는 이름을 잡는다). 글리프·표지 그림은 icon_glyphs.py.

GEN_TOOL = "menu_icons.py"
GEN_MODEL = "claude"
GEN_DATE = "2026-11-22"     # 라이선스 행 날짜. 다시 만들어도 같은 값이라 --check 가 흔들리지 않는다

# 메뉴 ID: (글리프, 보조 표지 또는 None). 주석은 화면 ID·화면명.
ICONS = {
    "1001": ("login", None),            # SCR-IAM-01 로그인·가입·추가 인증
    "1002": ("shield", None),           # SCR-IAM-02 역할·권한
    "1003": ("key", None),              # SCR-IAM-03 SSO·프로비저닝
    "1004": ("list", "search"),         # SCR-IAM-04 감사 로그
    "1005": ("tree", None),             # SCR-ORG-01 조직도
    "1006": ("people", None),           # SCR-ORG-02 구성원·투입 직군
    "1007": ("idcard", None),           # SCR-HRS-01 인사 정보·단가
    "1008": ("eye", "check"),           # SCR-HRS-02 민감정보 열람 승인
    "1009": ("folder", None),           # SCR-PRJ-01 프로젝트 목록·등록
    "1010": ("folder", "clock"),        # SCR-PRJ-02 프로젝트 상세(개요·생애주기)
    "1011": ("building", None),         # SCR-PRJ-03 계약·고객사
    "1012": ("docs", None),             # SCR-PRJ-04 산출물·첨부
    "1013": ("target", None),           # SCR-PRJ-05 포트폴리오·OKR
    "1014": ("archive", None),          # SCR-PRJ-06 보존·파기
    "1015": ("gantt", None),            # SCR-SCH-01 WBS·간트
    "1016": ("flag", None),             # SCR-SCH-02 마일스톤·보드·캘린더 뷰
    "1017": ("gantt", "person"),        # SCR-SCH-03 자원 배정
    "1018": ("issue", "search"),        # SCR-ISS-01 이슈 목록·검색
    "1019": ("issue", None),            # SCR-ISS-02 이슈 상세
    "1020": ("kanban", None),           # SCR-ISS-03 백로그·스프린트·칸반
    "1021": ("warn", None),             # SCR-ISS-04 리스크·변경요청
    "1022": ("flow", None),             # SCR-ISS-05 워크플로·자동화 규칙
    "1023": ("branch", None),           # SCR-ISS-06 릴리스·Git 연동
    "1024": ("inbox", None),            # SCR-ISS-07 메일 수신·유지보수 큐
    "1025": ("clock", None),            # SCR-TIM-01 공수 기록·주간 타임시트
    "1026": ("calendar", "person"),     # SCR-TIM-02 근태·휴가
    "1027": ("coin", None),             # SCR-FIN-01 예산·원가
    "1028": ("chartline", None),        # SCR-FIN-02 손익·EVM·견적 대비 실적
    "1029": ("calc", None),             # SCR-FIN-03 견적
    "1030": ("receipt", None),          # SCR-FIN-04 청구·수금
    "1031": ("wallet", None),           # SCR-FIN-05 경비
    "1032": ("medal", None),            # SCR-EVA-01 프로젝트·개인·조직 평가
    "1033": ("gauge", None),            # SCR-EVA-02 KPI 정의
    "1034": ("home", None),             # SCR-DSH-01 홈 대시보드
    "1035": ("dashboard", None),        # SCR-DSH-02 포트폴리오 대시보드
    "1036": ("report", "up"),           # SCR-DSH-03 주간보고·내보내기
    "1037": ("warn", "spark"),          # SCR-AIN-01 AI 리포트·위기 탐지
    "1038": ("chat", "spark"),          # SCR-AIN-02 자연어 질의
    "1039": ("doc", "spark"),           # SCR-AIN-03 계약서·문서 분석
    "1040": ("tree", "down"),           # SCR-IMP-01 조직도 동기화
    "1041": ("folder", "down"),         # SCR-IMP-02 프로젝트 가져오기
    "1042": ("card", None),             # SCR-LIC-01 회사·요금제·AI 크레딧
    "1043": ("stamp", None),            # SCR-APR-01 결재함
    "1044": ("stamp", "person"),        # SCR-APR-02 대결·위임·전결 설정
    "1045": ("share", None),            # SCR-APR-03 공유·외부 협업
    "1046": ("globe", None),            # SCR-APR-04 고객 포털·외부 요청 폼·CSAT
    "1047": ("funnel", None),           # SCR-CRM-01 영업 파이프라인
    "1048": ("briefcase", None),        # SCR-CRM-02 영업 활동·고객 소통
    "1049": ("note", "search"),         # SCR-PRE-01 입수 문서·요구 추출
    "1050": ("people", "gear"),         # SCR-PRE-02 구성안·인력 계획
    "1051": ("target", "check"),        # SCR-PRE-03 참여 결정(Go/No-Go)
    "1052": ("bell", None),             # SCR-COL-01 알림 센터
    "1053": ("chat", None),             # SCR-COL-02 메신저
    "1054": ("megaphone", None),        # SCR-COL-03 댓글·멘션·공지
    "1055": ("book", None),             # SCR-COL-04 위키·지식베이스
    "1056": ("calendar", None),         # SCR-COL-05 캘린더·화상회의
    "1057": ("tag", None),              # SCR-ADM-01 코드·카테고리
    "1058": ("database", None),         # SCR-ADM-03 백업·복원
    "1059": ("book", "search"),         # SCR-ADM-04 검색 사전·색인
    "1060": ("window", None),           # SCR-PLT-01 작업공간 셸
    "1061": ("plug", None),             # SCR-PLT-02 API·Webhook·BI·플러그인
    "1062": ("shield", "spark"),        # SCR-AIP-02 AI 데이터 정책·호출 감사
    "1063": ("gauge", "spark"),         # SCR-AIP-03 AI 사용량·한도·정산
    "1064": ("layers", None),           # SCR-DOC-01 산출물 템플릿 카탈로그
    "1065": ("pen", None),              # SCR-DOC-02 산출물 에디터·내보내기
    "1066": ("docs", "alert"),          # SCR-DOC-03 가져오기 미리보기·변환 손실 보고
    "1067": ("wand", None),             # SCR-DOC-04 RFP 프로젝트 생성 마법사·추정
    "1068": ("pie", None),              # SCR-DOC-05 산출물 커버리지
    "1069": ("idcard", "down"),         # SCR-HRS-04 연봉 수신
    "1070": ("target", "lock"),         # SCR-PRE-04 사전 VRB·기준 고정
    "1071": ("trophy", None),           # SCR-EVA-04 연말 통합 평가
    "1072": ("lanes", None),            # SCR-SCH-04 로드맵·스윔레인 타임라인
    "1073": ("chartbar", None),         # SCR-DSH-04 분석 차트(S-Curve·진척 분석)
    "1074": ("medal", "gear"),          # SCR-EVA-03 회사 평가 설정
    "1075": ("idcard", "gear"),         # SCR-HRS-03 연봉 수신 설정
    "1076": ("medal", "person"),        # SCR-EVA-05 연말 개인별 평가
    "1077": ("clipboard", "alert"),     # SCR-ISS-08 범위·변경 관리
    "1078": ("book", "star"),           # SCR-KMS-01 프로젝트 지식
    "1079": ("bulb", None),             # SCR-PRJ-07 아이디어함
    "1080": ("clipboard", "spark"),     # SCR-IMP-04 붙여넣기 분석
    "1081": ("inbox", "spark"),         # SCR-AIN-04 제안함
    "1082": ("chat", "check"),          # SCR-AIN-05 질의응답
    "1083": ("checklist", None),        # SCR-COL-06 할 일
    "1084": ("mic", None),              # SCR-COL-07 회의 실시간(STT)
    "1085": ("rocket", None),           # SCR-PLT-03 온보딩·빠른 시작
    "1086": ("graph", None),            # SCR-KMS-02 지식 연결 그래프
    "1087": ("note", None),             # SCR-COL-08 회고·브레인스토밍 보드
    "1088": ("key", "gear"),            # SCR-IAM-05 로그인 수단 관리
    "1089": ("window", "gear"),         # SCR-ADM-08 화면·항목 정책
    "a1001": ("database", "down"),      # SCR-IMP-03 Mars 이관 시험 하네스
    "a1002": ("card", "gear"),          # SCR-LIC-02 요금제·빌링 운영
    "a1003": ("help", None),            # SCR-ADM-02 도움말 관리
    "a1004": ("server", None),          # SCR-ADM-05 시스템관리자 콘솔·테넌트 운영
    "a1005": ("server", "spark"),       # SCR-AIP-01 AI 공급자·모델·공통 키·라우팅
    "a1006": ("coin", "spark"),         # SCR-AIP-04 AI 단가표·공급자 대사
    "a1007": ("chartline", "alert"),    # SCR-AIP-05 AI 실시간 모니터링·급증 경보
    "a1008": ("flask", None),           # SCR-AIP-06 프롬프트·평가·실험·자기개선
    "a1009": ("building", "gear"),      # SCR-ADM-07 법인 마스터 콘솔
    "a1010": ("server", "gear"),        # SCR-ADM-06 서버 설정 콘솔
    "a1011": ("coin", "check"),         # SCR-FIN-06 공공 SW 평균임금 단가표
}

# 셸 창·상태 아이콘(메뉴 ID 가 없는 셸 창은 고정 이름 키 — 22 설계 셸 아이콘). 이름: (글리프, 표지) 또는 ("@badge", 표지 이름) = 표지 단독.
# 생성 키는 셸 창 PdShellIcon<이름>, 상태 PdIcon<이름>.
SHELL = {
    "Document": ("doc", None),          # 메뉴 ID 를 모르는 문서 창의 기본 아이콘
    "Settings": ("gear", None),
    "ThemeEditor": ("drop", None),
    "Log": ("list", None),
    "Explorer": ("folder", None),
    "Notify": ("bell", None),
    "Help": ("help", None),
    "Favorite": ("star", None),
}
STATUS = {
    "BusyStatic": ("hourglass", None),  # 움직임 줄이기·소프트웨어 렌더 때 스피너 대신 쓰는 정지 글리프
    "BadgeSlow": ("@badge", "clock"),   # 오래 걸리는 중 표지
    "BadgeError": ("@badge", "alert"),  # 불러오기 실패 표지
}

# 명령 아이콘(상단 메뉴 실행 항목 — 메뉴 ID 가 없는 명령). 이름: (글리프, 표지). 생성 키 PdCmdIcon<이름>, 상수 CmdIconKeys.<이름>.
# 이름은 사이클27 계획서 계약표(메뉴·창·진단 코드가 같은 상수를 쓴다)라 바꾸지 않는다. 명령 글리프·표지 그림은 icon_glyphs_cmd.py.
# 조합은 ICONS·SHELL·CMD 전체에서 달라야 한다. 문서 열기 항목은 MenuIconCatalog.Grid/Gantt/FormIconKey, 셸 창은 PdShellIcon 을 재사용한다.
CMD = {
    # 파일
    "OpenSdi": ("popout", "plus"),      # 새 독립 창 ▸
    "OpenBench": ("gauge", "clock"),    # 벤치(그리드·차트)
    "SaveLayout": ("floppy", None),     # 레이아웃 저장
    "LoadLayout": ("folderopen", None), # 레이아웃 복원
    "LayoutSave": ("floppy", "plus"),   # 현재 레이아웃 백업 저장…
    "LayoutManage": ("floppy", "gear"), # 레이아웃 백업 관리…
    "Exit": ("exit", None),             # 끝내기
    # 창
    "Cascade": ("cascade", None),
    "TileH": ("tileh", None),
    "TileV": ("tilev", None),
    "Tabbed": ("tabbed", None),
    "Snap": ("snap", None),             # 창 배치 ▸(칸 항목은 SnapCellGlyph 가 그린다)
    "Minimize": ("minimize", None),
    "Maximize": ("maximize", None),     # 최대화 전환
    "Restore": ("restore", None),
    "Pin": ("pin", None),
    "Rename": ("rename", None),
    "ResetLayout": ("reset", None),
    "MinimizeAll": ("cascade", "down"),
    "RestoreAll": ("cascade", "up"),
    "RestoreLastMin": ("restore", "clock"),
    "Next": ("next", None),
    "Prev": ("prev", None),
    "SdiGroup": ("sdi", None),          # 독립 창 ▸
    "ToggleSdi": ("sdi", "check"),
    "Float": ("popout", None),          # 독립 창으로 띄우기
    "Redock": ("dockin", None),         # 메인 창으로 되돌리기
    "SdiResetPos": ("sdi", "reset"),
    "SdiMinimize": ("sdi", "down"),
    "SdiRestore": ("sdi", "up"),
    "SdiCloseAll": ("sdi", "cross"),
    "Monitor": ("monitors", None),      # 모니터 ▸
    "SingleMonitor": ("monitor", None),
    "MultiMonitor": ("monitors", "plus"),
    "MonitorItem": ("monitor", "right"),  # 모니터 라디오 목록 각 항목
    "MonitorNone": ("monitor", "cross"),
    "MonitorMaximize": ("monitor", "up"),
    "ResetMain": ("window", "reset"),   # 메인 창 위치 초기화
    "Overview": ("overview", None),
    "Close": ("close", None),
    "CloseOthers": ("tabbed", "cross"),
    "CloseAll": ("cascade", "cross"),
    "Flash": ("flash", None),
    "WinListMore": ("more", None),
    # 보기
    "FontReset": ("font", "reset"),     # 문서 글자 크기 초기화
    "ToggleTheme": ("contrast", None),
    "NextTheme": ("contrast", "right"),
    # 시스템
    "GpuRestart": ("chip", "reset"),    # 재시작 필요(GPU)
    "SysMonitor": ("pulse", None),      # 모니터링 ▸
    "AppMonitor": ("chip", None),       # 앱 자원
    "LogGroup": ("list", "clock"),      # 로그 ▸
    "LogFolder": ("folder", "search"),
    "LogExport": ("list", "up"),
    "Inquiry": ("chat", "plus"),
    "InquiryList": ("chat", "search"),
    "Update": ("cloud", "down"),
    "Changelog": ("doc", "clock"),
    "About": ("info", None),
}
