using UnityEngine;

/// <summary>
/// 싱글톤 베이스. Resources/{TypeName} 프리팹이 있으면 사용하고, 없으면 생성한다.
/// </summary>
public partial class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance
    {
        get
        {
            if (null != instance)
                return instance;

            if (!Application.isPlaying)
                return null;

            string typeName = typeof(T).Name;
            var prefab = Resources.Load(typeName);

            if (null != prefab)
                instance = (Instantiate(prefab) as GameObject).GetComponent<T>();

            if (null == instance && Time.timeScale != 0)
                instance = new GameObject(typeof(T).Name).AddComponent<T>();

            if (null != instance && null == instance.transform.parent)
                DontDestroyOnLoad(instance.gameObject);

            return instance;
        }
    }

    protected virtual void Awake()
    {
        if (null != instance)
        {
            Destroy(gameObject);
            return;
        }

        instance = this as T;

        if (null == transform.parent)
            DontDestroyOnLoad(gameObject);
    }

    public static bool HasInstance => instance != null;

    public static void DestroyInstance()
    {
        if (null == instance)
            return;

        if (Application.isPlaying)
            Object.Destroy(instance.gameObject);
        else
            Object.DestroyImmediate(instance.gameObject);

        instance = null;
    }
}

public partial class SingletonBehaviour<T>
{
    static T instance;
}
