#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 조준 정보는 발사 시점에 복사하고 비행 중 대상의 머리를 다시 평가하지 않는다.
    internal readonly struct OriginalAimResult
    {
        internal Vector3 Point { get; }
        internal OriginalEnemyController? Target { get; }
        internal bool Head { get; }
        internal OriginalAimResult(Vector3 point, OriginalEnemyController? target, bool head)
        { Point = point; Target = target; Head = head; }
    }

    // 무기와 탄약 값을 발사 순간에 고정해, 탄이 날아가는 동안 장비를 바꿔도 영향이 없게 한다.
    internal readonly struct OriginalShot
    {
        internal Transform Shooter { get; }
        // 쏜 적이다. 플레이어 탄이면 비어 있다. 탄이 적을 맞히면 이 적을 공격자로 넘겨, 처치가 플레이어 몫이 되지 않게 한다.
        // 적은 씬 동안 파괴되지 않아 비행 중에도 참조가 살아 있다.
        internal OriginalEnemyController? Attacker { get; }
        // 원작 ProjectileContext.team이다. 발사 순간 쏜 쪽 팀을 고정하고, 탄 거르기가 같은 팀 통과를 이 값으로 본다.
        internal int Team { get; }
        // 플레이어 탄인지는 쏜 적이 비었는지로 본다. 두 값을 따로 두면 서로 어긋날 수 있다.
        internal bool Player => ReferenceEquals(Attacker, null);
        internal OriginalAimResult Aim { get; }
        internal float Damage { get; }
        internal float HeadMultiplier { get; }
        // 원작 ProjectileContext.critRate다. 맞은 대상마다 이 확률로 치명을 뽑고, 치명이면 HeadMultiplier를 곱한다.
        internal float CritRate { get; }
        // 원작 ItemAgent_Gun.cs:1233처럼 치명 확률이 0.99를 넘는 탄은 쏜 위치와 관계없이 모든 모래주머니를 지나간다.
        internal bool PassesAllHalfCover => CritRate > _allHalfCoverCritRate;
        const float _allHalfCoverCritRate = .99f;
        // 원작 ProjectileContext.armorPiercing이다. 원작 Projectile.cs:362처럼 탄은 맞힌 캐릭터마다 이 관통을 넘긴다.
        internal float ArmorPiercing { get; }
        internal float Range { get; }
        internal float Speed { get; }
        internal float Radius { get; }
        internal float Falloff { get; }
        // 원본 ItemAgent_Gun처럼 BulletDistance에 더하는 보정 거리다. Hit의 거리 감쇠도 이 값을 더한 사거리로 잰다.
        const float _rangePadding = .4f;
        // distanceMultiplier는 원본 ItemAgent_Gun.BulletDistance가 곱하는 쏜 캐릭터의 GunDistanceMultiplier다.
        // attacker는 쏜 적이고 플레이어 탄이면 null이다. 팀은 쏜 적의 팀, 플레이어면 TeamRules.Player다.
        // critRateGain은 쏜 캐릭터의 GunCritRateGain이다. 플레이어 탄은 원작처럼 머리 조준 여부가 치명 확률을 정해 읽지 않는다.
        internal OriginalShot(Transform shooter, OriginalEnemyController? attacker, OriginalAimResult aim,
            OriginalItemDefinition weapon, OriginalItemDefinition? ammo, float multiplier, float speedMultiplier, float critGain = 0,
            float distanceMultiplier = 1, float critRateGain = 0)
        {
            Shooter = shooter; Attacker = attacker; Aim = aim;
            Team = attacker != null ? attacker.Team : TeamRules.Player;
            // 원본 총기처럼 기본 피해가 1보다 크면 산탄 한 발 피해를 최소 1로 둔다. 피해, 산탄 수, 사거리는 정의 접근자로 읽는다.
            // 구조체 속성은 모두 대입하기 전에는 읽을 수 없으므로 지역 값으로 계산한다.
            float baseDamage = weapon.BaseDamage;
            float damage = baseDamage * (ammo?.Stat("damageMultiplier", 1) ?? 1) * multiplier / Mathf.Max(1, weapon.ShotCount);
            if (baseDamage > 1 && damage < 1) damage = 1;
            Damage = damage;
            HeadMultiplier = (weapon.Stat("CritDamageFactor", 1) + (ammo?.Stat("CritDamageFactorGain") ?? 0)) * (1 + critGain);
            // 원작 ItemAgent_Gun.cs:1170-1174다. 주인공 탄은 발사 순간 머리를 노렸으면 1, 아니면 0이고, AI 탄은 무기와 탄과 캐릭터 증가를 곱한다.
            CritRate = ReferenceEquals(attacker, null) ? (aim.Head ? 1f : 0f)
                : DamageRules.GunCritRate(weapon.Stat("CritRate"), critRateGain, ammo?.Stat("CritRateGain") ?? 0);
            // 원작 ItemAgent_Gun.cs:1175처럼 총 ArmorPiercing에 탄 ArmorPiercingGain을 더한다. 녹슨 탄의 음수 증가도 그대로 더해
            // 방어를 덜 뚫는다. 탄 정의가 없으면 탄 증가는 0이다.
            ArmorPiercing = weapon.Stat("ArmorPiercing") + (ammo?.Stat("ArmorPiercingGain") ?? 0);
            Range = weapon.BulletDistance * distanceMultiplier + _rangePadding;
            Speed = weapon.Stat("BulletSpeed") * speedMultiplier;
            Radius = weapon.Stat("ProjectileRadius");
            // 원본 Projectile처럼 거리 감쇠 값이 0 이하면 빠진 값으로 보고 0.5를 쓴다.
            float falloff = weapon.Stat("DmgOverDistance");
            Falloff = falloff <= 0 ? .5f : falloff;
        }
        // crit은 호출자가 맞은 대상마다 CritRate로 뽑은 치명 여부다. 원작 Projectile.cs:361처럼 머리 조준 탄은 사이에 낀 적도
        // 치명으로 맞히므로 target은 피해 값을 바꾸지 않는다. 치명을 넘기지 않으면 일반 피해다.
        internal OriginalHitResult Hit(OriginalEnemyController? target, Vector3 point, float distance, bool crit = false)
        {
            // 원본 ProjectileContext처럼 보정 거리를 더한 사거리의 절반을 넘으면 감쇠 값을 곱한다.
            float damage = Damage * (distance > Range * .5f ? Falloff : 1) * (crit ? HeadMultiplier : 1);
            return new OriginalHitResult(point, damage, crit);
        }
    }

    // 전투가 확정한 피격 결과 하나다. 피해 숫자, 효과음, 카메라 반응은 모두 이 값만 표시한다.
    internal readonly struct OriginalHitResult
    {
        internal Vector3 Point { get; }
        internal float Damage { get; }
        // 원작 DamageInfo.crit이다. 주인공 탄은 머리를 노린 탄만 치명이라, 머리 명중 표시음도 이 값으로 고른다.
        internal bool Head { get; }
        internal OriginalHitResult(Vector3 point, float damage, bool head)
        { Point = point; Damage = damage; Head = head; }
    }

    // 프레임이 낮거나 탄이 빨라도 얇은 벽을 지나치지 않도록, 이동 구간 전체에서 가장 가까운 충돌을 고른다.
    internal static class OriginalBallistics
    {
        // 캐스트 충돌과, 발사 원점이 엄폐물 안에 있는 시작 겹침을 같은 형태로 전달한다.
        // 거르기에서 찾은 소속도 함께 넘겨, 명중 처리가 같은 Collider의 적과 플레이어 모터를 다시 찾지 않게 한다. 적이면 모터는 비어 있다.
        internal readonly struct Contact
        {
            internal Collider Collider { get; }
            internal Vector3 Point { get; }
            internal Vector3 Normal { get; }
            internal float Distance { get; }
            internal OriginalEnemyController? Enemy { get; }
            internal OriginalPlayerMotor? Motor { get; }
            // 단순 체력 대상(드럼통, 벽, 모래주머니)의 원작 DamageReceiver다. 캐릭터 접촉이면 비어 있다.
            internal DamageReceiver? Receiver { get; }
            internal Contact(Collider collider, Vector3 point, Vector3 normal, float distance, OriginalEnemyController? enemy, OriginalPlayerMotor? motor,
                DamageReceiver? receiver = null)
            { Collider = collider; Point = point; Normal = normal; Distance = distance; Enemy = enemy; Motor = motor; Receiver = receiver; }
        }
        static readonly Collider[] _overlaps=new Collider[32];
        // 원본 Projectile.UpdateMoveAndCheck처럼 날아가는 탄의 캐스트는 시작점을 0.1m 뒤로 당기고 거리를 0.3m 더 본다.
        const float _flightPullBack = .1f, _flightLookAhead = .3f;
        // 이 길이 이하의 구간은 검사하지 않고 충돌 없음으로 본다. 첫 구간은 방향을 정할 수 없고, 비행 구간은 시간이 멈춘 프레임이다.
        const float _minSweepDistance = .00001f;
        // 반지름 0인 레이 탄도 시작 겹침을 찾도록 겹침 구에 쓰는 최소 반지름이다.
        const float _minOverlapRadius = .0001f;
        // 캐스트 결과가 호출자 버퍼를 가득 채우면 쓰는 전용 확장 버퍼다. 한 번 키우면 다음에도 재사용한다.
        // 호출자 버퍼는 호출자의 고정 크기 배열이라 키운 배열을 거기에 남기지 않는다.
        static RaycastHit[] _castOverflow = Array.Empty<RaycastHit>();
        // 원본 InputManager의 머리 조준 레이 길이다. 몸 조준 레이도 같은 길이를 쓴다.
        const float _aimDistance = 100f;
        // 몸 조준 레이가 닿은 피해 Collider를 받는 전용 버퍼다. 사격마다 새 배열을 만들지 않는다.
        static readonly RaycastHit[] _aimHits = new RaycastHit[16];

        // 원본 InputManager처럼 카메라 레이가 처음 닿은 HeadCollider(20) 구가 조준 가능한 적의 머리면 머리를 노린다.
        // 조준점은 맞은 점에서 레이 방향으로 머리 중심까지 거리의 절반을 더 들어간 곳이다. 소속은 호출자가 가진 씬 수명 캐시에서 찾는다.
        internal static OriginalAimResult Aim(Camera camera, Vector2 screen, Vector3 planePoint, ColliderOwnerCache owners)
        {
            Ray ray = camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out RaycastHit head, _aimDistance, 1 << GameLayers.HeadCollider)
                && TargetEnemy(owners, head.collider, out OriginalEnemyController? headEnemy))
            {
                Vector3 point = head.point + ray.direction * (Vector3.Dot(head.collider.transform.position - head.point, ray.direction) * .5f);
                return new OriginalAimResult(point, headEnemy, true);
            }
            // 머리가 아니면 같은 레이에 닿은 피해 Collider 중 가장 가까운 조준 가능한 적의 몸을 노린다.
            // 원작은 총구에서 45개 레이를 부채꼴로 쏘는데, 화면 레이 하나로 대신하는 것은 알려진 차이다.
            int count = Physics.RaycastNonAlloc(ray, _aimHits, _aimDistance, GameplayDataSettings.Layers.DamageReceiverLayerMask,
                QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            OriginalAimResult result = new OriginalAimResult(planePoint, null, false);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _aimHits[i];
                if (hit.distance >= closest || !TargetEnemy(owners, hit.collider, out OriginalEnemyController? enemy)) continue;
                closest = hit.distance;
                result = new OriginalAimResult(hit.point, enemy, false);
            }
            return result;
        }

        // 조준 레이에 닿은 Collider가 생성되어 살아 있는 적의 것인지 소속 캐시로 확인한다.
        static bool TargetEnemy(ColliderOwnerCache owners, Collider collider, out OriginalEnemyController? enemy)
        {
            owners.FindOwner(collider, out enemy, out _);
            return enemy != null && enemy.Targetable;
        }

        // 탄의 첫 구간인 몸에서 총구까지를 검사한다. 원본 firstFrameCheck처럼 첫 구간만 총구 뒤에서 시작한다.
        // 레이 캐스트가 검출하지 않는 시작 겹침도 이 구간에서만 먼저 보아, 벽 안의 총구가 벽 밖으로 발사하지 못하게 한다.
        // 소속은 호출자가 가진 씬 수명 캐시에서 찾는다.
        // shooterTeam은 발사 정보의 팀(OriginalShot.Team)이다. 적 피해 Collider의 같은 팀 통과를 이 값으로 본다.
        // allHalfCover는 발사 정보의 PassesAllHalfCover다. 참이면 모든 모래주머니를 지나간다.
        internal static bool Sweep(Vector3 origin, Vector3 delta, float radius, Transform shooter, bool player, int shooterTeam, RaycastHit[] buffer,
            ColliderOwnerCache owners, out Contact selected, bool allHalfCover = false)
        {
            selected = default;
            float distance = delta.magnitude;
            if (distance <= _minSweepDistance) return false;
            Vector3 direction = delta / distance;
            // 시작 겹침도 원작 Projectile.hitLayers와 같은 레이어만 본다.
            int layers=HitLayers();
            int overlapCount=Physics.OverlapSphereNonAlloc(origin,Mathf.Max(radius,_minOverlapRadius),_overlaps,layers,QueryTriggerInteraction.Ignore);
            Collider[] overlaps=_overlaps;
            if(overlapCount==overlaps.Length) { overlaps=Physics.OverlapSphere(origin,Mathf.Max(radius,_minOverlapRadius),layers,QueryTriggerInteraction.Ignore);overlapCount=overlaps.Length; }
            // 시작 겹침도 캐스트 충돌처럼 거르기에서 찾은 소속을 접촉에 담는다.
            for(int i=0;i<overlapCount;i++)
            {
                Collider collider=overlaps[i];
                // 단순 체력 대상의 수신자도 접촉에 담는다.
                // 같은 팀 통과와 치명 탄의 모래주머니 통과도 캐스트와 같은 거르기로 본다.
                if(Ignore(collider,shooter,player,shooterTeam,allHalfCover,owners,out OriginalEnemyController? enemy,out OriginalPlayerMotor? motor,
                    out DamageReceiver? receiver))continue;
                selected=new Contact(collider,origin,-direction,0,enemy,motor,receiver);return true;
            }
            return Cast(origin, direction, distance, 0, radius, shooter, player, shooterTeam, allHalfCover, buffer, owners, out selected);
        }

        // 날아가는 탄의 한 프레임 이동이다. 원본처럼 시작 겹침 검사 없이, 뒤로 당긴 시작점에서 캐스트를 한 번만 한다.
        // 앞 프레임 끝과 이번 구간 앞쪽까지 덮으며, 접촉 거리는 당기기 전 위치부터 잰다. 시간이 멈춘 프레임은 검사하지 않는다.
        // shooterTeam과 allHalfCover는 Sweep과 같은 발사 정보의 값이다.
        internal static bool SweepFlight(Vector3 position, Vector3 direction, float length, float radius, Transform shooter, bool player,
            int shooterTeam, RaycastHit[] buffer, ColliderOwnerCache owners, out Contact selected, bool allHalfCover = false)
        {
            selected = default;
            if (length <= _minSweepDistance) return false;
            return Cast(position - direction * _flightPullBack, direction, length + _flightPullBack + _flightLookAhead, _flightPullBack,
                radius, shooter, player, shooterTeam, allHalfCover, buffer, owners, out selected);
        }

        // 캐스트 한 번에서 가장 가까운 유효 충돌을 고르고, 거리에서 pullBack을 빼 돌려준다.
        // 시작부터 겹친 Collider는 PhysX가 거리 0과 영점으로 주므로, 그때는 캐스트 시작점을 충돌점으로 쓴다.
        static bool Cast(Vector3 origin, Vector3 direction, float distance, float pullBack, float radius, Transform shooter, bool player,
            int shooterTeam, bool allHalfCover, RaycastHit[] buffer, ColliderOwnerCache owners, out Contact selected)
        {
            selected = default;
            // 원작 Projectile.hitLayers처럼 피해 Collider, 벽, 바닥, BlockBullet 레이어만 캐스트한다.
            int layers = HitLayers();
            int count = CastNonAlloc(origin, direction, distance, radius, layers, buffer);
            // 결과가 버퍼를 가득 채우면 빠진 충돌이 있을 수 있다. 매번 새 배열을 만드는 *All API 대신
            // 전용 버퍼를 두 배씩 키워 넘치지 않을 때까지 다시 캐스트한다. 결과는 정렬되지 않으므로 거리가 같으면 먼저 나온 충돌을 고른다.
            if (count == buffer.Length)
            {
                int size = Mathf.Max(_castOverflow.Length, Mathf.Max(1, buffer.Length) * 2);
                while (true)
                {
                    if (_castOverflow.Length < size) _castOverflow = new RaycastHit[size];
                    // 확장 버퍼로도 같은 레이어와 같은 캐스트 규칙으로 다시 찾는다.
                    count = CastNonAlloc(origin, direction, distance, radius, layers, _castOverflow);
                    if (count < _castOverflow.Length) break;
                    size = _castOverflow.Length * 2;
                }
                buffer = _castOverflow;
            }
            bool found = false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = buffer[i];
                // 더 가까운 충돌을 이미 골랐으면 소속을 찾지 않는다. 그다음 시작 겹침 검사와 같은 필터로 쏜 자신, 같은 팀, 회피 무적을 건너뛴다.
                if (hit.distance >= nearest) continue;
                // 단순 체력 대상의 수신자도 접촉에 담는다. 같은 팀 통과는 쏜 쪽 팀으로, 모래주머니 통과는 치명 확률로도 본다.
                if (Ignore(hit.collider, shooter, player, shooterTeam, allHalfCover, owners, out OriginalEnemyController? enemy,
                    out OriginalPlayerMotor? motor, out DamageReceiver? receiver)) continue;
                nearest = hit.distance; found = true;
                selected = hit.distance > 0
                    ? new Contact(hit.collider, hit.point, hit.normal, Mathf.Max(0, hit.distance - pullBack), enemy, motor, receiver)
                    : new Contact(hit.collider, origin, -direction, 0, enemy, motor, receiver);
            }
            return found;
        }

        // 반지름이 있으면 구 캐스트, 없으면 레이 캐스트로 buffer를 채우고 개수를 돌려준다. Trigger는 보지 않는다.
        static int CastNonAlloc(Vector3 origin, Vector3 direction, float distance, float radius, int layers, RaycastHit[] buffer) =>
            radius > 0
                ? Physics.SphereCastNonAlloc(origin, radius, direction, buffer, distance, layers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, direction, buffer, distance, layers, QueryTriggerInteraction.Ignore);

        // 원본 팀 규칙과 회피 무적은 모든 연속 충돌 검사에 동일하게 적용한다. 쏜 자신이면 소속을 찾지 않고 비워 둔다.
        // 적 Collider는 GetComponentInParent로 소속을 찾으므로 늘 그 적의 Transform 아래에 있어, 쏜 적 자신의 Collider는 팀과 관계없이 첫 줄이 거른다.
        static bool Ignore(Collider collider,Transform shooter,bool player,int shooterTeam,bool allHalfCover,ColliderOwnerCache owners,
            out OriginalEnemyController? enemy,out OriginalPlayerMotor? motor,out DamageReceiver? receiver)
        {
            enemy=null; motor=null; receiver=null;
            if(collider.transform==shooter || collider.transform.IsChildOf(shooter))return true;
            // 원작 Projectile처럼 DamageReceiver(3)가 아닌 벽, 바닥, BlockBullet은 소속 없이 막는다. 탄 필터는 피해 Collider만 소속을 찾는다.
            if(collider.gameObject.layer!=GameLayers.DamageReceiver)return false;
            // 탄마다 매 프레임 GetComponentInParent를 부르지 않도록 소속은 캐시에서 찾는다. 소속이 없는 피해 Collider는 벽처럼 막는다.
            // 조준 가능 여부와 대시 여부는 계속 바뀌므로 매번 읽는다.
            owners.FindOwner(collider,out enemy,out motor,out receiver);
            // 원작 Projectile.cs:316처럼 적 탄도 다른 팀 적을 맞히고, 같은 팀이면 대상이 All일 때만 맞힌다.
            // 플레이어 팀(0)인 적은 없어 플레이어 탄은 조준 가능한 적을 모두 맞힌다. 원작 Projectile.cs:335처럼 대시 중인 적은 피해 없이 지나간다.
            if(enemy!=null)return !enemy.Targetable || enemy.IsDashing || TeamRules.BulletPasses(shooterTeam,enemy.Team);
            // 단순 체력 대상은 원작에서 모두 Teams.all이라 양쪽 탄이 맞힌다. 모래주머니만 원작 ignoreHalfObsticle 규칙으로 지나간다.
            if(receiver!=null)return PassesHalfObsticle(receiver,shooter,player,allHalfCover);
            return motor!=null && (player || motor.IsDashing);
        }

        // 원작 ItemAgent_Gun.ShootOneBullet의 규칙이다. 주인공은 아무 모래주머니 근처에 있으면 모든 모래주머니를,
        // 적은 자기가 근처에 있는 모래주머니만 지나간다. 원작은 발사 순간에 정하지만 여기서는 거르는 순간의 쏜 위치로 본다.
        // 치명 확률이 0.99를 넘는 탄(allHalfCover)은 원작 ItemAgent_Gun.cs:1233처럼 쏜 위치와 관계없이 모두 지나간다.
        static bool PassesHalfObsticle(DamageReceiver receiver,Transform shooter,bool player,bool allHalfCover)
        {
            HalfObsticle? half=receiver.HalfObsticle;
            if(half==null)return false;
            if(allHalfCover)return true;
            Vector3 position=shooter.position;
            return player ? HalfObsticle.AnyNear(position) : half.Near(position);
        }

        // 원작 Projectile.hitLayers다. 탄은 공기 벽(0), HalfObsticle(14), FowBlock(18)을 지나가며, 첫 명중을 확인하는 테스트도 이 탄 마스크를 쓴다.
        // 설정 에셋은 앱 수명이라 캐스트마다 읽어도 할당이 없다.
        internal static int HitLayers()
        {
            GameplayDataSettings.LayersData layers = GameplayDataSettings.Layers;
            return (int)layers.DamageReceiverLayerMask | (int)layers.WallLayerMask | (int)layers.GroundLayerMask | (int)layers.BlockBulletLayers;
        }
    }
}
