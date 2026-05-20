using UnityEngine;

namespace THUAI9.Unity.Core
{
    /// <summary>
    /// MonoBehaviour 单例基类
    /// </summary>
    public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<T>();
                    if (instance == null)
                    {
                        GameObject obj = new GameObject(typeof(T).Name);
                        instance = obj.AddComponent<T>();
                    }
                }
                return instance;
            }
        }

        public static bool TryGetInstance(out T existingInstance)
        {
            if (instance == null)
            {
                instance = FindObjectOfType<T>();
            }

            existingInstance = instance;
            return existingInstance != null;
        }

        protected virtual void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
        }

        protected void ReleaseSingletonInstance()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
