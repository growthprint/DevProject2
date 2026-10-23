#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.App
{
    // 씬 흐름과 편집 흐름이 로드한 씬을 다루는 도우미다. 컨텍스트 찾기와 활성 씬 지정을 맡는다.
    // 씬 컨텍스트의 루트 연결은 Release에서 다시 검사하지 않고 제작 도구와 EditMode 저작 테스트가 검사한다.
    internal static class SceneContexts
    {
        // 유효하게 로드된 씬을 활성 씬으로 만든다. 이미 활성인 씬에 SetActiveScene을 부르면 false라 다를 때만 바꾼다.
        // 무효하거나 내려간 씬, 지정 실패는 false이고, 기록 여부는 호출자가 정한다.
        internal static bool TryMakeActive(Scene scene) =>
            scene.IsValid() && scene.isLoaded && (SceneManager.GetActiveScene() == scene || SceneManager.SetActiveScene(scene));

        // 로드된 씬에 하나만 저작한 T 컨텍스트를 찾는다. 로더가 무효한 씬을 주면 열기 실패 정리가 회수하도록 던진다.
        // 편집을 열 때마다 불리므로 배열을 모으지 않고 루트 순서대로 첫 컨텍스트를 돌려준다. 하나뿐인지는 EditMode 저작 테스트가 확인한다.
        internal static T Single<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("로더가 유효한 로드 씬을 반환하지 않았습니다.");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T context = root.GetComponentInChildren<T>(true);
                if (context != null) return context;
            }
            // 반복이 끝나면 돌려줄 값이 없어 언어가 요구하는 끝을 닫는다. 저작 테스트를 통과한 씬에서는 오지 않는다.
            throw new InvalidOperationException("로드한 씬에 컨텍스트가 없습니다: " + typeof(T).Name);
        }
    }
}
