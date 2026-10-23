#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 ConstructionSite다. 건설 전과 후의 물체를 켜고 끄며, 건설하면 setActiveOnBuilt를 켜고 원작 키(ConstructionSite_<키>)를 저장한다.
    // 같은 키를 쓰는 지점(제로존과 기지의 ManHole)은 함께 건설된 것으로 본다. dontSave 지점은 원작 inLevelData처럼 이 레벨에서만 기억한다.
    // 건설 상태는 씬을 불러오기 전에 저장 기록에서 읽혀 있다.
    [DisallowMultipleComponent]
    public sealed class ConstructionSite : MonoBehaviour
    {
        [SerializeField] string _key = "";
        [SerializeField] bool _dontSave;
        // 원작 costTaker다. 지점은 원본 CostTaker 객체를 켜고 끈다. 기지 ManHole도 이제 CostTaker 상호작용으로 옮겨져 같은 경로를 쓴다.
        [SerializeField] GameObject? _costTaker;
        [SerializeField] GameObject[] _notBuilt = Array.Empty<GameObject>();
        [SerializeField] GameObject[] _built = Array.Empty<GameObject>();
        [SerializeField] GameObject[] _setActiveOnBuilt = Array.Empty<GameObject>();
        bool _wasBuilt;

        internal bool WasBuilt => _wasBuilt;

        // 원작 Awake의 Load와 RefreshGameObjects다.
        void Awake()
        {
            _wasBuilt = !_dontSave && GameManager.HasInstance && GameManager.LevelObjects.IsBuilt(_key);
            Refresh();
        }

        // 원작 OnBuilt다. 세션 관문이 닫혀 저장 기록이 거절되면 원작 저장 시점이 없는 것과 같아 기록 없이 넘어간다.
        internal void OnBuilt()
        {
            _wasBuilt = true;
            Refresh();
            foreach (GameObject target in _setActiveOnBuilt) if (target != null) target.SetActive(true);
            if (!_dontSave && GameManager.HasInstance) GameManager.LevelObjects.TryMarkBuilt(_key);
        }

        // 원작 RefreshGameObjects다.
        void Refresh()
        {
            if (_costTaker != null) _costTaker.SetActive(!_wasBuilt);
            foreach (GameObject target in _notBuilt) if (target != null) target.SetActive(!_wasBuilt);
            foreach (GameObject target in _built) if (target != null) target.SetActive(_wasBuilt);
        }

        // 제작 도구가 쓰는 구성이다.
        internal void Configure(string key, bool dontSave, GameObject? costTaker, GameObject[] notBuilt, GameObject[] built, GameObject[] setActiveOnBuilt)
        {
            _key = key; _dontSave = dontSave; _costTaker = costTaker; _notBuilt = notBuilt; _built = built; _setActiveOnBuilt = setActiveOnBuilt;
        }
    }
}
