# PC 비움 전수 시험 계약 판정기(pcaway_gate) — `pcaway_gate.py item <ID>` : 0=PASS 1=FAIL 2=판정불가. 새 홈(frameworks/ui) 경로로 옮긴 사본
# 출처: Pd worktree pd-18 `.claude/archive/test/2026-11/20261111-1429_conv_179435695241_pcaway_gate_before_lowload_removal.py`(sha256 3f259d45455a0f47e2a3fdf8b7e8f1e45c18ffe8b985e91bbf51a920bb33bb0d).
#   남은 보관본 중 가장 최근 판이며 최종판이 아니다(그 뒤 '저부하 조건 제거' 등 변경분은 보관되지 않아 없다). 2026-11-24 경로(poc/ui→frameworks/ui)·프로젝트 이름(Pd.*→xkxk.frameworks.ui.*)·ROOT 폴백만 기계 치환했고 판정 로직은 그대로다.
# PC 비움 전수 시험·2차 묶음·이월 결함 계약 판정기 — `pcaway_gate.py item <ID>` : 0=PASS 1=FAIL 2=판정불가. 저장소·시험 결과(frameworks/ui/logs/pcaway/<ID>.json)·최종 빌드 목록만 읽는다
# 결과 파일 존재만으로 통과시키지 않는다: writer=otest*, 계약 시각 이후 실행, 스크립트·빌드 sha 일치, 필수 측정 이름 전부 pass=true, 캡처 판독 기록, 안전 필드를 모두 요구한다.
import hashlib, json, os, re, subprocess, sys
from datetime import datetime
SESSION = os.environ.get("PCAWAY_SESSION") or os.path.dirname(os.path.abspath(__file__))
def _root():
    if os.environ.get("PCAWAY_ROOT"): return os.environ["PCAWAY_ROOT"]
    try:
        v = open(os.path.join(SESSION, "ui28_root.txt"), encoding="utf-8").read().strip()
        if v: return v
    except OSError: pass
    return os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", ".."))  # 저장소 루트(옛 Pd worktree 절대경로 폴백 대체)
ROOT = _root()
RUN_TIMEOUT = 60
CYCLE_START = "2026-11-05T07:00:00+09:00"  # 이 사이클 지시 시각. 이전 실행 결과는 인정하지 않는다
R2_START = "2026-11-07T00:00:00+09:00"  # 2차 묶음(P·Q·T·N) 계약 시각. 새 항목은 이 시각 이전 실행 결과를 인정하지 않는다
UI, SH, VW, VM, DK, AN, MS, JD = ("frameworks/ui/src/xkxk.frameworks.ui/", "frameworks/ui/src/xkxk.frameworks.ui.shell/", "frameworks/ui/src/xkxk.frameworks.ui.views/", "frameworks/ui/src/xkxk.frameworks.ui.shell/ViewModels/",
    "frameworks/ui/src/xkxk.frameworks.ui.shell/Docking/", "frameworks/ui/src/xkxk.frameworks.ui.android/", "frameworks/ui/test/desktop/measure/", "frameworks/ui/test/desktop/judge/")
PNG = b"\x89PNG\r\n\x1a\n"
def R(req, roles=("desktop_work",), caps=0, safety=True, human=False, files=(), has=(), lacks=(), absent=(), run=None, static=False, expect=None, themes=False, start=None):
    return {"start": start, "req": list(req), "roles": list(roles), "caps": caps, "safety": safety, "human": human, "files": list(files), "has": list(has),
            "lacks": list(lacks), "absent": list(absent), "run": run, "static": static, "expect": dict(expect or {}), "themes": themes}
GUARD, TOPO = MS + "user_away_guard.py", MS + "display_topology.ps1"
THEME_SRC = SH + "Theming/BuiltinThemes.cs"
TITLEBAR_TOOL, STATES_TOOL, STATE_RULES, WHEEL_TOOL = JD + "main_titlebar_contrast.py", JD + "chrome_button_states.py", JD + "chrome_state_rules.py", JD + "dense_wheel_check.py"
TABLET_TOOL = "frameworks/ui/test/mobile/judge/tablet_first_layout_judge.py"
REPLAY_JUDGE, REPLAY_MEASURE = "frameworks/ui/test/mobile/judge/replay_target_judge.py", "frameworks/ui/test/mobile/measure/retest_android_pcaway_replay.py"
REPLAY_RULES = {"replay_target_rc": ("==", 0), "replay_sent_open": (">=", 20), "replay_sent_closed": (">=", 20), "replay_bad": ("==", 0), "replay_fatal": ("==", 0)}
REPLAY_PREFIX_SHA = {"309ba30eae9ead697b9d8c6e82cf26b633866664765db9656ab703d822481cbc", "0aa51f789d2f4a32e368bddbd5270c0b238e409d4862253e339642bf959607bc"}  # 수정 전 android_prod·android_release(plans/pcaway_final_build_before_touchfix.json)
def ATLEAST(*names, v): return {n: (">=", v) for n in names}
# K 묶음의 측정 이름은 도구 출력 키를 따른다. 테마별 값은 전 테마 중 가장 낮은 값을 적는다. 기준 수치는 임시 기준(plan/09_화면설계/01 §4.2)이다
WHEEL_GANTT = ["gantt_rows_grow", "gantt_rows_anchor", "gantt_rows_keep_dayWidth", "gantt_rows_keep_offsetX", "gantt_rows_max", "gantt_rows_min", "gantt_rows_delta_x", "gantt_rows_back_to_0", "gantt_alt_width", "gantt_alt_keep_row", "gantt_shift_hscroll", "gantt_plain_vscroll", "gantt_ctrl_handled", "gantt_ctrl_reset"]
WHEEL_GRID = ["grid_rows_grow", "grid_rows_anchor", "grid_rows_keep_columnSum", "grid_rows_max", "grid_rows_min", "grid_rows_delta_x", "grid_rows_back_to_0", "grid_alt_width", "grid_alt_keep_row", "grid_shift_hscroll", "grid_ctrl_handled", "grid_ctrl_reset"]
WHEEL_LOG = ["log_rows_grow", "log_alt_swallowed", "log_alt_no_change", "log_ctrl_local_font"]
WHEEL_REAL = ["real_wheel_%s_%s" % (t, m) for t in ("gantt", "grid") for m in ("ctrl", "alt", "shift", "alt_shift")]
GRID_TOOL_NAMES = ["grid_first_row_height", "grid_lang_en_not_narrower", "grid_lang_back", "grid_theme_keep_row_pd-light", "grid_theme_keep_row_pd-dark", "grid_theme_switched"]
STATE_ON_BARS = ["rest_hover", "rest_on", "on_hover_fill", "on_onhover_border", "glyph_on_hover", "glyph_on_on"]
# W 웹 속도. 기준 25ms·500ms 는 a9 잠정 기준(plan/04_기술스택/04_판정보고서_부록/a9 §PERF-02·창 열기) 그대로다. 완화 금지
WEBPERF_START = "2026-11-06T01:04:17+09:00"  # 웹 속도 계약 시각. 이전 실행 결과는 W3·W4 에서 인정하지 않는다
BROWSER_DIR = "frameworks/ui/src/xkxk.frameworks.ui.browser/"
BROWSER_CSPROJ, BUILD_BROWSER = BROWSER_DIR + "xkxk.frameworks.ui.browser.csproj", "frameworks/ui/tools/build_browser.sh"
AOT_MARKS = (b"mono_aot_aot_instances_get_method", b"mono_aot_Pd_Ui_Views_get_method", b"mono_aot_Pd_Poc_Shell_get_method")  # AOT 판 dotnet.native.wasm 에만 있는 ASCII 심볼(webperf_aot_prod 실측, base_diag 에는 없음)
W3_RULES = {"br_wasm_content_encoding": ("==", "br"), "br_js_content_encoding": ("==", "br"), "br_body_is_precompressed": ("==", True),
            "identity_without_accept_br": ("==", True), "vary_accept_encoding": ("==", True),
            "immutable_max_age_s": (">=", 31536000), "immutable_directive": ("==", True), "index_html_revalidate": ("==", True)}
W4_RULES = {"real_chrome_headed": ("==", True), "product_build_no_diag": ("==", True),
            "drag_frame_p95_ms_doc1": ("<=", 25), "drag_frame_p95_ms_doc3": ("<=", 25), "drag_frame_p95_ms_doc5": ("<=", 25), "drag_runs_per_doc": (">=", 3),
            "open_grid_p95_ms": ("<=", 500), "open_form_p95_ms": ("<=", 500), "open_gantt_p95_ms": ("<=", 500), "open_repeats": (">=", 10)}
# R2 2차 묶음(P·Q·T·N). 계약 시각은 R2_START. 불리언 측정은 값이 정확히 True 여야 한다(expect). 결과 파일 부재는 항상 FAIL(rc=1)이다
def ALL(names, op, v): return {n: (op, v) for n in names}
def TRUE(*names): return {n: ("==", True) for n in names}
R2_FILES = {"fonts_csproj": "frameworks/ui/src/xkxk.frameworks.ui.fonts/xkxk.frameworks.ui.fonts.csproj", "dto": SH + "Theming/UiSettingsDto.cs", "inv": VM + "Settings/SettingsInventory.cs", "win": VW + "Settings/WindowPage.axaml",
            "ko": SH + "Localization/shell.ko.json", "en": SH + "Localization/shell.en.json", "main": VW + "MainView.axaml", "tour": SH + "Tour/tour_catalog.json"}
R2_DOCS = {"d01": "plan/09_화면설계/01_화면설계_공통원칙.md", "d03": "plan/09_화면설계/03_데스크톱_화면설계.md", "d04": "plan/09_화면설계/04_웹_화면설계.md", "d06": "plan/09_화면설계/06_모바일_화면설계.md"}
P1_OPEN = ["fold5_open_doc_p95_ms_open", "fold5_open_doc_p95_ms_closed", "fold5_open_grid_p95_ms", "fold5_open_form_p95_ms", "fold5_open_gantt_p95_ms"]
P1_DRAG = ["fold5_drag_frame_p95_ms_doc1", "fold5_drag_frame_p95_ms_doc3", "fold5_drag_frame_p95_ms_doc5"]
Q_NAMES = ["pc_default_on_drop_visible", "pc_default_on_pin_visible", "tablet_default_off_drop_hidden", "tablet_default_off_pin_hidden", "tablet_off_pin_refused", "mode_on_in_tablet_enables",
           "mode_off_in_pc_disables", "off_unpins_pinned_panel", "reorder_same_tabs_kept", "setting_persisted_restart"]
T1_NAMES = ["order_ui_then_theme_then_min", "theme_min_gap_le_8", "ui_icons_are_paths", "ui_icons_tooltips", "theme_toggle_changes_theme", "theme_glyph_switches", "toolbar_row_absent", "menu_reflow_ok_narrow"]
T3_BOOLS = ["menu_bar_32_unchanged", "tab_text_not_clipped", "sdi_icon_fits_strip"]
T4_NAMES = ["theme_icon_rightmost", "ui_icons_present", "toolbar_row_absent", "strip_ratio", "tab_ratio", "theme_toggle_changes_theme"]
# T3·T4 제목 줄·제목 탭 높이(2026-11-11 사용자 지시, 공통원칙 §2-1): 옛 비율 0.62~0.71 을 '사양과 일치'로 대체한다.
# 사양 = 줄 ShellTabStripHeight(기본 38, 19~48), 탭 round(줄 × 16/19)(기본 32). strip_ratio·tab_ratio 는 도구가 |실측 − 사양| ≤ 0.5 DIP 로 낸 판정(불리언)을 그대로 본다(범위 완화 없음)
STRIP_SPEC_H, TAB_SPEC_H = (19, 48), (16, 40)  # 기대 높이(설정값 유래)의 허용 범위 = 설정 범위 19~48 과 그 탭 높이
T4_EXPECT = dict(TRUE("theme_icon_rightmost", "ui_icons_present", "toolbar_row_absent", "theme_toggle_changes_theme", "strip_ratio", "tab_ratio"))
P5_NAMES = ["fonts_sha_build1", "fonts_sha_build2", "fonts_name_build1", "fonts_name_build2", "built_at_1", "built_at_2"]
R2_ITEMS = {
 "P1": R(P1_OPEN + P1_DRAG + ["fold5_runs_low_load", "frame_source_chrome_trace"], roles=("web_prod",), caps=2,
         expect=dict(ALL(P1_OPEN, "<=", 500), **ALL(P1_DRAG, "<=", 25), fold5_runs_low_load=(">=", 10), frame_source_chrome_trace=("==", True)), start=R2_START),
 "P2": R(["fold5_tab_body_p95_ms_on", "tab_body_on_le_off", "tab_order_crossed", "tab_runs_each"], roles=("web_prod",),
         expect=dict(fold5_tab_body_p95_ms_on=("<=", 500), tab_body_on_le_off=("==", True), tab_order_crossed=("==", True), tab_runs_each=(">=", 10)), start=R2_START),
 "P3": R(["closed_menu_median_ms", "closed_tab_median_ms", "closed_low_load_only"], roles=("web_prod",),
         expect=dict(closed_menu_median_ms=("<=", 118), closed_tab_median_ms=("<=", 86), closed_low_load_only=("==", True)), start=R2_START),
 "P4": R(["splash_loss_classified_all", "loss_app_unexpected", "loss_before_window_reported", "touch_after_first_frame"], roles=("android_prod",),
         expect=dict(splash_loss_classified_all=("==", True), loss_app_unexpected=("==", 0), loss_before_window_reported=("==", True), touch_after_first_frame=("==", True)), start=R2_START),
 "P5": R(P5_NAMES, roles=("web_prod",), safety=False, static=True, files=[R2_FILES["fonts_csproj"]], start=R2_START),
 "Q1": R([], safety=False, static=True, files=[R2_FILES["dto"], R2_FILES["inv"], R2_FILES["win"], R2_FILES["ko"], R2_FILES["en"]],
         has=[(R2_FILES["dto"], "PanelFeature"), (R2_FILES["inv"], '"panelFeature"'), (R2_FILES["win"], "settings.cmbPanelFeature"), (R2_FILES["ko"], "settings.panelFeature"), (R2_FILES["en"], "settings.panelFeature")]),
 "Q2": R(Q_NAMES + ["real_drag_no_indicator"], roles=("desktop_work",), caps=2, expect=TRUE(*Q_NAMES, "real_drag_no_indicator"), start=R2_START),
 "Q3": R(Q_NAMES + ["real_drag_no_indicator"], roles=("web_diag",), caps=2, expect=TRUE(*Q_NAMES, "real_drag_no_indicator"), start=R2_START),
 "Q4": R(list(Q_NAMES), roles=("android_debug",), caps=2, expect=TRUE(*Q_NAMES), start=R2_START),
 "T1": R(list(T1_NAMES), roles=("desktop_work",), caps=2, expect=TRUE(*T1_NAMES), start=R2_START),
 "T2": R([], safety=False, static=True, files=[R2_FILES["main"], R2_FILES["tour"]]),
 "T3": R(["strip_height_before", "tab_height_before", "strip_ratio", "tab_ratio"] + T3_BOOLS, roles=("desktop_work",), caps=2, lacks=[(R2_FILES["main"], r'Height="28"')],
         expect=dict(TRUE(*T3_BOOLS, "strip_ratio", "tab_ratio"), strip_height_before=("range", STRIP_SPEC_H), tab_height_before=("range", TAB_SPEC_H)), start=R2_START),
 "T4": R([], safety=False, static=True),
 "T4_web": R(list(T4_NAMES), roles=("web_prod",), expect=T4_EXPECT, start=R2_START),
 "T4_android": R(list(T4_NAMES), roles=("android_debug",), expect=T4_EXPECT, start=R2_START),
 "N1": R([], safety=False, static=True, files=list(R2_DOCS.values())),
}
W4_COMPARE_OPS = ("open_grid", "open_form", "open_gantt", "drag_doc1", "drag_doc3", "drag_doc5")
WEB_UI_PROJECTS = ("frameworks/ui/src/xkxk.frameworks.ui.shell/", "frameworks/ui/src/xkxk.frameworks.ui/", "frameworks/ui/src/xkxk.frameworks.ui.views/")  # 데스크톱·Android·웹 공용 프로젝트(호스트 프로젝트 제외)
SPEC = {
 # 안전
 "S1": R([], safety=False, static=True, files=[GUARD, MS + "safe_key.ps1"], has=[(GUARD, "GetLastInputInfo"), (MS + "safe_key.ps1", "GetForegroundWindow")], run=[GUARD, "--self-test"]),
 "S2": R(["user_pd_alive_end", "user_layout_md5_same", "user_settings_md5_same", "no_signal_to_user_pd", "own_pids_cleaned", "disk_free_ge_15gb", "test_window_monitor_rule"], static=True),
 # A 데스크톱 실입력
 "A01": R(["maximize_each_monitor", "straddle_60_40", "straddle_40_60", "straddle_50_50", "restore_bounds", "dpi_match", "win32_control_match"], caps=3),
 "A02": R(["mdi_max_doc_to_sdi_monitor", "new_doc_sdi_monitor"], caps=1),
 "A03": R(["popup_main_title", "popup_sdi_title", "popup_mdi_title", "popup_title_tab", "nc_rbutton_pd_menu_only"], caps=4),
 "A04": R(["minimized_help_restores", "tray_hidden_help_restores", "window_visible_observed"], caps=1),
 "A05": R(["f1_main", "f1_mdi", "f1_sdi", "esc_ends_tour", "foreground_gate_all_passed"], caps=2),
 "A06": R(["product_launch_alive", "log_allow_duplicate_true", "mutex_not_taken", "diag_remote_absent", "first_frame_rendered", "clean_exit"], roles=("desktop_product",), caps=1),
 "A07": R(["hold4", "hold5", "hold6", "hold7"], caps=1),
 "A08": R(["real_rclick_main", "real_rclick_sdi", "real_rclick_mdi", "real_rclick_tab", "alt_space_menu", "caption_min", "caption_max", "caption_close"], caps=3),
 "A09": R(["real_drag_explorer", "real_drag_log", "real_drag_notify", "dock_guide_visible", "splitter_drag", "panel_kept_after_drop"], caps=3),
 "A10": R(["sdi_not_pushed_by_panel", "spinner_hidden_outside_window", "tab_drag_to_other_window", "panel_only_window_minimize", "panel_only_window_maximize", "panel_only_window_menu"], caps=3),
 "A11": R(["h21_cursor_is_spinner_on_busy", "h21_cursor_restored", "h22_overlay_size_opacity"], caps=2, human=True),
 "A12": R(["h31_button_real_click_expand", "h31_button_real_click_restore", "h41_redock_same_position_real"], caps=2, human=True),
 "A13": R(["h61_frames_advance", "h61_step_text_readable_capture", "motion_off_no_transition"], caps=3, human=True),
 # B 성능
 "B1": R(["ops_all_measured_1doc", "ops_all_measured_20doc", "repeat_ge_10", "on_screen_true", "load_recorded", "doc_a9_table_updated"]),
 "B2": R(["immediate_le_100ms", "open_le_500ms", "theme_font_20win_le_300ms", "frame_p95_le_25ms"]),
 "B3": R(["perf01_first_frame", "perf02_move", "perf03_grid", "perf04_gantt", "perf05_web_chrome"], roles=("desktop_work", "web_diag")),
 "B4": R(["plan_rc0", "measure_rc_recorded", "doc_05_section9_updated"], static=True, safety=False),
 "B5": R(["restored_profile_measured", "busy_idle_within_2s", "timer_zero_after_idle"]),
 # C Fold5
 "C1": R(["cover_state_closed", "sdi_join_phone", "sdi_split_phone", "tool_error_absent"], roles=("android_debug",), caps=2),
 "C2": R(["product_apk_installed", "sdi_real_touch_open", "sdi_real_touch_close", "no_fatal"], roles=("android_prod",), caps=2),
 "C3": R(["apk_diag_strings_absent", "apk_channel_prod"], roles=("android_prod",), safety=False),
 "C4": R(["back_reproduced_before_fix", "back_closes_menu", "back_closes_flyout", "back_no_app_exit", "gesture_back"], roles=("android_debug", "android_release"), caps=2),
 "C5": R(["first_frame_ms_fold5", "first_frame_improved_or_le_2000ms", "touch_after_first_frame_ok", "touch_during_splash_not_lost"], roles=("android_release",), caps=2),
 "C6": R(["override_closed_ok", "cover_size_904x2316", "nav_on_closed", "override_opened_ok", "restored_style_doc_state", "reset_base_state"], roles=("android_debug",), caps=2),
 "C7": R(["density_restored", "stay_on_restored", "no_test_package_left", "device_state_override_empty"], static=True, safety=False),
 "C8": R(["hs1_tour_touch", "hs1_settings_sheet_touch"], roles=("android_debug",), caps=2, human=True),
 # D 웹
 "D1": R(["real_chrome_headed", "own_profile_only", "real_mouse_leave_hides_spinner", "real_drag_panel", "real_rclick_menu", "real_f1", "console_error_zero"], roles=("web_diag",), caps=3, human=True),
 "D2": R(["ac_2_09", "ac_4_05", "ac_6_09", "ac_7_06", "panel_menu", "sdi_join"], roles=("web_diag",)),
 "D3": R(["webkit_boot", "firefox_absent_recorded", "edge_excluded_recorded"], roles=("web_diag",), caps=1),
 "D4": R(["check_product_web_rc0", "product_boot", "diag_absent"], roles=("web_prod",)),
 # E 2차 묶음
 "E1": R(["sdi_absent_from_tabs", "icon_count_eq_sdi", "tooltip_title", "click_activates", "click_restores_minimized", "icon_context_menu"], caps=2,
         files=[VM + "TitleStripViewModel.cs"], has=[(VM + "TitleStripViewModel.cs", "SdiItems"), (VW + "MainView.axaml", "shell.sdiIcon")]),
 "E2": R(["tab_natural_le_240", "tab_shrink_common_cap", "tab_min_72_then_scroll", "ellipsis_tooltip"], caps=2,
         files=[UI + "Controls/TitleTabWidth.cs", UI + "Controls/TitleTabPanel.cs", JD + "title_tab_width_check.py"], has=[(VW + "MainView.axaml", "TitleTabPanel")], run=[JD + "title_tab_width_check.py"]),
 "E3": R(["strip_height_28", "tab_height_24", "menu_bar_32_unchanged"], caps=1, lacks=[(VW + "MainView.axaml", r'MinHeight="32"')]),
 "E4": R(["dock_tabs_hidden", "dock_icons_only", "dock_strip_hidden_no_sdi", "mdi_tabs_kept", "document_tabs_kept"], caps=2, has=[(VM + "TitleStripViewModel.cs", "ShowTitleStrip"), (VM + "TitleStripViewModel.cs", "ShowTitleTabs")]),
 "E5": R(["icon_click_os_foreground_sdi", "sdi_caption_active_color", "main_caption_inactive_color", "before_fix_reproduced"], caps=2),
 "E6": R(["web_icon_count", "web_click_activates", "web_tab_width", "android_tablet_icon", "phone_nav_no_strip"], roles=("web_diag", "android_debug"), caps=2),
 "E7": R([], safety=False, static=True, has=[("plan/09_화면설계/03_데스크톱_화면설계.md", "제목 줄의 독립 창 아이콘")], lacks=[("plan/09_화면설계/03_데스크톱_화면설계.md", "지금은 독립 창도 제목 탭에"), (VW + "MainView.axaml", "탭 스트립"), (VW + "MainView.axaml", "탭 띠")]),
 # F 이월
 "F03": R(["hidden_panel_alert_shown", "click_reveals_panel", "zero_when_read", "visible_panel_no_alert"], roles=("desktop_work", "web_diag"), caps=2, has=[(VW + "ShellStatusBar.axaml", "shell.notifyBadge")]),
 "F04": R([], safety=False, static=True, lacks=[(DK + "DockFactoryDiag.cs", "notify-layout"), (VW + "NotificationPanelView.axaml.cs", "legacy"), (SH + "Diagnostics/AutomationArgs.cs", "notify-layout"), (SH + "Diagnostics/AutomationCommandRunner.cs", "notify_layout_legacy")]),
 "F05": R(["hidden_window_not_main", "main_hwnd_correct_with_hidden"], static=True, safety=False, has=[(MS + "win_owner_z.ps1", "hidden_excluded")]),
 "F06": R(["doc_tab_rclick_menu", "common_items_present", "tab_not_activated_by_rclick", "split_clear_kept"], caps=1),
 "F07": R(["tablet_tab_menu_touch", "single_tab_moved", "other_tabs_stay", "doc_updated"], roles=("android_debug",), caps=1),
 "F08": R(["left_remain_measured", "grab_rule_kept", "user_report_listed"], static=True),
 "F09": R(["submenu_rect_in_screen", "submenu_touch_opens", "search_scope_recorded"], roles=("android_debug",), caps=1),
 "F10": R(["touch_injected", "corner_button_shown_on_touch", "corner_button_touch_click", "mouse_behavior_unchanged"], caps=1, has=[(UI + "Controls/PanelCornerProximity.cs", "PointerType.Touch")]),
 "F11": R(["product_log_panel_hidden", "view_menu_consistent", "doc_03_records_exception"], roles=("desktop_product",)),
 "F13": R([], safety=False, static=True),
 "F14": R([], safety=False, static=True),
 "F15": R(["ac_2_06_real_idle_no_timer", "ac_5_06_real_events_one_eval", "ac_6_10_real_motion_off"], caps=1),
 # G 표시 토폴로지
 "G1": R(["validate_only_rc0", "watchdog_restores_after_main_kill", "topology_equal_after_restore", "no_save_to_database"], files=[TOPO], has=[(TOPO, "SDC_VALIDATE"), (TOPO, "watchdog")], lacks=[(TOPO, r"0x200\b|SDC_SAVE_TO_DATABASE\s*=")], static=True),
 "G2": R(["detach_right_monitor", "main_moved_visible", "sdi_moved_visible", "visible_windows_unmoved", "reattach_ok", "topology_equal_after_restore", "user_windows_compared", "user_pd_windows_unmoved", "user_pd_windows_outside_target", "debounce_one_eval"], caps=2),
 # 회귀
 "R1": R(["ui28_gate_all_pass", "final_build_all_axes"], roles=("desktop_work", "web_diag", "android_debug")),
 # K 사이클 중 추가 요청(포그라운드 축: 데스크톱 역할)
 "K1": R(["themes_all", "bar_strip", "text_bar", "text_inactive", "inactive_strip", "active_ne_inactive", "live_bar_bg_match_token", "px_drag_ne_px_strip", "menu_fg_match_token", "live_state", "tool_rc"], caps=3, themes=True,
         files=[TITLEBAR_TOOL], run=[TITLEBAR_TOOL, "--self-test"],
         expect={"bar_strip": (">=", 3.0), "text_bar": (">=", 4.5), "text_inactive": (">=", 3.0), "inactive_strip": (">=", 1.2), "live_state": ("==", "active"), "tool_rc": ("==", 0)}),
 "K2": R(["themes_all", "gap_ink_min", "gap_ink_max", "gap_count", "base_gap", "tool_rc"], caps=1, themes=True, files=[STATES_TOOL],
         expect={"gap_ink_min": ("range", (8.0, 10.0)), "gap_ink_max": ("range", (8.0, 10.0)), "gap_count": ("==", 6), "base_gap": ("==", 18), "tool_rc": ("==", 0)}),
 "K3": R(["themes_all"] + STATE_ON_BARS + ["inactive_rest_on", "surface_inactive_rest_on", "cache_min", "cache_surface_min", "cache_surface_inactive_min", "caption_min", "hover_live", "real_hover_accent", "real_hover_surface", "tool_rc"], caps=3, themes=True,
         files=[STATES_TOOL, STATE_RULES, THEME_SRC], run=[STATES_TOOL, "--static", THEME_SRC],
         expect=dict(ATLEAST(*STATE_ON_BARS, "cache_min", "cache_surface_min", "caption_min", v=3.0), **ATLEAST("inactive_rest_on", "surface_inactive_rest_on", "cache_surface_inactive_min", v=2.0), hover_live=("==", True), tool_rc=("==", 0))),
 "K4": R(WHEEL_GANTT + WHEEL_GRID + WHEEL_LOG + WHEEL_REAL + ["tool_rc"], caps=1, files=[WHEEL_TOOL], run=[WHEEL_TOOL], expect={"tool_rc": ("==", 0)}),
 "K5": R(GRID_TOOL_NAMES + ["grid_row_stripes", "grid_selected_row_line", "grid_user_drag_width_kept", "tool_rc"], caps=5, files=[WHEEL_TOOL], expect={"tool_rc": ("==", 0)}),
 # K6 발견 결함: Fold5 축(user_returned.flag 면제). 첫 기동 두 측정의 value 는 판정기 rc, tablet_no_fatal 의 value 는 치명 건수다
 "K6": R(["tablet_first_doc0", "tablet_first_doc1", "tablet_first_empty_hint", "tablet_fold_cycle_keeps", "tablet_closed_start_open", "tablet_no_fatal"], roles=("android_debug",), caps=4,
         files=[TABLET_TOOL], run=[TABLET_TOOL, "--self-test"],
         expect={"tablet_first_doc0": ("==", 0), "tablet_first_doc1": ("==", 0), "tablet_first_empty_hint": ("==", True), "tablet_fold_cycle_keeps": ("==", True), "tablet_closed_start_open": ("==", True), "tablet_no_fatal": ("==", 0)}),
 # K7 발견 결함 D-REPLAY: Fold5 축. 위 5개 값은 두 판본 중 나쁜 쪽을 적고, replay_by_build 는 판본별 값이다(check_k7_builds 가 두 판본 모두 요구한다)
 "K7": R(list(REPLAY_RULES) + ["replay_by_build"], roles=("android_prod", "android_release"), caps=2, files=[REPLAY_JUDGE, REPLAY_MEASURE], run=[REPLAY_JUDGE, "--self-test"], expect=REPLAY_RULES),
 # W 웹 속도(oto 자율 발견, 근거 frameworks/ui/logs/webperf/desktop/summary_otest-w-7.md). W1·W2 는 정적, W3·W4 는 결과 JSON(WEBPERF_START 이후 실행)
 "W1": R([], safety=False, static=True, run=["frameworks/ui/tools/check_layers.py"]),
 "W2": R([], safety=False, static=True, files=[BROWSER_CSPROJ, BUILD_BROWSER]),
 "W3": R(list(W3_RULES), roles=("web_prod",), safety=False, static=True, expect=W3_RULES),
 "W4": R(list(W4_RULES) + ["app_compare_table"], roles=("web_prod",), caps=3, expect=W4_RULES),
}
SPEC.update(R2_ITEMS)
GATE_R1 = "frameworks/ui/logs/ui28"
def fail(m): print("FAIL", m); sys.exit(1)
def und(m): print("UNDET", m); sys.exit(2)
def rd(p):
    try: return open(os.path.join(ROOT, p), encoding="utf-8", errors="ignore").read()
    except OSError: return None
def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()
def abspath(p): return p if os.path.isabs(p) else os.path.join(ROOT, p)
def has_code(t, sub):
    for ln in t.splitlines():
        if sub in ln and not re.match(r"^\s*(//|#(?!\w)|\*|/\*)", ln): return True
    return False
def stamp(v):
    try: return datetime.fromisoformat(v)
    except (TypeError, ValueError): return None
def manifest():
    try: return json.load(open(os.path.join(SESSION, "plans", "pcaway_final_build.json"), encoding="utf-8"))
    except (OSError, ValueError): return None
def check_build(i, r, s):
    bd = r.get("build")
    if s["static"]: return
    if not isinstance(bd, dict) or not bd.get("path") or not bd.get("sha256"): fail("결과 build{path,sha256} 누락")
    m = manifest()
    if m is None: fail("최종 빌드 목록 plans/pcaway_final_build.json 부재 — 최종 빌드 전 결과는 인정하지 않는다")
    ok = {e.get("sha256") for role in s["roles"] for e in [m.get(role) or {}]}
    if bd["sha256"] not in ok: fail("결과 build.sha256 이 이 항목의 최종 빌드 역할(%s)과 다름" % ",".join(s["roles"]))
    bp = abspath(bd["path"])
    if not os.path.isfile(bp): fail("결과 build.path 부재 " + bd["path"])
    if sha(bp) != bd["sha256"]: fail("build 산출물이 시험 뒤 바뀜")
DESKTOP_ROLES, WEB_ROLES, ANDROID_ROLES = {"desktop_work", "desktop_product"}, {"web_diag", "web_prod"}, {"android_debug", "android_release", "android_prod"}
def flag_axis(s):
    # 계획 §2-3: 복귀 표지 뒤에는 포그라운드만 거부하고 헤드리스 웹·Fold5·정적은 계속한다. 데스크톱이 섞이거나 실제 브라우저 창 측정이면 포그라운드, 모르는 역할은 unknown(거부 쪽)
    if s["static"]: return "static"
    roles = set(s["roles"])
    if roles & DESKTOP_ROLES: return "foreground"
    if roles & WEB_ROLES and any("headed" in n or n.startswith("real_") for n in s["req"]): return "foreground"
    if not roles or roles - WEB_ROLES - ANDROID_ROLES: return "unknown"
    return "headless_web_or_fold5"
INJECTION_LOG = "frameworks/ui/logs/pcaway/injections.jsonl"
NOT_INJECTION = {"arm", "returned"}  # user_away_guard 의 시작 기준·복귀 기록. 입력을 만들지 않는다
INJECTION_MARGIN_MS = 2000  # 결과 시각은 WSL, 주입 기록은 Windows 시계다. 차이만큼 구간을 넓혀 거부 쪽으로 둔다
FG_MARKS = ("foreground", "headed")
def returned_block(r, s, root=None):
    # safety.user_returned=true 결과의 판단(계획 §2-3). FAIL 사유 문자열, 면제면 None. pcaway_report emit·s2 도 이 함수로 verdict 를 정해 판단이 갈리지 않는다
    if flag_axis(s) in ("foreground", "unknown"): return "사용자 복귀 정황으로 측정이 유효하지 않음"
    sf = r.get("safety") if isinstance(r.get("safety"), dict) else {}
    for m in FG_MARKS:
        if r.get(m) or sf.get(m): return "복귀 정황 뒤 면제 축 결과에 포그라운드 표시: " + m
    for x in r.get("measurements") or []:
        if isinstance(x, dict) and any(m in str(x.get("name")) for m in FG_MARKS) and x.get("value") is not False: return "복귀 정황 뒤 면제 축 결과에 포그라운드 측정: %s" % x.get("name")
    # 결과의 started_at·finished_at 은 emit 시각이라 측정 구간을 덮지 못한다. measured_* 만 측정 구간으로 인정한다
    st, fi, wrote = stamp(r.get("measured_started_at")), stamp(r.get("measured_finished_at")), stamp(r.get("finished_at"))
    if st is None or fi is None or st.tzinfo is None or fi.tzinfo is None: return "복귀 정황 뒤 측정 시작·끝 시각이 없어 PC 쪽 주입 여부를 확인할 수 없음"
    if fi <= st: return "복귀 정황 뒤 측정 구간이 역전돼 PC 쪽 주입 여부를 확인할 수 없음"
    if st < datetime.fromisoformat(CYCLE_START): return "복귀 정황 뒤 측정 구간이 이 사이클 이전이라 PC 쪽 주입 여부를 확인할 수 없음"
    if wrote is None or wrote.tzinfo is None or fi > wrote: return "복귀 정황 뒤 측정 끝이 결과 작성보다 늦어 PC 쪽 주입 여부를 확인할 수 없음"
    lo, hi = st.timestamp() * 1000 - INJECTION_MARGIN_MS, fi.timestamp() * 1000 + INJECTION_MARGIN_MS
    try:
        with open(os.path.join(root or ROOT, INJECTION_LOG), encoding="utf-8", errors="replace") as f: lines = f.read().splitlines()
    except OSError: return "복귀 정황 뒤 주입 기록을 읽지 못해 PC 쪽 주입 여부를 확인할 수 없음"
    for ln in lines:
        if not ln.strip(): continue
        try: rec = json.loads(ln)
        except ValueError: return "복귀 정황 뒤 주입 기록에 깨진 줄이 있어 PC 쪽 주입 여부를 확인할 수 없음"
        t = rec.get("t_ms") if isinstance(rec, dict) else None
        if isinstance(t, bool) or not isinstance(t, (int, float)): return "복귀 정황 뒤 주입 기록에 시각 없는 줄이 있어 PC 쪽 주입 여부를 확인할 수 없음"
        if lo <= t <= hi and rec.get("kind") not in NOT_INJECTION: return "복귀 정황 뒤 면제 축 실행 구간에 PC 쪽 실입력 주입: %s" % rec.get("kind")
    return None
def unwrap(v):
    # pcaway_report emit --measure-file 형식({value,source})만 한 겹 푼다. 그 밖의 값은 그대로다
    return v["value"] if isinstance(v, dict) and set(v) == {"value", "source"} else v
def meets(v, op, want):
    v = unwrap(v)
    if isinstance(want, bool) or isinstance(want, str): return type(v) is type(want) and v == want
    if isinstance(v, bool) or not isinstance(v, (int, float)): return False
    if op == ">=": return v >= want
    if op == "<=": return v <= want
    if op == "range": return want[0] <= v <= want[1]
    return v == want
USER_PD_CASES = ("present_untouched", "absent_both")
def check_user_pd_case(sf):
    # team-lead 계약 해석(11-07): user_pd_untouched 는 (가) present_untouched 같은 PID·같은 시작 시각 생존, (나) absent_both 시작·끝 사용자 Pd 경로 프로세스 0개 + 측정 중 실행 기록 없음, 두 경우에만 참이다.
    # user_pd_case 를 남기는 도구(11-07 이후) 결과는 근거 필드까지 대조한다. 이 필드가 없는 이전 형식 결과는 기존 규칙(user_pd_untouched 값)만 본다
    if "user_pd_case" not in sf and "user_pd_before" not in sf: return
    case, b, a = sf.get("user_pd_case"), sf.get("user_pd_before"), sf.get("user_pd_after")
    if case not in USER_PD_CASES or not isinstance(b, dict) or not isinstance(a, dict): fail("사용자 Pd 판정 경우 값 불충족: %s" % (case,))
    if case == "present_untouched":
        if not b.get("pid") or b.get("pid") != a.get("pid") or not b.get("start") or b.get("start") != a.get("start"): fail("사용자 Pd present_untouched 근거 불충족(같은 PID·같은 시작 시각 아님)")
        if not b.get("rects") or b.get("rects") != a.get("rects"): fail("사용자 Pd present_untouched 근거 불충족(창 사각형 없음 또는 바뀜)")
    elif b.get("pid") or a.get("pid") or b.get("present") != [] or a.get("present") != [] or sf.get("user_pd_launches") != []:
        fail("사용자 Pd absent_both 근거 불충족(시작·끝 0개·실행 기록 없음이 아님)")
def check_result(i, s, ids=()):
    # ids: 결과 파일 이름이 항목 ID 와 다른 경우(T4_web 등)의 허용 ac_id 목록. 새 항목(start 지정)은 그 시각 이전 실행을 인정하지 않는다
    t = rd("frameworks/ui/logs/pcaway/%s.json" % i)
    if t is None: fail("시험 결과 부재 frameworks/ui/logs/pcaway/%s.json" % i)
    try: r = json.loads(t)
    except ValueError: fail("결과 JSON 파싱 실패")
    if not isinstance(r, dict): fail("결과가 객체가 아님")
    if r.get("ac_id") not in (i,) + tuple(ids): fail("ac_id 불일치")
    if r.get("verdict") != "PASS": fail("verdict != PASS")
    if not str(r.get("writer", "")).startswith("otest"): fail("writer 가 otest 아님")
    if not str(r.get("run_id", "")).strip(): fail("run_id 누락")
    st, fi = stamp(r.get("started_at")), stamp(r.get("finished_at"))
    if st is None or fi is None or st.tzinfo is None: fail("started_at/finished_at ISO(시간대 포함) 아님")
    if st < datetime.fromisoformat(s.get("start") or CYCLE_START) or fi < st: fail("이 사이클(계약 시각) 이전 실행 또는 시각 역전")
    if flag_axis(s) in ("foreground", "unknown"):
        fl = os.path.join(ROOT, "frameworks/ui/logs/pcaway/user_returned.flag")
        if os.path.exists(fl) and st.timestamp() >= os.path.getmtime(fl): fail("사용자 복귀 표지(user_returned.flag) 이후 시작한 포그라운드·축 불명 시험은 유효하지 않음")
    sc = r.get("script")
    if not isinstance(sc, dict) or not sc.get("path") or not sc.get("sha256"): fail("script{path,sha256} 누락")
    if not os.path.isfile(abspath(sc["path"])): fail("script.path 부재 " + sc["path"])
    if sha(abspath(sc["path"])) != sc["sha256"]: fail("script 가 시험 뒤 바뀜")
    check_build(i, r, s)
    ms = r.get("measurements")
    if not isinstance(ms, list) or not ms: fail("measurements 비어 있음")
    seen = {}
    for m in ms:
        if not isinstance(m, dict) or "name" not in m or "value" not in m or "pass" not in m: fail("measurements 항목에 name/value/pass 누락")
        seen[m["name"]] = m
        if m["pass"] is not True: fail("측정 실패: %s value=%s" % (m["name"], m["value"]))
    for n in s["req"]:
        if n not in seen: fail("필수 측정 누락: " + n)
        if unwrap(seen[n].get("value")) is False: fail("필수 측정 값 false: " + n)
    for n, (op, want) in s["expect"].items():
        if not meets(seen[n].get("value"), op, want): fail("측정 값 기준 미달: %s value=%s 기준 %s %s" % (n, seen[n].get("value"), op, want))
    if s["themes"]:
        ids = re.findall(r'"theme\.builtin\.([\w-]+)"', rd(THEME_SRC) or "")
        if not ids: und("내장 테마 목록을 읽지 못함 " + THEME_SRC)
        got = unwrap(seen["themes_all"].get("value"))  # emit --measure-file 형식({value,source})도 meets 와 같게 한 겹 푼다(otest-9 11-07)
        miss = sorted(set(ids) - set(got if isinstance(got, list) else []))
        if miss: fail("측정하지 않은 내장 테마: " + ",".join(miss))
    if s["caps"]:
        cs = r.get("captures")
        if not isinstance(cs, list) or len(cs) < s["caps"]: fail("판독한 캡처 %d장 미만" % s["caps"])
        for c in cs:
            if not isinstance(c, dict) or not c.get("path"): fail("captures 형식 오류")
            p = abspath(c["path"])
            if not os.path.isfile(p): fail("캡처 부재 " + c["path"])
            with open(p, "rb") as f:
                if f.read(8) != PNG: fail("PNG 아님 " + c["path"])
            if sha(p) != c.get("sha256"): fail("캡처 sha 불일치 " + c["path"])
            if not str(c.get("read_by", "")).startswith("otest") or len(str(c.get("finding", "")).strip()) < 10: fail("캡처 판독 기록 없음 " + c["path"])
    hr = r.get("human_remaining", [])
    if hr and not s["human"]: fail("사람 확인으로 넘길 수 없는 항목")
    if s["human"] and not isinstance(hr, list): fail("human_remaining 형식 오류")
    if s["safety"]:
        sf = r.get("safety")
        if not isinstance(sf, dict): fail("safety 누락")
        if sf.get("user_pd_untouched") is not True or sf.get("user_files_same") is not True: fail("사용자 Pd·파일 안전 값 불충족")
        check_user_pd_case(sf)
        if sf.get("input_leak") is not False: fail("입력 유출 값 불충족")
        ur = sf.get("user_returned")
        if ur is True:
            why = returned_block(r, s)
            if why: fail(why)
        elif ur is not False: fail("사용자 복귀 정황 값 불충족")
def git(*a):
    return subprocess.run(["git", "-C", ROOT] + list(a), capture_output=True, text=True, timeout=30)
def check_discarded():
    o = git("ls-files").stdout.splitlines()
    bad = [f for f in o if re.search(r"\.(discarded|cand|bak_c2)$", f)]
    if bad: fail("추적 중인 폐기 파일 %d건 (%s …)" % (len(bad), bad[0]))
def check_readme():
    readme = rd("frameworks/ui/test/README.md")
    if readme is None: fail("README 부재")
    fs = git("ls-files", "-c", "-o", "--exclude-standard", "frameworks/ui/test").stdout.splitlines()
    tools = [f for f in fs if re.search(r"\.(py|ps1|mjs|sh)$", f) and "/archive/" not in f and "/logs/" not in f and os.path.isfile(os.path.join(ROOT, f))]
    if not tools: und("시험 도구 목록 없음")
    miss = [f for f in tools if os.path.basename(f) not in readme]
    if miss: fail("README 미등재 %d건 (%s …)" % (len(miss), miss[0]))
def check_commit():
    if not git("ls-files", UI + "Controls/TitleTabPanel.cs").stdout.strip(): fail("TitleTabPanel 이 커밋되지 않음")
    if git("status", "--porcelain", "--", "frameworks/ui", "plan").stdout.strip(): fail("frameworks/ui·plan 작업트리가 clean 이 아님")
    if git("merge-base", "--is-ancestor", "HEAD", "origin/main").returncode != 0: fail("HEAD 가 origin/main 에 push 되지 않음")
def check_history():
    t = rd("HISTORY.md")
    if t is None: fail("HISTORY 부재")
    if "conv_179371711303, O4, PC 비움" not in t: fail("HISTORY 에 이번 사이클 항목 없음")
    if "PC 비움 전수" not in (rd("LESSONS.md") or ""): fail("LESSONS 에 이번 사이클 항목 없음")
def check_r1():
    d = os.path.join(ROOT, GATE_R1)
    m = manifest()
    if m is None: fail("최종 빌드 목록 부재")
    if not os.path.isdir(d): fail("ui28 결과 폴더 부재")
    shas = {e.get("sha256") for e in m.values() if isinstance(e, dict)}
    n = 0
    for f in os.listdir(d):
        if not f.endswith(".json"): continue
        n += 1
        try: r = json.load(open(os.path.join(d, f), encoding="utf-8"))
        except ValueError: fail("ui28 결과 파싱 실패 " + f)
        if r.get("verdict") != "PASS": fail("ui28 결과 PASS 아님 " + f)
        b = r.get("build")
        if isinstance(b, dict) and b.get("sha256") not in shas: fail("ui28 결과가 최종 빌드가 아님 " + f)
    if n < 59: fail("ui28 결과 %d건 < 59" % n)
def check_k7_builds():
    # check_build 는 역할 중 한 sha 만 맞아도 통과한다. K7 은 두 판본 측정을 요구하므로 판본별 sha·산출물·값을 각각 본다
    ms = {m["name"]: m for m in json.loads(rd("frameworks/ui/logs/pcaway/K7.json"))["measurements"]}
    by, man = ms["replay_by_build"]["value"], manifest()
    if not isinstance(by, dict): fail("replay_by_build 가 판본별 객체가 아님")
    for role in SPEC["K7"]["roles"]:
        e = by.get(role)
        if not isinstance(e, dict): fail("판본 값 누락: " + role)
        got = e.get("sha256")
        if got in REPLAY_PREFIX_SHA: fail("수정 전 빌드 값은 인정하지 않는다: %s %s" % (role, got))
        if not got or got != (man.get(role) or {}).get("sha256"): fail("판본 sha 가 최종 빌드와 다름: %s %s" % (role, got))
        if not e.get("path") or not os.path.isfile(abspath(e["path"])): fail("판본 산출물 부재: " + role)
        if sha(abspath(e["path"])) != got: fail("판본 산출물이 시험 뒤 바뀜: " + role)
        for n, (op, want) in REPLAY_RULES.items():
            if not meets(e.get(n), op, want): fail("판본 값 기준 미달: %s %s value=%s 기준 %s %s" % (role, n, e.get(n), op, want))
def blank_keep_lines(m): return re.sub(r"[^\n]", " ", m.group(0))
def strip_cs(t): return re.sub(r"(?<![:\"])//[^\n]*", "", re.sub(r"/\*.*?\*/", blank_keep_lines, t, flags=re.S))
def strip_xml(t): return re.sub(r"<!--.*?-->", blank_keep_lines, t, flags=re.S)
def diag_lines(t):
    # PD_DIAGNOSTICS 판에서만 컴파일되는 줄 번호. #if PD_DIAGNOSTICS 본문·#if !PD_DIAGNOSTICS 의 #else 본문은 진단 전용, #elif 와 판단 못 한 조건은 거부 쪽(진단 전용)으로 센다
    out, st = set(), []
    for n, ln in enumerate(t.splitlines(), 1):
        m = re.match(r"\s*#\s*(if|elif|else|endif)\b(.*)", ln)
        if m:
            k, cond = m.group(1), re.sub(r"//.*", "", m.group(2)).strip()
            if k == "if":
                if "PD_DIAGNOSTICS" not in cond: st.append("other")
                elif re.fullmatch(r"!\s*PD_DIAGNOSTICS", cond): st.append("neg")
                elif "||" not in cond and not re.search(r"!\s*\(?\s*PD_DIAGNOSTICS", cond): st.append("diag")
                else: st.append("unknown")
            elif k == "else" and st: st[-1] = {"diag": "prod", "neg": "diag"}.get(st[-1], st[-1])
            elif k == "elif" and st: st[-1] = "unknown"
            elif k == "endif" and st: st.pop()
            out.add(n); continue
        if any(x in ("diag", "unknown") for x in st): out.add(n)
    return out
def src_files(exts):
    for dp, dn, fn in os.walk(os.path.join(ROOT, "frameworks/ui/src")):
        dn[:] = sorted(d for d in dn if d not in ("bin", "obj"))
        for f in sorted(fn):
            if f.endswith(exts): yield os.path.relpath(os.path.join(dp, f), ROOT).replace(os.sep, "/")
def prod_code_lines(t):
    dl = diag_lines(t)
    return [(n, ln) for n, ln in enumerate(strip_cs(t).splitlines(), 1) if n not in dl]
def check_w1():
    # LayerFirst 가 진단 전용(PD_DIAGNOSTICS·AppContext 스위치·빌드 속성)이 아니라 공용 제품 코드 기본 동작인지 본다. 이름 규칙은 계획 문구 'LayerFirst' 만 쓴다
    for p in list(src_files((".csproj", ".props", ".targets", ".json"))) + ["frameworks/ui/Directory.Build.props"]:
        t = rd(p)
        if t and re.search(r"PdLayerFirst|Pd\.Diagnostics\.LayerFirst", strip_xml(t)): fail("진단 전용 LayerFirst 빌드 조건이 남아 있음 " + p)
    cs = {p: rd(p) or "" for p in src_files((".cs",))}
    defs = {}
    for p, t in cs.items():
        if "Pd.Diagnostics.LayerFirst" in strip_cs(t): fail("진단 스위치 Pd.Diagnostics.LayerFirst 가 남아 있음 " + p)
        for n, ln in prod_code_lines(t):
            m = re.search(r"\bclass\s+(\w*LayerFirst\w*)", ln)
            if m: defs[m.group(1)] = p
    if not defs: fail("진단 조건 밖(제품 판)에 LayerFirst 클래스 정의 없음")
    for name, p in sorted(defs.items()):
        if not p.startswith(WEB_UI_PROJECTS) or "/Diagnostics/" in p: fail("LayerFirst 정의가 공용 프로젝트의 비진단 위치가 아님 " + p)
        bad = [n for n, ln in prod_code_lines(cs[p]) if re.search(r"\bPerfProbe\b|Pd\.Diagnostics\.", ln)]
        if bad: fail("LayerFirst 제품 코드가 진단 전용 의존을 조건 없이 씀 %s:%d" % (p, bad[0]))
        if any(q != p and q.startswith(WEB_UI_PROJECTS) and any(re.search(r"\b%s\s*\." % re.escape(name), ln) for _, ln in prod_code_lines(u)) for q, u in cs.items()):
            return
    fail("LayerFirst 를 진단 조건 밖에서 호출하는 공용 코드 없음(정의 %s)" % ",".join(sorted(defs)))
REL_COND = re.compile(r"^\s*'\$\(Configuration\)'\s*==\s*'Release'\s*$", re.I)
def aot_property_ok(t):
    # RunAOTCompilation=true 가 조건 없는 PropertyGroup 이나 Release 조건에만 있어야 한다. 진단·사용자 속성 조건(PdDiagnostics·PdAot 등)은 인정하지 않는다
    t = strip_xml(t)
    if re.search(r"<RunAOTCompilation[^>]*>\s*false\s*<", t, re.I): fail("RunAOTCompilation=false 가 남아 있음 " + BROWSER_CSPROJ)
    for g in re.finditer(r"<PropertyGroup([^>]*)>(.*?)</PropertyGroup>", t, re.S):
        gc = re.search(r'Condition\s*=\s*"([^"]*)"', g.group(1))
        for e in re.finditer(r"<RunAOTCompilation([^>]*)>\s*true\s*</RunAOTCompilation>", g.group(2), re.I):
            ec = re.search(r'Condition\s*=\s*"([^"]*)"', e.group(1))
            if all(c is None or REL_COND.match(c.group(1)) for c in (gc, ec)): return True
    return False
def aot_bundle(fw, label):
    ws = [f for f in os.listdir(fw) if re.fullmatch(r"dotnet\.native(\.\w+)?\.wasm", f)] if os.path.isdir(fw) else []
    if len(ws) != 1: fail("%s 번들의 dotnet.native*.wasm 이 1개가 아님(%d) %s" % (label, len(ws), fw))
    with open(os.path.join(fw, ws[0]), "rb") as f: data = f.read()
    miss = [m.decode() for m in AOT_MARKS if m not in data]
    if miss: fail("%s 번들에 AOT 표지 없음: %s" % (label, ",".join(miss)))
    if os.path.exists(os.path.join(os.path.dirname(fw), "diag.js")): fail("%s 번들에 진단 브리지 diag.js 가 있음(제품 판 아님)" % label)
    return os.path.getmtime(os.path.join(fw, ws[0]))
def check_w2():
    if not aot_property_ok(rd(BROWSER_CSPROJ) or ""): fail("Browser csproj 에 Release 제품 빌드용 RunAOTCompilation=true 없음(조건 없음 또는 Release 조건만 인정)")
    if re.search(r"RunAOTCompilation=false", rd(BUILD_BROWSER) or ""): fail("build_browser.sh 가 AOT 를 끔")
    # build_browser.sh 의 제품 산출 위치(PD_DIAG=false → bin/Release/<TFM>/publish_product). TFM 은 탐색한다
    base = os.path.join(ROOT, BROWSER_DIR, "bin/Release")
    fws = [os.path.join(base, d, "publish_product/wwwroot/_framework") for d in (sorted(os.listdir(base)) if os.path.isdir(base) else [])]
    fws = [f for f in fws if os.path.isdir(f)]
    if not fws: fail("제품 웹 번들(bin/Release/*/publish_product/wwwroot) 없음")
    fw = max(fws, key=os.path.getmtime)
    mt = aot_bundle(fw, "제품 웹")
    if mt < datetime.fromisoformat(WEBPERF_START).timestamp(): fail("제품 웹 번들이 웹 속도 계약 시각 이전 빌드")
    if mt < os.path.getmtime(os.path.join(ROOT, BROWSER_CSPROJ)): fail("제품 웹 번들이 csproj 수정 이전 빌드")
def w_result():
    r = json.loads(rd("frameworks/ui/logs/pcaway/%s.json" % sys.argv[2]))
    if stamp(r.get("started_at")) < datetime.fromisoformat(WEBPERF_START): fail("웹 속도 계약 시각 이전 실행")
    return r
def check_w3():
    r = w_result()
    sv = r.get("server")
    if not isinstance(sv, dict) or not str(sv.get("path", "")).startswith("frameworks/ui/tools/serve_browser"): fail("server{path} 가 사용자 웹 서버 스크립트(frameworks/ui/tools/serve_browser*)가 아님")
    p = abspath(sv["path"])
    if not os.path.isfile(p) or sha(p) != sv.get("sha256"): fail("server 스크립트 부재 또는 시험 뒤 바뀜 " + sv["path"])
    t = rd(sv["path"]) or ""
    for need in ("Content-Encoding", "immutable"):
        if need not in t: fail("server 스크립트에 %s 처리 없음" % need)
def check_w4():
    r = w_result()
    if r.get("frame_source") != "chrome_trace": fail("frame_source 가 chrome_trace 가 아님(제품 판은 PerfProbe 가 없다)")
    fw = os.path.dirname(abspath(r["build"]["path"]))
    if os.path.basename(fw) != "_framework": fail("build.path 가 번들 _framework 안의 파일이 아님")
    aot_bundle(fw, "측정")
    ms = {m["name"]: m for m in r["measurements"]}
    tab = unwrap(ms["app_compare_table"]["value"])
    if not isinstance(tab, list): fail("app_compare_table 이 목록이 아님")
    rows = {x.get("op"): x for x in tab if isinstance(x, dict)}
    for op in W4_COMPARE_OPS:
        x = rows.get(op)
        if x is None: fail("앱 대비 비교표에 항목 없음: " + op)
        a, w, k = x.get("app_ms"), x.get("web_ms"), x.get("ratio")
        if not all(isinstance(v, (int, float)) and not isinstance(v, bool) and v > 0 for v in (a, w, k)): fail("비교표 값 오류: " + op)
        if abs(k - w / a) > 0.01 + 0.01 * k: fail("비교표 배율이 web_ms/app_ms 와 다름: " + op)
def r2_result(i):
    return json.loads(rd("frameworks/ui/logs/pcaway/%s.json" % i))
def check_p1():
    d = r2_result("P1").get("detail")
    ls = d.get("load1_per_run") if isinstance(d, dict) else None
    if not isinstance(ls, list) or not ls: fail("detail.load1_per_run 목록 없음")
    bad = [v for v in ls if isinstance(v, bool) or not isinstance(v, (int, float)) or v >= 8]
    if bad: fail("부하 8 이상 또는 값 오류 시행 %d건 (%s …)" % (len(bad), bad[0]))
def check_p5():
    ms = {m["name"]: unwrap(m["value"]) for m in r2_result("P5")["measurements"]}
    s1, s2 = ms["fonts_sha_build1"], ms["fonts_sha_build2"]
    if not (isinstance(s1, str) and re.fullmatch(r"[0-9a-f]{64}", s1)): fail("fonts_sha_build1 이 sha256 이 아님")
    if s1 != s2: fail("xkxk.frameworks.ui.fonts wasm sha256 이 두 빌드에서 다름")
    n1, n2 = ms["fonts_name_build1"], ms["fonts_name_build2"]
    if not (isinstance(n1, str) and n1.strip()) or n1 != n2: fail("지문 파일명이 두 빌드에서 다르거나 비어 있음")
    for k in ("built_at_1", "built_at_2"):
        t = stamp(ms[k])
        if t is None or t.tzinfo is None or t < datetime.fromisoformat(R2_START): fail("%s 가 시간대 포함 ISO 가 아니거나 계약 시각 이전" % k)
    if not has_code(rd(R2_FILES["fonts_csproj"]) or "", "<Deterministic>true"): fail("xkxk.frameworks.ui.fonts.csproj 코드 줄에 <Deterministic>true 없음")
def check_q1():
    hits = [p for p in src_files((".cs",)) if os.path.basename(p) == "PanelFeatureRules.cs"]
    if not hits: fail("PanelFeatureRules.cs 부재")
def check_t2():
    pats = []
    # git 이 아는 파일(추적+미추적·무시 제외)만 훑는다. NTFS 전체 순회는 부하 때 시간 초과가 난다
    # frameworks/ui/test/ 계층은 "없어야 한다" 부재 단언이 정당하게 이 토큰을 쓰므로 제외한다
    for base, exts in (("frameworks/ui/src", (".axaml", ".json", ".cs", ".js", ".html", ".xaml")),):
        for f in git("ls-files", "-c", "-o", "--exclude-standard", base).stdout.splitlines():
            p = os.path.join(ROOT, f)
            if f.endswith(exts) and os.path.isfile(p) and re.search(r"shell\.btnSettings(?![A-Za-z0-9_])", open(p, encoding="utf-8", errors="ignore").read()): pats.append(f)
    if pats: fail("shell.btnSettings 가 남아 있음 %d파일 (%s …)" % (len(pats), pats[0]))
    if "toolbar.theme" in (rd(R2_FILES["main"]) or ""): fail("MainView.axaml 에 toolbar.theme 가 남아 있음")
    if re.search(r'"main\.settings"', rd(R2_FILES["tour"]) or ""): fail("tour_catalog.json 에 main.settings 단계가 남아 있음")
def check_n1():
    t = {k: rd(p) for k, p in R2_DOCS.items()}
    for k, v in t.items():
        if v is None: fail("문서 부재 " + R2_DOCS[k])
    for need in ("패널 기능", "테마 아이콘", "제목 줄 높이"):
        if need not in t["d01"]: fail("01 문서에 '%s' 없음" % need)
    if "제목 줄 높이 28 DIP" in t["d01"]: fail("01 문서에 옛 '제목 줄 높이 28 DIP' 문구가 남아 있음")
    for k in ("d03", "d04", "d06"):
        if "테마 아이콘" not in t[k]: fail("%s 에 '테마 아이콘' 없음" % R2_DOCS[k])
def check_t4():
    for n in ("T4_web", "T4_android"): check_result(n, SPEC[n], ids=("T4",))
def find_superseder(node, target):
    if isinstance(node, dict):
        if target in (node.get("supersedes") or []) and node.get("id"): return node["id"]
        for v in node.values():
            x = find_superseder(v, target)
            if x: return x
    elif isinstance(node, list):
        for v in node:
            x = find_superseder(v, target)
            if x: return x
    return None
def check_e3():
    # goal.json 에 supersedes:[E3] 인 항목(T3)이 있으면 그 항목 판정을 E3 의 판정으로 쓴다. 없으면 False 를 돌려 기존 E3 판정을 그대로 한다
    for d in (os.environ.get("GOAL_SESSION_DIR"), SESSION):
        try: g = json.load(open(os.path.join(d, "goal.json"), encoding="utf-8")) if d else None
        except (OSError, ValueError): g = None
        if g is not None: break
    sup = find_superseder(g, "E3") if g is not None else None
    if not sup: return False
    p = subprocess.run([sys.executable, "-I", os.path.abspath(__file__), "item", sup], capture_output=True, text=True, timeout=RUN_TIMEOUT * 2)
    if p.returncode == 0:
        print("PASS superseded→%s" % sup); return True
    fail("E3 대체 항목 %s 실패(rc=%d): %s" % (sup, p.returncode, p.stdout.strip()))
SPECIAL = {"F13": check_discarded, "F14": check_readme, "T4": check_t4}
W_CHECKS = {"W1": check_w1, "W2": check_w2, "W3": check_w3, "W4": check_w4, "P1": check_p1, "P5": check_p5, "Q1": check_q1, "T2": check_t2, "N1": check_n1}
def main():
    if len(sys.argv) != 3 or sys.argv[1] != "item": und("사용법: item <ID>")
    i = sys.argv[2]
    if not os.path.isdir(os.path.join(ROOT, "frameworks/ui")): und("frameworks/ui 부재")
    if i == "Z1": check_commit(); print("PASS"); return
    if i == "Z2": check_history(); print("PASS"); return
    if i == "E3" and check_e3(): return
    s = SPEC.get(i)
    if s is None: und("미등록 항목 " + i)
    if i in SPECIAL: SPECIAL[i](); print("PASS"); return
    for f in s["files"]:
        if not os.path.isfile(os.path.join(ROOT, f)): fail("파일 부재 " + f)
    for f in s["absent"]:
        if os.path.exists(os.path.join(ROOT, f)): fail("제거돼야 할 파일 존재 " + f)
    for f, sub in s["has"]:
        t = rd(f)
        if t is None: fail("파일 부재 " + f)
        if not has_code(t, sub): fail("코드 줄에 문자열 없음 %s in %s" % (sub, f))
    for f, rx in s["lacks"]:
        t = rd(f)
        if t is None: fail("파일 부재 " + f)
        if re.search(rx, t): fail("금지 패턴 존재 %s in %s" % (rx, f))
    if s["run"]:
        try: r = subprocess.run(["python3", "-I", os.path.join(ROOT, s["run"][0])] + s["run"][1:], cwd=ROOT, capture_output=True, timeout=RUN_TIMEOUT)
        except subprocess.TimeoutExpired: fail("실행 시간 초과 " + s["run"][0])
        if r.returncode != 0: fail("실행 rc=%d %s" % (r.returncode, s["run"][0]))
    if i == "R1": check_r1()
    if s["req"]: check_result(i, s)
    if i == "K7": check_k7_builds()
    if i in W_CHECKS: W_CHECKS[i]()
    print("PASS")
if __name__ == "__main__":
    main()
