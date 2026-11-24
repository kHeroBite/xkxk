# 모바일 PMS 앱 비교 조사

> 상태 조사. 조사일 2026-10-28. 채택 결과의 정본은 `06_모바일_화면설계.md` 이다.
> 모든 외부 사실은 WebSearch/WebFetch 로 확인한 출처를 붙였다. 공식 문서에서 확인하지 못한 내용은 "확인 못함" 또는 "추정"으로 표기했다.
> 신뢰 등급은 A(공식 도움말·릴리스노트), B(스토어·공식 블로그 요약), C(제3자·앱 미러·검색 요약)이다. C 등급은 단정하지 않는다.
> 주의: 모바일 앱 UI 는 버전마다 바뀐다. 아래 탭 구성은 조사 시점에 출처가 말하는 내용이며 실제 앱 실측은 아니다.

## 1. 앱별 조사 결과

### 1.1 Jira Cloud (Atlassian)
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 하단 내비게이션 | 구형 Android 문서는 Projects·Issues·Notifications·Account 로 설명한다. 이후 버전에서 Home 이 추가되고 계정 정보는 상단 아바타로 이동했다(릴리스 86.0). 현재 정확한 탭 구성은 확인 못함. | A [Android 릴리스노트](https://support.atlassian.com/jira-cloud-android/docs/whats-new-in-jira-cloud-for-android/), [검색 문서](https://support.atlassian.com/jira-cloud-android/docs/search-for-issues/) |
| 홈 | "Home 에서 최근 본 이슈·프로젝트·보드 등에 빠르게 접근"(86.0). | A 릴리스노트 |
| 보드·백로그·로드맵 | 프로젝트 안의 탭으로 board/backlog/roadmap 전환(40.0). 보드는 하단 탭이 아니라 프로젝트 내부에서 진입한다. | A 릴리스노트 |
| 이슈 목록·검색 | Issues 탭에서 검색창에 입력하면 프로젝트·유형·담당자·보고자 빠른 필터가 나타난다. Assigned to me, Reported by me 등 저장 필터 제공. | A [검색 문서](https://support.atlassian.com/jira-cloud-android/docs/search-for-issues/) |
| 알림 | 알림 탭, 알림 내 이슈 선로딩(31.0), 스와이프 제거(34.0). | A 릴리스노트 |
| 태블릿 | 태블릿 모드 개선(86.0), 태블릿에서 보드·백로그 필터 표시 수정(108.0). 분할(마스터-디테일) 구조 여부는 확인 못함. | A 릴리스노트 |
| 오프라인 | 릴리스노트에 오프라인 모드 설명 없음. 확인 못함. | A 릴리스노트(부재 확인) |
| 이슈 생성 버튼(FAB) | 확인 못함. | - |

### 1.2 Asana
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 하단 내비게이션 | 도움말은 Inbox 와 My tasks 를 하단 바 아이콘으로 안내한다. 즐겨찾기 프로젝트에도 하단 바로 접근한다. 전체 탭 목록은 확인 못함. | B [Asana 블로그(ko)](https://asana.com/ko/inside-asana/ios7.md), 검색 결과가 인용한 [도움말](https://help.asana.com/s/article/getting-started-with-asana-s-mobile-app)(본문 직접 열람 실패) |
| 빠른 생성 | "오른쪽 아래에 초록색 + 가 항상 표시"되어 어디서나 작업을 추가한다(구형 iOS 블로그 기준). | B Asana 블로그 |
| 홈 | 오늘 마감 작업과 중요한 작업을 보여주고, 탭하면 My Tasks 전체, 스와이프하면 Inbox 로 이동(구형 블로그). 최신 버전에서 Projects 와 Portfolios 가 Home 으로 합쳐졌다는 앱 미러 기록이 있으나 공식 확인 못함. | B 블로그, C [APKMirror 6.45.5](https://www.apkmirror.com/?p=1224532) |
| 보드·간트·캘린더 | 확인 못함. | - |
| 오프라인·태블릿 | 확인 못함. | - |

### 1.3 monday.com
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 하단 내비게이션 | 공식 지원 문서가 My Work 아이콘이 화면 하단에 있다고 하고, 보드·대시보드를 보는 중에는 아이콘이 숨겨져 Home·Notifications·More 로 돌아가라고 안내한다. 따라서 Home·Notifications·My Work·More 구성으로 추정된다(전체 목록은 확인 못함). | A [My Work 문서](https://support.monday.com/hc/en-us/articles/360019159959-Mobile-App-My-Work)(검색 요약 기준, 직접 열람은 403) |
| 워크스페이스 | 홈의 "최근 방문" 옆 화살표로 워크스페이스·즐겨찾기 보드에 접근한다. 별도 탭은 아니다. | A [워크스페이스 문서](https://support.monday.com/hc/en-us/articles/360020246179-Workspaces-on-the-mobile-app) |
| 그 외(간트·오프라인·태블릿) | 확인 못함. | - |

### 1.4 ClickUp
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 모바일 핵심 | 공식 모바일 개요는 Home·Inbox·Search 를 핵심 기능으로 언급한다. 배치 방식은 확인 못함. | A [Mobile app 2.0 overview](https://help.clickup.com/hc/en-us/articles/6311486610583-Mobile-app-2-0-overview)(검색 요약 기준) |
| 하단 바 | 제3자 가이드는 Home·Tasks·Inbox·Favorites·More 라고 쓰나 공식 문서와 다르다. 단정 금지. 하단 바에 Chat 아이콘 추가 요청이 완료 처리된 피드백 글이 있다. | C [consultevo](https://consultevo.com/clickup-mobile-app-guide-3/), [피드백](https://feedback.clickup.com/new-mobile-app/p/chat-icon-in-bottom-navigation-bar) |
| 오프라인 | 2026-01 업데이트에서 Chat/Home 과 My Tasks 오프라인 캐싱을 개선했다고 한다. 오프라인 작업 생성·Docs 는 기능 요청 단계로 부분 지원으로 추정. | B [피드백 게시판](https://feedback.clickup.com/new-mobile-app/p/enable-offline-task-search-and-creation-in-the-mobile-app) |
| 웹 내비게이션 | 데스크톱은 Global Navigation(좌측 세로 바), Planner·Chat 은 고정 가능 기능. | A [Intro to ClickUp 4.0](https://help.clickup.com/hc/en-us/articles/12157360089495) |

### 1.5 Notion
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 내비게이션 | 신형 릴리스 노트는 상단 탭 Home·Chats·Meetings·Inbox 라고 한다. 구형은 하단에 Search·Inbox·New page 였다는 기록이 있어 출처끼리 불일치. 버전별 변경으로 추정. | C [릴리스 노트 미러](https://matthiasfrank.de/en/notion-updates/new-tabbed-mobile-navigation/), C [도움말 미러](https://notion-proxy.senuto.com/nl-nl/help/workspaces-on-mobile) |
| 모바일 제약 | 공식 도움말: 모바일은 hover 상태가 없어 아이콘이 항상 보이고, 데스크톱의 다단 열 구성은 단일 열로 접힌다. 여러 블록 선택·데이터 가져오기는 데스크톱에서 해야 한다. | A [Notion for mobile](https://www.notion.com/en-gb/help/notion-for-mobile) |
| 데이터베이스 뷰·오프라인·태블릿 | 이 공식 페이지에서 확인 못함. | - |

### 1.6 Linear
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 앱 구성 | iOS·Android 모바일 앱. Home 탭의 My issues(할당·생성·구독), 즐겨찾기, 팀별 이슈·사이클·트리아지. Inbox 탭에서 댓글·이슈 상태 변경까지 가능, 스누즈·삭제 지원. 검색·설정·가운데 이슈 생성 탭이 있다는 검색 요약은 공식 원문 확인 못함. | B [모바일 소개 changelog](https://linear.app/changelog/2024-09-19-introducing-linear-mobile), [모바일 페이지](https://linear.app/mobile) |
| 빠른 생성 | "속도에 집착한 빠른 이슈 작성기", 공유한 스크린샷·사진에서 이슈 생성. | A [모바일 페이지](https://linear.app/mobile) |
| 툴바 | 2026-01 부터 하단 툴바를 사용자가 재배열하고 프로젝트·이니셔티브·문서를 고정할 수 있다고 한다. | B [Pulse on mobile](https://linear.app/changelog/2025-11-13-pulse-on-mobile) 및 검색 요약(날짜 원문 미확인) |
| 알림 | 푸시 알림과 알림 스케줄 설정. 오프라인은 확인 못함. | A 모바일 페이지 |

### 1.7 두레이(Dooray!, NHN Dooray)
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 구성 | 프로젝트(이슈 트래커, @멘션 담당 지정, 우선순위·상태·태그), 메신저(실시간 번역, 화상 통화), 메일, 캘린더, 드라이브, 위키, 결재(맞춤 양식·자동 결재선·부재 시 위임), 스트림(프로젝트·메일·캘린더·위키 활동 알림). | B [App Store Dooray! for Intune](https://apps.apple.com/kr/app/id6448622191) |
| 메신저 → 업무 | 글타래 대화와 첨부를 업무로 전환하는 기능(AI 구독 시 제목·담당·참조 자동 지정). | C [바이라인 2026-07](https://byline.network/2026/07/14-596/) 등 검색 요약 |
| 하단 탭·태블릿·오프라인 | 확인 못함. 일반 앱의 탭 구성은 스토어 설명에 없다. | - |

### 1.8 Trello (추가)
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 내비게이션 | iOS 하단 바에 Boards 와 Inbox 탭이 있고, Inbox·Planner 는 보드 하단 바에서만 열린다는 요청 글이 있다. 최신 리디자인은 Inbox·Planner·보드 전환 바를 소개한다. 모바일 세부는 확인 못함. | B [TRELLO-1722](https://jira.atlassian.com/browse/TRELLO-1722), [TRELLO-1594](https://jira.atlassian.com/browse/TRELLO-1594), [Trello 리디자인](https://support.atlassian.com/trello/docs/new-trello-support) |
| 오프라인 | 스토어 설명: 인터넷 없이도 보드를 갱신하고 복구되면 자동 동기화. | B [App Store](https://apps.apple.com/US/app/id461504587) |

### 1.9 Microsoft Planner / Teams (추가)
| 항목 | 확인 내용 | 출처·등급 |
|---|---|---|
| 모바일 | Teams 모바일의 Planner 는 기능이 제한되고 채널 탭에 붙은 플랜만 보인다. Planner 는 모바일 하단 앱 바에 고정되거나 … 메뉴에 들어간다. | A [Manage the Planner app](https://learn.microsoft.com/microsoftteams/manage-planner-app) |
| 개인 작업 | My Day(매일 초기화), My Tasks(개인 작업, 플래그 메일, 할당 작업). | A [Getting started with Planner in Teams](https://support.microsoft.com/en-us/office/use-planner-in-microsoft-teams-62798a9f-e8f7-4722-a700-27dd28a06ee0) |
| 결재 | 확인 못함. | - |

## 2. Mars 모바일 (코드 직접 확인)
출처는 Mars 저장소의 `Mars.Mobile/AppShell.xaml`·`Mars.Mobile/Views/*` 이며 2026-10-28 에 읽기 전용으로 열람했다(열람 커밋 미기록, 근거 수준 미확인). 줄 수 같은 값은 판이 바뀌면 달라진다. Mars 전체 구조 분석의 정본은 `../02_Mars분석/01_Mars_기능설계_분석.md` §7 이고 이 절은 모바일 화면 구조만 적는다.

| 항목 | 확인 내용 |
|---|---|
| 하단 5탭 | 홈(DashboardPage), 프로젝트(ProjectListPage), 작업(WorkListPage), 메신저(ChatListPage), 더보기(MorePage). 로그인은 별도 ShellItem. 플라이아웃 비활성. |
| 홈 | 진행프로젝트·금년프로젝트·오늘/어제/최근 활동 작업·온라인 인원·읽지 않은 메시지·최근 알림 카드와 그 아래 매출·ROI/KPI·팀원 분석·가용성/부하율 등 다수 차트(DashboardPage.xaml 약 1140줄). 모바일에서 정보 과밀 가능성은 추정이다. |
| 작업 | WorkListPage 에 "작업자·기간 조회/초기화", 목록과 상세가 같은 화면에서 "왼쪽 목록에서 작업을 선택하세요"·"‹ 목록" 버튼이 있어 마스터-디테일 구조를 이미 갖는다(태블릿 대응 추정). 상세에 수정·공유·티켓 보기. "작업 등록" 버튼 존재. |
| 더보기 | 조직도·설정이 push 진입(AppShell 주석). 알림(NotificationsPage), 공지, 지식, 주간계획·보고, 문의 등 페이지가 존재. |
| 오프라인 | `Services/OfflineService.cs`, `Data/Entities/CachedEntities.cs` 존재(캐시 구조 있음). 동작 범위는 확인 못함. |
| 푸시 | `PushNotificationService.cs`, FIREBASE.md 존재. |
| FAB·태블릿 분기 | FAB, DeviceIdiom 분기 코드는 grep 에서 발견 못함(범위 한정 확인). |
| 결재 | Mars 모바일의 결재 페이지는 Views 목록에서 발견 못함. |

## 3. 비교 요약표
(○ 확인, △ 부분·추정, ? 확인 못함)

| 앱 | 하단 탭 | 홈=내 작업 | 목록·상세·생성 | 보드/간트/캘린더 | 알림함 | 빠른 생성 | 오프라인 | 태블릿 | 메신저·댓글 | 결재 |
|---|---|---|---|---|---|---|---|---|---|---|
| Jira | Home·Issues·Projects·Notifications 계열(버전 변동) | ○ 최근 본 항목 | ○ 필터 검색 | ○ 보드·백로그·로드맵(프로젝트 내부) | ○ | ? | ? | △ | △ 댓글 | ? |
| Asana | Inbox·My tasks·Home 등(전체 ?) | ○ 오늘 작업 | ○ | ? | ○ Inbox | ○ 우하단 + | ? | ? | △ | ? |
| monday | Home·Notifications·My Work·More 추정 | ○ My Work | ? | ? | ○ | ? | ? | ? | △ | ? |
| ClickUp | Home·Inbox 등(불확실) | △ | ? | ? | ○ | ? | △ 부분 | ? | ○ Chat 요청 완료 | ? |
| Notion | 상단 탭 또는 하단(버전 불일치) | ○ Home | ? | ? | ○ Inbox | △ | ? | ? | △ | ? |
| Linear | Home·Inbox·검색 등 | ○ My issues | ○ | ? | ○ 스누즈 | ○ 이슈 작성기 | ? | ? | ○ 댓글 | ? |
| 두레이 | ? | ? | ○ 프로젝트 | ? | ○ 스트림 | ? | ? | ? | ○ 메신저 | ○ |
| Trello | Boards·Inbox | △ | ○ | ○ 보드 | △ | ? | ○ | ? | △ | ? |
| Planner/Teams | 하단 앱 바에 고정 또는 … | ○ My Day | ○ | △ | ? | ? | ? | ? | ○ Teams | ? |
| Mars | 홈·프로젝트·작업·메신저·더보기 | △ 대시보드 중심 | ○ 마스터-디테일 | ? | ○ 알림 | △ 작업 등록 버튼 | △ 캐시 | △ 추정 | ○ 메신저 | ? |

## 4. 시사점
1. 이슈 트래커 계열(Jira·Linear·Asana)은 "내 작업" 중심의 홈과 알림함(Inbox)을 하단 탭의 최상위로 둔다. 근거는 위 표의 Jira 홈, Linear Home/Inbox, Asana My tasks/Inbox 이다.
2. 보드·간트 같은 시각 뷰는 하단 탭이 아니라 프로젝트 내부 탭으로 두는 사례가 확인된다(Jira).
3. 빠른 생성은 우하단 + 버튼(Asana)이나 전용 작성기(Linear)로 항상 도달 가능하게 한다.
4. 두레이는 메신저→업무 전환과 결재를 한 앱에 묶어 Pd 와 같은 국내 그룹웨어형 요구와 가깝다.
5. Mars 홈은 지표 위주라 "내 작업·오늘 할 일" 진입점이 작업 탭에 분리된다. 이는 타 앱 대비 차이이며 실측 후 판단이 필요하다.

## 5. Pd 모바일 권장 구조 후보 (스마트폰/태블릿, Android/iOS)
채택 결과의 정본은 `06_모바일_화면설계.md` 다. 이 절은 조사 시점(2026-10-28)의 제안을 그대로 둔다. 조사 시점의 전제는 Mars 와의 연속성(5탭)과 푸시 인프라 구조의 개념 참조·새로 작성이다(D-92). 조사 뒤 바뀐 전제는 §6 에 있다.

| 안 | 하단 탭(스마트폰) | 태블릿 | 장점 | 단점·위험 | 근거 |
|---|---|---|---|---|---|
| A. Mars 5탭 유지·보강 (조사 시점 권장) | 홈(내 작업+요약 카드)·프로젝트·작업·메신저·더보기, 우하단 FAB 로 작업 등록, 알림은 홈 상단 벨 | 스마트폰 구조 동일, 작업·프로젝트 화면을 목록-상세 2열(Mars WorkListPage 방식 확장) | 기존 코드·사용자 학습 재사용, 변경량 최소, 오프라인 캐시 연속 | 알림함 전용 탭이 없어 알림이 묻힐 수 있음 | Mars 코드, Asana 우하단 +, Jira 보드는 프로젝트 내부 |
| B. 내 작업·Inbox 중심 재편 | 홈(내 작업)·Inbox(알림+멘션)·프로젝트·메신저·더보기, 중앙 생성 | 좌측 내비게이션 레일+2열/3열 | Linear·Asana 방식과 유사, 알림 가시성 높음 | 기존 작업 탭 이관 필요, 관리자용 대시보드 이동처 필요 | Linear Home/Inbox, Asana Inbox/My tasks |
| C. 그룹웨어형 | 홈·업무·메신저·결재·더보기 | 2열(목록-상세)+메신저 분할 | 결재·메신저 동선이 중요한 조직에 적합, 두레이 모델 | 결재 요구 범위 미확정(조사 시점), 개발량 큼 | 두레이 스토어 설명(결재·메신저→업무 전환) |

### 공통 권고 (표)
| 영역 | 권고 | 근거 |
|---|---|---|
| 뷰 | 목록 기본, 보드·간트·캘린더는 프로젝트 상세 내부 탭 | Jira 프로젝트 탭 |
| 생성 | 어디서나 접근 가능한 FAB(또는 홈/작업의 상단 버튼) | Asana, Linear |
| 알림 | 알림함에서 바로 댓글·상태 변경 가능, 읽음·스누즈 | Linear Inbox |
| 오프라인 | 목록·상세 조회 캐시 + 작성 큐(조사 시점 권고, 바뀐 것은 §6) | Trello 스토어 설명, ClickUp 부분 지원 사례 |
| 태블릿 | 600dp 이상에서 목록-상세 분할, 하단 탭은 레일 전환 검토 | Mars 마스터-디테일, Jira 태블릿 개선(구조 세부는 확인 못함) |
| 메신저 | 업무 상세에 댓글, 메신저에서 업무 생성 연계 | 두레이 |

## 6. 조사 뒤 바뀐 것
- 오프라인: 업무 데이터 오프라인 모드는 두지 않는다. 캐시는 온라인 속도용만 둔다(ADR-050·D-54). §5 의 오프라인 권고와 A안의 '오프라인 캐시 연속'은 이 결정으로 쓰지 않는다.
- UI 기술: Mars 의 MAUI Shell 이 아니라 데스크톱·웹·모바일 공용 Shell 이다(ADR-007).
- 모바일 결재: 기능 APR-04(모바일 승인)로 범위에 든다. 스마트폰 노출은 `06_모바일_화면설계.md` §5 가 가리키는 메뉴구조 생성물을 따른다.
