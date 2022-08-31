namespace MVC
{
    public class ApplicationEntry : NotifierBehaviour
    {

        private void Start()
        {
            OnLaunch();
        }

        protected virtual void OnLaunch()
        {
            //var provider = new AddressableProvider();
            //provider.InitializedCallback = OnStart;
        }

        private void OnApplicationQuit()
        {
            OnStop();
        }

        protected virtual void InitializeCommand() { }
        protected virtual void InitializeProxy() { }
        protected virtual void InitializeMediator() { }
        protected virtual void InitializeSystem() { }

        public virtual void OnStart()
        {
            InitializeProxy();
            InitializeCommand();
            InitializeMediator();
            InitializeSystem();
        }

        protected virtual void OnStop() { }
    }
}
