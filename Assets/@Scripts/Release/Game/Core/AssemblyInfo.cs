// 내부 접근 허용은 에디터와 Debug 빌드에만 두고 릴리즈 메타데이터에는 넣지 않는다.
#if UNITY_EDITOR || DEBUG
using System.Runtime.CompilerServices;
// EditMode 테스트가 LocalFileStorage의 실패 주입 생성자 같은 내부 멤버를 쓰고, LeakTracker 같은 내부 형식을 리플렉션으로 읽는다.
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
#endif
