#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.App
{
    // 손 소켓에 붙인 무기 표시와 반동을 플레이어와 적이 같은 규칙으로 쓰게 한다.
    // 원본 Muzzle 소켓은 생성 때 한 번 찾아 두고, Dispose가 표시 인스턴스를 파괴한다.
    internal sealed class WeaponVisual : IDisposable
    {
        const string _muzzleName = "Muzzle";
        // 원본 표시 반동 값이다. 초당 20으로 뒤로 최대 0.2m까지 밀린 뒤 초당 8로 원위치한다.
        const float _recoilOutSpeed = 20f, _recoilReturnSpeed = 8f, _recoilDistance = .2f;
        GameObject? _instance;
        float _recoil;
        bool _recoilBack;

        WeaponVisual(GameObject instance, Transform? muzzle)
        {
            _instance = instance;
            Muzzle = muzzle;
        }

        // 원본 무기 프리팹의 발사 소켓이다. 근접 무기처럼 소켓이 없거나 해제한 뒤에는 null이다.
        internal Transform? Muzzle { get; private set; }

        // 소켓 아래에, 소켓이 없으면 씬 루트에 표시 프리팹을 만든다. 장착 변경과 적 외형 준비는 표시 프리팹이 있을 때만 부른다.
        // 총 표시 프리팹은 원본 Muzzle 소켓을 늘 갖는다. 근접 무기처럼 소켓이 없으면 Muzzle은 null이다.
        internal static WeaponVisual Create(GameObject prefab, Transform? socket)
        {
            GameObject instance = Object.Instantiate(prefab, socket, false);
            Transform? muzzle = Array.Find(instance.GetComponentsInChildren<Transform>(true), value => value.name == _muzzleName);
            return new WeaponVisual(instance, muzzle);
        }

        // 발사 순간 표시 반동을 시작한다.
        internal void Kick() => _recoilBack = true;

        // 원본 ItemAgentHolder처럼 무기를 꺼내는 0.15초 동안 표시를 숨겼다가 다시 보인다.
        internal void SetVisible(bool visible)
        {
            if (_instance != null) _instance.SetActive(visible);
        }

        // 표시 반동을 원본 값으로 한 프레임 진행한다. 끝까지 밀리면 원위치로 돌아오고, 다음 발사의 Kick이 다시 민다.
        internal void Tick(float deltaTime)
        {
            if (_instance == null) return;
            _recoil = Mathf.MoveTowards(_recoil, _recoilBack ? 1f : 0f, deltaTime * (_recoilBack ? _recoilOutSpeed : _recoilReturnSpeed));
            if (_recoil >= 1f) _recoilBack = false;
            _instance.transform.localPosition = Vector3.back * (_recoil * _recoilDistance);
        }

        // 표시 인스턴스를 한 번만 파괴하고 캐시한 Muzzle 참조를 지운다.
        public void Dispose()
        {
            GameObject? instance = _instance;
            _instance = null;
            Muzzle = null;
            UnityObjects.DestroyOwned(instance);
        }
    }
}
