#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // Bootstrap 씬의 유일한 카메라다. 로고가 끝난 뒤 첫 씬 카메라가 그릴 때까지 화면을 검정으로 지운다.
    // 지우는 카메라가 없으면 지우지 않은 화면 버퍼들이 번갈아 나와 찢김과 겹쳐 검은 가로줄로 보인다. 규칙: Docs/game-runtime.md#startup-and-options
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class BootCamera : MonoBehaviour
    {
        void Awake()
        {
            // 그릴 것이 없는 부팅 구간이 초당 수천 프레임을 돌지 않게 제품 기본값으로 묶는다. 옵션을 처음 적용할 때 사용자 값으로 바뀐다.
            // GameScreenOptions처럼 Editor에서는 개발자의 프레임 설정을 바꾸지 않는다.
            if (!Application.isEditor) Application.targetFrameRate = GameOptions._defaultFrameRate;
            // Bootstrap 씬은 앱이 끝날 때까지 남으므로, 첫 씬 카메라가 화면을 맡는 앱 시작 완료 때 끈다. 시작이 실패하면 검정 화면으로 남는다.
            if (GameRuntime.IsInitialized) TurnOff();
            else GameRuntime.Initialized += TurnOff;
        }

        void OnDestroy() => GameRuntime.Initialized -= TurnOff;

        void TurnOff()
        {
            GameRuntime.Initialized -= TurnOff;
            GetComponent<Camera>().enabled = false;
        }
    }
}
