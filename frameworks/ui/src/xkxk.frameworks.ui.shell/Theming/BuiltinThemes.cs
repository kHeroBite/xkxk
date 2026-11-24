// 내장 테마 템플릿 7종(UI-07, 기본 밝게 = Mars 팔레트 + Light(이전) 보존) — 자체 명칭·자체 정의 색. 색 배열 순서 = ThemeTokens.All 순서
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Theming;

public static class BuiltinThemes
{
    public const string LightId = "pd-light";
    public const string DarkId = "pd-dark";
    public const string PrevLightId = "pd-light-prev";

    // 순서: 바탕·면·보조면·글자·보조글자·강조·강조글자·선택·선택글자·경계선·
    //       탭활성·탭비활성·탭활성글자·MDI제목(활성)·MDI제목(비활성)·MDI제목글자·상태바·상태바글자·
    //       그리드줄무늬·그리드헤더·그리드격자선·간트막대·간트주공정·간트진척·오늘선·경고·오류·성공·주의반짝임·
    //       창외곽선(활성)·창외곽선(비활성)·창그림자(#AARRGGBB)·제목줄버튼오버·제목줄버튼켜짐·
    //       제목줄(항상위)·제목줄글리프·항상위켜짐글리프·캐시낮음·캐시높음·캐시100%·위치고정켜짐글리프·
    //       창 hairline(활성/비활성)·제목줄 버튼 오버/누름·닫기 오버 — Windows 11 풍 PD 설계값(공식 문서에 픽셀·색 값 없음, 05_창크롬_OS디자인_기준.md).
    //       밝은 테마는 검정 반투명, 어두운 테마는 흰색 반투명 hairline. 닫기 빨강 #C42B1C 는 PD 설계값.
    //       이어서 U6 기호 상태 색 10개(강조 띠 위 hover·누름·켜짐·닫기 hover·닫기 누름 → 표면색 위 같은 5개, 누름≠hover 채널합 차 ≥ 10)·
    //       U7·U19 탭 8개(창 탭 활성·활성글자·비활성·비활성글자·최소화글자·hover, 패널 선택·선택글자 — 활성·패널 선택 = MDI 제목줄 반전). PD 설계값(05_창크롬 §2.6).
    //       마지막 3개는 메인 창 타이틀바(활성 바탕·비활성 바탕·글자). MDI 제목줄 색을 출발점으로 하되 제목 줄 대비 3.0·글자 대비 4.5·비활성 글자 대비 3.0 을 맞춰 조정한 PD 설계값(공통원칙 §4.2, 판정 main_titlebar_contrast.py).
    //       밝게·Light(이전)는 활성 #40568D(MDI 제목줄과 같은 파랑)·비활성 #6E6E6E 이다.
    //       그 뒤 4개는 표면색 제목줄(강조 끔·macOS 스타일) 위 새로고침 적중률 색(0%·낮음·높음·100%). 밝은 면은 어두운 값, 어두운 면은 기존 Mars 값이며 대비 3.0 을 맞춘 PD 설계값이다.
    // pd-light 의 MDI 제목줄·크롬 색은 Mars 실측값(odev-1 §3.3: 제목줄 RGB 64,86,141, 비활성 Gray, 항상위 Maroon 등).
    // 창 외곽선(활성/비활성)은 Mars 형(MarsFrame) 1px 단색 전용 — pd-light 는 Mars 실측 #7988AF. ocean·forest 비활성 제목줄은 채도를 낮춤.
    // 이름은 다국어 사전 키(theme.builtin.<Id>, Q17) — 표시는 ThemeDefinition.DisplayName 이 현재 언어로 번역한다.
    private static readonly (string Id, string NameKey, ThemeBase Base, string Colors)[] Table =
    {
        // 기본 밝게 = Mars 실측 팔레트(odev-1 mars_palette_analysis.md, 색상값 인용): MDI 배경 #778899·제목줄/띠 #40568D·외곽선 #7988AF·본문·메뉴·툴바·상태바 #FFFFFF·
        // 글자 #000000·그리드 헤더 #F0F0F0·줄무늬 #F5F5F5·격자선 없음(투명)·입력 테두리 #ABADB3·창 목록 활성 탭 #40568D/#FFFFFF.
        // 선택 행: Mars 와 같게 바탕 흰색(PdBrushSelection #FFFFFF) + 위·아래 1px 선(PdBrushSelectionLine #40568D, odev-3 행 템플릿) — team-lead 00:0x 결정.
        // 미실측(PD 값): 보조 글자·간트·상태 색.
        (LightId, "theme.builtin.pd-light", ThemeBase.Light,
            "#FFFFFF,#FFFFFF,#F0F0F0,#000000,#5F6670,#40568D,#FFFFFF,#FFFFFF,#000000,#ABADB3,"
            + "#40568D,#F0F0F0,#FFFFFF,#40568D,#808080,#FFFFFF,#FFFFFF,#000000,"
            + "#F5F5F5,#F0F0F0,#00FFFFFF,#3B82F6,#DC2626,#1E40AF,#E11D48,#B45309,#DC2626,#15803D,#FFB000,"
            + "#7988AF,#5A5A5A,#70000000,#33FFFFFF,#59FFFFFF,"
            + "#800000,#C8C8C8,#FF4500,#FFFFE0,#FFD700,#FFA500,#00BFFF,"
            + "#4D000000,#26000000,#1A000000,#33000000,#C42B1C,#99FFFFFF,#778899,"
            + "#FFFFFF,#FFFFFF,#646464,#40568D,#FDFDFD,#D0D0D0,#ABADB3,"
            + "#FFF4CC,#2E1D00,#FFE066,#FF7A6E,#E0483A,#2F6FD6,#163F86,#2F6FD6,#C42B1C,#8E1F14,"
            + "#40568D,#FFFFFF,#F0F0F0,#000000,#5F6670,#1A000000,#40568D,#FFFFFF,"
            + "#40568D,#6E6E6E,#FFFFFF,#808080,#8F7A00,#A86800,#C04000,#D2000000"),
        // Light(이전) = Mars 팔레트 적용 전 기본 밝게(보존, team-lead 23:5x 지시)
        (PrevLightId, "theme.builtin.pd-light-prev", ThemeBase.Light,
            "#F5F6F8,#FFFFFF,#EEF0F3,#1F2328,#5F6670,#2563EB,#FFFFFF,#CFE0FF,#0B1F44,#D0D5DC,"
            + "#FFFFFF,#E6E9EE,#1F2328,#40568D,#808080,#FFFFFF,#2563EB,#FFFFFF,"
            + "#F7F9FC,#EBEEF2,#E1E5EA,#3B82F6,#DC2626,#1E40AF,#E11D48,#B45309,#DC2626,#15803D,#FFB000,"
            + "#7988AF,#5A5A5A,#70000000,#33FFFFFF,#59FFFFFF,"
            + "#800000,#C8C8C8,#FF4500,#FFFFE0,#FFD700,#FFA500,#00BFFF,"
            + "#4D000000,#26000000,#1A000000,#33000000,#C42B1C,#99FFFFFF,#F5F6F8,"
            + "#FFFFFF,#FFFFFF,#D0D5DC,#2563EB,#EEF0F3,#D0D5DC,#D0D5DC,"
            + "#FFF4CC,#2E1D00,#FFE066,#FF7A6E,#E0483A,#2F6FD6,#163F86,#2F6FD6,#C42B1C,#8E1F14,"
            + "#40568D,#FFFFFF,#E6E9EE,#1F2328,#5F6670,#1A000000,#40568D,#FFFFFF,"
            + "#40568D,#6E6E6E,#FFFFFF,#808080,#8F7A00,#A86800,#C04000,#D2000000"),
        (DarkId, "theme.builtin.pd-dark", ThemeBase.Dark,
            "#1E1F22,#2B2D30,#35373B,#DFE1E5,#9DA0A8,#4C8DF6,#FFFFFF,#2E436E,#FFFFFF,#43454A,"
            + "#2B2D30,#1E1F22,#FFFFFF,#3366CC,#4A4D52,#F0F0F0,#3D6FD1,#FFFFFF,"
            + "#26282B,#323438,#3A3C40,#4C8DF6,#F2545B,#8AB4FF,#FF6B81,#E0A23B,#F2545B,#4CC38A,#FFB000,"
            + "#5B79C9,#4A4D52,#B0000000,#33FFFFFF,#59FFFFFF,"
            + "#8B2A2A,#E8E8E8,#FF4500,#FFFFE0,#FFD700,#FFBB33,#00BFFF,"
            + "#59FFFFFF,#26FFFFFF,#1AFFFFFF,#33FFFFFF,#C42B1C,#1FFFFFFF,#1E1F22,"
            + "#2B2D30,#2B2D30,#43454A,#4C8DF6,#35373B,#43454A,#43454A,"
            + "#FFF4CC,#2E1D00,#FFFFFF,#FF7A6E,#E0483A,#8AB4FF,#4C8DF6,#8AB4FF,#F2545B,#C42B1C,"
            + "#3366CC,#F0F0F0,#1E1F22,#DFE1E5,#9DA0A8,#1AFFFFFF,#3366CC,#F0F0F0,"
            + "#3C72DC,#4A4D52,#FFFFFF,#C0C0C0,#FFFFE0,#FFD700,#FFA500,#A0FFFFFF"),
        ("pd-contrast", "theme.builtin.pd-contrast", ThemeBase.Dark,
            "#000000,#0A0A0A,#1A1A1A,#FFFFFF,#E0E0E0,#FFD400,#000000,#FFD400,#000000,#FFFFFF,"
            + "#000000,#000000,#FFD400,#FFD400,#A0A0A0,#000000,#FFD400,#000000,"
            + "#141414,#1F1F1F,#8A8A8A,#00E5FF,#FF3B3B,#FFFFFF,#FF00FF,#FFD400,#FF3B3B,#3BFF6B,#FF00FF,"
            + "#FFD400,#A0A0A0,#C0000000,#73000000,#80000000,"
            + "#00E5FF,#000000,#C00000,#4D4D00,#806000,#C04000,#0050C0,"
            + "#FFD400,#A0A0A0,#33FFFFFF,#66FFFFFF,#FF3B3B,#66FFFFFF,#000000,"
            + "#0A0A0A,#0A0A0A,#FFFFFF,#FFD400,#1A1A1A,#FFFFFF,#FFFFFF,"
            + "#00007A,#FFFFFF,#5A0A3E,#FF3B3B,#800000,#FFD400,#FF8C00,#00E5FF,#FF3B3B,#C00000,"
            + "#FFD400,#000000,#000000,#FFFFFF,#E0E0E0,#33FFFFFF,#FFD400,#000000,"
            + "#FFD400,#A0A0A0,#000000,#C0C0C0,#FFFFE0,#FFD700,#FFA500,#B2000000"),
        ("pd-ocean", "theme.builtin.pd-ocean", ThemeBase.Light,
            "#EEF5FB,#FFFFFF,#E0ECF6,#10263B,#4D6478,#0B6FA4,#FFFFFF,#BFE0F5,#0A2A40,#C3D6E6,"
            + "#FFFFFF,#D7E6F2,#0B4F75,#0B6FA4,#8E979E,#FFFFFF,#0B5F8C,#FFFFFF,"
            + "#F3F8FC,#DCEAF5,#D3E2EE,#1C8BC7,#D64545,#0B4F75,#E0457B,#C47F12,#D64545,#1F9D6B,#F59E0B,"
            + "#0B4F75,#8FA9BE,#600B2A40,#33FFFFFF,#59FFFFFF,"
            + "#800000,#E8E8E8,#FF4500,#FFFFE0,#FFD700,#FFB733,#7CFC00,"
            + "#4D000000,#26000000,#1A000000,#33000000,#C42B1C,#99FFFFFF,#EEF5FB,"
            + "#FFFFFF,#FFFFFF,#C3D6E6,#0B6FA4,#E0ECF6,#C3D6E6,#C3D6E6,"
            + "#FFF4CC,#2E1D00,#FFE066,#FF7A6E,#E0483A,#2F6FD6,#163F86,#2F6FD6,#C42B1C,#8E1F14,"
            + "#0B6FA4,#FFFFFF,#D7E6F2,#10263B,#4D6478,#1A000000,#0B6FA4,#FFFFFF,"
            + "#0B6FA4,#6B757C,#FFFFFF,#808080,#8F7A00,#A86800,#C04000,#B8000000"),
        ("pd-ember", "theme.builtin.pd-ember", ThemeBase.Dark,
            "#231C18,#2E2520,#3A2F28,#F1E6DA,#B9A794,#E8853D,#1A120C,#5A3B24,#FFF4E8,#4A3C33,"
            + "#2E2520,#231C18,#FFD9B8,#B85A22,#4F4038,#FFF4E8,#B85A22,#FFF4E8,"
            + "#2A221D,#352B25,#3E332C,#E8A23D,#E5484D,#FFD08A,#FF6F91,#F2B33D,#E5484D,#7CC47F,#FFB000,"
            + "#E8853D,#5A4A40,#B0000000,#33FFFFFF,#59FFFFFF,"
            + "#7A1F1F,#F8F8F8,#FF4500,#FFFFE0,#FFE066,#FFCC33,#00BFFF,"
            + "#59FFFFFF,#26FFFFFF,#1AFFFFFF,#33FFFFFF,#C42B1C,#1FFFFFFF,#231C18,"
            + "#2E2520,#2E2520,#4A3C33,#E8853D,#3A2F28,#4A3C33,#4A3C33,"
            + "#00007A,#2E1D00,#FFE066,#FF9A8B,#E5484D,#E8A23D,#FFE9D2,#FFD08A,#F2545B,#C42B1C,"
            + "#B85A22,#FFF4E8,#231C18,#F1E6DA,#B9A794,#1AFFFFFF,#B85A22,#FFF4E8,"
            + "#B85A22,#5E4D44,#FFFFFF,#C0C0C0,#FFFFE0,#FFD700,#FFA500,#94FFFFFF"),
        ("pd-forest", "theme.builtin.pd-forest", ThemeBase.Light,
            "#F3F6F1,#FFFFFF,#E6EEE2,#1E2A1F,#5A6B5C,#2F7D4F,#FFFFFF,#CDE7D5,#123321,#C8D5C6,"
            + "#FFFFFF,#DDE7D9,#1F5434,#2F7D4F,#969B97,#FFFFFF,#2F6B47,#FFFFFF,"
            + "#F6F9F4,#E3ECDF,#D7E2D3,#3E9A63,#C2410C,#1F5434,#B91C1C,#A16207,#C2410C,#15803D,#D97706,"
            + "#1F5434,#9AAE9E,#60123321,#33FFFFFF,#59FFFFFF,"
            + "#800000,#F0F0F0,#FF4500,#FFFFE0,#FFD700,#FFC04D,#00BFFF,"
            + "#4D000000,#26000000,#1A000000,#33000000,#C42B1C,#99FFFFFF,#F3F6F1,"
            + "#FFFFFF,#FFFFFF,#C8D5C6,#2F7D4F,#E6EEE2,#C8D5C6,#C8D5C6,"
            + "#00007A,#2E1D00,#FFE066,#FF7A6E,#E0483A,#2F6FD6,#163F86,#2F6FD6,#C42B1C,#8E1F14,"
            + "#2F7D4F,#FFFFFF,#DDE7D9,#1E2A1F,#5A6B5C,#1A000000,#2F7D4F,#FFFFFF,"
            + "#2F7D4F,#6E7570,#FFFFFF,#808080,#8F7A00,#A86800,#C04000,#B6000000"),
        // 디자인 테마 추천 색(REQ-104) — 아기자기(ds-cute)용 밝은 파스텔·화려(ds-rich)용 어두운 오로라. 본문 대비 4.5:1·강조 위 글자 4.5:1 이상 확인(설계 18 §색 직교).
        ("pd-pastel", "theme.builtin.pd-pastel", ThemeBase.Light,
            "#FFF7FB,#FFFFFF,#F6EAF5,#2B2A3A,#6B6480,#7C5CBF,#FFFFFF,#E9DDFB,#2B1A4D,#E3D3EA,"
            + "#FFFFFF,#F0E4F0,#2B2A3A,#40568D,#808080,#FFFFFF,#7C5CBF,#FFFFFF,"
            + "#FCF3FA,#F3E6F1,#EAD9E8,#9B7FE0,#DC2626,#5B3FA8,#E11D48,#B45309,#DC2626,#15803D,#FFB000,"
            + "#7988AF,#5A5A5A,#70000000,#33FFFFFF,#59FFFFFF,"
            + "#800000,#C8C8C8,#FF4500,#FFFFE0,#FFD700,#FFA500,#00BFFF,"
            + "#4D000000,#26000000,#1A000000,#33000000,#C42B1C,#99FFFFFF,#FFF7FB,"
            + "#FFFFFF,#FFFFFF,#E3D3EA,#7C5CBF,#F6EAF5,#E3D3EA,#E3D3EA,"
            + "#FFF4CC,#2E1D00,#FFE066,#FF7A6E,#E0483A,#2F6FD6,#163F86,#2F6FD6,#C42B1C,#8E1F14,"
            + "#40568D,#FFFFFF,#F0E4F0,#2B2A3A,#6B6480,#1A000000,#40568D,#FFFFFF,"
            + "#40568D,#6E6E6E,#FFFFFF,#808080,#8F7A00,#A86800,#C04000,#D2000000"),
        // pd-aurora 타이틀바 활성 #6A5BD8 = 제목 줄 #1D1840 대비 3.26·흰 글자 5.12(K11 titlebar_static 기준 3.0·4.5, 구 #5B4DC8 은 2.65)
        ("pd-aurora", "theme.builtin.pd-aurora", ThemeBase.Dark,
            "#14112B,#1D1840,#2A2358,#EEEBFF,#A9A4D6,#6D5BE0,#FFFFFF,#3A3380,#FFFFFF,#3B3470,"
            + "#1D1840,#14112B,#FFFFFF,#4B3FB0,#4A4D52,#F0F0F0,#5B4DC8,#FFFFFF,"
            + "#1A1638,#241E4D,#30296A,#6D5BE0,#F2545B,#8AB4FF,#FF6B81,#E0A23B,#F2545B,#4CC38A,#FFB000,"
            + "#7C6BE0,#4A4D52,#B0000000,#33FFFFFF,#59FFFFFF,"
            + "#8B2A2A,#E8E8E8,#FF4500,#FFFFE0,#FFD700,#FFBB33,#00BFFF,"
            + "#59FFFFFF,#26FFFFFF,#1AFFFFFF,#33FFFFFF,#C42B1C,#1FFFFFFF,#14112B,"
            + "#1D1840,#1D1840,#3B3470,#6D5BE0,#2A2358,#3B3470,#3B3470,"
            + "#FFF4CC,#2E1D00,#FFFFFF,#FF7A6E,#E0483A,#8AB4FF,#6D5BE0,#8AB4FF,#F2545B,#C42B1C,"
            + "#4B3FB0,#F0F0F0,#14112B,#EEEBFF,#A9A4D6,#1AFFFFFF,#4B3FB0,#F0F0F0,"
            + "#6A5BD8,#4A4D52,#FFFFFF,#C0C0C0,#FFFFE0,#FFD700,#FFA500,#A0FFFFFF"),
    };

    /// <summary>내장 테마 새 인스턴스 목록(IsBuiltin=true). 색 개수가 토큰 수와 다르면 개발 오류로 예외.</summary>
    public static IReadOnlyList<ThemeDefinition> Create()
    {
        var keys = ThemeTokens.Keys.ToArray();
        var list = new List<ThemeDefinition>(Table.Length);
        foreach (var (id, nameKey, @base, colors) in Table)
        {
            var values = colors.Split(',');
            if (values.Length != keys.Length)
            {
                throw new InvalidOperationException($"내장 테마 {id}: 색 {values.Length}개 ≠ 토큰 {keys.Length}개"); // i18n-ok 개발 오류(테이블 불일치)
            }

            // Name = 생성 시점 언어의 이름(복제본 기본 이름의 원천), 표시는 NameKey 로 매번 현재 언어.
            var theme = new ThemeDefinition { Id = id, Name = Loc.Tr(nameKey), NameKey = nameKey, Base = @base, IsBuiltin = true };
            for (var i = 0; i < keys.Length; i++)
            {
                theme.Colors[keys[i]] = values[i];
            }

            list.Add(theme);
        }

        return list;
    }
}
