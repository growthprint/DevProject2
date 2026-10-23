#nullable enable annotations
using Game.Core;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Game.App
{
    // 씬 컨트롤러가 따로 저작한 NavMeshData를 이 씬 전용 복제본으로 같은 GameObject의 NavMeshSurface에 등록하고 푼다.
    // Editor의 중복 Surface 검증이 Surface 참조를 비울 수 있어서다. 전투 월드와 프롤로그가 함께 쓴다.
    internal static class SceneNavigationData
    {
        // Surface가 없는 씬은 null을 돌려준다. 루트가 로드 때부터 켜져 있어 Surface의 OnEnable이 이미 데이터를 등록했으므로,
        // 등록을 빼고 복제본으로 바꾼 뒤 다시 등록한다. 레벨 초기화 안에서만 불러 복제 실패는 초기화를 멈춘다.
        internal static NavMeshData? Register(Component owner, NavMeshData? data)
        {
            if (!owner.TryGetComponent(out NavMeshSurface navigation)) return null;
            NavMeshData instance = Object.Instantiate(data);
            navigation.RemoveData(); navigation.navMeshData = instance; navigation.AddData();
            return instance;
        }

        // Register가 돌려준 복제본의 등록을 빼고 파괴한다. 단계마다 Cleanup.Run으로 계속하므로 던지지 않는다.
        // Surface가 다른 데이터를 들고 있으면 그 등록은 건드리지 않는다.
        internal static void Release(Component owner, NavMeshData? instance)
        {
            if (instance == null) return;
            Cleanup.Run(() =>
            {
                if (owner.TryGetComponent(out NavMeshSurface navigation) && navigation.navMeshData == instance)
                { navigation.RemoveData(); navigation.navMeshData = null; }
            }, owner);
            Cleanup.Run(() => UnityObjects.DestroyOwned(instance), owner);
        }
    }
}
