#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace Game.App
{
    // 고정 TMP 문구의 대상과 번역 키를 직접 직렬화하는 컴포넌트다.
    // 씬과 프리팹이 스크립트 참조를 잃지 않도록 클래스 이름을 파일 이름과 같게 둔다.
    [DisallowMultipleComponent]
    public sealed class LocalizedUguiText : MonoBehaviour
    {
        // 제작 도구가 같은 오브젝트의 TMP를 연결한다. 실행 중 이름으로 찾지 않는다.
        [SerializeField] TMP_Text _target = null!;
        // 표시 문자열 대신 안정된 GameUI 테이블 키를 직렬화한다.
        [SerializeField] string _key = "";

        // 같은 대리자로 구독을 해제하고 재활성화 때 다시 쓰도록 참조와 대리자를 보관한다.
        LocalizedString? _reference;
        LocalizedString.ChangeHandler? _handler;

        // 활성 상태일 때만 번역 변경을 구독하고, 현재 로캘 문자열을 비동기로 받는다.
        void OnEnable()
        {
            // 키는 실행 중 바뀌지 않으므로 참조와 대리자를 첫 활성화에서 한 번만 만든다. 둘 다 UnityEngine.Object가 아니라 ??=를 쓴다.
            // 구독이 모두 풀린 뒤 다시 구독하면 패키지가 현재 로캘 문자열을 새로 읽는다.
            _reference ??= new LocalizedString(GameLocalization.Table, _key);
            // 대상과 키는 제작 도구가 연결하고 EditMode 테스트가 확인한다. OnDisable이 구독을 해제하므로 콜백에서 null을 검사하지 않는다.
            _handler ??= value => _target.text = value;
            _reference.StringChanged += _handler;
        }

        // 비활성 화면으로 늦은 번역 콜백이 가지 않게 구독을 해제한다.
        // 다음 활성화가 같은 참조와 대리자를 다시 쓰므로 비우지 않는다.
        void OnDisable()
        {
            if (_reference != null && _handler != null)
                _reference.StringChanged -= _handler;
        }
    }
}
