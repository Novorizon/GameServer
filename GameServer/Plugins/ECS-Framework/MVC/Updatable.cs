//using PureMVC.Patterns.Observer;

//namespace MVC
//{
//    public class Updatable : Notifier
//    {
//        private bool enabled;
//        private TimerManager.TimerTask updateTask;

//        public void EnableUpdate(bool tf)
//        {
//            if (tf != enabled)
//            {
//                enabled = tf;
//                if (enabled)
//                    updateTask = TimerManager.Instance.AddFrameExecuteTask(Update);
//                else
//                    updateTask.Stop();
//            }
//        }

//        private bool Update()
//        {
//            OnUpdate(TimerManager.Instance.DeltaTime);

//            return !enabled;
//        }
//        protected virtual void OnUpdate(float deltaTime) { }
//    }
//}
