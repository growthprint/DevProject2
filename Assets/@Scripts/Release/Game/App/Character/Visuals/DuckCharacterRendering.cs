#nullable enable annotations

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckCharacter
{
    // 리그의 Transform 조작과 따로 몸통 표시와 스키닝 원본을 맡는 직렬화 하위 객체다. 렌더러 참조만 갖고 재질은 복제하지 않는다.
    [Serializable]
    public sealed class DuckCharacterRendering
    {
        // 몸통의 본 순서와 루트는 원본 스킨 렌더러에서만 읽는다.
        [SerializeField] SkinnedMeshRenderer? _body;
        // 피부색을 칠할 렌더러와 셰이더 색 프로퍼티다.
        [SerializeField] Renderer[] _mainRenderers = Array.Empty<Renderer>();
        [SerializeField] string _mainColorKey = "_BaseColor";
        // 반복 외형 적용에서 같은 프로퍼티 블록을 재사용한다.
        readonly DuckRendererTint _tint = new DuckRendererTint();

        // 스킨 장비는 이 몸통 렌더러를 부모로 삼아 붙는다.
        public SkinnedMeshRenderer? BodyRenderer => _body;

        // 피부색 렌더러의 현재 표시 상태와 프로퍼티를 기록해 첫 적용이 실패해도 되돌릴 수 있게 한다.
        internal void CaptureAppearance(DuckAppearanceState state) => state.CaptureRenderers(_mainRenderers);

        // 피부색 적용은 표시 하위 객체에서만 수행한다.
        public void ApplyMainColor(Color color) => _tint.Apply(_mainRenderers, _mainColorKey, color);

        // 이름별 본 사전을 만들어 돌려주고 원본 본 순서는 그대로 둔다. 몸통의 rootBone과 bones 연결, 본 이름의 누락과 중복은
        // 리그 프리팹을 검사하는 EditMode 테스트가 확인하므로 실패 결과를 두지 않는다.
        public Dictionary<string, Transform> BuildBoneMap()
        {
            // 본 배열 속성은 호출마다 복사본을 만들므로 한 번만 읽는다.
            Transform[] bones = _body.bones;
            var result = new Dictionary<string, Transform>(bones.Length + 1, StringComparer.Ordinal);
            // 이름이 겹치면 먼저 나온 본을 둔다. 잘못된 본으로 덮어쓰지 않기 위해서다.
            foreach (Transform bone in bones)
                result.TryAdd(bone.name, bone);
            // 루트 본은 bones에 같은 이름이 없을 때만 더한다.
            Transform root = _body.rootBone;
            result.TryAdd(root.name, root);
            return result;
        }
    }
}
