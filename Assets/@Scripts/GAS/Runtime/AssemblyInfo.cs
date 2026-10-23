// 내부 멤버 공개는 Editor와 Debug 코드 빌드에서만 연다. 릴리즈 메타데이터에는 넣지 않는다.
#if UNITY_EDITOR || DEBUG
using System.Runtime.CompilerServices;
// 태그 드롭다운 같은 제작 도구와 프레임워크 테스트, 게임 통합 테스트만 내부 계약에 접근한다.
[assembly: InternalsVisibleTo("Game.GAS.Editor")]
[assembly: InternalsVisibleTo("Game.GAS.Tests.EditMode")]
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
#endif
