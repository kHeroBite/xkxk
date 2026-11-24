// 브라우저 호스트 전역 브리지(__pd*) 등록 — C# JSImport 와 입력 보정이 쓰며 dotnet 기동 전에 평가돼야 하는 부수 효과 모듈
// BrowserLayoutStore 가 JSImport 로 부르는 localStorage 래퍼
globalThis.__pdStorage = {
    get: (key) => globalThis.localStorage.getItem(key),
    set: (key, value) => globalThis.localStorage.setItem(key, value),
    remove: (key) => globalThis.localStorage.removeItem(key), // BrowserKeyValueStore(개인 레이아웃 삭제, UI-11)
};

// 문서 초점이 BODY 면 키가 앱에 오지 않아 앱 컨테이너로 옮긴다. 기동 직후·누를 때마다·Flyout 열 때(PageKeyFocus) 부른다. 입력 요소가 아니라 모바일 키보드는 뜨지 않고, 입력 칸 등 이미 다른 곳에 초점이 있으면 건드리지 않는다
globalThis.__pdFocusAppHost = () => {
    if (document.activeElement === document.body) document.querySelector('.avalonia-container')?.focus({ preventScroll: true });
};

// 실제 렌더 백엔드 판정(웹): Avalonia 웹은 WebGL 로 그려도 IPlatformGraphics 를 등록하지 않아 C# 형식명으로는 구분이 안 된다.
// 앱이 이미 그린 뒤(컨텍스트가 만들어진 뒤)에만 부른다 — 만들어지기 전 getContext 는 새 컨텍스트를 만들어 렌더러를 바꿔 버린다.
// 이미 다른 종류 컨텍스트가 있는 캔버스에 getContext(다른 종류)는 null 을 돌려주므로 순서대로 물어 판정한다. 반환 webgl2|webgl1|2d|none.
globalThis.__pdRenderer = () => {
    const c = document.querySelector('canvas.avalonia-canvas');
    if (!c || !(c.width > 0)) return 'none';
    if (c.getContext('webgl2')) return 'webgl2';
    if (c.getContext('webgl')) return 'webgl1';
    if (c.getContext('2d')) return '2d';
    return 'none';
};

// OS '애니메이션 줄이기'(prefers-reduced-motion: reduce) — BrowserMotionPreference 가 JSImport 로 읽고 변경을 구독한다. matchMedia 미지원·예외면 false(웹 시험은 CDP Emulation.setEmulatedMedia 로 바꾼다)
globalThis.__pdReducedMotion = () => {
    try { return !!(globalThis.matchMedia && globalThis.matchMedia('(prefers-reduced-motion: reduce)').matches); } catch { return false; }
};
globalThis.__pdReducedMotionWatch = (handler) => {
    try {
        const mql = globalThis.matchMedia && globalThis.matchMedia('(prefers-reduced-motion: reduce)');
        if (mql && mql.addEventListener) mql.addEventListener('change', () => handler(globalThis.__pdReducedMotion()));
    } catch { /* 구독 불가면 시작 때 값만 쓴다 */ }
};

// 순수 Ctrl+휠 / Ctrl+0·+·- 는 앱의 글자 크기·창 확대(UiSettings)가 처리한다. 브라우저 페이지 확대를 막기 위해
// 기본 동작만 취소하고(preventDefault) 전파는 막지 않아 Avalonia 캔버스가 그대로 이벤트를 받는다.
// Shift·Alt 조합(간트 Alt+휠 줌 등)은 건드리지 않는다. wheel 은 passive:false 여야 취소할 수 있다.
const pureCtrl = (e) => (e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey;
globalThis.addEventListener('wheel', (e) => { if (pureCtrl(e)) e.preventDefault(); }, { passive: false, capture: true });
globalThis.addEventListener('keydown', (e) => {
    if (pureCtrl(e) && ['0', '+', '=', '-', '_'].includes(e.key)) e.preventDefault();
}, { capture: true });

// 누름 원래 시각(BrowserPressTimeSource 가 읽음): Avalonia 는 누름 시각을 처리 시점에 찍어 주 스레드 지연이 더블클릭 간격에 더해진다.
// PointerEvent.timeStamp(이벤트가 만들어진 시각)를 Avalonia 처리기보다 먼저(window 캡처 단계) 써 두고, 이 이벤트 처리가 끝나면(setTimeout 0) 지운다.
// 단일 스레드 WASM 은 Avalonia 처리기 안에서 .NET 누름 처리가 동기로 끝나므로, 처리 중 읽은 값은 같은 누름의 시각이다. 기본 동작·전파는 건드리지 않는다.
globalThis.__pdPress = { t: -1 };
globalThis.addEventListener('pointerdown', (e) => {
    const t = e.timeStamp;
    globalThis.__pdPress.t = t;
    setTimeout(() => { if (globalThis.__pdPress.t === t) globalThis.__pdPress.t = -1; }, 0);
    setTimeout(globalThis.__pdFocusAppHost, 0); // 앱이 누름을 처리한 뒤(입력 칸을 눌렀으면 그쪽 초점이 먼저다). 초점 받지 않는 빈 곳을 눌러도 키가 오게 한다
}, { capture: true });
