#nullable enable annotations

using System;
using Character;
using DuckCharacter.AvatarParts;
using UnityEngine;

namespace DuckCharacter
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Duck Character/Duck Avatar Part")]
    public class DuckAvatarPart : DuckPart
    {
        // 파츠의 부위다. 라이브러리 카테고리에 등록한 프리팹은 그 카테고리 부위 값을 가져야 한다.
        [SerializeField]
        DuckAvatarPartType _type = DuckAvatarPartType.Hair;

        [Header("배치 기준"), Tooltip("단일 파츠의 배치 기준 트랜스폼.")]
        [SerializeField]
        Transform? _centerObject;

        [Tooltip("좌우 한 쌍 파츠의 왼쪽 기준 트랜스폼.")]
        [SerializeField]
        Transform? _leftObject;

        [Tooltip("좌우 한 쌍 파츠의 오른쪽 기준 트랜스폼.")]
        [SerializeField]
        Transform? _rightObject;

        [Tooltip("틴트를 넣을 셰이더 프로퍼티 이름. URP Lit / Unlit 은 _BaseColor 다.")]
        [SerializeField]
        string _customColorKey = "_BaseColor";

        // 색을 바꿀 렌더러만 담는다. 눈의 흰자처럼 색이 고정된 렌더러는 넣지 않는다.
        [SerializeField] Renderer[] _customColorRenderers = Array.Empty<Renderer>();

        // 프로퍼티 블록과 셰이더 ID를 인스턴스마다 재사용한다.
        readonly DuckRendererTint _tint = new DuckRendererTint();

        public DuckAvatarPartType Type { get { return _type; } }

        // 원작 UpdateHands와 같은 규칙으로, 중앙 기준점이 있는 날개인 장갑만 손을 따른다.
        // 모든 빌드에서 외형 적용이 이 값으로 부착할 소켓을 고른다.
        internal bool FollowsHands => _type == DuckAvatarPartType.Wing && _centerObject != null;

        // 재사용하는 파츠의 배치와 활성 상태, 렌더러 표시와 틴트를 외형 트랜잭션에 기록해 실패하면 되돌릴 수 있게 한다.
        internal void CaptureAppearance(DuckAppearanceState state)
        {
            state.CaptureTransform(transform, attachment: true);
            state.CaptureTransform(_centerObject);
            state.CaptureTransform(_leftObject);
            state.CaptureTransform(_rightObject);
            state.CaptureRenderers(_renderers);
            state.CaptureRenderers(_customColorRenderers);
        }

        // 배치와 색을 함께 적용하고, 넘겨받은 외형 데이터는 바꾸지 않는다.
        public void Apply(DuckAvatarPartInfo info)
        {
            ApplyTransform(info);
            _tint.Apply(_customColorRenderers, _customColorKey, info._color);
        }

        // 중앙 파츠도 원작처럼 좌우각의 음수만큼 위치를 돌리고, 비틀기를 원작과 같은 순서로 곱한다.
        public void ApplyTransform(DuckAvatarPartInfo info)
        {
            if (_centerObject != null)
            {
                // 원작은 장갑 기준점의 위치와 회전을 매 프레임 손 소켓 값으로 덮어써서 반지름, 높이, 비틀기가 효과가 없다.
                // 그래서 장갑은 손 파츠 소켓에 붙은 루트의 원점에 회전 없이 두고 배율만 적용한다.
                if (FollowsHands)
                {
                    _centerObject.localPosition = Vector3.zero;
                    _centerObject.localRotation = Quaternion.identity;
                }
                else
                {
                    Vector3 direction = Quaternion.Euler(0f, -info._leftRightAngle, 0f) * Vector3.forward;
                    _centerObject.localPosition = direction * info._radius + Vector3.up * (info._height + info._heightOffset);
                    _centerObject.localRotation = Quaternion.Euler(0f, 0f, info._twist) * Quaternion.LookRotation(direction);
                }
                _centerObject.localScale = Vector3.one * info.SafeScale;
            }

            PlaceSide(_leftObject, -1f, info);
            PlaceSide(_rightObject, 1f, info);
        }

        // 외형 적용은 오른쪽 소켓 인스턴스에 true를 넘겨 루트 X 스케일을 -1로 뒤집는다.
        public void SetMirrored(bool mirrored)
        {
            transform.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
        }

        // 좌우 기준점의 SRT 를 계산한다. sign 은 왼쪽 -1, 오른쪽 +1 이며 distanceAngle 과 비틀기 방향을 정한다.
        void PlaceSide(Transform? t, float sign, DuckAvatarPartInfo info)
        {
            if (t == null)
                return;

            Vector3 direction = Quaternion.Euler(0f, -info._leftRightAngle + sign * info._distanceAngle, 0f) * Vector3.forward;

            t.localPosition = direction * info._radius + Vector3.up * (info._height + info._heightOffset);
            t.localRotation = Quaternion.AngleAxis(-sign * info._twist, direction) * Quaternion.LookRotation(direction, Vector3.up);
            t.localScale = Vector3.one * info.SafeScale;
        }
    }

    // 파츠(DuckAvatarPart)와 몸통(DuckCharacterRendering)이 같은 렌더러 틴트 규칙을 쓴다.
    internal sealed class DuckRendererTint
    {
        // 색상 적용마다 새로 만들지 않고 재사용하는 블록이다.
        MaterialPropertyBlock? _block;
        // 프로퍼티 이름이 바뀔 때만 셰이더 ID 를 다시 계산한다.
        string? _cachedKey;
        int _propertyId;
        // 공유 재질은 바꾸지 않고 렌더러 전체 프로퍼티 블록에 불투명 색을 칠한다. 피격 밝기(_HurtValue)도 같은 블록에 있으므로
        // 원작 CustomFacePart처럼 한 블록을 함께 쓴다. 슬롯별 블록을 쓰면 Unity가 렌더러 블록을 무시해 피격 림이 사라진다.
        internal void Apply(Renderer[]? targets, string key, Color color)
        {
            if (targets == null || targets.Length == 0)
                return;

            if (_cachedKey != key)
            {
                _cachedKey = key;
                _propertyId = string.IsNullOrEmpty(key) ? 0 : Shader.PropertyToID(key);
            }

            _block ??= new MaterialPropertyBlock();
            color.a = 1f;
            for (int r = 0; r < targets.Length; r++)
            {
                Renderer renderer = targets[r];

                if (renderer == null)
                    continue;

                // 블록을 읽어 색만 덮어써서, 피격 밝기처럼 다른 소유자가 넣은 값을 지우지 않는다.
                renderer.GetPropertyBlock(_block);
                _block.SetColor(_propertyId, color);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
