#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 명령 관문 하나와 저장 구역 한 쌍을 갖는 세션 소유자의 공통 바탕이다. GameManager 프리팹 자식 컴포넌트가 상속한다.
    // 하위 클래스는 Awake와 OnDestroy를 override하고 base를 불러야 한다. 그냥 선언하면 이 구독을 가려 저장 구역이 조용히 빠진다.
    public abstract class SessionSectionOwner : MonoBehaviour
    {
        // 플레이어 명령을 받을지는 GameRuntime이 Configure로 넘긴 조건이 정한다. 구성 전에는 모두 거절한다.
        Func<bool> _acceptsCommands = () => false;

        // GameRuntime이 구성 때 한 번 부른다.
        internal void Configure(Func<bool> acceptsCommands)
        {
            _acceptsCommands = acceptsCommands;
        }

        // 명령마다 상태를 바꾸기 전에 본다. 여러 소유자에 걸친 명령도 다른 상태를 바꾸기 전에 보아,
        // 이 소유자의 쓰기가 같은 호출에서 거절되지 않게 한다.
        internal bool AcceptsCommands => _acceptsCommands();

        // 원작 매니저처럼 Awake에서 저장 수집과 불러오기를 구독하고 OnDestroy에서 끊는다.
        protected virtual void Awake()
        {
            SaveManager.CollectSaveData += WriteSection;
            SaveManager.FileLoaded += ReadSection;
        }

        protected virtual void OnDestroy()
        {
            SaveManager.CollectSaveData -= WriteSection;
            SaveManager.FileLoaded -= ReadSection;
        }

        // 저장마다 이 소유자의 구역을 한 번 쓴다.
        private protected abstract void WriteSection(GameSaveData data);

        // 불러온 기록으로 바꾼다. 기록은 코덱이 검증한 값이라 다시 검사하지 않는다.
        private protected abstract void ReadSection(GameSaveData data);
    }
}
