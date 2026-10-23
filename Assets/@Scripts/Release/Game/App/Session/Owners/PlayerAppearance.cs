#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using DuckCharacter.AvatarParts;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 CustomFaceManager처럼 플레이어 오리의 외형 한 벌을 소유한다. GameManager 프리팹 자식의 컴포넌트이고
    // GameManager.Appearance로 찾는다. 저장할 때 외형 구역을 써 넣고, 기록을 불러오면 읽어 간다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class PlayerAppearance : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 구독자는 Current를 직접 읽는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 외형은 바뀔 수 있는 객체라 들어올 때와 나갈 때 복사해 밖에서 고쳐도 이 값이 바뀌지 않게 한다.
        // 기록을 불러오기 전에는 검증을 통과하는 기본 외형이다. 읽는 쪽은 GameRuntime.HasSession으로 세션이 있는지 먼저 본다.
        DuckAvatarPreset _preset = new DuckAvatarPreset().CreateDefault();

        // 읽을 때마다 복사본을 준다.
        public DuckAvatarPreset Current => _preset.Clone();

        // 저장마다 외형 구역을 한 번 쓴다. 구역 묶음이 받을 때 복사한다.
        private protected override void WriteSection(GameSaveData data) => data.SetAppearance(_preset);

        // 불러온 기록의 외형으로 바꾼 뒤 알린다. 구역 묶음이 읽을 때마다 복사본을 주므로 다시 복사하지 않는다.
        private protected override void ReadSection(GameSaveData data)
        {
            _preset = data.Appearance;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 여러 소유자에 걸친 편집 확정이 다른 상태를 바꾸기 전에 확인하는 조건이다. 외형은 편집 화면에서 온 입력이라
        // null을 포함한 검증 실패를 기록 없이 거절한다. 통과하면 호출자가 '!' 없이 Apply에 넘기도록 null 아님을 알린다.
        internal bool CanApply([NotNullWhen(true)] DuckAvatarPreset? value) => AcceptsCommands && value != null && value.TryValidate();

        // 호출 쪽이 같은 호출에서 CanApply로 확인한 외형의 복사본을 보관하고 알린다. 늘 성공하므로 결과를 돌려주지 않고,
        // 검증한 값이라 재검사 없는 Clone을 쓴다.
        internal void Apply(DuckAvatarPreset value)
        {
            _preset = value.Clone();
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
