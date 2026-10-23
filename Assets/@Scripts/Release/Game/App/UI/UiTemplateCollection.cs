#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 화면 하나의 가변 목록 항목을 템플릿으로 만들어 소유하고 재사용한다. Begin이 모든 항목의 연결을 초기화하고,
    // Acquire가 앞에서부터 꺼내며, End가 이번에 쓰지 않은 나머지만 숨긴다. 원본 InventoryDisplay처럼 계속 쓰는 항목은 끄고 켜지 않는다.
    // 끄고 켜면 항목마다 그래픽 등록과 메시 재생성, 레이아웃 재계산이 다시 일어나기 때문이다. Begin과 End는 같은 동기 호출 안에서 짝을 이룬다.
    internal sealed class UiTemplateCollection<T> where T : Component
    {
        readonly T _template;
        readonly Action<T>? _reset;
        readonly List<T> _instances = new List<T>();
        int _used;

        // 템플릿은 저작 검사가 연결을 확인하는 View의 직렬화 참조다.
        public UiTemplateCollection(T template, Action<T>? reset = null)
        {
            _template = template;
            _reset = reset;
        }

        // 인스턴스는 소유 View 계층 안에서만 만들어 View와 함께 파괴되므로 파괴 여부를 확인하지 않는다.
        // 앞 목록의 연결은 여기서 모두 끊어, End 전에 눌려도 앞 항목을 부르지 않는다. 표시는 End까지 그대로 둔다.
        public void Begin()
        {
            if (_reset != null)
                foreach (T instance in _instances) _reset(instance);
            _used = 0;
        }

        // 남은 인스턴스를 재사용하고, 모자랄 때만 템플릿을 복제한다. Begin과 같은 이유로 파괴 여부는 확인하지 않는다.
        // 부모가 다를 때만 옮기고, 같은 부모의 앞 항목 바로 뒤에 이미 있으면 형제 순서를 건드리지 않는다. 표시 여부는 호출자가 정한다.
        public T Acquire(Transform parent)
        {
            // 이번 채우기에서 바로 앞에 꺼낸 항목이다. Begin 뒤 첫 호출이면 없다. 같은 부모의 다음 항목을 그 바로 뒤에 둔다.
            Transform? previous = _used > 0 ? _instances[_used - 1].transform : null;
            T instance;
            if (_used == _instances.Count)
            {
                instance = UnityEngine.Object.Instantiate(_template, parent, false);
                _instances.Add(instance);
            }
            else
            {
                instance = _instances[_used];
                if (instance.transform.parent != parent) instance.transform.SetParent(parent, false);
            }
            ++_used;
            Transform placed = instance.transform;
            // 이 부모의 첫 항목은 처음 만들거나 옮겨 왔으면 이미 맨 뒤에 있고, 남아 있던 항목이면 지난 순서 그대로라 옮기지 않는다.
            if (previous != null && previous.parent == parent)
            {
                // 옮긴 뒤 앞 항목 바로 뒤가 되는 순번이다. 이 항목이 앞 항목보다 앞에 있으면 빠지는 자리만큼 하나 당겨진다.
                int after = previous.GetSiblingIndex(), current = placed.GetSiblingIndex();
                int target = current < after ? after : after + 1;
                if (current != target) placed.SetSiblingIndex(target);
            }
            return instance;
        }

        // 첫 채우기가 한 프레임에 항목을 몰아 만들지 않도록, 화면을 준비할 때 숨긴 항목을 count개까지 미리 만든다.
        // 한 번 켰다 꺼서 Awake와 첫 OnEnable 비용도 이때 치른다. 이미 있는 만큼은 만들지 않고, 모자라면 Acquire가 그때 더 만든다.
        public void Prewarm(Transform parent, int count)
        {
            for (int index = _instances.Count; index < count; index++)
            {
                T instance = UnityEngine.Object.Instantiate(_template, parent, false);
                GameObject created = instance.gameObject;
                created.SetActive(true);
                created.SetActive(false);
                _instances.Add(instance);
            }
        }

        // 이번 Begin 뒤에 꺼내지 않은 항목만 숨긴다. 연결은 Begin이 이미 끊었다.
        public void End()
        {
            for (int index = _used; index < _instances.Count; index++) _instances[index].gameObject.SetActive(false);
        }

        // 모든 항목의 연결을 끊고 숨겨 풀로 돌린다. 목록을 닫을 때 쓴다.
        public void Clear()
        {
            Begin();
            End();
        }
    }
}
