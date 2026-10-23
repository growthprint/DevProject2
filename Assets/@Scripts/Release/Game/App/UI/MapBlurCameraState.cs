using System.Collections.Generic;
using UnityEngine;
namespace Game.App
{
    // 지도 블러 여부는 전역 플래그가 아니라 월드 카메라에 붙은 이 컴포넌트가 정한다. 렌더러 기능이 매 프레임 카메라마다
    // 컴포넌트를 찾지 않도록, 켜져 있는 동안 자기 GameObject를 정적 집합에 등록한다. GameObject로 등록하므로 그 위의 모든 Camera가
    // 같은 상태를 본다. [ExecuteAlways]가 없어 편집 모드에서는 OnEnable이 불리지 않으므로 블러는 플레이 모드에서만 들어간다.
    [DisallowMultipleComponent]
    public sealed class MapBlurCameraState : MonoBehaviour
    {
        static readonly HashSet<GameObject> _blurredObjects = new HashSet<GameObject>();

        // 카메라의 GameObject에 켜진 상태가 있는지 본다. MapBlurRendererFeature.AddRenderPasses만 부른다.
        internal static bool IsBlurred(Camera camera) => _blurredObjects.Contains(camera.gameObject);

        void OnEnable() => _blurredObjects.Add(gameObject);

        // 끄기, 파괴, 씬 종료, 플레이 종료가 모두 OnDisable을 먼저 부르므로 여기서만 집합에서 빼면 된다.
        void OnDisable() => _blurredObjects.Remove(gameObject);
    }
}
