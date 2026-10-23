#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원본 DialogueTree의 Say 노드 한 줄과 그 직전 AT_SetVirtualCamera 노드를 함께 저작한 값이다.
    // NodeCanvas는 옮기지 않으므로 대사 순서에 묶인 카메라 지시만 줄 데이터로 남긴다.
    [Serializable]
    public sealed class OriginalDialogueLine
    {
        [SerializeField] string _key = "";
        // 참이면 이 줄을 보이기 직전에 _camera로 바꾼다. _camera가 비어 있으면 원본의 빈 AT_SetVirtualCamera처럼 플레이 구도로 돌아간다.
        [SerializeField] bool _switchCamera;
        [SerializeField] OriginalVirtualCamera? _camera;

        internal string Key => _key;
        internal bool SwitchesCamera => _switchCamera;
        // Editor는 비운 직렬화 참조에 가짜 null 객체를 넣는다. 게임 카메라는 진짜 null만 플레이 구도로 보므로 여기서 진짜 null로 바꾼다.
        internal OriginalVirtualCamera? Camera => _camera != null ? _camera : null;
    }

    // 원본 대사 트리 하나를 줄 순서대로 저작한 값이다. 프롤로그 컨트롤러와 전투 월드의 대사 지점이 직렬화해 가지며,
    // 제작 도구가 원본 줄 키와 화자, 카메라 지시로 채운다. 두 씬의 대사는 모두 이 값의 Begin으로만 시작한다.
    [Serializable]
    public sealed class OriginalDialogueScript
    {
        // 원본 대사 트리의 actor 파라미터가 가리키는 DialogueActor의 nameKey다. 비어 있으면 이름 탭을 숨긴다.
        // 원본 프롤로그 트리처럼 actor 객체가 없는 트리는 비워 둔다.
        [SerializeField] string _speakerKey = "";
        [SerializeField] OriginalDialogueLine[] _lines = Array.Empty<OriginalDialogueLine>();

        // 대사 진행자로 줄을 보이고, 각 줄 직전에 그 줄의 카메라 지시를 게임 카메라에 적용한다.
        // 마지막 줄을 넘기면 finished를 먼저 부르고, 원본 트리 끝의 빈 AT_SetVirtualCamera처럼 플레이 구도로 돌아간다.
        internal void Begin(DialogueSequenceRunner runner, OriginalGameCamera camera, Action? finished)
        {
            // 줄 키 목록은 시작할 때마다 지금 줄로 만든다. 대사 시작은 드물고, Play Mode 중 Inspector로 고친 줄도 다음 대사에 쓰인다.
            var keys = new string[_lines.Length];
            for (int i = 0; i < _lines.Length; i++) keys[i] = _lines[i].Key;
            runner.Begin(keys, _speakerKey.Length > 0 ? _speakerKey : null, index =>
            {
                OriginalDialogueLine line = _lines[index];
                if (line.SwitchesCamera) camera.SetDialogueCamera(line.Camera);
            }, () =>
            {
                if (finished != null) finished();
                camera.SetDialogueCamera(null);
            });
        }
    }
}
