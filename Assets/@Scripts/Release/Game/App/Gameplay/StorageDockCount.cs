#nullable enable annotations
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 StorageDockCountTMP다. 기지 우편함(OriginalWorld_Base/StorageDock/CountDisplay)에 창고 임시 보관함 항목 수를 3D 글자로 쓰고,
    // 원작 setActiveFalseWhenCountIsZero처럼 0이면 이 물체를 끈다. 원작 OnItemAddedToBuffer, OnTakeBufferItem, OnLoadingFinished 대신
    // 세션 변경 알림(GameSessionManager.Changed)을 듣는다. 꺼진 뒤에도 다시 켜지도록 원작처럼 파괴될 때까지 구독을 유지한다.
    [DisallowMultipleComponent]
    public sealed class StorageDockCount : MonoBehaviour
    {
        // 원작 tmp다. CountDisplay 아래 Text (TMP)의 3D TextMeshPro를 제작 도구가 연결한다.
        [SerializeField] TMP_Text _text = null!;
        // 원작 setActiveFalseWhenCountIsZero다(원작 값 1).
        [SerializeField] bool _hideWhenZero = true;

        // 구독한 세션이다. 해제는 세션이 바뀌었어도 구독한 그 Manager에서 한다.
        GameSessionManager? _session;
        // 마지막으로 쓴 수다. 세션 변경마다 불리므로 수가 같으면 글자를 다시 만들지 않는다.
        int _shown = -1;

        // 원작은 Awake에서 구독하고 Start에서 그린다. 세션은 씬을 불러오기 전에 만들어지므로 Start에서 한 번 잡는다.
        // 세션이 없는 씬 실행에서는 원작 빈 보관함처럼 0으로 보인다.
        void Start()
        {
            _session = GameManager.HasInstance ? GameManager.SessionManager : null;
            if (_session == null)
            {
                Show(0);
                return;
            }
            _session.Changed += Refresh;
            Refresh(_session.Snapshot());
        }

        void OnDestroy()
        {
            if (_session != null) _session.Changed -= Refresh;
            _session = null;
        }

        void Refresh(GameSessionSnapshot snapshot) => Show(snapshot.Campaign.Gameplay.Inventory.StorageBuffer.Count);

        void Show(int count)
        {
            if (count != _shown)
            {
                _text.text = count.ToString();
                _shown = count;
            }
            if (_hideWhenZero) gameObject.SetActive(count > 0);
        }
    }
}
