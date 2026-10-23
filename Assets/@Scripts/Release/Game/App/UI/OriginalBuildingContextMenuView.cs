#nullable enable annotations
using System;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Buildings.UI.BuildingContextMenu와 그 회수 항목(BuildingContextMenuEntry)이다. 오른쪽 클릭한 건물 위에 이름과 회수 버튼을 띄운다.
    // 원작처럼 매 프레임 끝에 대상 건물의 화면 위치를 따라가고, 대상이 회수되어 파괴되면 스스로 숨는다. 회수 의도만 이벤트로 낸다.
    public sealed class OriginalBuildingContextMenuView : MonoBehaviour
    {
        // 원작 nameText와 회수 항목의 text다. 항목 문구(원작 textKey UI_Recycle)는 화면 컨트롤러가 번역해 넘긴다.
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _recycleLabel = null!;

        Transform? _target;
        Camera? _camera;

        // 원작 OnRecycleButtonClicked다. 무엇을 회수할지는 메뉴를 연 컨트롤러가 안다.
        internal event Action? RecycleRequested;

        internal bool IsShown => gameObject.activeSelf;

        // 원작 Setup과 Show다. 카메라는 건물의 화면 위치를 구하는 게임 카메라다.
        internal void Show(Transform target, Camera renderCamera, string displayName, string recycleLabel)
        {
            _target = target;
            _camera = renderCamera;
            _name.text = displayName;
            _recycleLabel.text = recycleLabel;
            gameObject.SetActive(true);
        }

        internal void Hide()
        {
            _target = null;
            _camera = null;
            gameObject.SetActive(false);
        }

        // 원작 LateUpdate다. 공용 HUD 루트 캔버스가 Overlay라 화면 좌표를 카메라 없이 부모 지역 좌표로 바꾼다.
        // 대상이나 카메라가 파괴됐으면 Unity null 비교로 걸러 숨는다.
        void LateUpdate()
        {
            if (_target == null || _camera == null)
            {
                Hide();
                return;
            }
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(_camera, _target.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, screenPoint, null, out Vector2 localPoint);
            transform.localPosition = localPoint;
        }

        // 고정 회수 버튼의 저장된 Runtime Only OnClick이 부른다. 버튼은 열린 메뉴 안에만 있다.
        public void OnRecycleClicked()
        {
            Log.Instance.Assert(IsShown, "건물 메뉴가 열린 동안에만 회수합니다.", this);
            RecycleRequested?.Invoke();
        }
    }
}
