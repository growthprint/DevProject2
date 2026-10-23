#nullable enable annotations
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.Core
{
    // Release 코드가 로그를 남기고 결함을 검사하는 유일한 입구다. 에디터와 DEBUG 빌드는 DebugLog를 써서
    // 검사가 실패하면 예외로 멈추고, 그 밖의 빌드는 ReleaseLog를 써서 위치마다 한 번 기록하고 계속한다.
    // DEBUG는 Managed Code Variant가 Debug인 빌드다. Unity 6.6에서 폐기 예정인 DEVELOPMENT_BUILD는 쓰지 않는다.
    public abstract class Log
    {
        // 게임 전체가 쓰는 하나뿐인 인스턴스다. 테스트용 LogTestScope가 리플렉션으로 이 필드를 바꾸므로
        // 이름을 바꾸거나 readonly로 만들지 않는다.
        static Log _instance = Create();

        public static Log Instance => _instance;

        // DebugLog는 릴리즈 실행 파일에 포함되지 않도록 같은 조건부 컴파일 안에서만 만든다.
        static Log Create()
        {
#if UNITY_EDITOR || DEBUG
            return new DebugLog();
#else
            return new ReleaseLog();
#endif
        }

        // 일반 로그는 두 구현이 똑같이 남긴다. Action<Exception> 같은 대리자로 바로 넘길 수 있게 선택 인수 대신 오버로드로 둔다.
        public void Warning(object message) => UnityEngine.Debug.LogWarning(message);

        public void Warning(object message, UnityEngine.Object context) => UnityEngine.Debug.LogWarning(message, context);

        public void Error(object message) => UnityEngine.Debug.LogError(message);

        public void Error(object message, UnityEngine.Object context) => UnityEngine.Debug.LogError(message, context);

        // 이 메서드 이름이 Exception 형식을 가리므로 매개변수 형식은 System.Exception으로 쓴다.
        public void Exception(System.Exception exception) => UnityEngine.Debug.LogException(exception);

        public void Exception(System.Exception exception, UnityEngine.Object context) => UnityEngine.Debug.LogException(exception, context);

        // 빌드마다 동작이 다른 검사다. 인수는 모든 빌드에서 평가된다.
        // 호출 위치는 컴파일러가 채우고, 이 메서드를 감싸는 도우미는 받은 호출 위치를 그대로 넘긴다.
        public abstract void Assert(bool condition, string? message = null, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0);

        // 매 프레임 부르거나 비용이 큰 검사에 쓴다. 릴리즈 빌드에서는 호출과 인수 평가가 통째로 컴파일에서 빠지므로
        // 괄호 안에 꼭 실행돼야 하는 코드를 넣지 않는다.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG")]
        public static void Check(bool condition, string? message = null, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
            => _instance.Assert(condition, message, context, callerFile, callerLine);

        // 코드 결함을 throw 대신 보고한다. 에디터와 DEBUG 빌드는 받은 예외를 로그 없이 그대로 던지고, 경계의 catch나 Unity가 한 번 기록한다.
        // 릴리즈 빌드는 위치와 문구마다 Assert 로그를 한 번 남긴다. 항상 false라서 `return Log.Instance.Fail(...)`로 실패를 돌려준다.
        // 돌려줄 수 없는 자리에서는 다음 줄이 안전하게 계속되는지 확인한다.
        public abstract bool Fail(System.Exception exception, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0);

        // 손상된 저장이나 옵션, 붙여넣은 외형 JSON처럼 외부 데이터의 오류를 보고한다. 모든 빌드가 Warning 로그를 남기고,
        // 에디터와 DEBUG 빌드는 이어서 예외를 던지며 릴리즈 빌드는 false를 돌려준다. 데이터를 읽는 소유자가 읽기마다 한 번만 부른다.
        public abstract bool InvalidData(System.Exception exception, UnityEngine.Object? context = null);

        // Fail과 InvalidData의 로그 문구다. "형식: 메시지" 뒤에 안쪽 예외를 " ---> 형식: 메시지"로 이어 붙인다.
        protected static string Describe(System.Exception exception)
        {
            var text = new System.Text.StringBuilder();
            for (System.Exception? current = exception; current != null; current = current.InnerException)
            {
                if (text.Length > 0)
                    text.Append(" ---> ");
                text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return text.ToString();
        }

        // 메시지가 없거나 위치를 덧붙일 때 쓰는 "파일명:줄" 표기다. 전체 경로 대신 파일명만 남긴다.
        protected static string Location(string callerFile, int callerLine)
            => System.IO.Path.GetFileName(callerFile) + ":" + callerLine;

        // 검사 실패를 LogType.Assert로 남긴다. Debug.LogAssertion은 릴리즈 빌드에서 호출이 빠지므로 unityLogger를 직접 쓴다.
        // (object) 변환이 없으면 Log(LogType, string tag, object) 오버로드와 모호해진다.
        protected static void WriteAssert(string text, UnityEngine.Object? context)
            => UnityEngine.Debug.unityLogger.Log(UnityEngine.LogType.Assert, (object)text, context);

        // 소유 객체마다 하나씩 두는 "키당 한 번" 기록기다. HashSet이나 bool 플래그를 직접 두는 대신 이것을 쓴다.
        // 소유자 수명을 따르므로 "월드마다 한 번"이나 "복구 뒤 Reset하고 다시 기록" 같은 규칙을 지킬 수 있다.
        // Log.Instance로 기록하므로 테스트의 로그 교체와 LogAssert가 그대로 본다.
        public sealed class Once
        {
            // 이미 기록한 키다. 워커 스레드에서도 불리므로 잠금 아래에서만 읽고 쓴다.
            readonly HashSet<string> _keys = new HashSet<string>(System.StringComparer.Ordinal);

            // 처음 보는 키면 기억하고 true를 돌려준다. 직접 로그를 남기거나 메시지를 만드는 비용이 큰 호출자가 먼저 확인할 때 쓴다.
            public bool First(string key)
            {
                lock (_keys)
                    return _keys.Add(key ?? string.Empty);
            }

            // 키당 한 번 오류 로그를 남긴다. 이번에 기록했으면 true다.
            public bool Error(string key, string message, UnityEngine.Object? context = null)
            {
                if (!First(key))
                    return false;
                Instance.Error(message, context);
                return true;
            }

            // 기록한 키를 모두 잊는다. 문제가 해결된 뒤 다시 생기면 다시 기록해야 하는 소유자가 쓴다.
            public void Reset()
            {
                lock (_keys)
                    _keys.Clear();
            }
        }
    }

#if UNITY_EDITOR || DEBUG
    // 에디터와 DEBUG 빌드용이라 릴리즈 실행 파일에는 들어가지 않는다.
    // #if 밖에서 이 형식을 참조하면 릴리즈 빌드가 실패하는데, 이것은 의도한 안전장치다.
    public sealed class DebugLog : Log
    {
        // 실패하면 메시지를 Assert 로그로 남기고 AssertionException으로 현재 작업을 멈춘다. 메시지가 없으면 "파일명:줄"을 쓴다.
        // 메시지에 아무것도 덧붙이지 않으므로 테스트의 메시지 정규식이 그대로 맞는다.
        public override void Assert(bool condition, string? message = null, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            if (condition)
                return;

            string text = message ?? Location(callerFile, callerLine);
            WriteAssert(text, context);
            throw new UnityEngine.Assertions.AssertionException(text, null);
        }

        // 받은 예외를 로그 없이 원래 형식 그대로 던진다. 그래서 테스트가 Assert.Throws로 원래 예외 형식을 확인할 수 있다.
        public override bool Fail(System.Exception exception, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
            => throw exception;

        public override bool InvalidData(System.Exception exception, UnityEngine.Object? context = null)
        {
            Warning(Describe(exception), context);
            throw exception;
        }
    }
#endif

    // 릴리즈 빌드용이다. 에디터 테스트에서도 직접 만들어 릴리즈 동작을 확인할 수 있도록 조건부 컴파일로 감싸지 않는다.
    public sealed class ReleaseLog : Log
    {
        // 이미 기록한 (파일, 줄, 메시지)다. 매 프레임 실패하는 검사가 로그를 쏟아내지 않게 막고, 워커 스레드에서도 불리므로 잠금 안에서만 다룬다.
        // 메시지까지 키에 넣어, 한 도우미 안의 두 검사가 같은 호출 위치를 넘겨받아도 서로를 가리지 않게 한다.
        readonly HashSet<(string file, int line, string message)> _reportedChecks = new HashSet<(string file, int line, string message)>();

        // 실패하면 같은 위치와 메시지마다 한 번만 "{메시지} (검사 실패 파일명:줄)"을 Assert 로그로 남기고 예외 없이 계속한다.
        // 위치를 메시지 뒤에 붙이므로 ^로 시작하는 메시지 정규식이 그대로 맞는다.
        public override void Assert(bool condition, string? message = null, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            if (condition)
                return;
            if (!FirstReport(callerFile, callerLine, message ?? string.Empty))
                return;

            string failedAt = FailedAt(callerFile, callerLine);
            WriteAssert(message == null ? failedAt : message + " (" + failedAt + ")", context);
        }

        // 예외를 던지지 않고 "형식: 메시지 (검사 실패 파일명:줄)"을 위치와 문구마다 한 번 Assert 로그로 남긴 뒤 false를 돌려준다.
        // Assert와 같은 기록 집합을 쓰므로 릴리즈의 모든 결함이 위치마다 Assert 로그 한 번이라는 같은 모양으로 남는다.
        public override bool Fail(System.Exception exception, UnityEngine.Object? context = null,
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            string text = Describe(exception);
            if (FirstReport(callerFile, callerLine, text))
                WriteAssert(text + " (" + FailedAt(callerFile, callerLine) + ")", context);
            return false;
        }

        // Warning 로그만 남기고 false를 돌려준다. 소유자가 읽기마다 한 번 부르므로 중복을 거르지 않는다.
        public override bool InvalidData(System.Exception exception, UnityEngine.Object? context = null)
        {
            Warning(Describe(exception), context);
            return false;
        }

        // Assert와 Fail이 같은 기록 집합으로 위치와 문구마다 한 번만 남기게 한다. 처음 보는 조합이면 기억하고 true다.
        bool FirstReport(string callerFile, int callerLine, string message)
        {
            lock (_reportedChecks)
                return _reportedChecks.Add((callerFile, callerLine, message));
        }

        // 릴리즈 검사 로그가 끝에 붙이는 "검사 실패 파일명:줄" 표기다. 테스트의 로그 정규식이 이 문구를 그대로 본다.
        static string FailedAt(string callerFile, int callerLine) => "검사 실패 " + Location(callerFile, callerLine);
    }
}
