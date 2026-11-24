// 그리드 한 행과 결정적 샘플 생성기 — 행 값은 처음 읽힐 때 만든다(지연 생성, 웹 인터프리터에서 1만 행 일괄 생성 비용 회피)
using Pd.Poc.Shell;
using System;
using System.Globalization;

namespace Pd.Ui.Views.Documents;

/// <summary>
/// 시드 고정 난수열(20261028)로 행 원시값을 순서대로 만든다. 행 i 의 값은 1..i 를 차례로 뽑아야 나오므로(이전과 같은 화면),
/// 원시 정수만 앞에서부터 필요한 만큼 뽑아 두고 문자열은 행마다 처음 읽힐 때 만든다. UI 스레드 전용.
/// </summary>
internal sealed class GridSeed
{
    private static readonly string[] Owners = { "김민수", "이서연", "박지훈", "최유진", "정다은", "한성호" }; // i18n-ok 샘플 데이터(사람 이름)
    private static readonly string[] Statuses = { "대기", "진행", "검토", "완료", "보류" }; // i18n-ok 샘플 데이터(작업 상태 값 — 실제 구현에서는 코드값+사전)
    private static readonly string[] Verbs = { "설계", "구현", "시험", "배포", "검토", "문서화" }; // i18n-ok 샘플 데이터(작업 이름 조각)
    private static readonly string[] Targets = { "로그인 화면", "일정 API", "간트 차트", "결재선", "알림", "보고서" }; // i18n-ok 샘플 데이터(작업 이름 조각)
    private static readonly DateTime BaseDate = new(2026, 1, 5);

    private readonly Random _rnd = new(20261028);
    private readonly Raw[] _raw;
    private int _generated; // 원시값을 뽑아 둔 행 수(1-기준 마지막 행 번호)

    public GridSeed(int count) => _raw = new Raw[count];

    private struct Raw
    {
        public int StartDay;
        public int Duration;
        public int Target;
        public int Verb;
        public int Owner;
        public int Status;
        public int Progress;
        public double Hours;
    }

    /// <summary>행 no(1-기준)의 값을 row 에 채운다. 난수 소비 순서는 이전 일괄 생성과 같다(시작일→기간→대상→동사→담당자→상태→진척→공수).</summary>
    public void Fill(GridRow row, int no)
    {
        Avalonia.Threading.Dispatcher.UIThread.VerifyAccess(); // 난수열·캐시는 스레드 안전하지 않다 — UI 스레드 밖에서 읽으면 즉시 예외(조용한 값 오염 방지)
        while (_generated < no)
        {
            ref var r = ref _raw[_generated++];
            r.StartDay = _rnd.Next(0, 365);
            r.Duration = _rnd.Next(1, 30);
            r.Target = _rnd.Next(Targets.Length);
            r.Verb = _rnd.Next(Verbs.Length);
            r.Owner = _rnd.Next(Owners.Length);
            r.Status = _rnd.Next(Statuses.Length);
            r.Progress = _rnd.Next(0, 101);
            r.Hours = Math.Round(_rnd.NextDouble() * 80, 1);
        }

        var v = _raw[no - 1];
        var start = BaseDate.AddDays(v.StartDay);
        var end = start.AddDays(v.Duration);
        row.SetValues(
            code: $"TSK-{no:00000}",
            name: $"{Targets[v.Target]} {Verbs[v.Verb]} {no}",
            owner: Owners[v.Owner],
            status: Statuses[v.Status],
            start: start,
            end: end,
            progress: v.Progress,
            hours: v.Hours,
            note: no % 7 == 0 ? "선행 작업 확인 필요" : ""); // i18n-ok 샘플 데이터(비고 값)
    }
}

/// <summary>그리드 한 행(작업 목록 형태 샘플). 10열. 값은 속성을 처음 읽을 때 GridSeed 에서 채운다.</summary>
public sealed class GridRow
{
    private readonly GridSeed _seed;
    private bool _filled;
    private string _code = "", _name = "", _owner = "", _status = "", _startText = "", _endText = "", _progressText = "", _hoursText = "", _note = "";
    private DateTime _start, _end;
    private int _progress;
    private double _hours;

    internal GridRow(GridSeed seed, int no)
    {
        _seed = seed;
        No = no;
        NoText = no.ToString(CultureInfo.InvariantCulture);
    }

    public int No { get; }
    public string Code => Fill()._code;
    public string Name => Fill()._name;
    public string Owner => Fill()._owner;
    public string Status => Fill()._status;
    public DateTime Start => Fill()._start;
    public DateTime End => Fill()._end;

    /// <summary>표시용 날짜 문자열(셀마다 StringFormat 변환을 피하려고 미리 계산, yyyy-MM-dd 라 문자열 정렬 = 날짜 정렬).</summary>
    public string StartText => Fill()._startText;
    public string EndText => Fill()._endText;

    /// <summary>숫자 열 표시 문자열. 웹(WASM 트리밍)에서 int/double→문자열 리플렉션 변환이 빈칸이 된 관찰 대응. 정렬은 SortMemberPath 로 숫자 속성 사용.</summary>
    public string NoText { get; }
    public string ProgressText => Fill()._progressText;
    public string HoursText => Fill()._hoursText;
    public int Progress => Fill()._progress;
    public double Hours => Fill()._hours;
    public string Note => Fill()._note;

    /// <summary>값이 채워졌는가(계측·시험용).</summary>
    internal bool IsFilled => _filled;

    internal GridRow Fill()
    {
        if (!_filled)
        {
            _filled = true;
            _seed.Fill(this, No);
        }

        return this;
    }

    internal void SetValues(string code, string name, string owner, string status, DateTime start, DateTime end, int progress, double hours, string note)
    {
        (_code, _name, _owner, _status, _start, _end, _progress, _hours, _note) = (code, name, owner, status, start, end, progress, hours, note);
        _startText = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _endText = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _progressText = progress.ToString(CultureInfo.InvariantCulture);
        _hoursText = hours.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
