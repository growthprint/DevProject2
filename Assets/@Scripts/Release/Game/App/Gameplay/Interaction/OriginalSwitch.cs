#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 기지 ShipBuildingFrame/Switcher2 같은 스위치다. 원작은 Door 컴포넌트로 만들었지만 문이 아니므로 문 목록(DoorSync)에 넣지 않는다.
    // 같은 객체의 상호작용(Switcher2/Interact)이 끝나면 켜고 끈다. 레버 부품은 전환 시간 동안 돌고, 조명 부품은 다 돌았을 때 켜지거나 꺼진다.
    // 상태는 원작 inLevelData처럼 저장하지 않아 레벨에 들어올 때마다 처음 상태로 시작한다.
    [DisallowMultipleComponent]
    public sealed class OriginalSwitch : MonoBehaviour
    {
        [SerializeField] OriginalPartPose[] _parts = Array.Empty<OriginalPartPose>();
        // 원작 lerpTime(초)이다.
        [SerializeField] float _lerpTime = .25f;
        // 원작 closed 초기값의 반대다.
        [SerializeField] bool _startOn;
        // 원작 openSound와 closeSound의 FMOD 경로다. 원작 hasSound가 꺼졌으면 비어 있다.
        [SerializeField] string _onSound = "";
        [SerializeField] string _offSound = "";
        bool _on;
        float _progress;

        internal bool On => _on;

        // 원작 Door.Start처럼 처음 상태의 부품 자세와 활성을 바로 적용하고, 켜고 끌 때까지 Update를 쉰다.
        void Awake()
        {
            _on = _startOn;
            _progress = _on ? 1f : 0f;
            // 원작 Door와 같은 부품 자세 규칙을 문과 함께 쓴다.
            OriginalPartPose.ApplyAll(_parts, _progress, true);
            enabled = false;
        }

        // 원작 Door.Switch다. 앱 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
        internal void Toggle()
        {
            _on = !_on;
            enabled = true;
            string sound = _on ? _onSound : _offSound;
            if (sound.Length != 0 && GameManager.HasInstance) GameManager.Audio.PlayOneShot(sound, transform.position);
        }

        void Update()
        {
            float goal = _on ? 1f : 0f;
            if (_progress == goal) { enabled = false; return; }
            // 원작 Door.Update와 같은 이동과 자세 적용을 문과 함께 쓴다. 활성은 끝까지 움직인 프레임에만 바뀐다.
            _progress = OriginalPartPose.Step(_progress, goal, _lerpTime);
            OriginalPartPose.ApplyAll(_parts, _progress, _progress == goal);
        }

        // 제작 도구가 쓰는 구성이다.
        internal void Configure(OriginalPartPose[] parts, float lerpTime, bool startOn, string onSound, string offSound)
        {
            _parts = parts; _lerpTime = lerpTime; _startOn = startOn; _onSound = onSound; _offSound = offSound;
        }
    }
}
