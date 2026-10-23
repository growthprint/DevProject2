#nullable enable annotations
using System;
using Game.Core;
using UnityEngine.Rendering;

namespace Game.App
{
    // Volume.profile이 만드는 런타임 복제본을 혼자 소유한다. 공유 자산인 sharedProfile을 바꾸지 않으려고 복제본에만 쓰고,
    // 해제 때 복제된 효과와 프로필을 함께 파괴한다.
    internal sealed class RuntimeVolumeProfile : IDisposable
    {
        VolumeProfile? _profile;

        // Volume.profile을 읽는 순간 복제본이 생긴다. 두 호출자가 Volume이 있는지 확인한 뒤에만 만든다.
        internal RuntimeVolumeProfile(Volume volume)
        {
            _profile = volume.profile;
        }

        // 효과가 없어도 기록하지 않는다. 효과가 꼭 있어야 하는지는 호출자가 정하고, 해제 뒤에는 늘 false다.
        internal bool TryGet<T>(out T component) where T : VolumeComponent
        {
            if (_profile == null)
            {
                component = null!;
                return false;
            }
            return _profile.TryGet(out component);
        }

        // 여러 번 불러도 한 번만 파괴한다.
        public void Dispose()
        {
            VolumeProfile? profile = _profile;
            _profile = null;
            if (profile == null) return;
            foreach (VolumeComponent component in profile.components) UnityObjects.DestroyOwned(component);
            UnityObjects.DestroyOwned(profile);
        }
    }
}
