// 문서 창 크롬 스타일 종류 — 설정·원격·호스트 인자가 고르는 값(저장값은 이 순서의 정수라 새 값은 맨 끝에만 더한다)
namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>문서 창 크롬 스타일.</summary>
public enum ChromeStyleKind
{
    /// <summary>Windows 11 풍: 둥근 모서리 8px·1px 반투명 hairline·2겹 그림자·32px 제목줄·오른쪽 넓은 캡션 버튼.</summary>
    Windows,

    /// <summary>macOS 풍: 왼쪽 신호등(닫기·최소·확대)·가운데 제목·표면색 28px 제목줄(수치는 PD 설계값, CaptionLayout).</summary>
    MacOS,

    /// <summary>Mars 클래식: 남색 23px 제목줄·여백 띠 3,4,2,2·1px 단색 외곽선·그림자 없음(Mars 실측 수치 인용, odev-1 §3.3/§4.1).</summary>
    MarsClassic,

    /// <summary>자동(기본): 실행 OS 를 따른다 — macOS·iOS 는 MacOS, 그 밖은 Windows(ChromePlatform.PlatformDefault).</summary>
    Auto,
}
