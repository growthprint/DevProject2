#nullable enable annotations
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterMainControl.UpdateInventoryCapacity와 PetProxy처럼 소지 칸과 펫 가방 칸 수를 플레이어 GAS 스탯 현재값에 맞춘다.
    // 전투 월드 컨트롤러가 준비 때 만들고, 시뮬레이션이 준비된 프레임 끝에 부른다. 줄어 넘친 아이템은 바닥 아이템 생성 객체가 떨군다.
    internal sealed class InventoryCapacitySync
    {
        readonly GameplayWorldContext _world;
        readonly DroppedItemSpawner _drops;
        // 소지 칸과 펫 가방 칸 맞추기가 거절된 상태다. 같은 상태로 매 프레임 다시 시도해 결함 보고가 쌓이지 않게 한다.
        RejectedResize _carriedRejected;
        RejectedResize _petRejected;

        internal InventoryCapacitySync(GameplayWorldContext world, DroppedItemSpawner drops)
        {
            _world = world;
            _drops = drops;
        }

        // 소지 칸을 먼저, 펫 가방 칸을 나중에 맞춘다. 매 프레임 불리므로 칸 수가 같으면 정수 비교만 하고 할당하지 않는다.
        internal void Tick()
        {
            SyncCarried();
            SyncPet();
        }

        // 원작 UpdateInventoryCapacity처럼 InventoryCapacity 현재값을 반올림한 칸 수와 소지 컨테이너 용량이 다르면 맞춘다.
        // 초기화한 월드의 검증된 그래프에는 플레이어 루트와 소지 컨테이너가 늘 있다.
        void SyncCarried()
        {
            if (!TryLiveSession(out GameSessionManager? session)) return;
            ItemOwnershipState? items = session.Snapshot().Campaign.Gameplay.Inventory.Items;
            OriginalInventoryQueries.TryPlayerItem(items!, out ItemInstanceState? player);
            int target = Mathf.RoundToInt(_world.Runtime.Vitals.Abilities.GetCurrent(CharacterAttributes.InventoryCapacity));
            if (player.Inventory.Capacity == target) return;
            Resize(session, items, target, false, ref _carriedRejected);
        }

        // 원작 PetProxy.Update처럼 펫 가방 칸 수를 PetCapcity 현재값의 반올림에 맞춘다. 원작은 1초마다 보지만 여기서는 소지 칸과 함께
        // 매 프레임 정수만 비교한다. 줄어 넘친 아이템은 원작과 달리 소지 칸처럼 플레이어 위치에 떨군다. 첫 월드 초기화가 펫 가방을 만든다.
        void SyncPet()
        {
            if (!TryLiveSession(out GameSessionManager? session)) return;
            ItemOwnershipState? items = session.Snapshot().Campaign.Gameplay.Inventory.Items;
            OriginalInventoryQueries.TryContainer(items!, GameplayInventory._pet, out ItemContainerState? pet);
            int target = Mathf.RoundToInt(_world.Runtime.Vitals.Abilities.GetCurrent(CharacterAttributes.PetCapcity));
            if (pet!.Capacity == target) return;
            Resize(session, items, target, true, ref _petRejected);
        }

        // 플레이어가 먼저 파괴됐거나 세션이 없으면 맞출 대상이 없어 기록 없이 false다. 모터 비교는 Unity null 비교다.
        bool TryLiveSession([NotNullWhen(true)] out GameSessionManager? session)
        {
            session = null;
            return _world.Motor != null && _world.TrySession(out session);
        }

        // 두 가방이 함께 쓰는 맞추기 명령과 떨굼이다. 전환이나 저장으로 명령 게이트가 잠깐 닫힌 프레임은 실패로 기억하지 않고,
        // 게이트가 열린 다음 프레임에 다시 맞춘다. 원작 UpdateInventoryCapacity는 조준과 상관없이 Drop(위치, true, Vector3.forward, 360)으로
        // 사방에 던지므로 넘친 아이템을 플레이어 둘레에 떨군다.
        void Resize(GameSessionManager session, ItemOwnershipState? items, int target, bool pet, ref RejectedResize rejected)
        {
            if (!session.AcceptsCommands) return;
            if (rejected.Matches(items, target)) return;
            IReadOnlyList<ItemOwnershipState> dropped;
            bool resized = pet ? session.Inventory.TrySetPetCapacity(_world.Catalog, target, out dropped) :
                session.Inventory.TrySetCarriedCapacity(_world.Catalog, target, out dropped);
            if (!resized)
            {
                rejected.Remember(items, target);
                return;
            }
            if (dropped.Count > 0) _drops.SpawnAroundPlayer(dropped);
        }

        // 맞추기가 거절된 그래프와 목표 칸 수다. 그래프나 목표가 바뀌면 다시 시도한다. 기본값은 아직 거절이 없는 상태다.
        struct RejectedResize
        {
            ItemOwnershipState? _items;
            int _target;

            internal bool Matches(ItemOwnershipState? items, int target) => ReferenceEquals(items, _items) && target == _target;

            internal void Remember(ItemOwnershipState? items, int target)
            {
                _items = items;
                _target = target;
            }
        }
    }
}
