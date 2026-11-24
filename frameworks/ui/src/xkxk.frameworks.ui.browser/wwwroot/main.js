// .NET WASM 앱을 기동하는 브라우저 진입 스크립트
import './host_bridge.js'; // 전역 __pd* 브리지 등록(부수 효과) — dotnet 기동·다른 코드보다 먼저 평가된다
import { dotnet } from './_framework/dotnet.js';

// URL 로 받을 수 있는 사용자 기능 인자(허용 목록). 그 밖의 쿼리는 무시한다.
// fresh: 기동 시 레이아웃 자동 복원·종료 자동 저장을 하지 않는 깨끗한 시작(셸이 소비).
const PRODUCT_ARGS = new Set(['fresh']);

// .NET 이 뜨기 전 호스트 문구(host.*) — 사전은 host.i18n.json(셸 Localization/host.<언어>.json 과 같은 키·문구)
const SETTINGS_KEY = 'pd-poc-settings'; // 셸 설정 저장 키(Program.cs BrowserLayoutStore) — 저장된 Language 를 1순위로 쓴다
let hostDict = null;

/** 언어 결정(셸 UiSettings.LoadLanguageDto 와 같은 규칙): 저장된 Language 가 "en" 이면 영어, 그 밖(없음·null·"ko"·모르는 값·파싱 실패)은 한국어. */
function hostLanguage() {
    try {
        const s = JSON.parse(globalThis.localStorage.getItem(SETTINGS_KEY) || 'null');
        return String(s?.Language ?? s?.language ?? '').toLowerCase() === 'en' ? 'en' : 'ko';
    } catch {
        return 'ko';
    }
}

async function loadHostText() {
    try {
        const dicts = await (await fetch('./host.i18n.json')).json();
        const lang = hostLanguage();
        hostDict = dicts[lang];
        document.documentElement.lang = lang;
        for (const el of document.querySelectorAll('[data-i18n]')) {
            const text = hostDict[el.getAttribute('data-i18n')];
            if (text) el.textContent = text;
        }
    } catch (err) {
        console.warn('[pd] host text', err);
    }
}

const hostText = (key, fallback) => hostDict?.[key] ?? fallback;

// 값이 있는 사용자 기능 인자(허용 목록 → 허용 값). gpu: GPU 가속모드 이번 실행만(auto|off, 저장 안 함 — 셸 GpuSetting 이 해석)
const PRODUCT_VALUE_ARGS = new Map([['gpu', new Set(['auto', 'off'])]]);

/** 허용 목록에 있는 값 없는 플래그는 --키, 값 인자는 허용 값일 때만 --키 값 으로 바꾼다. */
function productArgs(search) {
    const args = [];
    for (const [key, value] of new URLSearchParams(search)) {
        if (PRODUCT_ARGS.has(key)) args.push(`--${key}`);
        else if (PRODUCT_VALUE_ARGS.get(key)?.has(String(value).toLowerCase())) args.push(`--${key}`, String(value).toLowerCase());
    }
    return args;
}

/** 선택 모듈: 어셈블리가 해당 export 를 제공할 때만 불러온다. 없으면 아무것도 하지 않는다. */
async function loadOptional(exportsApi, timing) {
    const automation = exportsApi?.xkxk?.frameworks?.ui?.browser?.AutomationExports;
    if (!automation?.Invoke) return null;
    try {
        const mod = await import('./diag.js');
        return mod.install({ automation, ...timing });
    } catch (err) {
        console.warn('[pd] 선택 모듈을 불러오지 못해 기본 모드로 계속합니다.', err);
        return null;
    }
}

/**
 * 페이지를 떠나거나(pagehide) 탭이 가려질 때(visibilityState==='hidden') 레이아웃을 즉시 저장한다.
 * 저장 여부는 C# LifecycleExports.FlushLayout 이 판정한다. export 가 없으면 아무것도 하지 않는다.
 */
function attachLayoutFlush(exportsApi) {
    const flush = () => {
        const fn = exportsApi?.xkxk?.frameworks?.ui?.browser?.LifecycleExports?.FlushLayout;
        if (!fn) return;
        try { fn(); } catch (err) { console.warn('[pd] 레이아웃 즉시 저장 실패', err); }
    };
    globalThis.addEventListener('pagehide', flush);
    const endAssist = () => {
        const fn = exportsApi?.xkxk?.frameworks?.ui?.browser?.LifecycleExports?.EndSnapAssist;
        if (!fn) return;
        try { fn(); } catch (err) { console.warn('[pd] Snap Assist 종료 실패', err); }
    };
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') { flush(); endAssist(); } });
}

// ── 단일 인스턴스(UI-17 프로그램 중복모드, 기본 끔): 같은 origin 의 PD 탭은 하나만 활성 ──
// Web Locks 배타 잠금. 보안 컨텍스트(HTTPS·localhost)에서만 있다 — 없으면 막지 않는다(기존 동작).
// 저장 키는 UiSettingsDto 속성명 AllowDuplicateInstance(리드 확정, 기본 false). 자동 시험은 이 설정을 true 로 넣어 끈다.
const INSTANCE_LOCK = 'pd-instance';
let exportsRef = null; // 잠금을 빼앗겼을 때 레이아웃을 저장하려고 보관

function allowDuplicate() {
    try {
        const s = JSON.parse(globalThis.localStorage.getItem(SETTINGS_KEY) || 'null');
        return s?.AllowDuplicateInstance === true;
    } catch {
        return false;
    }
}

/** 잠금을 탭이 살아 있는 동안 쥔다. 다른 탭이 steal 하면 hold 가 AbortError 로 끝나 onLost 를 부른다. */
function holdLock(options) {
    return new Promise((resolve) => {
        navigator.locks.request(INSTANCE_LOCK, options, (lock) => {
            resolve(Boolean(lock));
            return lock ? new Promise(() => {}) : undefined; // 끝나지 않는 약속 = 계속 보유
        }).catch((err) => {
            if (err?.name === 'AbortError') onInstanceLost();
            resolve(true); // 잠금 API 오류는 기동을 막지 않는다
        });
    });
}

/** 다른 탭이 '여기서 사용'으로 가져갔을 때: 레이아웃을 저장하고 이 탭을 덮는다(앱은 멈춘 상태로 둔다). */
function onInstanceLost() {
    try { exportsRef?.xkxk?.frameworks?.ui?.browser?.LifecycleExports?.FlushLayout?.(); } catch { /* 저장 실패는 무시 */ }
    showInstanceScreen(hostText('host.instance.moved', 'PD is now in use in another tab. This tab has stopped.'), null);
}

/** 화면 전체를 덮는 안내(+선택 버튼). 버튼을 누르면 onAction 을 부른다. */
function showInstanceScreen(message, action) {
    const box = document.createElement('div');
    box.id = 'pd-instance';
    box.setAttribute('role', 'alertdialog');
    box.style.cssText = 'position:fixed;inset:0;z-index:2147483647;display:flex;flex-direction:column;gap:16px;align-items:center;justify-content:center;background:#1e1e1e;color:#ddd;font:15px sans-serif;';
    const text = document.createElement('div');
    text.textContent = message;
    box.append(text);
    if (action) {
        const btn = document.createElement('button');
        btn.id = 'pd-instance-use-here';
        btn.textContent = action.label;
        btn.style.cssText = 'padding:8px 20px;font:14px sans-serif;cursor:pointer;';
        btn.addEventListener('click', action.onClick, { once: true });
        box.append(btn);
    }
    document.getElementById('pd-instance')?.remove();
    document.body.append(box);
    return box;
}

/** 잠금을 얻을 때까지 기다린다. 다른 탭이 쓰고 있으면 '여기서 사용'을 누를 때 빼앗아(steal) 진행한다. */
async function acquireInstance() {
    if (!globalThis.navigator?.locks || allowDuplicate()) return;
    if (await holdLock({ ifAvailable: true })) return;
    await hostTextReady;
    document.getElementById('pd-loading')?.remove();
    await new Promise((resolve) => {
        const box = showInstanceScreen(hostText('host.instance.busy', 'PD is already open in another tab.'), {
            label: hostText('host.instance.useHere', 'Use here'),
            onClick: async () => {
                await holdLock({ steal: true });
                box.remove();
                resolve();
            },
        });
    });
}

// ── 시작 구간 계측·리소스 캐시 설정(호스트 층 — 앱 UI 코드와 무관) ──
// 설정: localStorage 'pd-startup-options' = JSON {"timing":bool,"httpCache":"direct"|"revalidate","integrity":bool}. 없거나 깨지면 기본값.
//   timing    (기본 true)      시작 구간 표식을 window.__pdStartup 과 performance.mark('pd:<이름>') 에 남긴다. 표식 9개뿐이라 비용은 무시할 수준(추정).
//   httpCache (기본 'direct')  'direct' = .NET 설정 disableNoCacheFetch:true — 지문 붙은 _framework 파일을 서버 재검증 없이 HTTP 캐시에서 바로 쓴다.
//                              'revalidate' = .NET 기본(fetch cache:'no-cache', 매번 조건부 요청). 근거: dotnet.d.ts MonoConfig.disableNoCacheFetch,
//                              https://github.com/dotnet/runtime/blob/main/src/mono/wasm/features.md (no-cache fetch). 지문 없는 파일은 서버가 Cache-Control:no-cache 를
//                              보내므로 'direct' 에서도 재검증된다(serve_browser.py). 다른 서버에 올릴 때도 이 헤더 규칙이 전제다.
//   integrity (기본 true)      false = .NET 설정 disableIntegrityCheck:true — 리소스 SHA 무결성 검사 생략. 시험 비교용이며 제품 기본값은 켬.
// 끄는 법: localStorage.setItem('pd-startup-options', '{"timing":false,"httpCache":"revalidate"}') 후 새로고침.
const STARTUP_OPTIONS_KEY = 'pd-startup-options';

function startupOptions() {
    const opts = { timing: true, httpCache: 'direct', integrity: true };
    try {
        const s = JSON.parse(globalThis.localStorage.getItem(STARTUP_OPTIONS_KEY) || 'null');
        if (s && typeof s === 'object') {
            if (typeof s.timing === 'boolean') opts.timing = s.timing;
            if (s.httpCache === 'direct' || s.httpCache === 'revalidate') opts.httpCache = s.httpCache;
            if (typeof s.integrity === 'boolean') opts.integrity = s.integrity;
        }
    } catch { /* 기본값 */ }
    return opts;
}

const startupOpts = startupOptions();
// 측정 도구가 읽는 전역 window.__pdStartup: done 이 true 가 되면 marks·measures·resources 가 채워져 있다(timing 이 꺼져 있으면 done 은 false 로 남는다).
// marks 값은 performance.now() 기준 ms(탐색 시작 = 0). 완료 시 window 에 'pd-startup' 이벤트(detail = 이 객체)를 보낸다.
const startup = { version: 1, options: startupOpts, marks: {}, done: false, firstFrameSource: null, measures: null, resources: null };
globalThis.__pdStartup = startup;

const STARTUP_SPANS = [ // [이름, 시작 표식, 끝 표식]
    ['page_to_module', null, 'module-start'], // 탐색 시작 → main.js 실행(HTML·dotnet.js 모듈 로드)
    ['before_create', 'module-start', 'create-start'], // 단일 인스턴스 잠금 등
    ['runtime_create', 'create-start', 'create-end'], // 리소스 다운로드·wasm 컴파일·런타임 초기화
    ['exports', 'create-end', 'main-start'], // 어셈블리 export 조회·선택 모듈
    ['main_dispatch', 'main-start', 'main-entered'], // runMain → C# Main 진입
    ['avalonia_setup', 'main-entered', 'app-started'], // 호스트 설정·Avalonia 초기화·MainView 구성
    ['first_frame', 'app-started', 'first-frame'], // 첫 프레임 반영
    ['total', null, 'first-frame'], // 탐색 시작 → 첫 프레임
];

function startupResources() {
    const list = performance.getEntriesByType('resource').filter((e) => e.name.includes('/_framework/'));
    const via = (e) => (e.transferSize === 0 ? 'cache' : e.transferSize < 1024 && e.encodedBodySize > 1024 ? 'revalidated' : 'network');
    const pick = (e) => ({ name: e.name.split('/').pop(), via: via(e), transferSize: e.transferSize, encodedBodySize: e.encodedBodySize,
        decodedBodySize: e.decodedBodySize, start: Math.round(e.startTime), duration: Math.round(e.duration) });
    const count = { cache: 0, revalidated: 0, network: 0 };
    let transfer = 0;
    for (const e of list) { count[via(e)]++; transfer += e.transferSize; }
    return { total: list.length, ...count, transferBytes: transfer, nativeWasm: list.filter((e) => /dotnet\.native\.[^/]*\.wasm$/.test(e.name)).map(pick),
        navigationType: performance.getEntriesByType('navigation')[0]?.type ?? null };
}

function onFirstFrame(source) {
    document.getElementById('pd-loading')?.remove(); // 체감용: 런타임 준비가 아니라 첫 프레임까지 로딩 문구를 둔다
    if (startup.done || !startupOpts.timing) return;
    startup.firstFrameSource = source;
    const m = startup.marks;
    startup.measures = {};
    for (const [name, from, to] of STARTUP_SPANS) {
        const end = m[to];
        const begin = from === null ? 0 : m[from];
        startup.measures[name] = end === undefined || begin === undefined ? null : Math.round((end - begin) * 10) / 10;
        try { if (end !== undefined && begin !== undefined) performance.measure(`pd:${name}`, { start: begin, end }); } catch { /* 표시 실패 무시 */ }
    }
    try { startup.resources = startupResources(); } catch { startup.resources = null; }
    startup.done = true;
    try { globalThis.dispatchEvent(new CustomEvent('pd-startup', { detail: startup })); } catch { /* 무시 */ }
}

/** 시작 표식. C# StartupTiming 이 JSImport(globalThis.__pdStartupMark)로도 부른다. first-frame(·fallback)은 기록이 꺼져 있어도 로딩 문구를 걷는다. */
function startupMark(name) {
    const now = performance.now();
    if (name === 'first-frame-fallback') { onFirstFrame('fallback'); return; }
    if (startupOpts.timing && !(name in startup.marks)) {
        startup.marks[name] = Math.round(now * 10) / 10;
        try { performance.mark(`pd:${name}`, { startTime: now }); } catch { /* 무시 */ }
    }
    if (name === 'first-frame') onFirstFrame('frame');
}
globalThis.__pdStartupMark = startupMark;

/** .NET 런타임 빌더. 설정이 기본(.NET 동작)과 다를 때만 withConfig 를 건다. */
function startupBuilder() {
    const builder = dotnet.withDiagnosticTracing(false);
    const config = {};
    if (startupOpts.httpCache === 'direct') config.disableNoCacheFetch = true;
    if (!startupOpts.integrity) config.disableIntegrityCheck = true;
    return Object.keys(config).length ? builder.withConfig(config) : builder;
}

const t0 = performance.now();
startupMark('module-start');
const hostTextReady = loadHostText(); // 런타임 다운로드와 병렬
try {
    await acquireInstance(); // 다른 탭이 쓰고 있으면 사용자가 '여기서 사용'을 누를 때까지 런타임을 띄우지 않는다
    startupMark('create-start');
    const runtime = await startupBuilder().create();
    startupMark('create-end');
    const runtimeReadyMs = Math.round(performance.now() - t0);
    const config = runtime.getConfig();
    const exportsApi = await runtime.getAssemblyExports(config.mainAssemblyName);
    exportsRef = exportsApi;

    const optional = await loadOptional(exportsApi, { t0, runtimeReadyMs });
    const args = optional ? optional.args : productArgs(globalThis.location.search);
    optional?.markMainStarted();
    attachLayoutFlush(exportsApi);
    startupMark('main-start');
    await runtime.runMain(config.mainAssemblyName, args);
    startupMark('main-returned');
    // 첫 프레임 표식이 오지 않는 경우(C# 쪽 실패 등)에도 로딩 문구가 남지 않게 한다
    setTimeout(() => { if (!startup.done) document.getElementById('pd-loading')?.remove(); }, 5000);
    globalThis.__pdFocusAppHost(); // 페이지를 누르기 전에도 단축키가 앱에 오게 한다
} catch (err) {
    console.error('[pd] start failed', err);
    await hostTextReady;
    const loading = document.getElementById('pd-loading');
    if (loading) loading.textContent = hostText('host.startFailed', 'The app could not start. Please reload the page.');
}
