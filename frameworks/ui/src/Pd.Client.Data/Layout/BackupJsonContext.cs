// 백업 묶음·백업 목록의 System.Text.Json 소스 생성 컨텍스트(생성기 연동용 단일 파일 partial)
using System.Text.Json.Serialization;

namespace Pd.Client.Data.Layout;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(BackupBundle))]
[JsonSerializable(typeof(BackupIndex))]
public sealed partial class BackupJsonContext : JsonSerializerContext
{
}
