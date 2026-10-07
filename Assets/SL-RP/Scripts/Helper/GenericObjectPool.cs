using System.Collections.Generic;
using UnityEngine;

public class GenericObjectPool<T> : MonoBehaviour where T : MonoBehaviour
{
  [Header("Object Pool Settings")]
  [SerializeField] private int InitialCount; 
  [SerializeField] protected T PrefabToPool;
  [SerializeField] protected Transform ParentTransform;
  protected Queue<T> PoolQueue = new Queue<T>();
  [SerializeField] protected List<T> ItemsInUse = new List<T>();

  internal virtual void Start() => InitializePool(InitialCount);

  protected virtual void InitializePool(int count)
  {
    for (int i = 0; i < count; i++)
    {
      T item = Instantiate(PrefabToPool);
      item.transform.SetParent(ParentTransform, false);
      item.gameObject.SetActive(false);
      PoolQueue.Enqueue(item);
    }
  }

  internal virtual T GetFromPool()
  {
    if (PoolQueue.Count > 0)
    {
      T item = PoolQueue.Dequeue();
      item.gameObject.SetActive(true);
      ItemsInUse.Add(item);
      return item;
    }
    return CreateNewPooledItem();
  }

  internal virtual T CreateNewPooledItem()
  {
    // print("Creating new pooled item as pool is empty.");
    T newItem = Instantiate(PrefabToPool, this.transform);
    newItem.transform.SetParent(ParentTransform, false);
    ItemsInUse.Add(newItem);
    return newItem;
  }

  internal virtual void ReturnToPool(T item)
  {
    if (!ItemsInUse.Contains(item))
    {
      Debug.LogError($"[GenericObjectPool] Trying to return an item that is not in use! {item.name}");
      return;
    }

    item.gameObject.SetActive(false);
    if (ParentTransform != null)
      item.transform.SetParent(ParentTransform, false);
    item.transform.localPosition = Vector3.zero;
    item.transform.localRotation = Quaternion.identity;
    item.transform.localScale = Vector3.one;
    PoolQueue.Enqueue(item);
    ItemsInUse.Remove(item);
  }

  internal virtual void ReturnAllItemsToPool()
  {
    foreach (T item in ItemsInUse)
    {
      if (ParentTransform != null)
        item.transform.SetParent(ParentTransform, false);
      item.transform.localPosition = Vector3.zero;
      item.transform.localRotation = Quaternion.identity;
      item.transform.localScale = Vector3.one;
      item.gameObject.SetActive(false);
      PoolQueue.Enqueue(item);
    }
    ItemsInUse.Clear();
  }
}
