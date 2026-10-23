#nullable enable
using System.Collections.Generic;
using Game.App.World;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 전투가 World에서 빌려 재생하는 입자 효과의 대여, 재생, 끝남 판정, 반환을 맡는다. 전투 시뮬레이션이 하나를 가진다.
    // 탄착, 피격, 사망 효과는 제자리에, 섬광은 총구의 자식으로 두고, 공격 예고 효과는 적 손 소켓을 따라가게 옮긴다.
    internal sealed class CombatEffects
    {
        readonly WorldObjectService _objects;
        // 재생 중인 탄착, 피격, 사망 입자 효과의 World 등록 핸들과 입자다. 재생이 끝나면 World에 반환한다.
        readonly List<(WorldObjectHandle handle, ParticleSystem effect)> _effects = new();
        // 원본 ItemAgent_Gun.Shoot처럼 총구의 자식으로 붙은 섬광과 그 총구다. 총이 파괴되면 자식인 섬광도 함께 파괴되므로
        // 총구를 함께 들고, 끝나거나 총구가 꺼지거나 총 표시를 파괴하기 전에 World에 반환해 풀 보관 루트로 되돌린다.
        readonly List<(WorldObjectHandle handle, ParticleSystem effect, Transform muzzle)> _flashes = new();
        // 적 오른손을 따라다니는 공격 예고 효과다. 재생이 끝나면 World에 반환한다.
        readonly List<(WorldObjectHandle handle, ParticleSystem effect, Transform socket)> _alerts = new();

        // 효과를 빌리고 반환하는 곳은 전투 월드의 객체 서비스 하나라 생성자로 받는다.
        internal CombatEffects(WorldObjectService objects) => _objects = objects;

        // 탄착, 피격, 사망 효과는 어디에도 붙지 않고 월드 루트 아래 그 자리에서 재생한다. 끝난 효과는 ReturnFinished가 반환한다.
        internal void Emit(SpawnRule? rule, Vector3 position, Vector3 direction)
        {
            Quaternion rotation = direction.sqrMagnitude > 0 ? Quaternion.LookRotation(direction) : Quaternion.identity;
            Play(rule!, new Pose(position, rotation), null, out WorldObjectHandle handle, out ParticleSystem effect);
            _effects.Add((handle, effect));
        }

        // 원본 ItemAgent_Gun.Shoot처럼 섬광은 조준 방향이 아닌 총열 방향인 총구 회전으로 만들고 총구의 자식으로 붙인다.
        // 그래서 Local 공간인 핵, 파편, 빛 입자가 표시 반동과 몸 회전을 따라간다. World 공간인 연기는 제자리에 남는다.
        internal void AddFlash(SpawnRule rule, Transform muzzle)
        {
            Play(rule, new Pose(muzzle.position, muzzle.rotation), muzzle, out WorldObjectHandle flash, out ParticleSystem flashEffect);
            _flashes.Add((flash, flashEffect, muzzle));
        }

        // 원본 SpawnAlertFx처럼 적이 휘두르기 직전에 손 소켓에서 공격 예고 효과를 낸다. 원본은 소켓의 자식으로 만들어 손을 따라가므로,
        // 풀 객체를 소켓 아래로 옮기는 대신 TickAlerts가 매 프레임 소켓 위치로 옮긴다.
        internal void AddAlert(SpawnRule rule, Transform socket)
        {
            Play(rule, new Pose(socket.position, socket.rotation), null, out WorldObjectHandle handle, out ParticleSystem effect);
            _alerts.Add((handle, effect, socket));
        }

        // 재생이 끝났거나 파괴된 효과는 목록에서 빼고 World에 반환한다. 비활성화와 보관은 풀이 하고,
        // 입자 초기화는 템플릿의 PooledParticleEffect가 반환과 대여 때 한다.
        internal void ReturnFinished()
        {
            for (int e = _effects.Count - 1; e >= 0; e--)
            {
                var item = _effects[e];
                if (item.effect != null && item.effect.IsAlive(true)) continue;
                _effects.RemoveAt(e);
                _objects.Despawn(item.handle);
            }
            // 총구에 붙은 섬광은 재생이 끝나거나, 적이 죽어 모델과 함께 꺼지면 반환한다. 반환하면 Spawner가 풀 보관 루트 아래로 되돌린다.
            for (int m = _flashes.Count - 1; m >= 0; m--)
            {
                var flash = _flashes[m];
                if (IsPlayingOn(flash.effect, flash.muzzle)) continue;
                _flashes.RemoveAt(m);
                _objects.Despawn(flash.handle);
            }
        }

        // 예고 효과는 재생이 끝나면 반환하고, 그 전에는 휘두르는 손을 따라간다. 적 모델이 꺼지면 효과도 멈춘 것으로 본다.
        internal void TickAlerts()
        {
            for (int a = _alerts.Count - 1; a >= 0; a--)
            {
                var alert = _alerts[a];
                if (IsPlayingOn(alert.effect, alert.socket))
                {
                    alert.effect.transform.SetPositionAndRotation(alert.socket.position, alert.socket.rotation);
                    continue;
                }
                _alerts.RemoveAt(a);
                _objects.Despawn(alert.handle);
            }
        }

        // 총 표시를 파괴하기 직전에 그 총구의 섬광을 반환해, 섬광이 총과 함께 파괴되지 않고 풀 보관 루트로 돌아가게 한다.
        internal void ReturnFlashesOn(Transform? muzzle)
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var flash = _flashes[i];
                if (!ReferenceEquals(flash.muzzle, muzzle)) continue;
                _flashes.RemoveAt(i);
                _objects.Despawn(flash.handle);
            }
        }

        // 씬 정리 때 참조만 놓는다. 남은 효과는 뒤이은 씬 정리의 World 해제가 반환하거나 파괴한다.
        internal void Clear()
        {
            _effects.Clear(); _flashes.Clear(); _alerts.Clear();
        }

        // 생성 규칙으로 효과를 비활성 상태로 빌려 자세를 정하고, parent가 있으면 월드 자세를 지킨 채 그 자식으로 붙인 뒤 활성화하고 재생한다.
        // 풀은 규칙마다 재사용되고 입자 초기화는 템플릿의 PooledParticleEffect가 맡는다.
        void Play(SpawnRule rule, Pose pose, Transform? parent, out WorldObjectHandle handle, out ParticleSystem effect)
        {
            // 효과 규칙은 GameplayDataSettings에 저작되어 있고, 열린 월드가 방금 빌린 핸들이라 대여와 활성화가 실패하지 않는다.
            handle = _objects.Spawn(rule, pose)!;
            GameObject instance = handle.Instance!;
            if (parent != null) instance.transform.SetParent(parent, true);
            _objects.Activate(handle);
            effect = instance.GetComponent<ParticleSystem>();
            effect.Play(true);
        }

        // 따라가는 대상에 붙은 효과가 아직 재생 중인지다. 효과나 대상이 파괴되었거나 대상이 꺼졌으면 끝난 것으로 본다.
        // 파괴 여부는 Unity null 비교로 본다.
        static bool IsPlayingOn(ParticleSystem effect, Transform anchor) =>
            effect != null && effect.IsAlive(true) && anchor != null && anchor.gameObject.activeInHierarchy;
    }
}
