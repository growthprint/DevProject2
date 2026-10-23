#nullable enable annotations

using System;
using UnityEngine;

namespace DuckCharacter
{
    // 화면이 Animator를 직접 찾거나 조작하지 않도록 재생 책임을 모은다. Animator 위치는 바꾸지 않고 참조와 임시 미리보기
    // 설정만 소유한다.
    [Serializable]
    public sealed class DuckCharacterAnimation
    {
        // Armature 경로를 해석하는 Animator다.
        [SerializeField] Animator? _animator;
        // 미리보기 반복이 읽고 다시 재생하는 Animator 기본 레이어 번호다.
        const int _previewLayer = 0;
        // 임시 설정은 처음 변경하기 전에 한 번만 캡처한다.
        bool _configured;
        // 미리보기 종료 시 복원할 제작 당시 업데이트 방식과 루트 모션이다.
        AnimatorUpdateMode _originalUpdateMode;
        bool _originalRootMotion;

        // 일시정지한 게임과 독립적으로 미리보기를 재생한다. 미리보기 프리팹의 Animator 연결은 EditMode 테스트가 확인한다.
        public void ConfigurePreview()
        {
            // 설정은 처음 한 번만 캡처해야 Restore가 제작 당시 값으로 되돌린다.
            if (_configured) return;
            _originalUpdateMode = _animator.updateMode;
            _originalRootMotion = _animator.applyRootMotion;
            _configured = true;
            _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            _animator.applyRootMotion = false;
        }

        // 원본의 반복하지 않는 클립도 미리보기에서는 끝에 닿으면 처음부터 다시 재생한다.
        public void TickPreview()
        {
            AnimatorStateInfo state = _animator!.GetCurrentAnimatorStateInfo(_previewLayer);
            if (state.normalizedTime >= 1f) _animator.Play(state.fullPathHash, _previewLayer, state.normalizedTime % 1f);
        }

        // 여러 번 불러도 되고 Animator가 파괴됐어도 던지지 않는다. 캡처한 설정이 있을 때만 복원한다.
        public void Restore()
        {
            if (!_configured) return;
            if (_animator != null)
            {
                _animator.updateMode = _originalUpdateMode;
                _animator.applyRootMotion = _originalRootMotion;
            }
            _configured = false;
        }
    }
}
