#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterAnimationControl의 animatorHashes다. 컨트롤러에 있는 파라미터에만 값을 보낸다(Docs/rules/character.md "Dedicated rigs").
    // 전용 리그 컨트롤러(SpiderGunTower, AC_Dog 덮어쓰기)는 오리 이동 컨트롤러보다 파라미터가 적어, 거르지 않으면 없는 파라미터 경고가 프레임마다 난다.
    internal sealed class OriginalAnimatorParameters
    {
        // 원본 이동 AnimatorController가 가진 파라미터의 해시다. 플레이어 모터, 손 애니메이션, 적, 펫이 같은 해시를 읽는다.
        // 이름과 형식은 EditMode 저작 테스트가 확인한다.
        internal static readonly int _moveSpeed = Animator.StringToHash("MoveSpeed");
        internal static readonly int _moveDirX = Animator.StringToHash("MoveDirX");
        internal static readonly int _moveDirY = Animator.StringToHash("MoveDirY");
        internal static readonly int _dashing = Animator.StringToHash("Dashing");
        internal static readonly int _gunReady = Animator.StringToHash("GunReady");
        internal static readonly int _handState = Animator.StringToHash("HandState");
        internal static readonly int _rightHandOut = Animator.StringToHash("RightHandOut");
        internal static readonly int _attack = Animator.StringToHash("Attack");

        readonly Animator _animator;
        // 컨트롤러 파라미터의 해시 목록이다. 꺼진 Animator는 파라미터를 돌려주지 않으므로, 컨트롤러가 묶인 뒤 처음 쓸 때 한 번 모은다.
        HashSet<int>? _hashes;

        internal OriginalAnimatorParameters(Animator animator) => _animator = animator;

        internal void SetFloat(int hash, float value) { if (Has(hash)) _animator.SetFloat(hash, value); }
        internal void SetBool(int hash, bool value) { if (Has(hash)) _animator.SetBool(hash, value); }
        internal void SetInteger(int hash, int value) { if (Has(hash)) _animator.SetInteger(hash, value); }
        internal void SetTrigger(int hash) { if (Has(hash)) _animator.SetTrigger(hash); }

        // 컨트롤러가 아직 묶이지 않은 프레임에는 아무 값도 보내지 않는다. 다음 갱신이 다시 보낸다.
        bool Has(int hash)
        {
            if (_hashes == null)
            {
                if (!_animator.isInitialized) return false;
                _hashes = new HashSet<int>();
                foreach (AnimatorControllerParameter parameter in _animator.parameters) _hashes.Add(parameter.nameHash);
            }
            return _hashes.Contains(hash);
        }
    }
}
