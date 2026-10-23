#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 Grenade의 투사체 물리 부분이다. 제작 도구가 원작 Grenade_One으로 만든 템플릿 루트에 붙인다.
    // 첫 충돌 기록, 충돌 감속, 충돌 소리 요청만 맡고, 폭발 시각과 피해는 던진 쪽 전투 시뮬레이션이 정한다.
    internal sealed class OriginalGrenade : MonoBehaviour, IPoolable
    {
        [SerializeField] Rigidbody _body = null!;
        [SerializeField] Collider _collider = null!;
        [SerializeField] TrailRenderer? _trail;
        // 원작 Grenade.makeSoundCount와 collideSound다. 충돌 소리는 0.3초보다 짧은 간격으로 내지 않는다.
        [SerializeField] int _collideSoundCount = 3;
        [SerializeField] string _collideSound = "";
        int _soundsLeft;
        float _soundMark;
        bool _soundPending;
        // 원작 IgnoreCollisionForSeconds로 잠시 무시하는 던진 캐릭터의 Collider다.
        Collider? _thrower;

        // 원작 collide다. delayFromCollide 스킬은 이 값이 켜진 뒤부터 폭발 시간을 잰다.
        internal bool Collided { get; private set; }
        internal string CollideSound => _collideSound;

        // 원작 Grenade.Launch다. 회전은 원작처럼 수평축으로만 무작위로 준다.
        internal void Launch(Vector3 start, Vector3 velocity, Collider thrower)
        {
            _body.position = start;
            transform.position = start;
            _body.linearVelocity = velocity;
            Vector3 spin = (Random.insideUnitSphere + Vector3.one) * 7f;
            spin.y = 0f;
            _body.angularVelocity = spin;
            Physics.IgnoreCollision(thrower, _collider, true);
            _thrower = thrower;
        }

        // 원작처럼 던진 뒤 0.5초가 지나면 던진 캐릭터와 다시 부딪친다. 시간은 전투 시뮬레이션이 잰다.
        internal void RestoreThrowerCollision()
        {
            if (_thrower != null) Physics.IgnoreCollision(_thrower, _collider, false);
            _thrower = null;
        }

        // 충돌이 소리를 요청했으면 한 번만 true를 돌려준다. 소리는 오디오를 가진 전투 시뮬레이션이 낸다.
        internal bool TakeCollideSound()
        {
            bool pending = _soundPending;
            _soundPending = false;
            return pending;
        }

        // 원작 OnCollisionEnter처럼 부딪칠 때마다 수평 속도를 반으로, 회전을 0.3배로 줄인다.
        void OnCollisionEnter(Collision collision)
        {
            Collided = true;
            Vector3 velocity = _body.linearVelocity;
            velocity.x *= .5f;
            velocity.z *= .5f;
            _body.linearVelocity = velocity;
            _body.angularVelocity *= .3f;
            if (_soundsLeft <= 0 || Time.time - _soundMark <= .3f) return;
            _soundsLeft--;
            _soundMark = Time.time;
            _soundPending = true;
        }

        // 풀에서 다시 꺼낸 투사체가 앞선 비행의 충돌, 소리 횟수, 궤적을 이어받지 않게 한다.
        public void OnSpawn()
        {
            Collided = false;
            _soundPending = false;
            _soundsLeft = _collideSoundCount;
            _soundMark = -1f;
            _thrower = null;
            if (_trail != null) _trail.Clear();
        }

        // 비활성이 되면 Unity가 Collider 무시 설정을 풀므로 참조만 놓는다.
        public void OnDespawn() => _thrower = null;
    }
}
