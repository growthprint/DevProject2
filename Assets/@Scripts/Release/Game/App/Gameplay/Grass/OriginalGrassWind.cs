#nullable enable annotations
using System;
using UnityEngine;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 원본 TgsWindSettings의 바람 값이다. 제작 도구가 원본 값을 그대로 옮기도록 필드는 원본 이름 앞에 _를 붙이고 순서를 지킨다.
    // 풀 셰이더는 _WindParams 하나로 방향 라디안, 세기, 패치 크기, 속도를 읽는다.
    public sealed class OriginalGrassWind : ScriptableObject
    {
        [SerializeField, Range(0f, 360f)] float _direction;
        [SerializeField, Range(0f, 20f)] float _strength = .5f;
        [SerializeField, Range(0f, .5f)] float _patchSize = .05f;
        [SerializeField, Range(0f, 100f)] float _speed = 20f;

        static readonly int _windParamsId = Shader.PropertyToID("_WindParams");

        // 원본처럼 덩어리를 그릴 때마다 넣으므로 Play Mode에서 자산 값을 고치면 바로 보인다.
        internal void ApplyToMaterialPropertyBlock(MaterialPropertyBlock block)
            => block.SetVector(_windParamsId, new Vector4(_direction * (MathF.PI / 180f), _strength, _patchSize, _speed));
    }
}
