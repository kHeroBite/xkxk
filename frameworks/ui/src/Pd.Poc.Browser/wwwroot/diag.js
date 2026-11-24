// 진단 빌드 전용 브라우저 모듈 — URL 쿼리를 AutomationBridge 인자로 변환, window.pdAutomation 원격 브리지, 시험용 상태·콘솔 수집(window.__pdPoc)
// 제품 산출물에는 포함하지 않는다(Pd.Poc.Browser.csproj 가 PdDiagnostics=false 일 때 publish 에서 제외). main.js 가 진단 빌드에서만 import 한다.
// 쿼리 예: ?open=grid:5,gantt:5,form:10&layout=tile&sdi=2&save-layout&perf-out=x&exit-after=10
//   → ["--open","grid:5,gantt:5,form:10","--layout","tile","--sdi","2","--save-layout","--perf-out","x","--exit-after","10"]

/** 모든 쿼리 키를 --키 [값] 으로 바꾼다(값 없는 키는 플래그). */
function queryToArgs(search) {
    const args = [];
    for (const [key, value] of new URLSearchParams(search)) {
        if (!key) continue;
        args.push(key.startsWith('--') ? key : `--${key}`);
        if (value !== '') args.push(value);
    }
    return args;
}

/**
 * 진단 기능 설치. automation = exports.Pd.Poc.Browser.AutomationExports([JSExport] Invoke).
 * 반환: { args, markMainStarted } — main.js 가 runMain 인자로 args 를 쓴다.
 */
export function install({ automation, t0, runtimeReadyMs }) {
    const state = {
        args: queryToArgs(globalThis.location.search),
        t0,
        runtimeReadyMs,
        mainStartedMs: null,
        error: null,
        logs: [],
    };
    globalThis.__pdPoc = state;

    // 콘솔 출력(PERF_JSON:·[perf-ui]·[perf] frame gap 등)을 보관해 headless 시험이 회수할 수 있게 한다.
    const origLog = console.log.bind(console);
    console.log = (...items) => {
        try { state.logs.push(items.map(String).join(' ')); } catch { /* 수집 실패는 무시 */ }
        origLog(...items);
    };
    globalThis.addEventListener('error', (e) => { state.error = String(e?.error?.stack ?? e?.message ?? e); });

    // UI-08 원격 제어 브리지: 데스크톱 HTTP 서버와 같은 명령 JSON 을 JS 로 호출한다(진단 빌드 전용).
    globalThis.pdAutomation = {
        invoke: (req) => automation.Invoke(typeof req === 'string' ? req : JSON.stringify(req)),
    };

    return {
        args: state.args,
        markMainStarted: () => { state.mainStartedMs = Math.round(performance.now() - t0); },
    };
}
