#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 TeleportBeacon이다. 지도 선택 비콘 하나의 해금과 그 표시 전환을 맡는다. 해금 기록은 GameManager.Beacons(BeaconManager)가 소유한다.
    // 원작처럼 F 상호작용은 따로 둔 대상(TeleportBeaconInteract)이 끝날 때 ActivateBeacon을 부르고, 해금되면 활성 표시를 켜고 그 대상을 끈다.
    // 참조는 제작 도구가 SerializedObject로 적고, 빠진 참조는 제작 도구와 씬 데이터 테스트가 막는다(C6).
    [DisallowMultipleComponent]
    public sealed class TeleportBeacon : MonoBehaviour
    {
        // 원작 beaconScene([SceneID])과 beaconIndex다. 지도 선택 데이터와 같은 원작 장면 ID(예: Level_GroundZero_Main)를 그대로 쓴다.
        [SerializeField] string _beaconScene = "";
        [SerializeField] int _beaconIndex;
        // 원작 activeByUnlocked와 interactable이다. 해금 뒤에는 앞의 것을 켜고 뒤의 대상 물체를 끈다.
        [SerializeField] GameObject? _activeByUnlocked;
        [SerializeField] InteractableBase? _interactable;

        // 원작 Start다. 저장된 해금 상태대로 표시를 맞춘다. 앱 루트가 없으면 해금 기록이 없는 것으로 본다.
        void Start()
        {
            if (GameManager.HasInstance) ShowSavedState(GameManager.Beacons);
            else Show(false);
        }

        // 원작 ActivateBeacon이다. 상호작용 대상이 끝날 때 부른다. 그 상호작용 드라이버는 앱이 있을 때만 돌아 비콘 소유자를 바로 읽는다.
        internal void ActivateBeacon() => Activate(GameManager.Beacons);

        // 해금 기록이 있으면 활성 표시를 켜고 상호작용을 끈다. Start가 부르고, 테스트는 GameManager 없이 소유자를 넘긴다.
        internal void ShowSavedState(BeaconManager beacons) => Show(beacons.IsUnlocked(_beaconScene, _beaconIndex));

        // 원작 UnlockBeacon 뒤의 전환이다. 원작은 결과와 상관없이 전환하지만, 여기서는 해금이 기록됐을 때만 전환한다.
        // 명령 관문이 닫힌 거절(전환 중)은 정상 거절이라 기록하지 않고(C2), 상호작용이 남아 다시 시도할 수 있다. 이미 해금이면 소유자가 성공을 돌려준다.
        internal void Activate(BeaconManager beacons)
        {
            if (beacons.TryUnlock(_beaconScene, _beaconIndex)) Show(true);
        }

        void Show(bool unlocked)
        {
            _activeByUnlocked!.SetActive(unlocked);
            _interactable!.gameObject.SetActive(!unlocked);
        }
    }
}
