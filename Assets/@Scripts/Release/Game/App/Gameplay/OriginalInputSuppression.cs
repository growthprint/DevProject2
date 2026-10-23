#nullable enable annotations
using System;

namespace Game.App
{
    // 원본 InputManager의 blockInputSources 집합과 inputActiveCoolCounter다. 대사, 지연 상호작용, 전환이 각자 토큰을 들고,
    // 모든 토큰이 풀린 뒤에도 정한 프레임 수만큼 입력을 끈다. 플레이어 모터가 하나를 가지고 매 프레임 진행한다.
    internal sealed class OriginalInputSuppression
    {
        // 마지막 억제가 풀린 뒤에도 입력을 끄고 있는 프레임 수다(원본 inputActiveCoolCounter).
        const int _cooldownFrames = 2;
        // 각 소유자가 들고 있는 억제 토큰 수다.
        int _count;
        // 마지막 억제가 풀린 뒤에도 입력을 끄고 있을 남은 프레임 수다.
        int _cooldown;

        // 토큰이 남아 있거나, 모두 풀린 뒤 2프레임 대기 중이면 억제 상태다.
        internal bool IsSuppressed => _count > 0 || _cooldown > 0;

        // 소유자별 억제 토큰을 발급한다. 원본 DisableInput처럼 즉시 2프레임 대기를 채우고, 모든 토큰이 해제되어야 대기가 줄기 시작한다.
        internal IDisposable Acquire()
        {
            _count++;
            _cooldown = _cooldownFrames;
            return new Token(this);
        }

        // 원본 UpdateInputActived처럼 토큰이 남아 있으면 대기를 2로 유지하고, 모두 풀렸으면 한 프레임씩 줄인다.
        internal void AdvanceFrame()
        {
            if (_count > 0) _cooldown = _cooldownFrames;
            else if (_cooldown > 0) _cooldown--;
        }

        // 억제 토큰은 여러 번 Dispose해도 수를 한 번만 줄인다. 모터가 먼저 파괴된 뒤 늦게 와도 이 객체만 바꾸므로 던지지 않는다.
        sealed class Token : IDisposable
        {
            OriginalInputSuppression? _owner;
            internal Token(OriginalInputSuppression owner) => _owner = owner;
            public void Dispose()
            {
                OriginalInputSuppression? owner = _owner;
                _owner = null;
                if (owner != null && owner._count > 0) owner._count--;
            }
        }
    }
}
