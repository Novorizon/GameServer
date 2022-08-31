using PureMVC.Interfaces;
using PureMVC.Patterns.Facade;
using System.Collections;
using System.Collections.Generic;

namespace MVC
{
    public class NotifierBehaviour :INotifier
    {
        public void SendNotification(string notificationName, object body = null, string type = null)
        {
            Facade.SendNotification(notificationName, body, type);
        }

        /// <summary>Return the Singleton Facade instance</summary>
        protected IFacade Facade => PureMVC.Patterns.Facade.Facade.GetInstance(() => new Facade());
    }
}
