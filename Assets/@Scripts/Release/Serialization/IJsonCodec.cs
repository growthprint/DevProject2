#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace DevProject.Serialization
{
    // 사용처가 JSON 라이브러리를 몰라도 되도록 형식별 변환기를 생성자로 받아 쓴다. 실패하면 out 값이 null이므로 T는 참조 형식이다.
    /// <summary>
    /// 데이터 형식 하나의 동기 JSON 변환 계약. 읽기는 새 데이터를 반환하고 쓰기는 입력을 변경하지 않는다.
    /// 호출자는 변환 중 입력을 변경하지 않으며, 구현은 서로 독립된 데이터의 동시 호출을 지원한다.
    /// </summary>
    public interface IJsonCodec<T> where T : class
    {
        // 손상된 입력은 예외 없이 false와 경로가 붙은 첫 오류 문구로 알린다. 지원하지 않는 저장 버전도 따로 구분하지 않는다.
        // 성공 값은 null이 아니고 형식 자체 검증(예: DuckAvatarPreset.TryValidate)도 통과했으므로 다시 검증할 필요가 없다.
        bool TryRead(string json, [NotNullWhen(true)] out T? value, out string error);

        // 값 검증은 쓰기 전에 저장 명령이 맡고 호출자는 검증된 값만 넘기므로 다시 검증하지 않는다.
        // 저장 위치나 플랫폼 API는 다루지 않고 JSON 문자열만 돌려준다.
        string Write(T value, bool pretty = false);
    }
}
