// 그리드 벤치 행(20열, 한글·숫자·날짜·통화) — 실시간 갱신 열은 INotifyPropertyChanged 로 바뀐 셀만 다시 그린다
using Pd.Poc.Shell;
using System;
using System.ComponentModel;
using System.Globalization;

namespace Pd.Ui.Views.Documents.Bench;

/// <summary>
/// 벤치 행. 값은 시드 고정 난수로 만든다(실행마다 같은 화면). 고정 텍스트 열은 생성 때 문자열로 미리 만들어
/// 셀 그리기 때 서식 비용이 들지 않게 한다. 실시간 열(Price·Change·Volume)만 값이 바뀌고 PropertyChanged 를 낸다.
/// </summary>
public sealed class BenchRow : INotifyPropertyChanged
{
    private static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황", "안", "송", "류", "홍" };
    // 이름 = 성(20) × 첫 음절(30) × 둘째 음절(30) = 18,000 조합. 이름 16개 고정이면 320 조합뿐이라 정렬·필터 시험이 약했다(otest-1 b1126: head 10 이 모두 같은 이름).
    // 받침 유무·초성이 고르게 섞이게 골랐다. 약 3% 는 영문 이름(대소문자 섞음)으로 넣어 언어 규칙 정렬(a < B)도 함께 본다.
    private static readonly string[] GivenFirst = { "가", "강", "건", "경", "나", "남", "다", "도", "동", "라", "민", "보", "상", "서", "성", "수", "승", "아", "연", "영", "예", "유", "윤", "은", "재", "정", "지", "하", "현", "희" };
    private static readonly string[] GivenSecond = { "람", "린", "빈", "아", "연", "온", "우", "원", "윤", "율", "은", "음", "인", "재", "준", "진", "찬", "채", "하", "한", "현", "호", "환", "후", "훈", "희", "솔", "별", "결", "담" };
    private static readonly string[] LatinNames = { "alex Kim", "Brian Lee", "chloe Park", "Daniel Choi", "emma Jung", "Zoe Han", "noah Yoon", "Olivia Kang" };
    private static readonly string[] Depts = { "개발1팀", "개발2팀", "품질보증팀", "기획팀", "영업본부", "경영지원팀", "인프라팀", "디자인팀" };
    private static readonly string[] Statuses = { "진행", "완료", "대기", "지연", "보류" };
    private static readonly string[] Projects = { "차세대 PMS 구축", "모바일 앱 고도화", "ERP 연계", "데이터 이관", "보안 점검", "클라우드 전환", "AI 상담 도입", "Mars 유지보수" };
    private static readonly string[] Regions = { "서울", "부산", "대구", "인천", "광주", "대전", "울산", "세종", "경기", "제주" };
    private static readonly string[] Grades = { "사원", "주임", "대리", "과장", "차장", "부장" };
    private static readonly string[] Customers = { "한빛전자", "가온물산", "누리통신", "다온건설", "라온제약", "마루식품", "Acme Korea", "bright솔루션", "세움금융", "아라해운" };
    private static readonly string[] Notes = { "검토 요청", "고객 회의 예정", "산출물 보완", "일정 재조정", "", "리스크 보고", "인수인계 완료", "" };

    private double _price;
    private double _change;
    private long _volume;
    private string _priceText = "";
    private string _changeText = "";
    private string _volumeText = "";

    public BenchRow(int no, Random rnd)
    {
        var inv = CultureInfo.InvariantCulture;
        No = no;
        NoText = no.ToString("N0", inv);
        Code = $"PRJ-{2026 - (no % 3)}-{no:000000}";
        Name = rnd.Next(100) < 3
            ? LatinNames[rnd.Next(LatinNames.Length)]
            : Surnames[rnd.Next(Surnames.Length)] + GivenFirst[rnd.Next(GivenFirst.Length)] + GivenSecond[rnd.Next(GivenSecond.Length)];
        Dept = Depts[rnd.Next(Depts.Length)];
        Grade = Grades[rnd.Next(Grades.Length)];
        Project = Projects[rnd.Next(Projects.Length)];
        Status = Statuses[rnd.Next(Statuses.Length)];
        Region = Regions[rnd.Next(Regions.Length)];
        Start = new DateTime(2026, 1, 1).AddDays(rnd.Next(300));
        End = Start.AddDays(7 + rnd.Next(180));
        StartText = Start.ToString("yyyy-MM-dd", inv);
        EndText = End.ToString("yyyy-MM-dd", inv);
        Progress = rnd.Next(101);
        ProgressText = Progress + "%";
        Hours = Math.Round(rnd.NextDouble() * 400, 1);
        HoursText = Hours.ToString("N1", inv);
        Budget = rnd.Next(1, 5000) * 100_000L;
        BudgetText = "₩" + Budget.ToString("N0", inv);
        Rate = Math.Round(rnd.NextDouble() * 30 - 10, 2);
        RateText = Rate.ToString("+0.00;-0.00;0.00", inv) + "%";
        Note = Notes[rnd.Next(Notes.Length)];
        Customer = Customers[rnd.Next(Customers.Length)];
        Email = $"user{no:000000}@pd.example";
        SetLive(Math.Round(10_000 + (rnd.NextDouble() * 90_000), 0), 0, rnd.Next(1_000, 1_000_000), notify: false);
    }

    public int No { get; }
    public string NoText { get; }
    public string Code { get; }
    public string Name { get; }
    public string Dept { get; }
    public string Grade { get; }
    public string Project { get; }
    public string Status { get; private set; } // 실시간 중 Status 변경 시나리오(statusPct)에서만 SetStatus 로 바뀐다
    public string Region { get; }
    public DateTime Start { get; }
    public DateTime End { get; }
    public string StartText { get; }
    public string EndText { get; }
    public int Progress { get; }
    public string ProgressText { get; }
    public double Hours { get; }
    public string HoursText { get; }
    public long Budget { get; }
    public string BudgetText { get; }
    public double Rate { get; }
    public string RateText { get; }
    public string Note { get; }
    public string Customer { get; }
    public string Email { get; }

    // 실시간 열(시세판처럼 값이 계속 바뀐다)
    public double Price => _price;
    public double Change => _change;
    public long Volume => _volume;
    public string PriceText => _priceText;
    public string ChangeText => _changeText;
    public string VolumeText => _volumeText;

    /// <summary>바뀐 횟수(반짝임 표시·검증용).</summary>
    public int Ticks { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly PropertyChangedEventArgs PriceTextArgs = new(nameof(PriceText));
    private static readonly PropertyChangedEventArgs ChangeTextArgs = new(nameof(ChangeText));
    private static readonly PropertyChangedEventArgs VolumeTextArgs = new(nameof(VolumeText));
    private static readonly PropertyChangedEventArgs PriceArgs = new(nameof(Price));
    private static readonly PropertyChangedEventArgs ChangeArgs = new(nameof(Change));
    private static readonly PropertyChangedEventArgs StatusArgs = new(nameof(Status));

    /// <summary>상태 변경(실시간 중 일부 행의 Status 변경 — 강조가 따라가는지 검증용). 값이 같으면 알리지 않는다.</summary>
    public void SetStatus(string status)
    {
        if (Status == status) { return; }
        Status = status;
        PropertyChanged?.Invoke(this, StatusArgs);
    }

    /// <summary>실시간 값 갱신. 셀 3개(가격·등락·거래량)가 바뀐다.</summary>
    public void SetLive(double price, double change, long volume, bool notify = true)
    {
        var inv = CultureInfo.InvariantCulture;
        _price = price;
        _change = change;
        _volume = volume;
        _priceText = price.ToString("N0", inv);
        _changeText = change.ToString("+#,0;-#,0;0", inv);
        _volumeText = volume.ToString("N0", inv);
        if (!notify) { return; }
        Ticks++;
        var h = PropertyChanged;
        if (h is null) { return; }
        h(this, PriceArgs);
        h(this, ChangeArgs);
        h(this, PriceTextArgs);
        h(this, ChangeTextArgs);
        h(this, VolumeTextArgs);
    }
}
