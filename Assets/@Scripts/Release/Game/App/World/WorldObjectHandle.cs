#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App.World
{
    // 월드 등록 한 건을 가리키는 불변 토큰이다.
    public sealed class WorldObjectHandle
    {
        // 생존을 판정하는 소유 월드다. 등록 해제나 월드 종료 뒤에는 그 등록부에 없어 무효가 된다.
        readonly WorldObjectService _owner;
        // 외부 등록 객체다. 소유 객체는 이 필드를 비우고 Spawn에서 인스턴스를 읽는다.
        readonly GameObject _external;
        // 외부 등록 핸들이다. 객체의 생성 소유자는 그대로 두고 객체와 ID, 판정할 소유 월드만 저장한다.
        internal WorldObjectHandle(long id, GameObject instance, WorldObjectService owner)
        {
            Id = id;
            _external = instance;
            IsExternal = true;
            _owner = owner;
        }
        // 소유 등록 핸들이다. Spawner 대여를 보관해 인스턴스와 생존을 판정한다.
        internal WorldObjectHandle(long id, SpawnHandle spawn, WorldObjectService owner)
        {
            Id = id;
            Spawn = spawn;
            IsExternal = false;
            _owner = owner;
        }
        // ID는 현재 월드 수명에서만 고유하며 저장 데이터로 사용하지 않는다.
        public long Id { get; }
        // 조회한 Unity 참조는 이 등록의 수명 동안만 빌린다. 소유 객체는 Despawn, 월드 종료, 파괴로 대여가 끝나면 null이라
        // 지난 핸들이 재사용된 인스턴스를 가리키지 않는다.
        public GameObject Instance => IsExternal ? _external : Spawn.Instance;
        // 외부 등록은 원래 소유자가 파괴한다.
        public bool IsExternal { get; }
        // 소유 객체의 Spawner 대여다. 외부 등록은 기본값(살아 있지 않음)이며 월드 서비스만 읽는다.
        internal SpawnHandle Spawn { get; }
        // 소유 월드의 등록부에 이 토큰이 있어야 살아 있다. 그와 함께 외부 객체는 Unity 객체가, 소유 객체는 Spawner 대여가
        // 살아 있어야 한다.
        public bool IsAlive => _owner.IsRegistered(this) && (IsExternal ? _external != null : Spawn.IsAlive);
    }
}
