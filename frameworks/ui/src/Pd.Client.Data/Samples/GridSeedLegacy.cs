// 지연 생성 이전의 일괄 시드 알고리즘을 그대로 재현해 값 해시만 내는 대조용 코드(진단 빌드 전용, grid-hash 명령)
#if PD_DIAGNOSTICS
using System;
using System.Globalization;

namespace Pd.Client.Data.Samples;

/// <summary>
/// 지연 생성 도입(2026-10-29 03:3x) 전 GridDocumentViewModel.CreateSeed 의 생성 루프를 문장 그대로 옮긴 것(난수 소비 순서·형식 문자열 동일).
/// GridRow 를 만들지 않고 같은 형식으로 해시만 계산한다 — 새 GridSeed(lazy/eager) 결과와 비교해 값이 예전과 같은지 실행으로 증명한다.
/// </summary>
public static class GridSeedLegacy
{
    private static readonly string[] Owners = { "김민수", "이서연", "박지훈", "최유진", "정다은", "한성호" }; // i18n-ok 샘플 데이터(사람 이름)
    private static readonly string[] Statuses = { "대기", "진행", "검토", "완료", "보류" }; // i18n-ok 샘플 데이터(작업 상태 값)
    private static readonly string[] Verbs = { "설계", "구현", "시험", "배포", "검토", "문서화" }; // i18n-ok 샘플 데이터(작업 이름 조각)
    private static readonly string[] Targets = { "로그인 화면", "일정 API", "간트 차트", "결재선", "알림", "보고서" }; // i18n-ok 샘플 데이터(작업 이름 조각)

    public static string Hash(int rowCount)
    {
        var h = 14695981039346656037UL;
        void Add(string s) { foreach (var c in s) { h = (h ^ c) * 1099511628211UL; } h = (h ^ 0x1F) * 1099511628211UL; }
        var inv = CultureInfo.InvariantCulture;
        var rnd = new Random(20261028);
        var baseDate = new DateTime(2026, 1, 5);
        for (var i = 1; i <= rowCount; i++)
        {
            var start = baseDate.AddDays(rnd.Next(0, 365));
            var end = start.AddDays(rnd.Next(1, 30));
            var name = $"{Targets[rnd.Next(Targets.Length)]} {Verbs[rnd.Next(Verbs.Length)]} {i}";
            var owner = Owners[rnd.Next(Owners.Length)];
            var status = Statuses[rnd.Next(Statuses.Length)];
            var progress = rnd.Next(0, 101);
            var hours = Math.Round(rnd.NextDouble() * 80, 1);
            var note = i % 7 == 0 ? "선행 작업 확인 필요" : ""; // i18n-ok 샘플 데이터(비고 값)
            Add(i.ToString(inv)); Add(i.ToString(CultureInfo.InvariantCulture)); Add($"TSK-{i:00000}"); Add(name); Add(owner); Add(status);
            Add(start.ToString("o", inv)); Add(end.ToString("o", inv));
            Add(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); Add(end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(progress.ToString(inv)); Add(progress.ToString(CultureInfo.InvariantCulture));
            Add(hours.ToString("R", inv)); Add(hours.ToString("0.0", CultureInfo.InvariantCulture)); Add(note);
        }

        return h.ToString("x16", inv);
    }
}
#endif
