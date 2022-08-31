
//public sealed class Singleton<T> where T : class, new()
//{
//    private static readonly T instance = null;
//    static Singleton()
//    {
//        instance = new T();
//    }
//    private Singleton()
//    {
//    }
//    public static T Instance
//    {
//        get
//        {
//            return instance;
//        }
//    }
//}
public class Singleton<T> where T : class, new()
{
    private static T _instance;
    private static readonly object syslock = new object();
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (syslock)
                {
                    if (_instance == null)
                    {
                        _instance = new T();
                    }
                }
            }
            return _instance;
        }
    }
}