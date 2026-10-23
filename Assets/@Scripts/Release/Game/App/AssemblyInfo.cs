// 내부 멤버 공개는 Editor와 Unity 6.6의 Debug 코드 빌드에서만 연다.
#if UNITY_EDITOR || DEBUG
using System.Runtime.CompilerServices;
// 제작 도구, 개발 어셈블리, 테스트 어셈블리만 내부 초기화 경계에 접근한다.
[assembly: InternalsVisibleTo("Game.Editor")]
[assembly: InternalsVisibleTo("Game.Development")]
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
[assembly: InternalsVisibleTo("Game.Tests.PlayMode")]
// 캐릭터 내부 계약이 Game.App에 있으므로 DuckCharacter 제작 도구와 테스트에도 공개한다.
[assembly: InternalsVisibleTo("DuckCharacter.Editor")]
[assembly: InternalsVisibleTo("DuckCharacter.Tests.EditMode")]
#endif
